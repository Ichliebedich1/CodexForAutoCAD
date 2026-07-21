using System;
using System.Linq;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-007: null payload、零 payload、null entity、unsupported payload=null；
/// 检查 shape/entity 精确错误码。
/// </summary>
public static class AdvV2007_NullPayloadRejected
{
    public static void Run()
    {
        // null entity
        TestNullEntity();

        // null payload (Line entity with null Line)
        TestNullPayload();

        // zero payload (no payload set)
        TestZeroPayload();

        // unsupported payload=null
        TestUnsupportedNullPayload();

        // multiple payloads
        TestMultiplePayloads();
    }

    private static void TestNullEntity()
    {
        var context = CreateContextWithNullEntity();
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == "context_v2_entity_required"))
        {
            throw new InvalidOperationException(
                "Null entity: expected context_v2_entity_required failure.");
        }
    }

    private static void TestNullPayload()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Line,
                        StateHash = new string('b', 64),
                        Layer = "0",
                        Line = null, // null payload
                    },
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_shape_count"))
        {
            throw new InvalidOperationException(
                "Null Line payload: expected context_v2_shape_count failure.");
        }
    }

    private static void TestZeroPayload()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Line,
                        StateHash = new string('b', 64),
                        Layer = "0",
                        // No payload set - zero payload
                    },
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_shape_count"))
        {
            throw new InvalidOperationException(
                "Zero payload: expected context_v2_shape_count failure.");
        }
    }

    private static void TestUnsupportedNullPayload()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 1,
                ParsedEntityCount = 0,
                UnsupportedEntityCount = 1,
                Complete = false,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Unsupported,
                        StateHash = new string('b', 64),
                        Layer = "0",
                        Unsupported = null, // null unsupported payload
                    },
                ],
            },
        };

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_shape_mismatch"))
        {
            throw new InvalidOperationException(
                "Unsupported null payload: expected context_v2_shape_mismatch failure.");
        }
    }

    private static void TestMultiplePayloads()
    {
        var context = new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('a', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Line,
                        StateHash = new string('b', 64),
                        Layer = "0",
                        Line = new CadContextLineV2
                        {
                            Start = new CadPoint3(0, 0, 0),
                            End = new CadPoint3(10, 0, 0),
                        },
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

        var failures = CadContextJsonV2Validator.Validate(context);
        if (!failures.Any(f => f.Code == "context_v2_shape_count"))
        {
            throw new InvalidOperationException(
                "Multiple payloads: expected context_v2_shape_count failure.");
        }
    }

    private static CadContextJsonV2 CreateContextWithNullEntity()
    {
        var entities = new CadContextEntityV2[2];
        entities[0] = new CadContextEntityV2
        {
            Handle = "1",
            OwnerSpaceHandle = "1F",
            EntityType = CadContextEntityTypesV2.Line,
            StateHash = new string('a', 64),
            Layer = "0",
            Line = new CadContextLineV2
            {
                Start = new CadPoint3(0, 0, 0),
                End = new CadPoint3(10, 0, 0),
            },
        };
        entities[1] = null!; // null entity

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = CreateDocument(),
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 2,
                ParsedEntityCount = 2,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = entities,
            },
        };
    }

    private static CadContextDocumentV2 CreateDocument() => new()
    {
        DocumentId = "doc-adv-007",
        DrawingFingerprint = new string('c', 64),
        Revision = 1,
        CurrentSpace = CadContextJsonV2Constants.ModelSpace,
        DrawingVersion = "AC1027",
        Units = "Millimeters",
    };
}
