using System;
using System.Linq;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-002: 不同类型/顺序的重复Handle精确包含context_v2_handle_duplicate。
/// </summary>
public static class AdvV2002_DuplicateHandleRejected
{
    public static void Run()
    {
        // 测试相同类型重复Handle
        TestDuplicateHandles(
            CadContextEntityTypesV2.Line,
            CadContextEntityTypesV2.Line,
            "相同类型重复Handle");

        // 测试不同类型重复Handle
        TestDuplicateHandles(
            CadContextEntityTypesV2.Line,
            CadContextEntityTypesV2.Circle,
            "不同类型重复Handle");

        // 测试大小写不同的Handle（应通过，因为Handle是大写十六进制）
        TestCaseInsensitiveHandle();

        // 测试三个实体中两个重复
        TestTripleWithDuplicate();
    }

    private static void TestDuplicateHandles(string type1, string type2, string description)
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 2,
                ParsedEntityCount = 2,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    CreateEntity("1A", type1),
                    CreateEntity("1A", type2),
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_handle_duplicate"))
        {
            throw new InvalidOperationException(
                $"{description}: expected context_v2_handle_duplicate failure.");
        }
    }

    private static void TestCaseInsensitiveHandle()
    {
        // Handle "1A" 和 "1a" 在大写十六进制验证中应该都通过
        // 但 "1a" 不是有效的大写Handle，应该被拒绝
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 2,
                ParsedEntityCount = 2,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    CreateEntity("1A", CadContextEntityTypesV2.Line),
                    CreateEntity("1a", CadContextEntityTypesV2.Circle),
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        // "1a" 不是有效的大写Handle
        if (!failures.Any(f => f.Code == "context_v2_handle"))
        {
            throw new InvalidOperationException(
                "Lowercase handle '1a' should be rejected.");
        }
    }

    private static void TestTripleWithDuplicate()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 3,
                ParsedEntityCount = 3,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    CreateEntity("1", CadContextEntityTypesV2.Line),
                    CreateEntity("2", CadContextEntityTypesV2.Circle),
                    CreateEntity("1", CadContextEntityTypesV2.Arc),
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_handle_duplicate"))
        {
            throw new InvalidOperationException(
                "Triple with duplicate handle should be rejected.");
        }
    }

    private static CadContextDocumentV2 CreateDocument() => new()
    {
        DocumentId = "doc-adv-002",
        DrawingFingerprint = new string('b', 64),
        Revision = 1,
        CurrentSpace = CadContextJsonV2Constants.ModelSpace,
        DrawingVersion = "AC1027",
        Units = "Millimeters",
    };

    private static CadContextEntityV2 CreateEntity(string handle, string entityType)
    {
        var entity = new CadContextEntityV2
        {
            Handle = handle,
            OwnerSpaceHandle = "1F",
            EntityType = entityType,
            StateHash = new string('c', 64),
            Layer = "0",
        };

        switch (entityType)
        {
            case CadContextEntityTypesV2.Line:
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(0, 0, 0),
                    End = new CadPoint3(10, 0, 0),
                };
                break;
            case CadContextEntityTypesV2.Circle:
                entity.Circle = new CadContextCircleV2
                {
                    Center = new CadPoint3(5, 5, 0),
                    Radius = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
            case CadContextEntityTypesV2.Arc:
                entity.Arc = new CadContextArcV2
                {
                    Center = new CadPoint3(10, 10, 0),
                    Radius = 5,
                    StartAngle = 0.25,
                    EndAngle = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
        }

        return entity;
    }
}
