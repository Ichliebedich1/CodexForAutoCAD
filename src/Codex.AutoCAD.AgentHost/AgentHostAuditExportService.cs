using Codex.AutoCAD.AgentLauncher;

namespace Codex.AutoCAD.AgentHost;

internal static class AgentHostAuditExportService
{
    internal const string RejectedErrorCode = "audit_export_rejected";
    internal const string FailedErrorCode = "audit_export_failed";

    internal static void ExportCurrentUserSessionToStream(
        string systemSessionId,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new AgentHostAuditCatalogException(
                "Audit export destination is not writable.");
        }

        using var store = AgentPersistentAuditStoreLease.CreateForCurrentUser();
        ExportSessionToStream(
            systemSessionId,
            store.SegmentDirectory,
            store.AnchorDirectory,
            destination);
    }

    internal static void ExportSessionToStream(
        string systemSessionId,
        string segmentDirectory,
        string anchorDirectory,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new AgentHostAuditCatalogException(
                "Audit export destination is not writable.");
        }

        var data = AgentHostAuditCatalog.ReadCompleteSession(
            segmentDirectory,
            anchorDirectory,
            systemSessionId);
        WriteVerifiedSessionToStream(data, destination);
    }

    internal static void WriteVerifiedSessionToStream(
        AgentHostAuditCatalogSessionData data,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new AgentHostAuditCatalogException(
                "Audit export destination is not writable.");
        }

        using var buffered = new MemoryStream();
        AgentHostAuditRedactedExport.WriteVerified(
            buffered,
            data.Segments,
            data.Anchor);
        buffered.Position = 0;
        buffered.CopyTo(destination);
        destination.Flush();
    }

    internal static void ExportCurrentUserSessionToStandardOutput(string systemSessionId)
    {
        using var output = Console.OpenStandardOutput();
        ExportCurrentUserSessionToStream(systemSessionId, output);
    }
}