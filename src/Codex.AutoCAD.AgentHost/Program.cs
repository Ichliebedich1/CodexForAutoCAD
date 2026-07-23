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

        if (command is not ("doctor" or "run"))
        {
            return WriteUsageError(command);
        }

        var workspacePath = GetOption(args, "--workspace")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "CodexForAutoCAD",
                "workspace",
                command);

        try
        {
            var workspace = AgentWorkspace.Create(workspacePath);
            var codexConfiguration = CreateCodexConfiguration(
                GetOption(args, "--codex"),
                workspace);
            return command == "doctor"
                ? await RunDoctorAsync(codexConfiguration)
                : await RunUntilCancelledAsync(codexConfiguration);
        }
        catch (CodexLocalConfigurationException exception)
        {
            WriteJson(new
            {
                ok = false,
                command,
                error = "codex_configuration",
                errorCode = exception.Failure.ToString()
            });
            return 1;
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
            await using var audit = AgentHostAuditLog.CreateForCurrentUser(
                directionKeys!.SessionId);
            try
            {
                var sessionsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenAI",
                    "CodexForAutoCAD",
                    "workspace",
                    "sessions");
                using var workspace = AgentWorkspace.CreateSession(
                    sessionsRoot,
                    directionKeys!.SessionId);
                var codexConfiguration = CreateCodexConfiguration(null, workspace);
                var cadQueryBroker = new AgentHostCadQueryBroker();
                await using var runtime = new CodexAgentRuntime(
                    codexConfiguration.CreateClientOptions(),
                    new AgentRuntimeOptions
                    {
                        Sandbox = AgentSandboxMode.ReadOnly,
                        ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                        ApprovalsReviewer = AgentApprovalsReviewer.User,
                        WorkingDirectory = codexConfiguration.WorkingDirectory,
                        ManagedWorkspaceRoot = workspace.Root,
                        MaximumPromptCharacters = 320 * 1024,
                    },
                    cadDrawingQueryBroker: cadQueryBroker);
                var session = new AgentHostBridgeSession(
                    runtime,
                    "agenthost-" + directionKeys.SessionId,
                    audit,
                    cadQueryBroker);
                await session.RunAsync(directionKeys, shutdown.Token).ConfigureAwait(false);
                return 0;
            }
            catch (Exception exception)
            {
                try
                {
                    audit.Fail(AgentHostAuditErrorCodes.FromException(exception));
                }
                catch
                {
                }

                throw;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static async Task<int> RunDoctorAsync(CodexLocalAppServerConfiguration codexConfiguration)
    {
        await using var client = CreateClient(codexConfiguration);
        var initialized = await client.StartAsync().WaitAsync(codexConfiguration.StartupTimeout);
        WriteJson(new
        {
            ok = true,
            state = client.State.ToString(),
            workspaceReady = true,
            codexExecutableSource = codexConfiguration.ExecutableSource.ToString(),
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

    private static async Task<int> RunUntilCancelledAsync(CodexLocalAppServerConfiguration codexConfiguration)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        await using var client = CreateClient(codexConfiguration);
        var initialized = await client.StartAsync(shutdown.Token)
            .WaitAsync(codexConfiguration.StartupTimeout, shutdown.Token);
        WriteJson(new
        {
            ok = true,
            state = "ready",
            workspaceReady = true,
            codexExecutableSource = codexConfiguration.ExecutableSource.ToString(),
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

    private static CodexAppServerClient CreateClient(CodexLocalAppServerConfiguration codexConfiguration)
    {
        var client = new CodexAppServerClient(codexConfiguration.CreateClientOptions());
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

    private static CodexLocalAppServerConfiguration CreateCodexConfiguration(
        string? commandLineExecutablePath,
        AgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return CodexLocalAppServerConfigurationResolver.ResolveForCurrentProcess(
            commandLineExecutablePath,
            workspace.Work,
            workspace.Temp);
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
