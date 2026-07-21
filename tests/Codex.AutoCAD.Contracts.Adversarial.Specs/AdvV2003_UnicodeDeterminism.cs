using System;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-003: 中文、合法emoji、换行、制表符、组合Unicode的相同代码单元输入保持确定性。
/// </summary>
public static class AdvV2003_UnicodeDeterminism
{
    public static void Run()
    {
        var testCases = new TestCase[]
        {
            new TestCase("中文文字", "这是中文测试文字"),
            new TestCase("合法emoji", "测试🙂🎉"),
            new TestCase("换行符", "第一行\n第二行"),
            new TestCase("制表符", "列1\t列2"),
            new TestCase("组合Unicode", "café"),
            new TestCase("CJK扩展", "𠀀𠀁"),
        };

        foreach (var testCase in testCases)
        {
            var context = CreateContext(testCase.Text);
            var json1 = CadContextJsonV2Codec.SerializeCanonical(context);
            var json2 = CadContextJsonV2Codec.SerializeCanonical(context);
            var json3 = CadContextJsonV2Codec.SerializeCanonical(context);

            if (json1 != json2 || json2 != json3)
            {
                throw new InvalidOperationException(
                    testCase.Name + ": canonical JSON not deterministic across runs.");
            }

            var hash1 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
            var hash2 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
            var hash3 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

            if (hash1 != hash2 || hash2 != hash3)
            {
                throw new InvalidOperationException(
                    testCase.Name + ": canonical hash not deterministic across runs.");
            }
        }
    }

    private sealed class TestCase
    {
        public TestCase(string name, string text)
        {
            Name = name;
            Text = text;
        }

        public string Name { get; }
        public string Text { get; }
    }

    private static CadContextJsonV2 CreateContext(string text)
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-003",
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
                Entities = new CadContextEntityV2[]
                {
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.DbText,
                        StateHash = new string('c', 64),
                        Layer = "0",
                        DbText = new CadContextDbTextV2
                        {
                            Text = text,
                            Position = new CadPoint3(1, 2, 0),
                            Height = 2.5,
                            Rotation = 0,
                        },
                    },
                },
            },
        };
    }
}
