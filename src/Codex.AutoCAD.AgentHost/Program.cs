using System.Globalization;
using System.Text.Json;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Ipc;

var exitCode = await AgentHostProgram.RunAsync(args);
return exitCode;

internal static class AgentHostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "doctor";
        if (command is "bootstrap-doctor" or "bootstrap-serve")
        {
            try
            {
                return command == "bootstrap-doctor"
                    ? RunBootstrapDoctor(args)
                    : await RunBootstrapServeAsync(args).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    command
                    + ": "
                    + exception.GetType().Name);
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
        var codexExecutable = ResolveCodexExecutablePath(GetOption(args, "--codex"));

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
                error = exception.GetType().Name
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
            using var authenticator = keys.CreateConfirmationOutboundAuthenticator();
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

    private static async Task<int> RunBootstrapServeAsync(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException(
                "bootstrap-serve accepts no command-line bootstrap material.");
        }

        AgentBootstrapDirectionKeys? directionKeys = null;
        AgentBootstrapInheritedChannel.ClearStandardErrorInheritance();
        using (var bootstrapInput = AgentBootstrapInheritedChannel.OpenStandardInput())
        using (var confirmationOutput = AgentBootstrapInheritedChannel.OpenStandardOutput())
        using (var payload = AgentBootstrapInheritedChannel.ReadSingleBootstrapPacket(
            bootstrapInput))
        {
            var bootstrapId = payload.CopyBootstrapId();
            try
            {
                directionKeys = payload.DeriveDirectionKeys();
                using var authenticator = directionKeys.CreateConfirmationOutboundAuthenticator();
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
            }
            catch
            {
                directionKeys?.Dispose();
                directionKeys = null;
                throw;
            }
            finally
            {
                Array.Clear(bootstrapId, 0, bootstrapId.Length);
            }
        }

        using (directionKeys!)
        using (var shutdown = new CancellationTokenSource())
        {
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                var workspaceRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenAI",
                    "CodexForAutoCAD",
                    "workspace",
                    "sessions",
                    directionKeys!.SessionId);
                var workspace = AgentWorkspace.Create(workspaceRoot);
                var appServerOptions = new AppServerClientOptions
                {
                    CodexExecutablePath = ResolveCodexExecutablePath(null),
                    WorkingDirectory = workspace.Work,
                    MaximumFrameBytes = 8 * 1024 * 1024,
                    MaximumJsonDepth = 32,
                    ShutdownTimeout = TimeSpan.FromSeconds(5),
                };
                var cadQueryBroker = new AgentHostCadQueryBroker();
                await using var runtime = new CodexAgentRuntime(
                    appServerOptions,
                    new AgentRuntimeOptions
                    {
                        Sandbox = AgentSandboxMode.ReadOnly,
                        ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                        ApprovalsReviewer = AgentApprovalsReviewer.User,
                        WorkingDirectory = workspace.Work,
                        ManagedWorkspaceRoot = workspace.Root,
                        MaximumPromptCharacters = 320 * 1024,
                    },
                    cadDrawingQueryBroker: cadQueryBroker);
                var session = new AgentHostBridgeSession(
                    runtime,
                    "agenthost-" + directionKeys.SessionId,
                    cadQueryBroker);
                await session.RunAsync(directionKeys, shutdown.Token).ConfigureAwait(false);
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
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
            workspaceReady = true,
            codexHomeConfigured = !string.IsNullOrWhiteSpace(initialized.CodexHome),
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
            workspaceReady = true,
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
            Console.Error.WriteLine(
                "codex: stderrBytes="
                + message.Summary.Bytes.ToString(CultureInfo.InvariantCulture)
                + ", stderrTruncated="
                + (message.Summary.Truncated ? "true" : "false"));
        client.ProtocolFaulted += (_, fault) =>
            Console.Error.WriteLine("protocol: " + fault.Exception.GetType().Name);
        return client;
    }

    private static string ResolveCodexExecutablePath(string? commandLineValue)
    {
        var configured = string.IsNullOrWhiteSpace(commandLineValue)
            ? Environment.GetEnvironmentVariable("CODEX_EXECUTABLE")
            : commandLineValue;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().Trim('"');
        }

        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var applicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(applicationData))
            {
                var npmRoot = Path.Combine(applicationData, "npm", "node_modules", "@openai");
                var candidates = new[]
                {
                    Path.Combine(
                        npmRoot,
                        "codex",
                        "node_modules",
                        "@openai",
                        "codex-win32-x64",
                        "vendor",
                        "x86_64-pc-windows-msvc",
                        "bin",
                        "codex.exe"),
                    Path.Combine(
                        npmRoot,
                        "codex-win32-x64",
                        "vendor",
                        "x86_64-pc-windows-msvc",
                        "bin",
                        "codex.exe"),
                    Path.Combine(
                        npmRoot,
                        "codex",
                        "vendor",
                        "x86_64-pc-windows-msvc",
                        "bin",
                        "codex.exe"),
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                foreach (var pathEntry in pathValue.Split(
                             new[] { Path.PathSeparator },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    var directory = pathEntry.Trim().Trim('"');
                    if (directory.Length == 0 || !Path.IsPathRooted(directory))
                    {
                        continue;
                    }

                    try
                    {
                        var candidate = Path.Combine(directory, "codex.exe");
                        if (File.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException
                        or NotSupportedException
                        or PathTooLongException)
                    {
                    }
                }
            }
        }

        return "codex";
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
            usage = "Codex.AutoCAD.AgentHost [doctor|run|bootstrap-doctor|bootstrap-serve]"
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
