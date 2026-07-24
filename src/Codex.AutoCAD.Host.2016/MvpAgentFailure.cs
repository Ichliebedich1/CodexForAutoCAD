using System;
using Codex.AutoCAD.AgentLauncher;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentFailureStages
    {
        internal const string StartingAgentHost = "starting_agenthost";
        internal const string SendingTurn = "sending_turn";
        internal const string RunningTurn = "running_turn";
        internal const string CancellingTurn = "cancelling_turn";
        internal const string StartingConversation = "starting_conversation";
        internal const string ClearingConversation = "clearing_conversation";
        internal const string StoppingAgentHost = "stopping_agenthost";
        internal const string TerminatingAgentHost = "terminating_agenthost";
        internal const string AgentHostRuntime = "agenthost_runtime";
    }

    internal sealed class MvpAgentTurnException : Exception
    {
        internal MvpAgentTurnException(
            string requestId,
            string turnState,
            Exception innerException)
            : base("只读 Agent 回合失败；原始详情已隐藏。", innerException)
        {
            RequestId = requestId ?? string.Empty;
            TurnState = turnState ?? string.Empty;
        }

        internal string RequestId { get; private set; }

        internal string TurnState { get; private set; }
    }

    internal static class MvpAgentErrorCodes
    {
        internal const string AgentHostInvalidConfiguration = "agenthost_invalid_configuration";
        internal const string AgentHostProcessStartFailed = "agenthost_process_start_failed";
        internal const string AgentHostBootstrapWriteFailed = "agenthost_bootstrap_write_failed";
        internal const string AgentHostConfirmationInvalid = "agenthost_confirmation_invalid";
        internal const string AgentHostIdentityMismatch = "agenthost_identity_mismatch";
        internal const string AgentHostChildExited = "agenthost_child_exited";
        internal const string AgentHostTimeout = "agenthost_timeout";
        internal const string AgentHostCancelled = "agenthost_cancelled";
        internal const string AgentHostTerminationFailed = "agenthost_termination_failed";
        internal const string AgentHostProcessIsolationFailed = "agenthost_process_isolation_failed";
        internal const string AgentHostProcessLimitExceeded = "agenthost_process_limit_exceeded";
        internal const string AgentHostMemoryLimitExceeded = "agenthost_memory_limit_exceeded";
        internal const string AgentHostUserTimeLimitExceeded = "agenthost_user_time_limit_exceeded";
        internal const string AgentHostSessionRuntimeLimitExceeded =
            "agenthost_session_runtime_limit_exceeded";
        internal const string AgentHostCleanupFailed = "agenthost_cleanup_failed";
        internal const string AgentHostStopFailed = "agenthost_stop_failed";
        internal const string InvalidRequest = "invalid_request";
        internal const string InvalidState = "invalid_state";
        internal const string Cancelled = "cancelled";
        internal const string InternalError = "internal_error";
    }

    internal sealed class MvpAgentFailure
    {
        internal MvpAgentFailure(
            string errorCode,
            string errorStage,
            bool retryable,
            string userMessage,
            string requestId = null,
            string turnState = null)
        {
            ErrorCode = errorCode;
            ErrorStage = errorStage;
            Retryable = retryable;
            UserMessage = userMessage;
            RequestId = requestId ?? string.Empty;
            TurnState = turnState ?? string.Empty;
        }

        internal string ErrorCode { get; private set; }

        internal string ErrorStage { get; private set; }

        internal bool Retryable { get; private set; }

        internal string UserMessage { get; private set; }

        internal string RequestId { get; private set; }

        internal string TurnState { get; private set; }

        internal MvpAgentFailure WithRequest(string requestId, string turnState)
        {
            return new MvpAgentFailure(
                ErrorCode,
                ErrorStage,
                Retryable,
                UserMessage,
                requestId,
                turnState);
        }

        internal string FormatForUser(string operationName)
        {
            var operation = string.IsNullOrWhiteSpace(operationName)
                ? "Agent 操作"
                : operationName.Trim();
            return operation
                + "失败（error_code="
                + ErrorCode
                + ", error_stage="
                + ErrorStage
                + ", retryable="
                + (Retryable ? "true" : "false")
                + (string.IsNullOrEmpty(RequestId)
                    ? string.Empty
                    : ", request_id=" + RequestId)
                + (string.IsNullOrEmpty(TurnState)
                    ? string.Empty
                    : ", state=" + TurnState)
                + "）："
                + UserMessage;
        }
    }

    internal static class MvpAgentFailureFormatter
    {
        internal static MvpAgentFailure FromException(Exception exception, string errorStage)
        {
            if (exception is MvpAgentTurnException turn)
            {
                return FromException(turn.InnerException, errorStage)
                    .WithRequest(turn.RequestId, turn.TurnState);
            }

            if (exception is AgentBootstrapLaunchException bootstrap)
            {
                return FromBootstrapFailure(bootstrap.Failure, errorStage);
            }

            if (exception is AgentHostResourceLimitException resourceLimit)
            {
                return FromResourceLimitFailure(resourceLimit.Failure, errorStage);
            }

            if (exception is AgentBridgeClientException bridge)
            {
                return FromErrorCode(
                    NormalizeBridgeErrorCode(bridge),
                    errorStage);
            }

            if (exception is OperationCanceledException)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.Cancelled,
                    NormalizeStage(errorStage),
                    false,
                    "操作已取消；不会自动重试。");
            }

            if (exception is MvpAgentStopException)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.AgentHostStopFailed,
                    NormalizeStage(errorStage),
                    true,
                    "AgentHost 清理未完成；可再次执行停止命令重试剩余清理。");
            }

            if (exception is AggregateException)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.AgentHostCleanupFailed,
                    NormalizeStage(errorStage),
                    true,
                    "AgentHost 启动或清理未完整结束；请先执行停止命令回收资源。");
            }

            if (exception is TimeoutException)
            {
                return new MvpAgentFailure(
                    AgentBridgeErrorCodes.Timeout,
                    NormalizeStage(errorStage),
                    true,
                    "操作超时；连接或子进程已按 fail-closed 处理。");
            }

            if (exception is ArgumentException)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.InvalidRequest,
                    NormalizeStage(errorStage),
                    false,
                    "请求参数无效；请检查当前上下文和配置。");
            }

            if (exception is InvalidOperationException)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.InvalidState,
                    NormalizeStage(errorStage),
                    false,
                    "当前状态不允许执行该操作。");
            }

            return new MvpAgentFailure(
                MvpAgentErrorCodes.InternalError,
                NormalizeStage(errorStage),
                false,
                "内部操作失败；本地路径、令牌和原始异常详情已隐藏。");
        }

        internal static MvpAgentFailure FromErrorCode(string errorCode, string errorStage)
        {
            var code = NormalizeBridgeErrorCode(errorCode);
            if (IsResourceLimitErrorCode(code))
            {
                return new MvpAgentFailure(
                    code,
                    NormalizeStage(errorStage),
                    false,
                    "AgentHost 已触发受控资源限制；不会自动重试，请检查任务规模和管理员资源策略。");
            }

            var retryable = string.Equals(code, AgentBridgeErrorCodes.Offline, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.AgentUnavailable, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ConnectionLost, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Timeout, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Busy, StringComparison.Ordinal);
            var message = retryable
                ? "Agent 连接或服务暂不可用；请先停止并重新启动 AgentHost。"
                : "Agent 请求失败；原始 Provider 错误详情已隐藏。";
            return new MvpAgentFailure(
                code,
                NormalizeStage(errorStage),
                retryable,
                message);
        }

        internal static MvpAgentFailure FromResourceLimitFailure(
            AgentHostResourceLimitFailure failure,
            string errorStage)
        {
            if (failure == AgentHostResourceLimitFailure.None)
            {
                return new MvpAgentFailure(
                    MvpAgentErrorCodes.InternalError,
                    NormalizeStage(errorStage),
                    false,
                    "AgentHost 资源终态无效；原始详情已隐藏。");
            }

            return new MvpAgentFailure(
                AgentHostResourceLimitFailurePolicy.GetErrorCode(failure),
                NormalizeStage(errorStage),
                false,
                "AgentHost 已触发受控资源限制；不会自动重试，请检查任务规模和管理员资源策略。");
        }

        internal static string NormalizeBridgeErrorCode(AgentBridgeClientException exception)
        {
            var code = exception == null ? null : exception.Code;
            return IsKnownBridgeErrorCode(code)
                ? code
                : AgentBridgeErrorCodes.ConnectionLost;
        }

        internal static string NormalizeBridgeErrorCode(string errorCode)
        {
            return IsKnownBridgeErrorCode(errorCode)
                ? errorCode
                : AgentBridgeErrorCodes.InternalError;
        }

        private static MvpAgentFailure FromBootstrapFailure(
            AgentBootstrapLaunchFailure failure,
            string errorStage)
        {
            string code;
            bool retryable;
            switch (failure)
            {
                case AgentBootstrapLaunchFailure.InvalidConfiguration:
                    code = MvpAgentErrorCodes.AgentHostInvalidConfiguration;
                    retryable = false;
                    break;
                case AgentBootstrapLaunchFailure.ProcessStartFailed:
                    code = MvpAgentErrorCodes.AgentHostProcessStartFailed;
                    retryable = true;
                    break;
                case AgentBootstrapLaunchFailure.ProcessIsolationFailed:
                    code = MvpAgentErrorCodes.AgentHostProcessIsolationFailed;
                    retryable = false;
                    break;
                case AgentBootstrapLaunchFailure.BootstrapWriteFailed:
                    code = MvpAgentErrorCodes.AgentHostBootstrapWriteFailed;
                    retryable = true;
                    break;
                case AgentBootstrapLaunchFailure.ConfirmationInvalid:
                    code = MvpAgentErrorCodes.AgentHostConfirmationInvalid;
                    retryable = false;
                    break;
                case AgentBootstrapLaunchFailure.IdentityMismatch:
                    code = MvpAgentErrorCodes.AgentHostIdentityMismatch;
                    retryable = false;
                    break;
                case AgentBootstrapLaunchFailure.ChildExitedWithError:
                    code = MvpAgentErrorCodes.AgentHostChildExited;
                    retryable = true;
                    break;
                case AgentBootstrapLaunchFailure.Timeout:
                    code = MvpAgentErrorCodes.AgentHostTimeout;
                    retryable = true;
                    break;
                case AgentBootstrapLaunchFailure.Cancellation:
                    code = MvpAgentErrorCodes.AgentHostCancelled;
                    retryable = false;
                    break;
                case AgentBootstrapLaunchFailure.ChildTerminationFailed:
                    code = MvpAgentErrorCodes.AgentHostTerminationFailed;
                    retryable = true;
                    break;
                case AgentBootstrapLaunchFailure.InternalError:
                    code = MvpAgentErrorCodes.InternalError;
                    retryable = false;
                    break;
                default:
                    code = MvpAgentErrorCodes.InternalError;
                    retryable = false;
                    break;
            }

            var message = failure == AgentBootstrapLaunchFailure.InvalidConfiguration
                ? "检查 AgentHost 配置、候选包完整性和可执行文件哈希。"
                : "AgentHost 启动未完成；本地路径、标准错误和内部异常详情已隐藏。";
            return new MvpAgentFailure(
                code,
                NormalizeStage(errorStage),
                retryable,
                message);
        }

        private static string NormalizeStage(string errorStage)
        {
            return string.IsNullOrWhiteSpace(errorStage)
                ? MvpAgentFailureStages.RunningTurn
                : errorStage;
        }

        private static bool IsKnownBridgeErrorCode(string code)
        {
            return IsResourceLimitErrorCode(code)
                || string.Equals(code, AgentBridgeErrorCodes.Offline, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContractMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.AuthenticationFailed, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ReplayRejected, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.RequestInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContextInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ContextHashMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.AgentUnavailable, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ConnectionLost, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Timeout, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.Busy, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.TurnNotFound, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalInvalid, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalExpired, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ApprovalAlreadyConsumed, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.ResultIdentityMismatch, StringComparison.Ordinal)
                || string.Equals(code, AgentBridgeErrorCodes.InternalError, StringComparison.Ordinal);
        }

        private static bool IsResourceLimitErrorCode(string code)
        {
            return string.Equals(
                    code,
                    MvpAgentErrorCodes.AgentHostProcessLimitExceeded,
                    StringComparison.Ordinal)
                || string.Equals(
                    code,
                    MvpAgentErrorCodes.AgentHostMemoryLimitExceeded,
                    StringComparison.Ordinal)
                || string.Equals(
                    code,
                    MvpAgentErrorCodes.AgentHostUserTimeLimitExceeded,
                    StringComparison.Ordinal)
                || string.Equals(
                    code,
                    MvpAgentErrorCodes.AgentHostSessionRuntimeLimitExceeded,
                    StringComparison.Ordinal);
        }
    }
}
