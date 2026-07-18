using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge;

public sealed class BridgeConnectionOptions
{
    public const int DefaultMaximumPendingRequests = 64;
    public const int DefaultMaximumPendingNotifications = 64;
    public const int DefaultMaximumActiveRequests = 32;
    public const int DefaultMaximumConcurrentHandlers = 64;
    public const int AbsoluteMaximumConcurrentEntries = 4 * 1024;
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AbsoluteMaximumShutdownTimeout = TimeSpan.FromMinutes(1);

    public int MaximumPendingRequests { get; init; } = DefaultMaximumPendingRequests;

    public int MaximumPendingNotifications { get; init; } = DefaultMaximumPendingNotifications;

    public int MaximumActiveRequests { get; init; } = DefaultMaximumActiveRequests;

    public int MaximumConcurrentHandlers { get; init; } = DefaultMaximumConcurrentHandlers;

    public int MaximumFrameBytes { get; init; } = ProtocolConstants.MaximumMessageBytes;

    public TimeSpan ShutdownTimeout { get; init; } = DefaultShutdownTimeout;

    public IpcSessionGuardOptions SessionGuard { get; init; } = new();

    internal void Validate()
    {
        ValidateEntryLimit(MaximumPendingRequests, nameof(MaximumPendingRequests));
        ValidateEntryLimit(MaximumPendingNotifications, nameof(MaximumPendingNotifications));
        ValidateEntryLimit(MaximumActiveRequests, nameof(MaximumActiveRequests));
        ValidateEntryLimit(MaximumConcurrentHandlers, nameof(MaximumConcurrentHandlers));
        if (MaximumConcurrentHandlers < MaximumActiveRequests)
        {
            throw new ArgumentException(
                "并发handler上限不能小于入站活动请求上限。",
                nameof(MaximumConcurrentHandlers));
        }

        if (MaximumFrameBytes <= 0 || MaximumFrameBytes > ProtocolConstants.MaximumMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFrameBytes),
                $"IPC帧上限必须为1至{ProtocolConstants.MaximumMessageBytes}字节。");
        }

        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout > AbsoluteMaximumShutdownTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownTimeout),
                $"连接关闭等待必须大于零且不超过{AbsoluteMaximumShutdownTimeout}。");
        }

        ArgumentNullException.ThrowIfNull(SessionGuard);
    }

    private static void ValidateEntryLimit(int value, string parameterName)
    {
        if (value <= 0 || value > AbsoluteMaximumConcurrentEntries)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"并发容量必须为1至{AbsoluteMaximumConcurrentEntries}。");
        }
    }
}

public enum BridgeCapacityKind
{
    PendingRequests = 0,
    ActiveRequests = 1,
    ConcurrentHandlers = 2,
    PendingNotifications = 3
}

public sealed class BridgeCapacityExceededException : BridgeProtocolException
{
    public BridgeCapacityExceededException(BridgeCapacityKind capacityKind, int limit)
        : base($"IPC容量已满：{capacityKind}上限为{limit}。")
    {
        CapacityKind = capacityKind;
        Limit = limit;
    }

    public BridgeCapacityKind CapacityKind { get; }

    public int Limit { get; }
}
