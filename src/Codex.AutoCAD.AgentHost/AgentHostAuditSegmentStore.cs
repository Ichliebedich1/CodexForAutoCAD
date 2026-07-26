namespace Codex.AutoCAD.AgentHost;

internal interface IAgentHostAuditSegmentStore : IDisposable
{
    Stream OpenSegment(string segmentId);
}

internal sealed class AgentHostAuditFileSegmentStore : IAgentHostAuditSegmentStore
{
    internal const int DefaultMaximumSegments = 64;
    internal const int AbsoluteMaximumSegments = 1024;

    private readonly string _directory;
    private readonly string _sessionId;
    private readonly int _maximumSegments;
    private int _openedSegments;
    private int _disposed;

    internal AgentHostAuditFileSegmentStore(
        string directory,
        string sessionId,
        int maximumSegments = DefaultMaximumSegments)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("Audit segment directory is invalid.", nameof(directory));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Audit session id is invalid.", nameof(sessionId));
        }

        if (maximumSegments is < 1 or > AbsoluteMaximumSegments)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        }

        _directory = directory;
        _sessionId = sessionId;
        _maximumSegments = maximumSegments;
    }

    public Stream OpenSegment(string segmentId)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var next = checked(_openedSegments + 1);
        if (next > _maximumSegments)
        {
            throw new AgentHostAuditException("AgentHost audit segment capacity is exhausted.");
        }

        var expected = AgentHostAuditLog.FormatSegmentId(next);
        if (!string.Equals(segmentId, expected, StringComparison.Ordinal))
        {
            throw new AgentHostAuditException("AgentHost audit segment sequence is invalid.");
        }

        var path = Path.Combine(
            _directory,
            _sessionId + "." + segmentId + ".jsonl");
        try
        {
            var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan | FileOptions.WriteThrough);
            _openedSegments = next;
            return stream;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostAuditException(
                "AgentHost audit segment could not be created safely.",
                exception);
        }
    }

    public void Dispose()
    {
        _disposed = 1;
    }
}
