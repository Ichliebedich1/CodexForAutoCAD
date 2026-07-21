using System;
using System.Linq;
using System.Reflection;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-009: 反射v2 DTO公共属性和canonical属性名，
/// 确认不存在documentPath/path/exception/stackTrace/trustedPaths/apiKey/token/credential/externalReferencePath等隐私字段；
/// 不要错误禁止普通CAD文本值。
/// </summary>
public static class AdvV2009_NoPrivacyFieldsInDto
{
    public static void Run()
    {
        var forbiddenFields = new[]
        {
            "documentPath",
            "path",
            "exception",
            "stackTrace",
            "trustedPaths",
            "apiKey",
            "token",
            "credential",
            "externalReferencePath",
            "password",
            "secret",
            "authorization",
        };

        // 检查所有v2 DTO类型
        var v2Types = new[]
        {
            typeof(CadContextJsonV2),
            typeof(CadContextDocumentV2),
            typeof(CadContextSelectionV2),
            typeof(CadContextEntityV2),
            typeof(CadContextLineV2),
            typeof(CadContextCircleV2),
            typeof(CadContextPolylineV2),
            typeof(CadContextDbTextV2),
            typeof(CadContextMTextV2),
            typeof(CadContextBlockReferenceV2),
            typeof(CadContextArcV2),
            typeof(CadContextEllipseV2),
            typeof(CadContextSplineV2),
            typeof(CadContextPointV2),
            typeof(CadContextRayV2),
            typeof(CadContextXlineV2),
            typeof(CadContextPolyline2dV2),
            typeof(CadContextPolyline3dV2),
            typeof(CadContextDimensionV2),
            typeof(CadContextHatchV2),
            typeof(CadContextLeaderV2),
            typeof(CadContextMLeaderV2),
            typeof(CadContextTableV2),
            typeof(CadContextUnsupportedV2),
        };

        foreach (var type in v2Types)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var propNameLower = prop.Name.ToLowerInvariant();
                foreach (var forbidden in forbiddenFields)
                {
                    if (propNameLower.Contains(forbidden))
                    {
                        throw new InvalidOperationException(
                            $"Type {type.Name} has forbidden property: {prop.Name}");
                    }
                }
            }
        }

        // 验证规范化JSON不含敏感信息
        var context = CreateFullContext();
        var json = CadContextJsonV2Codec.SerializeCanonical(context);

        var forbiddenTexts = new[]
        {
            "\"documentPath\"",
            "\"exception\"",
            "\"stackTrace\"",
            "\"trustedPaths\"",
            "\"apiKey\"",
            "\"token\"",
            "\"credential\"",
            "\"externalReferencePath\"",
            "\"password\"",
            "\"secret\"",
            "C:\\",
            "D:\\",
            "\\\\server\\",
            "Bearer ",
            "Authorization:",
        };

        foreach (var forbidden in forbiddenTexts)
        {
            if (json.Contains(forbidden))
            {
                throw new InvalidOperationException(
                    $"Canonical JSON contains forbidden text: {forbidden}");
            }
        }

        // 验证普通CAD文本值被允许
        if (!json.Contains("中文表格"))
        {
            throw new InvalidOperationException(
                "Canonical JSON should contain normal CAD text values.");
        }
    }

    private static CadContextJsonV2 CreateFullContext()
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-009",
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
                        EntityType = CadContextEntityTypesV2.DbText,
                        StateHash = new string('c', 64),
                        Layer = "0",
                        DbText = new CadContextDbTextV2
                        {
                            Text = "中文表格",
                            Position = new CadPoint3(1, 2, 0),
                            Height = 2.5,
                            Rotation = 0,
                        },
                    },
                    new CadContextEntityV2
                    {
                        Handle = "2",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.MText,
                        StateHash = new string('d', 64),
                        Layer = "0",
                        MText = new CadContextMTextV2
                        {
                            Text = "第一行\n第二行\t🙂",
                            Location = new CadPoint3(2, 3, 0),
                            TextHeight = 3,
                            Rotation = 0.2,
                        },
                    },
                ],
            },
        };
    }
}
