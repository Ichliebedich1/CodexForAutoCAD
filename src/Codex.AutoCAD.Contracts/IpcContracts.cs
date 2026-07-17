namespace Codex.AutoCAD.Contracts;

public sealed class IpcEnvelope
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentVersion;

    public string MessageId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public long Sequence { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Mac { get; set; } = string.Empty;
}
