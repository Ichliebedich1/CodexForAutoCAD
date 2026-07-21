using System;
using System.Linq;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-004: U+0000/U+0007/U+001B注入text/layer/name，验证精确结构化失败码。
/// </summary>
public static class AdvV2004_ControlCharInjectionRejected
{
    public static void Run()
    {
        // U+0000 (NUL) 注入
        TestControlCharInjection("\0", "NUL", "text",
            CadContextEntityTypesV2.DbText, "context_v2_text_unicode");

        // U+0007 (BEL) 注入
        TestControlCharInjection("\u0007", "BEL", "text",
            CadContextEntityTypesV2.DbText, "context_v2_text_unicode");

        // U+001B (ESC) 注入
        TestControlCharInjection("\u001B", "ESC", "text",
            CadContextEntityTypesV2.DbText, "context_v2_text_unicode");

        // U+0000 注入到 layer
        TestControlCharInjection("\0", "NUL", "layer",
            CadContextEntityTypesV2.Line, "context_v2_layer_unicode");

        // U+0007 注入到 layer
        TestControlCharInjection("\u0007", "BEL", "layer",
            CadContextEntityTypesV2.Line, "context_v2_layer_unicode");

        // U+001B 注入到 layer
        TestControlCharInjection("\u001B", "ESC", "layer",
            CadContextEntityTypesV2.Line, "context_v2_layer_unicode");

        // U+0000 注入到 name (NUL 被 IsSafeUnicode 捕获，错误码是 context_v2_block_name_unicode)
        TestControlCharInjection("\0", "NUL", "name",
            CadContextEntityTypesV2.BlockReference, "context_v2_block_name_unicode");

        // U+0007 注入到 name
        TestControlCharInjection("\u0007", "BEL", "name",
            CadContextEntityTypesV2.BlockReference, "context_v2_block_name_unicode");
    }

    private static void TestControlCharInjection(
        string controlChar,
        string charName,
        string field,
        string entityType,
        string expectedCode)
    {
        var context = CreateContextWithInjection(controlChar, field, entityType);
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == expectedCode))
        {
            throw new InvalidOperationException(
                $"{charName} injection into {field}: expected {expectedCode} failure.");
        }
    }

    private static CadContextJsonV2 CreateContextWithInjection(
        string controlChar, string field, string entityType)
    {
        var entity = new CadContextEntityV2
        {
            Handle = "1",
            OwnerSpaceHandle = "1F",
            EntityType = entityType,
            StateHash = new string('a', 64),
            Layer = field == "layer" ? "safe" + controlChar : "0",
        };

        switch (entityType)
        {
            case CadContextEntityTypesV2.DbText:
                entity.DbText = new CadContextDbTextV2
                {
                    Text = field == "text" ? "text" + controlChar + "safe" : "safe text",
                    Position = new CadPoint3(1, 2, 0),
                    Height = 2.5,
                    Rotation = 0,
                };
                break;
            case CadContextEntityTypesV2.Line:
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(0, 0, 0),
                    End = new CadPoint3(10, 0, 0),
                };
                break;
            case CadContextEntityTypesV2.BlockReference:
                entity.BlockReference = new CadContextBlockReferenceV2
                {
                    Position = new CadPoint3(3, 4, 0),
                    Rotation = 0,
                    Scale = new CadPoint3(1, 1, 1),
                    EffectiveName = field == "name" ? "Block" + controlChar : "SafeBlock",
                    IsDynamic = false,
                    IsExternalReference = false,
                };
                break;
        }

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-004",
                DrawingFingerprint = new string('b', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('c', 64),
                EntityCount = 1,
                ParsedEntityCount = 1,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities = [entity],
            },
        };
    }
}
