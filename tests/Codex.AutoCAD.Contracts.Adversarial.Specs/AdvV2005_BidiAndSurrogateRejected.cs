using System;
using System.Linq;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-005: U+202E、U+200B、孤立高/低代理项稳定拒绝。
/// </summary>
public static class AdvV2005_BidiAndSurrogateRejected
{
    public static void Run()
    {
        // U+202E (Right-to-Left Override)
        TestBidiChar("\u202E", "RTO", "context_v2_text_unicode");

        // U+200B (Zero Width Space)
        TestBidiChar("\u200B", "ZWS", "context_v2_text_unicode");

        // U+200C (Zero Width Non-Joiner)
        TestBidiChar("\u200C", "ZWNJ", "context_v2_text_unicode");

        // U+200D (Zero Width Joiner)
        TestBidiChar("\u200D", "ZWJ", "context_v2_text_unicode");

        // U+2028 (Line Separator)
        TestBidiChar("\u2028", "LS", "context_v2_text_unicode");

        // U+2029 (Paragraph Separator)
        TestBidiChar("\u2029", "PS", "context_v2_text_unicode");

        // 孤立高代理项
        TestIsolatedSurrogate("\uD800", "High Surrogate");

        // 孤立低代理项
        TestIsolatedSurrogate("\uDC00", "Low Surrogate");

        // 代理项对中的低代理项在前
        TestReversedSurrogatePair();
    }

    private static void TestBidiChar(string bidiChar, string charName, string expectedCode)
    {
        var context = CreateContext("text" + bidiChar + "safe");
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == expectedCode))
        {
            throw new InvalidOperationException(
                $"{charName} (U+{(int)bidiChar[0]:X4}): expected {expectedCode} failure.");
        }
    }

    private static void TestIsolatedSurrogate(string surrogate, string name)
    {
        // 孤立代理项应该被拒绝
        var context = CreateContext("text" + surrogate + "safe");
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == "context_v2_text_unicode"))
        {
            throw new InvalidOperationException(
                $"{name}: expected context_v2_text_unicode failure.");
        }
    }

    private static void TestReversedSurrogatePair()
    {
        // 低代理项在前，高代理项在后
        var reversed = "\uDC00\uD800";
        var context = CreateContext("text" + reversed + "safe");
        var failures = CadContextJsonV2Validator.Validate(context);

        if (!failures.Any(f => f.Code == "context_v2_text_unicode"))
        {
            throw new InvalidOperationException(
                "Reversed surrogate pair: expected context_v2_text_unicode failure.");
        }
    }

    private static CadContextJsonV2 CreateContext(string text)
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-005",
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
                            Text = text,
                            Position = new CadPoint3(1, 2, 0),
                            Height = 2.5,
                            Rotation = 0,
                        },
                    },
                ],
            },
        };
    }
}
