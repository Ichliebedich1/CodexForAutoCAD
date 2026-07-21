using System;
using System.Linq;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-006: 字段本身合法、最多64实体，但总canonical JSON超过256 KiB，
/// 精确包含context_v2_json_bytes_limit。
/// </summary>
public static class AdvV2006_JsonBytesLimitEnforced
{
    public static void Run()
    {
        // 创建刚好超过256 KiB的context
        var context = CreateOversizedContext();
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == "context_v2_json_bytes_limit"))
        {
            throw new InvalidOperationException(
                "Expected context_v2_json_bytes_limit failure for oversized JSON.");
        }

        // 创建刚好在限制内的context
        var contextWithinLimit = CreateContextWithinLimit();
        var failuresWithin = CadContextJsonV2Validator.Validate(contextWithinLimit);

        if (failuresWithin.Any(f => f.Code == "context_v2_json_bytes_limit"))
        {
            throw new InvalidOperationException(
                "Context within limit should not trigger context_v2_json_bytes_limit.");
        }
    }

    private static CadContextJsonV2 CreateOversizedContext()
    {
        // 使用最大实体数和最大文本长度使JSON超过256 KiB
        // 每个实体的文本约6KB (2000个中文字符 * 3字节)，64个实体约384KB
        var longText = new string('测', 2000); // 接近MaximumTextCharacters
        var longLayer = new string('层', 255); // 接近MaximumNameCharacters
        var entities = new CadContextEntityV2[CadContextJsonV2Constants.MaximumEntities];

        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = new CadContextEntityV2
            {
                Handle = i.ToString("X"),
                OwnerSpaceHandle = "1F",
                EntityType = CadContextEntityTypesV2.DbText,
                StateHash = new string((char)('a' + (i % 6)), 64),
                Layer = longLayer,
                DbText = new CadContextDbTextV2
                {
                    Text = longText,
                    Position = new CadPoint3(i, 0, 0),
                    Height = 2.5,
                    Rotation = 0,
                },
            };
        }

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-006",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = entities.Length,
                ParsedEntityCount = entities.Length,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = entities,
            },
        };
    }

    private static CadContextJsonV2 CreateContextWithinLimit()
    {
        // 使用较短的文本
        var shortText = "短文本";
        var entities = new CadContextEntityV2[10];

        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = new CadContextEntityV2
            {
                Handle = i.ToString("X"),
                OwnerSpaceHandle = "1F",
                EntityType = CadContextEntityTypesV2.DbText,
                StateHash = new string((char)('a' + (i % 26)), 64),
                Layer = "0",
                DbText = new CadContextDbTextV2
                {
                    Text = shortText,
                    Position = new CadPoint3(i, 0, 0),
                    Height = 2.5,
                    Rotation = 0,
                },
            };
        }

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-006-limit",
                DrawingFingerprint = new string('c', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('d', 64),
                EntityCount = entities.Length,
                ParsedEntityCount = entities.Length,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = entities,
            },
        };
    }
}
