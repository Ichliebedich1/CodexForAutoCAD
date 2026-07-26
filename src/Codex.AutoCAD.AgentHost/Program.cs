using System.Globalization;
using System.Text.Json;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AppServer.Protocol;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

var exitCode = await AgentHostProgram.RunAsync(args);
return exitCode;

internal static class AgentHostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "doctor";
        if (command is "audit-export" or "audit-retention-plan" or "audit-retention-apply")
        {
            return RunAuditCliCommand(
                command,
                () => command switch
                {
                    "audit-export" => RunAuditExport(args),
                    "audit-retention-plan" => RunAuditRetentionPlan(args),
                    "audit-retention-apply" => RunAuditRetentionApply(args),
                    _ => throw new InvalidOperationException(
                        "The AgentHost audit command dispatcher reached an invalid state."),
                });
        }

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
                    FormatBootstrapFailureForStandardError(command, exception));
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
            return WriteCliFailure(
                command,
                "codex_configuration",
                exception.Failure.ToString(),
                exception.DiagnosticClassification,
                exception);
        }
        catch (CodexVersionPreflightException exception)
        {
            return WriteCliFailure(
                command,
                "codex_version_preflight",
                exception.Failure.ToString(),
                exception.DiagnosticClassification,
                exception);
        }
        catch (AgentHostCodexHealthException exception)
        {
            return WriteCliFailure(
                command,
                "codex_appserver_health",
                exception.Failure.ToString(),
                DiagnosticDataClassification.RemoteError,
                exception);
        }
        catch (Exception exception)
        {
            return WriteCliFailure(
                command,
                "agenthost_cli_failure",
                "agenthost_internal_error",
                ClassifyCliException(exception),
                exception);
        }
    }

    private static int RunAuditExport(string[] args)
    {
        if (args.Length != 3
            || !string.Equals(args[1], "--session", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine("audit-export: invalid_arguments");
            return 2;
        }

        try
        {
            AgentHostAuditExportService.ExportCurrentUserSessionToStandardOutput(args[2]);
            return 0;
        }
        catch (AgentHostAuditCatalogException)
        {
            Console.Error.WriteLine(
                "audit-export: " + AgentHostAuditExportService.RejectedErrorCode);
            return 1;
        }
        catch (AgentHostAuditException)
        {
            Console.Error.WriteLine(
                "audit-export: " + AgentHostAuditExportService.FailedErrorCode);
            return 1;
        }
        catch (AgentHostAuditIntegrityException)
        {
            Console.Error.WriteLine(
                "audit-export: " + AgentHostAuditExportService.FailedErrorCode);
            return 1;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            Console.Error.WriteLine(
                "audit-export: " + AgentHostAuditExportService.FailedErrorCode);
            return 1;
        }
    }

    private static int RunAuditRetentionPlan(string[] args)
    {
        if (!TryParseAuditRetentionPolicy(args, out var policy))
        {
            Console.Error.WriteLine("audit-retention-plan: invalid_arguments");
            return 2;
        }

        try
        {
            var plan = AgentHostAuditRetentionPlanner.CreateCurrentUserPlan(
                policy,
                DateTimeOffset.UtcNow);
            WriteJson(plan);
            return 0;
        }
        catch (AgentHostAuditCatalogException)
        {
            Console.Error.WriteLine("audit-retention-plan: audit_retention_rejected");
            return 1;
        }
        catch (OverflowException)
        {
            Console.Error.WriteLine("audit-retention-plan: audit_retention_rejected");
            return 1;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            Console.Error.WriteLine("audit-retention-plan: audit_retention_failed");
            return 1;
        }
    }

    private static int RunAuditRetentionApply(string[] args)
    {
        if (!TryParseAuditRetentionApplyArguments(
                args,
                out var policy,
                out var expectedPlanId))
        {
            Console.Error.WriteLine("audit-retention-apply: invalid_arguments");
            return 2;
        }

        try
        {
            var result = AgentHostAuditRetentionExecutor.ApplyCurrentUserPlan(
                policy,
                expectedPlanId,
                DateTimeOffset.UtcNow);
            WriteJson(result);
            return 0;
        }
        catch (AgentHostAuditRetentionExecutionException exception)
        {
            Console.Error.WriteLine(
                "audit-retention-apply: audit_retention_rejected/"
                + exception.ReasonCode);
            return 1;
        }
        catch (AgentHostAuditCatalogException)
        {
            Console.Error.WriteLine("audit-retention-apply: audit_retention_rejected");
            return 1;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or OverflowException
            or System.Security.SecurityException)
        {
            Console.Error.WriteLine("audit-retention-apply: audit_retention_failed");
            return 1;
        }
    }

    internal static bool TryParseAuditRetentionApplyArguments(
        string[] args,
        out AgentHostAuditRetentionPolicy policy,
        out string expectedPlanId)
    {
        policy = new AgentHostAuditRetentionPolicy();
        expectedPlanId = string.Empty;
        if (args.Length != 9)
        {
            return false;
        }

        var policyArguments = new List<string>(7) { args[0] };
        for (var index = 1; index < args.Length; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (string.Equals(option, "--plan", StringComparison.Ordinal))
            {
                if (expectedPlanId.Length != 0 || !IsLowerHex(value, 64))
                {
                    return false;
                }

                expectedPlanId = value;
                continue;
            }

            policyArguments.Add(option);
            policyArguments.Add(value);
        }

        return expectedPlanId.Length != 0
            && TryParseAuditRetentionPolicy(policyArguments.ToArray(), out policy);
    }

    internal static bool TryParseAuditRetentionPolicy(
        string[] args,
        out AgentHostAuditRetentionPolicy policy)
    {
        policy = new AgentHostAuditRetentionPolicy();
        if (args.Length != 7)
        {
            return false;
        }

        int? olderThanDays = null;
        long? maximumStoreBytes = null;
        int? retainComplete = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (string.Equals(option, "--older-than-days", StringComparison.Ordinal))
            {
                if (olderThanDays.HasValue
                    || !int.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedDays))
                {
                    return false;
                }

                olderThanDays = parsedDays;
                continue;
            }

            if (string.Equals(option, "--max-store-mib", StringComparison.Ordinal))
            {
                if (maximumStoreBytes.HasValue
                    || !long.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedMib))
                {
                    return false;
                }

                try
                {
                    maximumStoreBytes = checked(parsedMib * 1024L * 1024L);
                }
                catch (OverflowException)
                {
                    return false;
                }

                continue;
            }

            if (string.Equals(option, "--retain-complete", StringComparison.Ordinal))
            {
                if (retainComplete.HasValue
                    || !int.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedRetainComplete))
                {
                    return false;
                }

                retainComplete = parsedRetainComplete;
                continue;
            }

            return false;
        }

        if (!olderThanDays.HasValue
            || !maximumStoreBytes.HasValue
            || !retainComplete.HasValue)
        {
            return false;
        }

        policy = new AgentHostAuditRetentionPolicy
        {
            OlderThanDays = olderThanDays.Value,
            MaximumStoreBytes = maximumStoreBytes.Value,
            MinimumCompleteSessionsToRetain = retainComplete.Value,
        };
        try
        {
            policy.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
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
        CodexSessionHomeLease? sessionHome = null;
        AgentWorkspace? workspace = null;
        CodexLocalAppServerConfiguration? codexConfiguration = null;
        AgentBootstrapInheritedChannel.ClearStandardErrorInheritance();
        try
        {
        using (var bootstrapInput = AgentBootstrapInheritedChannel.OpenStandardInput())
        using (var confirmationOutput = AgentBootstrapInheritedChannel.OpenStandardOutput())
        using (var payload = AgentBootstrapInheritedChannel.ReadSingleBootstrapPacket(
            bootstrapInput))
        {
            var bootstrapId = payload.CopyBootstrapId();
            try
            {
                directionKeys = payload.DeriveDirectionKeys();
                var identity = AgentBootstrapInheritedChannel.GetCurrentProcessIdentity();
                var sessionRoot = GetSessionRoot(payload.SessionId);
                var workspaceRoot = Path.Combine(sessionRoot, "workspace");
                Directory.CreateDirectory(sessionRoot);
                workspace = AgentWorkspace.Create(workspaceRoot);
                sessionHome = CodexSessionHomeLease.Create(sessionRoot, payload.SessionId);
                codexConfiguration = CreateCodexConfiguration(
                    null,
                    workspace,
                    sessionHome.HomePath);
                using (var credentialGuard =
                    directionKeys.CreateConfirmationInboundGuard())
                using (var credentialDelivery =
                    await AgentCredentialPipeClient.ReceiveAsync(
                            payload.PipeName,
                            payload.SessionId,
                            bootstrapId,
                            identity.ProcessId,
                            identity.ProcessCreationFileTime,
                            credentialGuard,
                            TimeSpan.FromSeconds(10),
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    if (credentialDelivery.Mode == AgentCredentialDeliveryMode.AccessToken)
                    {
                        if (credentialDelivery.Secret is null)
                        {
                            throw new AgentBootstrapLaunchException(
                                AgentBootstrapLaunchFailure.CredentialUnavailable,
                                "Access-token credential payload is unavailable.");
                        }

                        await CodexCredentialLogin.LoginAsync(
                                codexConfiguration,
                                sessionHome.HomePath,
                                credentialDelivery.Secret,
                                codexConfiguration.StartupTimeout,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                using var authenticator = directionKeys.CreateConfirmationOutboundAuthenticator();
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
            var sessionRoot = GetSessionRoot(directionKeys!.SessionId);
            var workspaceRoot = Path.Combine(sessionRoot, "workspace");
            await using var audit = AgentHostAuditLog.CreateForCurrentUser(
                directionKeys.SessionId);
            try
            {
                using var verifiedLaunch = await CodexVersionPreflight.VerifyAsync(
                        codexConfiguration!,
                        shutdown.Token)
                    .ConfigureAwait(false);

                // M4.1：在启动 Agent 运行时之前加载分层策略。
                // 三个配置文件都不存在表示管理员尚未部署策略，此时不启用白名单，
                // 但 CodexAgentRuntime 仍强制模型标识的安全形态校验；
                // 只要有任一层存在却不可用（路径越界、超限、损坏、越权扩大或锁定冲突），
                // 就必须 fail-closed 拒绝启动，绝不静默降级为"无白名单"。
                var policyLoad = AgentHostPolicyStore.Load();
                if (!policyLoad.Accepted &&
                    !string.Equals(
                        policyLoad.ErrorCode,
                        AgentPolicyErrorCodes.NoEffectiveLayer,
                        StringComparison.Ordinal))
                {
                    throw new AgentHostPolicyConfigurationException(
                        policyLoad.ErrorCode, policyLoad.ErrorLayer);
                }

                var cadQueryBroker = new AgentHostCadQueryBroker();
                await using (var runtime = new CodexAgentRuntime(
                    verifiedLaunch.CreateClientOptions(),
                    new AgentRuntimeOptions
                    {
                        Sandbox = AgentSandboxMode.ReadOnly,
                        ApprovalPolicy = AgentApprovalPolicy.OnRequest,
                        ApprovalsReviewer = AgentApprovalsReviewer.User,
                        WorkingDirectory = codexConfiguration!.WorkingDirectory,
                        ManagedWorkspaceRoot = workspace!.Root,
                        MaximumPromptCharacters = 320 * 1024,
                        AgentPolicy = policyLoad.Accepted ? policyLoad.Policy : null,
                    },
                    cadDrawingQueryBroker: cadQueryBroker))
                {
                    await AgentHostCodexHealthCheck.StartAsync(
                            runtime.StartAsync,
                            codexConfiguration!.StartupTimeout,
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
        finally
        {
            sessionHome?.Dispose();
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
        WriteJson(AgentHostPublicStatus.CreateDoctor(
            client.State,
            codexConfiguration.ExecutableSource,
            verifiedLaunch.Version,
            initialized));
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
        WriteJson(AgentHostPublicStatus.CreateReady(
            codexConfiguration.ExecutableSource,
            verifiedLaunch.Version,
            initialized));

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
            Console.Error.WriteLine(FormatProtocolFaultForStandardError(fault));
        return client;
    }

    internal static int RunAuditCliCommand(string command, Func<int> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        try
        {
            return execute();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                FormatAuditFailureForStandardError(command, exception));
            return 1;
        }
    }

    internal static string FormatProtocolFaultForStandardError(
        AppServerProtocolFaultEventArgs fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return "protocol: appserver_protocol_fault; diagnosticClassification="
            + fault.DiagnosticClassification
            + "; diagnosticRedactions="
            + ((int)fault.DiagnosticRedactions).ToString(CultureInfo.InvariantCulture);
    }

    internal static string FormatBootstrapFailureForStandardError(
        string command,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(exception);
        var classification = ClassifyCliException(exception);
        var diagnostic = DiagnosticSanitizer.SanitizeException(
            classification,
            exception);
        var redactions = diagnostic.Redactions;
        if (exception is AgentBootstrapLaunchException bootstrapException)
        {
            redactions |= bootstrapException.DiagnosticRedactions;
        }

        var errorCode = exception switch
        {
            ArgumentException => "invalid_arguments",
            AgentBootstrapLaunchException bootstrapFailure
                => bootstrapFailure.ErrorCode,
            _ => "agenthost_bootstrap_failed",
        };
        return command
            + ": agenthost_bootstrap_failure; errorCode="
            + errorCode
            + "; errorStage=agenthost_bootstrap; diagnosticClassification="
            + diagnostic.Classification
            + "; diagnosticRedactions="
            + ((int)redactions).ToString(CultureInfo.InvariantCulture);
    }

    internal static string FormatAuditFailureForStandardError(
        string command,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var commandAndErrorCode = command switch
        {
            "audit-export" => ("audit-export", "audit_export_failed"),
            "audit-retention-plan" => ("audit-retention-plan", "audit_retention_failed"),
            "audit-retention-apply" => ("audit-retention-apply", "audit_retention_failed"),
            _ => ("audit", "agenthost_audit_failed"),
        };
        var classification = ClassifyCliException(exception);
        var diagnostic = DiagnosticSanitizer.SanitizeException(
            classification,
            exception);
        var redactions = diagnostic.Redactions;
        if (exception is AppServerException appServerException)
        {
            redactions |= appServerException.DiagnosticRedactions;
        }
        else if (exception is AgentBootstrapLaunchException bootstrapException)
        {
            redactions |= bootstrapException.DiagnosticRedactions;
        }

        return commandAndErrorCode.Item1
            + ": agenthost_audit_failure; errorCode="
            + commandAndErrorCode.Item2
            + "; errorStage=agenthost_audit; diagnosticClassification="
            + diagnostic.Classification
            + "; diagnosticRedactions="
            + ((int)redactions).ToString(CultureInfo.InvariantCulture);
    }

    private static CodexLocalAppServerConfiguration CreateCodexConfiguration(
        string? commandLineExecutablePath,
        AgentWorkspace workspace,
        string? codexHomeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return CodexLocalAppServerConfigurationResolver.ResolveForCurrentProcess(
            commandLineExecutablePath,
            workspace.Work,
            workspace.Temp,
            codexHomeDirectory);
    }

    private static string GetSessionRoot(string sessionId)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "CodexForAutoCAD",
            "workspace",
            "sessions",
            sessionId);
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
        var diagnostic = DiagnosticSanitizer.SanitizeText(
            DiagnosticDataClassification.Configuration,
            command);
        WriteJson(new
        {
            ok = false,
            error = "unknown_command",
            command = diagnostic.SafeText,
            diagnosticClassification = diagnostic.Classification.ToString(),
            diagnosticRedactions = (int)diagnostic.Redactions,
            usage = "Codex.AutoCAD.AgentHost [doctor|run|audit-export --session <id>|audit-retention-plan --older-than-days <1..3650> --max-store-mib <1..1048576> --retain-complete <0..4096>|audit-retention-apply --plan <64-lower-hex> --older-than-days <1..3650> --max-store-mib <1..1048576> --retain-complete <0..4096>|bootstrap-doctor|bootstrap-serve]"
        });
        return 2;
    }

    private static int WriteCliFailure(
        string command,
        string error,
        string errorCode,
        DiagnosticDataClassification classification,
        Exception exception)
    {
        var diagnostic = DiagnosticSanitizer.SanitizeException(
            classification,
            exception);
        var redactions = diagnostic.Redactions;
        if (exception is AppServerException appServerException)
        {
            redactions |= appServerException.DiagnosticRedactions;
        }

        WriteJson(new
        {
            ok = false,
            command,
            error,
            errorCode,
            errorStage = "agenthost_cli",
            diagnosticClassification = diagnostic.Classification.ToString(),
            diagnosticRedactions = (int)redactions,
        });
        return 1;
    }

    private static DiagnosticDataClassification ClassifyCliException(Exception exception)
        => exception switch
        {
            ArgumentException => DiagnosticDataClassification.Configuration,
            IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException
                => DiagnosticDataClassification.Environment,
            AppServerException appServerException
                => appServerException.DiagnosticClassification,
            AgentBootstrapLaunchException bootstrapException
                => bootstrapException.DiagnosticClassification,
            AgentHostAuditException auditException
                when IsEnvironmentCliException(auditException.InnerException)
                => DiagnosticDataClassification.Environment,
            AgentHostAuditRetentionExecutionException retentionException
                when IsEnvironmentCliException(retentionException.InnerException)
                => DiagnosticDataClassification.Environment,
            _ => DiagnosticDataClassification.Exception,
        };

    private static bool IsEnvironmentCliException(Exception? exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException;

    private static bool IsLowerHex(string? value, int length)
        => value != null
            && value.Length == length
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

}
