using System.Globalization;
using System.Text.Json;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
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
        catch (CodexVersionPreflightException exception)
        {
            WriteJson(new
            {
                ok = false,
                command,
                error = "codex_version_preflight",
                errorCode = exception.Failure.ToString()
            });
            return 1;
        }
        catch (AgentHostCodexHealthException exception)
        {
            WriteJson(new
            {
                ok = false,
                command,
                error = "codex_appserver_health",
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
                var workspaceRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenAI",
                    "CodexForAutoCAD",
                    "workspace",
                    "sessions",
                    directionKeys!.SessionId);
                var workspace = AgentWorkspace.Create(workspaceRoot);
                var codexConfiguration = CreateCodexConfiguration(null, workspace);
                using var verifiedLaunch = await CodexVersionPreflight.VerifyAsync(
                        codexConfiguration,
                        shutdown.Token)
                    .ConfigureAwait(false);
                var cadQueryBroker = new AgentHostCadQueryBroker();
                await using (var runtime = new CodexAgentRuntime(
                    verifiedLaunch.CreateClientOptions(),
                    new AgentRuntimeOptions
                    {
                        Sandbox = AgentSandboxMode.ReadOnly,
                        ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                        ApprovalsReviewer = AgentApprovalsReviewer.User,
                        WorkingDirectory = codexConfiguration.WorkingDirectory,
                        ManagedWorkspaceRoot = workspace.Root,
                        MaximumPromptCharacters = 320 * 1024,
                    },
                    cadDrawingQueryBroker: cadQueryBroker))
                {
                    await AgentHostCodexHealthCheck.StartAsync(
                            runtime.StartAsync,
                            codexConfiguration.StartupTimeout,
                            shutdown.Token)
                        .ConfigureAwait(false);
                    var session = new AgentHostBridgeSession(
                        runtime,
                        "agenthost-" + directionKeys.SessionId,
                        audit,
                        cadQueryBroker);
                    await session.RunAsync(directionKeys, shutdown.Token).ConfigureAwait(false);
                }

                audit.Complete();
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
        using var verifiedLaunch = await CodexVersionPreflight.VerifyAsync(codexConfiguration)
            .ConfigureAwait(false);
        await using var client = CreateClient(verifiedLaunch);
        var initialized = await AgentHostCodexHealthCheck.StartAsync(
                client.StartAsync,
                codexConfiguration.StartupTimeout,
                CancellationToken.None)
            .ConfigureAwait(false);
        WriteJson(new
        {
            ok = true,
            state = client.State.ToString(),
            workspaceReady = true,
            codexExecutableSource = codexConfiguration.ExecutableSource.ToString(),
            codexVersion = verifiedLaunch.Version.Version.ToString(),
            codexVersionCompatibility = verifiedLaunch.Version.Compatibility.ToString(),
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

        using var verifiedLaunch = await CodexVersionPreflight.VerifyAsync(
                codexConfiguration,
                shutdown.Token)
            .ConfigureAwait(false);
        await using var client = CreateClient(verifiedLaunch);
        var initialized = await AgentHostCodexHealthCheck.StartAsync(
                client.StartAsync,
                codexConfiguration.StartupTimeout,
                shutdown.Token)
            .ConfigureAwait(false);
        WriteJson(new
        {
            ok = true,
            state = "ready",
            workspaceReady = true,
            codexExecutableSource = codexConfiguration.ExecutableSource.ToString(),
            codexVersion = verifiedLaunch.Version.Version.ToString(),
            codexVersionCompatibility = verifiedLaunch.Version.Compatibility.ToString(),
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

    private static CodexAppServerClient CreateClient(CodexVerifiedLaunch verifiedLaunch)
    {
        var client = new CodexAppServerClient(verifiedLaunch.CreateClientOptions());
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
