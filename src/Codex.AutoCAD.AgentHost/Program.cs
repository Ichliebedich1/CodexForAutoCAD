using System.Text.Json;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentLauncher;

var exitCode = await AgentHostProgram.RunAsync(args);
return exitCode;

internal static class AgentHostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "doctor";
        if (command == "bootstrap-doctor")
        {
            try
            {
                return RunBootstrapDoctor(args);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    "bootstrap-doctor: "
                    + exception.GetType().Name
                    + ": "
                    + exception.Message);
                return 1;
            }
        }

        var workspacePath = GetOption(args, "--workspace")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "CodexForAutoCAD",
                "workspace",
                command);
        var codexExecutable = GetOption(args, "--codex")
            ?? Environment.GetEnvironmentVariable("CODEX_EXECUTABLE")
            ?? "codex";

        try
        {
            var workspace = AgentWorkspace.Create(workspacePath);
            return command switch
            {
                "doctor" => await RunDoctorAsync(codexExecutable, workspace),
                "run" => await RunUntilCancelledAsync(codexExecutable, workspace),
                _ => WriteUsageError(command)
            };
        }
        catch (Exception exception)
        {
            WriteJson(new
            {
                ok = false,
                command,
                error = exception.GetType().Name,
                message = exception.Message
            });
            return 1;
        }
    }

    private static int RunBootstrapDoctor(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException(
                "bootstrap-doctor accepts no command-line bootstrap material.");
        }

        AgentBootstrapInheritedChannel.ClearStandardErrorInheritance();
        using var bootstrapInput = AgentBootstrapInheritedChannel.OpenStandardInput();
        using var confirmationOutput = AgentBootstrapInheritedChannel.OpenStandardOutput();
        using var payload = AgentBootstrapInheritedChannel.ReadSingleBootstrapPacket(
            bootstrapInput);
        var bootstrapId = payload.CopyBootstrapId();
        try
        {
            using var keys = payload.DeriveDirectionKeys();
            using var authenticator = keys.CreateOutboundAuthenticator();
            var identity = AgentBootstrapInheritedChannel.GetCurrentProcessIdentity();
            var confirmation = AgentBootstrapConfirmationProtocol.CreateAgentConfirmation(
                payload.SessionId,
                bootstrapId,
                identity.ProcessId,
                identity.ProcessCreationFileTime,
                authenticator);
            AgentBootstrapConfirmationProtocol.WriteSingleFrame(
                confirmationOutput,
                confirmation);
            return 0;
        }
        finally
        {
            Array.Clear(bootstrapId, 0, bootstrapId.Length);
        }
    }

    private static async Task<int> RunDoctorAsync(string codexExecutable, AgentWorkspace workspace)
    {
        await using var client = CreateClient(codexExecutable, workspace);
        var initialized = await client.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
        WriteJson(new
        {
            ok = true,
            state = client.State.ToString(),
            workspace = workspace.Root,
            codexHome = initialized.CodexHome,
            platformFamily = initialized.PlatformFamily,
            platformOs = initialized.PlatformOs,
            userAgent = initialized.UserAgent,
            sandbox = new
            {
                mode = "workspace-write",
                approvals = "on-request",
                cadSessionApproval = false
            }
        });
        await client.StopAsync();
        return 0;
    }

    private static async Task<int> RunUntilCancelledAsync(string codexExecutable, AgentWorkspace workspace)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        await using var client = CreateClient(codexExecutable, workspace);
        var initialized = await client.StartAsync(shutdown.Token);
        WriteJson(new
        {
            ok = true,
            state = "ready",
            workspace = workspace.Root,
            platformFamily = initialized.PlatformFamily,
            userAgent = initialized.UserAgent
        });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }

        await client.StopAsync();
        return 0;
    }

    private static CodexAppServerClient CreateClient(string codexExecutable, AgentWorkspace workspace)
    {
        var options = new AppServerClientOptions
        {
            CodexExecutablePath = codexExecutable,
            WorkingDirectory = workspace.Work,
            MaximumFrameBytes = 8 * 1024 * 1024,
            MaximumJsonDepth = 32,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        var client = new CodexAppServerClient(options);
        client.StandardErrorReceived += (_, message) =>
            Console.Error.WriteLine("codex: " + message.Line);
        client.ProtocolFaulted += (_, fault) =>
            Console.Error.WriteLine("protocol: " + fault.Exception.Message);
        return client;
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int WriteUsageError(string command)
    {
        WriteJson(new
        {
            ok = false,
            error = "unknown_command",
            command,
            usage = "Codex.AutoCAD.AgentHost [doctor|run|bootstrap-doctor]"
        });
        return 2;
    }

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

}
