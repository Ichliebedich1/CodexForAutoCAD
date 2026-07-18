using System.Globalization;
using System.Text;

namespace Codex.AutoCAD.Contracts;

public sealed class CadValidationFailure
{
    public CadValidationFailure(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }
}

public static class CadContractValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static CadValidationFailure[] Validate(CadOperationBatch? batch)
    {
        var failures = new List<CadValidationFailure>();
        if (batch is null)
        {
            failures.Add(new CadValidationFailure("batch_required", "$", "操作计划不能为空。"));
            return failures.ToArray();
        }

        Require(batch.ProtocolVersion == ProtocolConstants.CurrentVersion, failures,
            "protocol_version", "$.protocolVersion", "协议版本不受支持。");
        ValidateRequiredPlanString(batch.BatchId, "batch_id", "$.batchId", "BatchId不能为空。", failures);
        ValidateRequiredPlanString(batch.ThreadId, "thread_id", "$.threadId", "ThreadId不能为空。", failures);
        ValidateRequiredPlanString(batch.TurnId, "turn_id", "$.turnId", "TurnId不能为空。", failures);
        if (batch.Document is null)
        {
            failures.Add(new CadValidationFailure(
                "document_required", "$.document", "文档引用不能为空。"));
            return failures.ToArray();
        }

        ValidateRequiredPlanString(batch.Document.DrawingFingerprint, "drawing_fingerprint",
            "$.document.drawingFingerprint", "图纸指纹不能为空。", failures);
        Require(IsSha256Hex(batch.Document.DrawingFingerprint), failures,
            "drawing_fingerprint_format", "$.document.drawingFingerprint",
            "图纸指纹必须是64位ASCII十六进制SHA-256摘要。");
        ValidateRequiredPlanString(batch.Document.DocumentId, "document_id",
            "$.document.documentId", "文档标识不能为空。", failures);
        Require(batch.Document.Revision >= 0, failures,
            "drawing_revision", "$.document.revision", "图纸修订号不能为负数。");
        ValidateRequiredPlanString(batch.SelectionSnapshotHash, "selection_snapshot_hash",
            "$.selectionSnapshotHash", "选择快照哈希不能为空。", failures);
        Require(IsSha256Hex(batch.SelectionSnapshotHash), failures,
            "selection_snapshot_hash_format", "$.selectionSnapshotHash",
            "选择快照哈希必须是64位ASCII十六进制SHA-256摘要。");

        var operations = batch.Operations ?? new CadOperation[0];
        Require(operations.Length > 0, failures,
            "operations_required", "$.operations", "操作计划至少包含一个操作。");
        Require(operations.Length <= ProtocolConstants.MaximumOperationsPerBatch, failures,
            "operations_limit", "$.operations", "操作数量超过安全上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hasExistingEntityTargets = false;
        long totalTargetHandles = 0;
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            var path = "$.operations[" + index + "]";
            if (operation is null)
            {
                failures.Add(new CadValidationFailure("operation_required", path, "操作不能为空。"));
                continue;
            }

            ValidateRequiredPlanString(operation.OperationId, "operation_id",
                path + ".operationId", "OperationId不能为空。", failures);
            if (!string.IsNullOrWhiteSpace(operation.OperationId))
            {
                Require(ids.Add(operation.OperationId), failures,
                    "operation_id_duplicate", path + ".operationId", "OperationId必须唯一。");
            }

            Require(batch.DeclaredRisk >= operation.MinimumRisk, failures,
                "risk_understated", path, "声明风险低于操作的最低风险级别。");

            switch (operation)
            {
                case CreateLineOperation line:
                    ValidateLine(line, path, failures);
                    break;
                case EraseEntitiesOperation erase:
                    hasExistingEntityTargets = true;
                    if (ReserveTargetHandles(erase.Handles, ref totalTargetHandles, failures))
                    {
                        ValidateHandles(erase.Handles, path + ".handles", failures);
                    }
                    break;
                case TransformEntitiesOperation transform:
                    hasExistingEntityTargets = true;
                    if (ReserveTargetHandles(transform.Handles, ref totalTargetHandles, failures))
                    {
                        ValidateHandles(transform.Handles, path + ".handles", failures);
                    }
                    Require(transform.Translation.IsFinite, failures,
                        "translation_finite", path + ".translation", "平移向量必须为有限数值。");
                    Require(IsFinite(transform.RotationRadians), failures,
                        "rotation_finite", path + ".rotationRadians", "旋转角必须为有限数值。");
                    Require(IsFinite(transform.UniformScale) && transform.UniformScale > 0d, failures,
                        "scale_positive", path + ".uniformScale", "缩放比例必须为正的有限数值。");
                    break;
                default:
                    failures.Add(new CadValidationFailure("operation_unknown", path, "操作类型不在能力白名单中。"));
                    break;
            }
        }

        Require(!hasExistingEntityTargets || batch.RequiresSelectionRevalidation, failures,
            "selection_revalidation_required", "$.requiresSelectionRevalidation",
            "针对现有图元的操作必须在锁内重新验证选择快照。");

        if (failures.Count == 0)
        {
            if (!TryComputeCanonicalByteCount(batch, out var canonicalByteCount))
            {
                failures.Add(new CadValidationFailure(
                    "plan_canonical_encoding",
                    "$",
                    "操作计划不能被严格UTF-8规范化。"));
            }
            else
            {
                Require(canonicalByteCount <= ProtocolConstants.MaximumPlanCanonicalBytes, failures,
                    "plan_canonical_bytes_limit", "$",
                    "操作计划规范化后的UTF-8字节数超过安全上限。");
            }
        }

        return failures.ToArray();
    }

    private static void ValidateLine(
        CreateLineOperation line,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(line.Start.IsFinite, failures,
            "start_finite", path + ".start", "起点必须为有限数值。");
        Require(line.End.IsFinite, failures,
            "end_finite", path + ".end", "终点必须为有限数值。");
        if (line.Start.IsFinite && line.End.IsFinite)
        {
            var dx = line.Start.X - line.End.X;
            var dy = line.Start.Y - line.End.Y;
            var dz = line.Start.Z - line.End.Z;
            Require((dx * dx) + (dy * dy) + (dz * dz) > 1e-20d, failures,
                "line_zero_length", path, "不能创建零长度直线。");
        }

        ValidateRequiredPlanString(line.Layer, "layer_required",
            path + ".layer", "图层名不能为空。", failures, maximumLength: 255);
        ValidateRequiredPlanString(line.LayerHandle, "layer_handle",
            path + ".layerHandle", "目标图层Handle不能为空。", failures, maximumLength: 16);
        Require(IsHandle(line.LayerHandle), failures,
            "layer_handle", path + ".layerHandle", "目标图层Handle必须由受信宿主解析。");
        ValidateRequiredPlanString(line.OwnerSpaceHandle, "owner_space_handle",
            path + ".ownerSpaceHandle", "目标空间Handle不能为空。", failures, maximumLength: 16);
        Require(IsHandle(line.OwnerSpaceHandle), failures,
            "owner_space_handle", path + ".ownerSpaceHandle", "目标空间Handle必须由受信宿主解析。");
    }

    private static void ValidateHandles(
        string[]? handles,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        handles ??= new string[0];
        Require(handles.Length > 0, failures,
            "handles_required", path, "目标Handle不能为空。");
        Require(handles.Length <= ProtocolConstants.MaximumEntityHandlesPerOperation, failures,
            "handles_limit", path, "目标Handle数量超过安全上限。");
        for (var index = 0; index < handles.Length; index++)
        {
            ValidateRequiredPlanString(handles[index], "handle_blank",
                path + "[" + index + "]", "Handle不能为空字符串。", failures, maximumLength: 16);
            Require(IsHandle(handles[index]), failures,
                "handle_format", path + "[" + index + "]",
                "Handle必须是1到16位ASCII十六进制值。");
        }
        Require(handles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == handles.Length, failures,
            "handle_duplicate", path, "Handle不能重复。");
    }

    private static bool ReserveTargetHandles(
        string[]? handles,
        ref long totalTargetHandles,
        ICollection<CadValidationFailure> failures)
    {
        totalTargetHandles += handles?.Length ?? 0;
        if (totalTargetHandles <= ProtocolConstants.MaximumEntityHandlesPerBatch)
        {
            return true;
        }

        if (!failures.Any(static failure => failure.Code == "handles_batch_limit"))
        {
            failures.Add(new CadValidationFailure(
                "handles_batch_limit",
                "$.operations",
                "批次目标Handle总数超过安全上限。"));
        }

        return false;
    }

    private static void ValidateRequiredPlanString(
        string? value,
        string requiredCode,
        string path,
        string requiredMessage,
        ICollection<CadValidationFailure> failures,
        int maximumLength = 256)
    {
        Require(!string.IsNullOrWhiteSpace(value), failures, requiredCode, path, requiredMessage);
        if (value is null)
        {
            return;
        }

        if (value.Length > maximumLength)
        {
            failures.Add(new CadValidationFailure(
                "string_length", path, "字符串长度超过安全上限。"));
            return;
        }

        var isWellFormed = IsWellFormedUtf16(value);
        Require(isWellFormed, failures,
            "string_unicode", path, "字符串必须是格式正确的Unicode，不能包含未配对代理项。");
        if (!isWellFormed)
        {
            return;
        }

        Require(!ContainsUnicodeCategory(value, UnicodeCategory.Control), failures,
            "string_control", path, "字符串不能包含控制字符。");
        Require(!ContainsDangerousFormat(value), failures,
            "string_format", path, "字符串不能包含方向覆盖、不可见格式或行分隔字符。");
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsUnicodeCategory(string value, UnicodeCategory expected)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(value, index) == expected)
            {
                return true;
            }

            if (char.IsHighSurrogate(value[index]))
            {
                index++;
            }
        }

        return false;
    }

    private static bool ContainsDangerousFormat(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }

            if (char.IsHighSurrogate(value[index]))
            {
                index++;
            }
        }

        return false;
    }

    private static bool TryComputeCanonicalByteCount(
        CadOperationBatch batch,
        out long canonicalByteCount)
    {
        canonicalByteCount = 0;
        try
        {
            AddCanonicalValue(ref canonicalByteCount,
                batch.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            AddCanonicalValue(ref canonicalByteCount, batch.BatchId);
            AddCanonicalValue(ref canonicalByteCount, batch.ThreadId);
            AddCanonicalValue(ref canonicalByteCount, batch.TurnId);
            AddCanonicalValue(ref canonicalByteCount, batch.Document.DocumentId);
            AddCanonicalValue(ref canonicalByteCount, batch.Document.DrawingFingerprint);
            AddCanonicalValue(ref canonicalByteCount,
                batch.Document.Revision.ToString(CultureInfo.InvariantCulture));
            AddCanonicalValue(ref canonicalByteCount, batch.SelectionSnapshotHash);
            AddCanonicalValue(ref canonicalByteCount,
                batch.RequiresSelectionRevalidation ? "1" : "0");
            AddCanonicalValue(ref canonicalByteCount,
                ((int)batch.DeclaredRisk).ToString(CultureInfo.InvariantCulture));
            AddCanonicalValue(ref canonicalByteCount,
                batch.Operations.Length.ToString(CultureInfo.InvariantCulture));

            foreach (var operation in batch.Operations)
            {
                AddCanonicalValue(ref canonicalByteCount, operation.OperationId);
                AddCanonicalValue(ref canonicalByteCount, operation.Kind);
                switch (operation)
                {
                    case CreateLineOperation line:
                        AddCanonicalValue(ref canonicalByteCount, line.Start.ToCanonicalString());
                        AddCanonicalValue(ref canonicalByteCount, line.End.ToCanonicalString());
                        AddCanonicalValue(ref canonicalByteCount, line.Layer);
                        AddCanonicalValue(ref canonicalByteCount, line.LayerHandle);
                        AddCanonicalValue(ref canonicalByteCount, line.OwnerSpaceHandle);
                        break;
                    case EraseEntitiesOperation erase:
                        AddCanonicalValues(ref canonicalByteCount, erase.Handles);
                        break;
                    case TransformEntitiesOperation transform:
                        AddCanonicalValues(ref canonicalByteCount, transform.Handles);
                        AddCanonicalValue(ref canonicalByteCount, transform.Translation.ToCanonicalString());
                        AddCanonicalValue(ref canonicalByteCount,
                            transform.RotationRadians.ToString("R", CultureInfo.InvariantCulture));
                        AddCanonicalValue(ref canonicalByteCount,
                            transform.UniformScale.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }
        catch (EncoderFallbackException)
        {
            canonicalByteCount = 0;
            return false;
        }
        catch (OverflowException)
        {
            canonicalByteCount = long.MaxValue;
            return true;
        }
    }

    private static void AddCanonicalValues(ref long byteCount, IReadOnlyCollection<string> values)
    {
        AddCanonicalValue(ref byteCount, values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var value in values)
        {
            AddCanonicalValue(ref byteCount, value);
        }
    }

    private static void AddCanonicalValue(ref long byteCount, string? value)
    {
        value ??= string.Empty;
        var lengthPrefix = value.Length.ToString(CultureInfo.InvariantCulture);
        byteCount = checked(
            byteCount
            + lengthPrefix.Length
            + 1
            + StrictUtf8.GetByteCount(value));
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsHandle(string? value)
    {
        return value is { Length: >= 1 and <= 16 }
            && value.All(static character =>
                character is >= '0' and <= '9'
                or >= 'A' and <= 'F'
                or >= 'a' and <= 'f');
    }

    private static bool IsSha256Hex(string? value)
    {
        return value is { Length: 64 }
            && value.All(static character =>
                character is >= '0' and <= '9'
                or >= 'A' and <= 'F'
                or >= 'a' and <= 'f');
    }

    private static void Require(
        bool condition,
        ICollection<CadValidationFailure> failures,
        string code,
        string path,
        string message)
    {
        if (!condition)
        {
            failures.Add(new CadValidationFailure(code, path, message));
        }
    }
}
