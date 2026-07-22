using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Bridge.Client;

public delegate Task<AgentDrawingQueryResponse> AgentDrawingQueryHandler(
    AgentDrawingQueryRequest request,
    CancellationToken cancellationToken);

public interface IAgentBridgeClient : IDisposable
{
    event EventHandler<AgentBridgeEventReceivedEventArgs>? EventReceived;

    event EventHandler<AgentBridgeConnectionFaultedEventArgs>? ConnectionFaulted;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<AgentCapabilitiesResponse> GetCapabilitiesAsync(
        AgentCapabilitiesRequest request,
        CancellationToken cancellationToken);

    Task<AgentThreadStartResponse> StartThreadAsync(
        AgentThreadStartRequest request,
        CancellationToken cancellationToken);

    Task<AgentTurnStartResponse> StartTurnAsync(
        AgentTurnStartRequest request,
        CancellationToken cancellationToken);

    Task<AgentTurnStartV2Response> StartTurnV2Async(
        AgentTurnStartV2Request request,
        CancellationToken cancellationToken);

    Task InterruptTurnAsync(
        AgentTurnInterruptRequest request,
        CancellationToken cancellationToken);

    Task ResolveApprovalAsync(
        AgentApprovalResolveRequest request,
        CancellationToken cancellationToken);
}

public sealed class AgentBridgeEventReceivedEventArgs : EventArgs
{
    public AgentBridgeEventReceivedEventArgs(AgentBridgeEvent bridgeEvent)
    {
        BridgeEvent = bridgeEvent ?? throw new ArgumentNullException(nameof(bridgeEvent));
    }

    public AgentBridgeEvent BridgeEvent { get; }
}

public sealed class AgentBridgeConnectionFaultedEventArgs : EventArgs
{
    public AgentBridgeConnectionFaultedEventArgs(AgentBridgeClientException exception)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public AgentBridgeClientException Exception { get; }
}

public sealed class AgentBridgeClientOptions
{
    public string PipeName { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public byte[] SessionSecret { get; set; } = new byte[0];

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaximumFrameBytes { get; set; } = ProtocolConstants.MaximumMessageBytes;

    /// <summary>
    /// Optional, read-only reverse query handler. No generic method dispatcher is exposed to the
    /// AutoCAD host process.
    /// </summary>
    public AgentDrawingQueryHandler? DrawingQueryHandler { get; set; }

    public int MaximumConcurrentDrawingQueries { get; set; } = 2;
}
