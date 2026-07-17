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
        Require(!string.IsNullOrWhiteSpace(batch.BatchId), failures,
            "batch_id", "$.batchId", "BatchId不能为空。");
        Require(!string.IsNullOrWhiteSpace(batch.ThreadId), failures,
            "thread_id", "$.threadId", "ThreadId不能为空。");
        Require(!string.IsNullOrWhiteSpace(batch.TurnId), failures,
            "turn_id", "$.turnId", "TurnId不能为空。");
        Require(!string.IsNullOrWhiteSpace(batch.Document.DrawingFingerprint), failures,
            "drawing_fingerprint", "$.document.drawingFingerprint", "图纸指纹不能为空。");
        Require(batch.Document.Revision >= 0, failures,
            "drawing_revision", "$.document.revision", "图纸修订号不能为负数。");

        var operations = batch.Operations ?? Array.Empty<CadOperation>();
        Require(operations.Length > 0, failures,
            "operations_required", "$.operations", "操作计划至少包含一个操作。");
        Require(operations.Length <= ProtocolConstants.MaximumOperationsPerBatch, failures,
            "operations_limit", "$.operations", "操作数量超过安全上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            var path = "$.operations[" + index + "]";
            if (operation is null)
            {
                failures.Add(new CadValidationFailure("operation_required", path, "操作不能为空。"));
                continue;
            }

            Require(!string.IsNullOrWhiteSpace(operation.OperationId), failures,
                "operation_id", path + ".operationId", "OperationId不能为空。");
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
                    ValidateHandles(erase.Handles, path + ".handles", failures);
                    break;
                case TransformEntitiesOperation transform:
                    ValidateHandles(transform.Handles, path + ".handles", failures);
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

        Require(!string.IsNullOrWhiteSpace(line.Layer), failures,
            "layer_required", path + ".layer", "图层名不能为空。");
    }

    private static void ValidateHandles(
        string[]? handles,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        handles ??= Array.Empty<string>();
        Require(handles.Length > 0, failures,
            "handles_required", path, "目标Handle不能为空。");
        Require(handles.Length <= ProtocolConstants.MaximumEntityHandlesPerOperation, failures,
            "handles_limit", path, "目标Handle数量超过安全上限。");
        Require(handles.All(static handle => !string.IsNullOrWhiteSpace(handle)), failures,
            "handle_blank", path, "Handle不能为空字符串。");
        Require(handles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == handles.Length, failures,
            "handle_duplicate", path, "Handle不能重复。");
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
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
