using System.Text.Json;
using Codex.AutoCAD.AppServer.Protocol;
using DiagnosticDataClassification = Codex.AutoCAD.Contracts.DiagnosticDataClassification;
using DiagnosticSanitizer = Codex.AutoCAD.Contracts.DiagnosticSanitizer;

namespace Codex.AutoCAD.AgentRuntime;

internal static class AgentEventProjector
{
    public static AgentEvent? Project(AppServerNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            return notification.Method switch
            {
                "item/agentMessage/delta" => ProjectAgentMessageDelta(notification),
                "item/started" => ProjectItem(notification, AgentItemLifecycle.Started, "startedAtMs"),
                "item/completed" => ProjectItem(notification, AgentItemLifecycle.Completed, "completedAtMs"),
                "item/commandExecution/outputDelta" => ProjectToolProgress(
                    notification,
                    AgentToolKind.CommandExecution,
                    "delta"),
                "item/mcpToolCall/progress" => ProjectToolProgress(
                    notification,
                    AgentToolKind.McpToolCall,
                    "message"),
                "item/fileChange/patchUpdated" => ProjectFileChangeProgress(notification),
                "item/autoApprovalReview/started" => ProjectApprovalReview(
                    notification,
                    AgentApprovalReviewLifecycle.Started,
                    "startedAtMs"),
                "item/autoApprovalReview/completed" => ProjectApprovalReview(
                    notification,
                    AgentApprovalReviewLifecycle.Completed,
                    "completedAtMs"),
                "turn/started" or "turn/completed" => ProjectTurn(notification),
                _ => null,
            };
        }
        catch (AgentEventProjectionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new AgentEventProjectionException(notification.Method, exception.Message, exception);
        }
    }

    private static AgentMessageDeltaEvent ProjectAgentMessageDelta(AppServerNotification notification)
    {
        var parameters = RequiredParams(notification);
        return new AgentMessageDeltaEvent(
            RequiredString(parameters, "threadId", notification.Method),
            RequiredString(parameters, "turnId", notification.Method),
            RequiredString(parameters, "itemId", notification.Method),
            RequiredString(parameters, "delta", notification.Method));
    }

    private static AgentEvent ProjectItem(
        AppServerNotification notification,
        AgentItemLifecycle lifecycle,
        string timestampProperty)
    {
        var parameters = RequiredParams(notification);
        var threadId = RequiredString(parameters, "threadId", notification.Method);
        var turnId = RequiredString(parameters, "turnId", notification.Method);
        var occurredAtMs = RequiredInt64(parameters, timestampProperty, notification.Method);
        var item = RequiredObject(parameters, "item", notification.Method);
        var wireType = RequiredString(item, "type", notification.Method);
        var snapshot = new AgentItemSnapshot(
            RequiredString(item, "id", notification.Method),
            ToItemKind(wireType),
            wireType,
            OptionalString(item, "status"),
            GetDisplayName(item, wireType),
            item.Clone());

        var toolKind = ToToolKind(wireType);
        if (toolKind != AgentToolKind.Unknown)
        {
            return new AgentToolStateChangedEvent(
                threadId,
                turnId,
                lifecycle,
                occurredAtMs,
                toolKind,
                ToToolStatus(snapshot.Status),
                snapshot);
        }

        return new AgentItemStateChangedEvent(threadId, turnId, lifecycle, occurredAtMs, snapshot);
    }

    private static AgentToolProgressEvent ProjectToolProgress(
        AppServerNotification notification,
        AgentToolKind toolKind,
        string messageProperty)
    {
        var parameters = RequiredParams(notification);
        return new AgentToolProgressEvent(
            RequiredString(parameters, "threadId", notification.Method),
            RequiredString(parameters, "turnId", notification.Method),
            RequiredString(parameters, "itemId", notification.Method),
            toolKind,
            RequiredString(parameters, messageProperty, notification.Method));
    }

    private static AgentToolProgressEvent ProjectFileChangeProgress(AppServerNotification notification)
    {
        var parameters = RequiredParams(notification);
        var changes = RequiredArray(parameters, "changes", notification.Method);
        return new AgentToolProgressEvent(
            RequiredString(parameters, "threadId", notification.Method),
            RequiredString(parameters, "turnId", notification.Method),
            RequiredString(parameters, "itemId", notification.Method),
            AgentToolKind.FileChange,
            "patchUpdated",
            changes.Clone());
    }

    private static AgentApprovalReviewStateChangedEvent ProjectApprovalReview(
        AppServerNotification notification,
        AgentApprovalReviewLifecycle lifecycle,
        string timestampProperty)
    {
        var parameters = RequiredParams(notification);
        var review = RequiredObject(parameters, "review", notification.Method);
        return new AgentApprovalReviewStateChangedEvent(
            RequiredString(parameters, "threadId", notification.Method),
            RequiredString(parameters, "turnId", notification.Method),
            RequiredString(parameters, "reviewId", notification.Method),
            OptionalString(parameters, "targetItemId"),
            lifecycle,
            RequiredInt64(parameters, timestampProperty, notification.Method),
            review.Clone());
    }

    private static AgentTurnStateChangedEvent ProjectTurn(AppServerNotification notification)
    {
        var parameters = RequiredParams(notification);
        var threadId = RequiredString(parameters, "threadId", notification.Method);
        var turn = RequiredObject(parameters, "turn", notification.Method);
        var turnId = RequiredString(turn, "id", notification.Method);
        var wireStatus = RequiredString(turn, "status", notification.Method);
        var status = ToTurnStatus(wireStatus);
        string? errorMessage = null;
        if (turn.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
        {
            errorMessage = OptionalString(error, "message");
        }

        var errorDiagnostic = errorMessage is null
            ? null
            : DiagnosticSanitizer
                .SanitizeText(
                    DiagnosticDataClassification.RemoteError,
                    errorMessage);
        var safeErrorMessage = errorDiagnostic?.SafeText;
        return new AgentTurnStateChangedEvent(
            threadId,
            turnId,
            status,
            safeErrorMessage,
            CreateSafeTurnSnapshot(turnId, wireStatus, safeErrorMessage))
        {
            ErrorDiagnosticClassification = errorDiagnostic?.Classification,
            ErrorDiagnosticRedactions = errorDiagnostic?.Redactions ?? default,
        };
    }

    private static JsonElement CreateSafeTurnSnapshot(
        string turnId,
        string status,
        string? errorMessage)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["id"] = turnId,
            ["status"] = status,
        };
        if (errorMessage is not null)
        {
            snapshot["error"] = new Dictionary<string, string>
            {
                ["message"] = errorMessage,
            };
        }

        return JsonSerializer.SerializeToElement(snapshot);
    }

    private static JsonElement RequiredParams(AppServerNotification notification)
    {
        if (notification.Params is not { ValueKind: JsonValueKind.Object } parameters)
        {
            throw new AgentEventProjectionException(notification.Method, "params must be an object.");
        }

        return parameters;
    }

    private static JsonElement RequiredObject(JsonElement parent, string property, string method)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentEventProjectionException(method, $"'{property}' must be an object.");
        }

        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string property, string method)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new AgentEventProjectionException(method, $"'{property}' must be an array.");
        }

        return value;
    }

    private static string RequiredString(JsonElement parent, string property, string method)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new AgentEventProjectionException(method, $"'{property}' must be a string.");
        }

        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement parent, string property, string method)
    {
        if (!parent.TryGetProperty(property, out var value) || !value.TryGetInt64(out var result))
        {
            throw new AgentEventProjectionException(method, $"'{property}' must be a 64-bit integer.");
        }

        return result;
    }

    private static string? OptionalString(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetDisplayName(JsonElement item, string wireType)
    {
        if (wireType == "mcpToolCall")
        {
            var server = OptionalString(item, "server");
            var tool = OptionalString(item, "tool");
            return server is not null && tool is not null ? $"{server}/{tool}" : tool ?? server;
        }

        return OptionalString(item, "tool")
            ?? OptionalString(item, "command")
            ?? OptionalString(item, "query")
            ?? OptionalString(item, "path");
    }

    internal static AgentTurnStatus ToTurnStatus(string? status) => status switch
    {
        "inProgress" => AgentTurnStatus.InProgress,
        "completed" => AgentTurnStatus.Completed,
        "interrupted" => AgentTurnStatus.Interrupted,
        "failed" => AgentTurnStatus.Failed,
        _ => AgentTurnStatus.Unknown,
    };

    private static AgentToolStatus ToToolStatus(string? status) => status switch
    {
        "inProgress" => AgentToolStatus.InProgress,
        "completed" => AgentToolStatus.Completed,
        "failed" => AgentToolStatus.Failed,
        "declined" => AgentToolStatus.Declined,
        _ => AgentToolStatus.Unknown,
    };

    private static AgentToolKind ToToolKind(string wireType) => wireType switch
    {
        "commandExecution" => AgentToolKind.CommandExecution,
        "fileChange" => AgentToolKind.FileChange,
        "mcpToolCall" => AgentToolKind.McpToolCall,
        "dynamicToolCall" => AgentToolKind.DynamicToolCall,
        "collabAgentToolCall" => AgentToolKind.Collaboration,
        "webSearch" => AgentToolKind.WebSearch,
        "imageGeneration" => AgentToolKind.ImageGeneration,
        _ => AgentToolKind.Unknown,
    };

    private static AgentItemKind ToItemKind(string wireType) => wireType switch
    {
        "userMessage" => AgentItemKind.UserMessage,
        "agentMessage" => AgentItemKind.AgentMessage,
        "plan" => AgentItemKind.Plan,
        "reasoning" => AgentItemKind.Reasoning,
        "commandExecution" => AgentItemKind.CommandExecution,
        "fileChange" => AgentItemKind.FileChange,
        "mcpToolCall" => AgentItemKind.McpToolCall,
        "dynamicToolCall" => AgentItemKind.DynamicToolCall,
        "collabAgentToolCall" => AgentItemKind.CollaborationToolCall,
        "webSearch" => AgentItemKind.WebSearch,
        "imageView" => AgentItemKind.ImageView,
        "imageGeneration" => AgentItemKind.ImageGeneration,
        "subAgentActivity" => AgentItemKind.SubAgentActivity,
        "hookPrompt" => AgentItemKind.HookPrompt,
        "sleep" => AgentItemKind.Sleep,
        "contextCompaction" => AgentItemKind.ContextCompaction,
        "enteredReviewMode" => AgentItemKind.EnteredReviewMode,
        "exitedReviewMode" => AgentItemKind.ExitedReviewMode,
        _ => AgentItemKind.Unknown,
    };
}
