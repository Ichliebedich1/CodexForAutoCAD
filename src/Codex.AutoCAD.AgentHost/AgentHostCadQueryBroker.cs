using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AgentRuntime;
using Codex.AutoCAD.Bridge;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.AgentHost;

/// <summary>
/// Carries a runtime drawing query back to the authenticated AutoCAD Host connection. It never
/// resolves document or index identity; the trusted Host owns and returns that binding.
/// </summary>
public sealed class AgentHostCadQueryBroker : IAgentCadDrawingQueryBroker
{
    private const int MaximumResponseJsonBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly object _sync = new();
    private Attachment? _attachment;

    internal IDisposable Attach(AuthenticatedPipeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_sync)
        {
            if (_attachment is not null)
            {
                throw new InvalidOperationException(
                    "AgentHost drawing-query broker already owns a Bridge connection.");
            }

            var attachment = new Attachment(connection);
            _attachment = attachment;
            return new AttachmentLease(this, attachment);
        }
    }

    public async ValueTask<AgentCadDrawingQueryResult> ExecuteAsync(
        AgentCadDrawingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Attachment attachment;
        CancellationToken attachmentToken;
        lock (_sync)
        {
            attachment = _attachment
                ?? throw new InvalidOperationException(
                    "No authenticated AutoCAD Host is attached for drawing queries.");
            attachment.ActiveExecutions++;
            attachmentToken = attachment.Cancellation.Token;
        }

        try
        {
            var request = new AgentDrawingQueryRequest
            {
                RequestId = query.RequestId,
                ThreadId = query.ThreadId,
                TurnId = query.TurnId,
                ToolCallId = query.CallId,
                QueryId = query.QueryId,
                Filter = query.Filter,
                PageSize = query.PageSize,
                Cursor = query.Cursor,
            };
            if (AgentBridgeContractValidator.Validate(request).Length != 0)
            {
                throw new InvalidDataException(
                    "Runtime drawing query violated the frozen Bridge contract.");
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                attachmentToken);
            var responseJson = await attachment.Connection.RequestAsync(
                    AgentBridgeMethods.QueryDrawing,
                    JsonSerializer.Serialize(request, SerializerOptions),
                    linkedCancellation.Token)
                .ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(responseJson) > MaximumResponseJsonBytes)
            {
                throw new InvalidDataException("Drawing query response exceeded the safe byte limit.");
            }

            AgentDrawingQueryResponse response;
            try
            {
                using var document = JsonDocument.Parse(responseJson, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
                EnsureNoDuplicateProperties(document.RootElement);
                response = JsonSerializer.Deserialize<AgentDrawingQueryResponse>(
                        responseJson,
                        SerializerOptions)
                    ?? throw new InvalidDataException("Drawing query response was null.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Drawing query response JSON was invalid.", exception);
            }

            if (AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response).Length != 0)
            {
                throw new InvalidDataException(
                    "Drawing query response identity or payload contract was invalid.");
            }

            lock (_sync)
            {
                if (!ReferenceEquals(_attachment, attachment) || attachment.Detached)
                {
                    throw new OperationCanceledException(
                        "Authenticated AutoCAD Host detached before the drawing query completed.",
                        linkedCancellation.Token);
                }
            }

            return new AgentCadDrawingQueryResult(
                response.ThreadId,
                response.TurnId,
                response.ToolCallId,
                response.QueryId,
                response.Query);
        }
        finally
        {
            ReleaseExecution(attachment);
        }
    }

    private void Detach(Attachment attachment)
    {
        var dispose = false;
        lock (_sync)
        {
            if (!ReferenceEquals(_attachment, attachment))
            {
                return;
            }

            _attachment = null;
            attachment.Detached = true;
            dispose = attachment.ActiveExecutions == 0;
        }

        TryCancel(attachment.Cancellation);
        if (dispose)
        {
            attachment.Cancellation.Dispose();
        }
    }

    private void ReleaseExecution(Attachment attachment)
    {
        var dispose = false;
        lock (_sync)
        {
            attachment.ActiveExecutions--;
            dispose = attachment.Detached && attachment.ActiveExecutions == 0;
        }

        if (dispose)
        {
            attachment.Cancellation.Dispose();
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException(
                            "Drawing query response contains a duplicate JSON property.");
                    }

                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    EnsureNoDuplicateProperties(item);
                }
                break;
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class Attachment(AuthenticatedPipeConnection connection)
    {
        public AuthenticatedPipeConnection Connection { get; } = connection;

        public CancellationTokenSource Cancellation { get; } = new();

        public int ActiveExecutions { get; set; }

        public bool Detached { get; set; }
    }

    private sealed class AttachmentLease(
        AgentHostCadQueryBroker owner,
        Attachment attachment) : IDisposable
    {
        private AgentHostCadQueryBroker? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Detach(attachment);
    }
}
