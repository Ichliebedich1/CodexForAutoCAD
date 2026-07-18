using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Security;

/// <summary>
/// 为经过强类型 Schema 验证的 CAD 操作计划生成稳定哈希。
/// 审批令牌绑定此哈希，计划中的任意执行字段变化都会使旧审批失效。
/// </summary>
public static class CadPlanHash
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Compute(CadOperationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var failures = CadContractValidator.Validate(batch);
        if (failures.Length != 0)
        {
            throw new InvalidOperationException(
                "操作计划未通过Schema验证: " +
                string.Join(", ", failures.Select(static failure => failure.Code)));
        }

        var canonical = new StringBuilder();
        Append(canonical, batch.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        Append(canonical, batch.BatchId);
        Append(canonical, batch.ThreadId);
        Append(canonical, batch.TurnId);
        Append(canonical, batch.Document.DocumentId);
        Append(canonical, batch.Document.DrawingFingerprint);
        Append(canonical, batch.Document.Revision.ToString(CultureInfo.InvariantCulture));
        Append(canonical, batch.SelectionSnapshotHash);
        Append(canonical, batch.RequiresSelectionRevalidation ? "1" : "0");
        Append(canonical, ((int)batch.DeclaredRisk).ToString(CultureInfo.InvariantCulture));
        Append(canonical, batch.Operations.Length.ToString(CultureInfo.InvariantCulture));

        foreach (var operation in batch.Operations)
        {
            Append(canonical, operation.OperationId);
            Append(canonical, operation.Kind);
            switch (operation)
            {
                case CreateLineOperation line:
                    Append(canonical, line.Start.ToCanonicalString());
                    Append(canonical, line.End.ToCanonicalString());
                    Append(canonical, line.Layer);
                    Append(canonical, line.LayerHandle);
                    Append(canonical, line.OwnerSpaceHandle);
                    break;
                case EraseEntitiesOperation erase:
                    AppendStrings(canonical, erase.Handles);
                    break;
                case TransformEntitiesOperation transform:
                    AppendStrings(canonical, transform.Handles);
                    Append(canonical, transform.Translation.ToCanonicalString());
                    Append(canonical, transform.RotationRadians.ToString("R", CultureInfo.InvariantCulture));
                    Append(canonical, transform.UniformScale.ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new InvalidOperationException("操作类型不在计划哈希白名单中: " + operation.Kind);
            }
        }

        var canonicalBytes = StrictUtf8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
    }

    private static void AppendStrings(StringBuilder builder, IReadOnlyCollection<string> values)
    {
        Append(builder, values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var value in values)
        {
            Append(builder, value);
        }
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}
