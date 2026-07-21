using System;
using System.Linq;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-008: entity/parsed/unsupported/count/complete/Selection/Entities 
/// 的所有不一致状态精确拒绝。
/// </summary>
public static class AdvV2008_InconsistentStateRejected
{
    public static void Run()
    {
        // EntityCount != Entities.Length
        TestEntityCountMismatch();

        // ParsedEntityCount 不匹配
        TestParsedCountMismatch();

        // UnsupportedEntityCount 不匹配
        TestUnsupportedCountMismatch();

        // ParsedEntityCount + UnsupportedEntityCount != EntityCount
        TestCountSumMismatch();

        // Complete 不一致
        TestCompleteInconsistency();

        // Selection 为 null
        TestNullSelection();

        // Entities 为 null
        TestNullEntities();
    }

    private static void TestEntityCountMismatch()
    {
        var context = CreateBaseContext();
        context.Selection.EntityCount = 999; // 不匹配实际数量

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_entity_count"))
        {
            throw new InvalidOperationException(
                "EntityCount mismatch: expected context_v2_entity_count failure.");
        }
    }

    private static void TestParsedCountMismatch()
    {
        var context = CreateBaseContext();
        context.Selection.ParsedEntityCount = 999;

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_parsed_count"))
        {
            throw new InvalidOperationException(
                "ParsedEntityCount mismatch: expected context_v2_parsed_count failure.");
        }
    }

    private static void TestUnsupportedCountMismatch()
    {
        var context = CreateBaseContext();
        context.Selection.UnsupportedEntityCount = 999;

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_unsupported_count"))
        {
            throw new InvalidOperationException(
                "UnsupportedEntityCount mismatch: expected context_v2_unsupported_count failure.");
        }
    }

    private static void TestCountSumMismatch()
    {
        var context = CreateBaseContext();
        context.Selection.ParsedEntityCount = context.Selection.EntityCount;
        context.Selection.UnsupportedEntityCount = 1;

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_count_sum"))
        {
            throw new InvalidOperationException(
                "Count sum mismatch: expected context_v2_count_sum failure.");
        }
    }

    private static void TestCompleteInconsistency()
    {
        // Complete=true 但有 unsupported entity
        var context = CreateBaseContext();
        context.Selection.Entities[0].EntityType = CadContextEntityTypesV2.Unsupported;
        context.Selection.Entities[0].Unsupported = new CadContextUnsupportedV2
        {
            DxfName = "ACAD_PROXY_ENTITY",
            Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
        };
        context.Selection.ParsedEntityCount = 0;
        context.Selection.UnsupportedEntityCount = 1;
        context.Selection.Complete = true; // 不一致

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_complete"))
        {
            throw new InvalidOperationException(
                "Complete inconsistency: expected context_v2_complete failure.");
        }
    }

    private static void TestNullSelection()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-008",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = null!,
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_selection_required"))
        {
            throw new InvalidOperationException(
                "Null selection: expected context_v2_selection_required failure.");
        }
    }

    private static void TestNullEntities()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-008b",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = null!,
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_entities_required"))
        {
            throw new InvalidOperationException(
                "Null entities: expected context_v2_entities_required failure.");
        }
    }

    private static CadContextJsonV2 CreateBaseContext()
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-008",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 2,
                ParsedEntityCount = 2,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Line,
                        StateHash = new string('c', 64),
                        Layer = "0",
                        Line = new CadContextLineV2
                        {
                            Start = new CadPoint3(0, 0, 0),
                            End = new CadPoint3(10, 0, 0),
                        },
                    },
                    new CadContextEntityV2
                    {
                        Handle = "2",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Circle,
                        StateHash = new string('d', 64),
                        Layer = "0",
                        Circle = new CadContextCircleV2
                        {
                            Center = new CadPoint3(5, 5, 0),
                            Radius = 2.5,
                            Normal = new CadPoint3(0, 0, 1),
                        },
                    },
                ],
            },
        };
    }
}
