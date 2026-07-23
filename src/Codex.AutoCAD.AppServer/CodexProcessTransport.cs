using System.Collections.Concurrent;
using System.Diagnostics;

namespace Codex.AutoCAD.AppServer;

/// <summary>Starts <c>codex app-server --stdio</c> and exposes its standard streams.</summary>
public sealed class CodexProcessTransport : IAppServerTransport
{
    private const int StandardErrorTailLimit = 1;
    private readonly AppServerClientOptions _options;
    private readonly object _sync = new();
    private readonly ConcurrentQueue<AppServerStandardErrorSummary> _standardErrorTail = new();
    private Process? _process;
    private Task? _standardErrorPump;
    private Stream? _readStream;
    private Stream? _writeStream;
    private bool _expectedExit;
    private int _exitRaised;

    public CodexProcessTransport(AppServerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public Stream ReadStream => _readStream
        ?? throw new InvalidOperationException("The App Server process has not started.");

    public Stream WriteStream => _writeStream
        ?? throw new InvalidOperationException("The App Server process has not started.");

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    public event EventHandler<AppServerStandardErrorEventArgs>? StandardErrorReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("The App Server process is already running.");
            }

            _process?.Dispose();
            _process = null;
            _readStream = null;
            _writeStream = null;
            _expectedExit = false;
            _exitRaised = 0;
            while (_standardErrorTail.TryDequeue(out _)) { }

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.CodexExecutablePath,
                WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
            foreach (var argument in _options.AdditionalArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var (name, value) in _options.Environment)
            {
                startInfo.Environment[name] = value;
            }

            var process = new Process
            {
                StartInfo = startInfo,
            };
            process.Exited += OnProcessExited;
            _process = process;

            try
            {
                if (!process.Start())
                {
                    throw new AppServerException("Failed to start the Codex App Server process.");
                }

                _readStream = process.StandardOutput.BaseStream;
                _writeStream = process.StandardInput.BaseStream;
                _standardErrorPump = PumpStandardErrorAsync(process);

                // A fast-failing child can exit before StartAsync returns. Do not enable the exit
                // callback until the stderr drain task exists, otherwise diagnostics race as empty.
                process.EnableRaisingEvents = true;
            }
            catch
            {
                process.Exited -= OnProcessExited;
                process.Dispose();
                _process = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_sync)
        {
            _expectedExit = true;
            process = _process;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
            // The process already exited before stdin could be closed.
        }

        if (!process.HasExited)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(gracefulTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (_standardErrorPump is not null)
        {
            await _standardErrorPump.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(_options.ShutdownTimeout).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (_process is not null)
                {
                    _process.Exited -= OnProcessExited;
                    _process.Dispose();
                    _process = null;
                }

                _readStream = null;
                _writeStream = null;
            }
        }
    }

    private async Task PumpStandardErrorAsync(Process process)
    {
        var summary = await AppServerStandardErrorCapture.DrainAsync(
                process.StandardError.BaseStream,
                _options.MaximumStandardErrorBytes)
            .ConfigureAwait(false);
        _standardErrorTail.Enqueue(summary);
        while (_standardErrorTail.Count > StandardErrorTailLimit)
        {
            _standardErrorTail.TryDequeue(out _);
        }

        StandardErrorReceived?.Invoke(
            this,
            new AppServerStandardErrorEventArgs(summary));
    }

    private async void OnProcessExited(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) != 0 || sender is not Process process)
        {
            return;
        }

        var standardErrorPump = _standardErrorPump;
        var expectedExit = _expectedExit;
        if (standardErrorPump is not null)
        {
            try
            {
                // The child has exited, so this ordinarily completes at pipe EOF. It runs
                // asynchronously to avoid blocking the Process event thread.
                await standardErrorPump.ConfigureAwait(false);
            }
            catch
            {
                // Stderr is diagnostics only; a drain failure must not strand exit propagation.
            }
        }

        int? exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            exitCode = null;
        }

        var eventArgs = new AppServerTransportExitedEventArgs(
            exitCode,
            expectedExit,
            _standardErrorTail.ToArray());
        RaiseExited(eventArgs);
    }

    private void RaiseExited(AppServerTransportExitedEventArgs eventArgs)
    {
        if (Exited is null)
        {
            return;
        }

        foreach (EventHandler<AppServerTransportExitedEventArgs> handler in Exited.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // Background exit observers cannot fault an async Process event callback.
            }
        }
    }
}
