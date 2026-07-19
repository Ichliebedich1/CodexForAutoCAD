using System;
using System.Collections.Generic;
using System.Globalization;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext.Specs
{
    internal static class Program
    {
        private const string LineEntityCanonicalHex = "4344584354584531010000000110000000000000000a000000000000000100000030000000000000f03f00000000000000400000000000000840000000000000104000000000000014400000000000001840";
        private const string LineEntityHash = "0dd7a46d82cb5cde49b2beddadcb59f6cad3f53032a870e10336467fee32def8";
        private const string LineSelectionCanonicalHex = "43445843545853310100000001000000520000004344584354584531010000000110000000000000000a000000000000000100000030000000000000f03f00000000000000400000000000000840000000000000104000000000000014400000000000001840";
        private const string LineSelectionHash = "342aa5f2046f63638a21205d6293b81c4d31bae6b57ab3ad0658a8ef37e129bd";
        private const string MixedSelectionHash = "1418bea1192e1a9d1e2f011cc900ca54ad4b41739803714da66092ecd8b6c938";
        private const string UnicodeSelectionHash = "bca19bbdebb6b6f591b751e3ffc76e3e6d7db5198639da9f12b14e36453c8bfb";

        private static readonly List<string> Passed = new List<string>();

        private static int Main(string[] arguments)
        {
            if (arguments.Length == 1 && arguments[0] == "--emit-vectors")
            {
                EmitVectors();
                return 0;
            }

            try
            {
                Run("empty selection is rejected", EmptySelectionIsRejected);
                Run("line golden vector is frozen", LineGoldenVectorIsFrozen);
                Run("mixed six-kind vector is frozen", MixedVectorIsFrozen);
                Run("unicode vector is frozen", UnicodeVectorIsFrozen);
                Run("reference encoder agrees", ReferenceEncoderAgrees);
                Run("input order is canonical", InputOrderIsCanonical);
                Run("handles sort numerically", HandlesSortNumerically);
                Run("duplicate handles are rejected", DuplicateHandlesAreRejected);
                Run("every exported field is bound", EveryExportedFieldIsBound);
                Run("positive and negative zero differ", PositiveAndNegativeZeroDiffer);
                Run("subnormal and max finite values are accepted", FiniteEdgeValuesAreAccepted);
                Run("NaN is rejected", NanIsRejected);
                Run("infinity is rejected", InfinityIsRejected);
                Run("isolated surrogate is rejected", IsolatedSurrogateIsRejected);
                Run("NUL is rejected", NulIsRejected);
                Run("Unicode format character is rejected", UnicodeFormatCharacterIsRejected);
                Run("text controls are constrained", TextControlsAreConstrained);
                Run("65 entities are rejected", EntityLimitIsEnforced);
                Run("257 polyline vertices are rejected", PolylineVertexLimitIsEnforced);
                Run("2049 text characters are rejected", TextLimitIsEnforced);
                Run("long names are rejected", NameLimitIsEnforced);
                Run("64 KiB canonical budget is enforced", CanonicalBudgetIsEnforced);
                Run("culture does not affect hashes", CultureDoesNotAffectHashes);
                Run("snapshot defensively copies entities", SnapshotDefensivelyCopiesEntities);
                Run("hashes are lowercase sha256", HashesAreLowercaseSha256);

                Console.WriteLine("ReadOnlyContext Specs: {0}/{0} passed.", Passed.Count);
                for (var index = 0; index < Passed.Count; index++)
                {
                    Console.WriteLine("PASS {0}", Passed[index]);
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL {0}", exception.Message);
                return 1;
            }
        }

        private static void EmitVectors()
        {
            var line = Line(0x10, 0xA, "0", 1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
            var lineRecord = ReferenceSelectionEncoder.EncodeEntity(line);
            var lineSelection = ReferenceSelectionEncoder.EncodeSelection(new[] { line });
            var mixed = ReferenceSelectionEncoder.EncodeSelection(Mixed());
            var unicode = ReferenceSelectionEncoder.EncodeSelection(new[] { UnicodeText() });

            Console.WriteLine("LINE_ENTITY_CANONICAL=" + ReferenceSelectionEncoder.ToHex(lineRecord));
            Console.WriteLine("LINE_ENTITY_HASH=" + ReferenceSelectionEncoder.ComputeHash(lineRecord));
            Console.WriteLine("LINE_SELECTION_CANONICAL=" + ReferenceSelectionEncoder.ToHex(lineSelection.Canonical));
            Console.WriteLine("LINE_SELECTION_HASH=" + lineSelection.Hash);
            Console.WriteLine("MIXED_SELECTION_HASH=" + mixed.Hash);
            Console.WriteLine("UNICODE_SELECTION_HASH=" + unicode.Hash);
        }

        private static void EmptySelectionIsRejected()
        {
            Throws("empty-selection", delegate { CanonicalSelectionHash.Build(new ContextEntityDraft[0]); });
        }

        private static void LineGoldenVectorIsFrozen()
        {
            var line = Line(0x10, 0xA, "0", 1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
            var record = ReferenceSelectionEncoder.EncodeEntity(line);
            var snapshot = CanonicalSelectionHash.Build(new[] { line });
            Equal(LineEntityCanonicalHex, ReferenceSelectionEncoder.ToHex(record));
            Equal(LineEntityHash, snapshot.Entities[0].StateHash);
            Equal(LineSelectionHash, snapshot.SnapshotHash);
            Equal(LineSelectionCanonicalHex, ReferenceSelectionEncoder.ToHex(
                ReferenceSelectionEncoder.EncodeSelection(new[] { line }).Canonical));
        }

        private static void MixedVectorIsFrozen()
        {
            Equal(MixedSelectionHash, CanonicalSelectionHash.Build(Mixed()).SnapshotHash);
        }

        private static void UnicodeVectorIsFrozen()
        {
            Equal(
                UnicodeSelectionHash,
                CanonicalSelectionHash.Build(new[] { UnicodeText() }).SnapshotHash);
        }

        private static void ReferenceEncoderAgrees()
        {
            var line = Line(0x10, 0xA, "0", 1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
            var productionLine = CanonicalSelectionHash.Build(new[] { line });
            var referenceLine = ReferenceSelectionEncoder.EncodeSelection(new[] { line });
            Equal(referenceLine.Hash, productionLine.SnapshotHash);
            Equal(ReferenceSelectionEncoder.ComputeHash(
                ReferenceSelectionEncoder.EncodeEntity(line)), productionLine.Entities[0].StateHash);

            var productionMixed = CanonicalSelectionHash.Build(Mixed());
            var referenceMixed = ReferenceSelectionEncoder.EncodeSelection(Mixed());
            Equal(referenceMixed.Hash, productionMixed.SnapshotHash);
        }

        private static void InputOrderIsCanonical()
        {
            var forward = Mixed();
            var reverse = new List<ContextEntityDraft>(forward);
            reverse.Reverse();
            Equal(
                CanonicalSelectionHash.Build(forward).SnapshotHash,
                CanonicalSelectionHash.Build(reverse).SnapshotHash);
        }

        private static void HandlesSortNumerically()
        {
            var fifteen = Line(0xF, 0xA, "0", 0, 0, 0, 1, 0, 0);
            var sixteen = Line(0x10, 0xA, "0", 0, 0, 0, 2, 0, 0);
            var first = CanonicalSelectionHash.Build(new[] { sixteen, fifteen });
            var second = CanonicalSelectionHash.Build(new[] { fifteen, sixteen });
            Equal(first.SnapshotHash, second.SnapshotHash);
            Equal((ulong)0xF, first.Entities[0].Draft.Handle);
        }

        private static void DuplicateHandlesAreRejected()
        {
            Throws(
                "duplicate-handle",
                delegate
                {
                    CanonicalSelectionHash.Build(new[]
                    {
                        Line(0x10, 0xA, "0", 0, 0, 0, 1, 0, 0),
                        Circle(0x10, 0xA, "0", 0, 0, 0, 1, 0, 0, 1),
                    });
                });
        }

        private static void EveryExportedFieldIsBound()
        {
            AssertChanged(
                Line(0x10, 0xA, "0", 1, 2, 3, 4, 5, 6),
                Line(0x11, 0xA, "0", 1, 2, 3, 4, 5, 6),
                Line(0x10, 0xB, "0", 1, 2, 3, 4, 5, 6),
                Line(0x10, 0xA, "A", 1, 2, 3, 4, 5, 6),
                Line(0x10, 0xA, "0", 9, 2, 3, 4, 5, 6),
                Line(0x10, 0xA, "0", 1, 2, 3, 4, 5, 9));

            AssertChanged(
                Circle(0x20, 0xA, "0", 1, 2, 3, 4, 0, 0, 1),
                Circle(0x20, 0xA, "0", 9, 2, 3, 4, 0, 0, 1),
                Circle(0x20, 0xA, "0", 1, 2, 3, 9, 0, 0, 1),
                Circle(0x20, 0xA, "0", 1, 2, 3, 4, 1, 0, 1));

            AssertChanged(
                Polyline(0x30, false, 0.0, 0.0),
                Polyline(0x30, true, 0.0, 0.0),
                Polyline(0x30, false, 2.0, 0.0),
                Polyline(0x30, false, 0.0, 0.5),
                Polyline(0x30, false, 0.0, 0.0, 9.0));

            AssertChanged(
                DbText(0x40, "文本", 1, 2, 3, 2.5, 0.1),
                DbText(0x40, "另一文本", 1, 2, 3, 2.5, 0.1),
                DbText(0x40, "文本", 9, 2, 3, 2.5, 0.1),
                DbText(0x40, "文本", 1, 2, 3, 3.5, 0.1),
                DbText(0x40, "文本", 1, 2, 3, 2.5, 0.2));

            AssertChanged(
                MText(0x50, "多行", 1, 2, 3, 2.5, 0.1),
                MText(0x50, "另一行", 1, 2, 3, 2.5, 0.1),
                MText(0x50, "多行", 9, 2, 3, 2.5, 0.1),
                MText(0x50, "多行", 1, 2, 3, 3.5, 0.1),
                MText(0x50, "多行", 1, 2, 3, 2.5, 0.2));

            AssertChanged(
                Block(0x60, "BLOCK-A", false, false, 1, 2, 3, 0.1, 1, 1, 1),
                Block(0x60, "BLOCK-B", false, false, 1, 2, 3, 0.1, 1, 1, 1),
                Block(0x60, "BLOCK-A", true, false, 1, 2, 3, 0.1, 1, 1, 1),
                Block(0x60, "BLOCK-A", false, true, 1, 2, 3, 0.1, 1, 1, 1),
                Block(0x60, "BLOCK-A", false, false, 9, 2, 3, 0.1, 1, 1, 1),
                Block(0x60, "BLOCK-A", false, false, 1, 2, 3, 0.2, 1, 1, 1),
                Block(0x60, "BLOCK-A", false, false, 1, 2, 3, 0.1, 2, 1, 1));
        }

        private static void PositiveAndNegativeZeroDiffer()
        {
            var negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
            AssertChanged(
                Line(0x10, 0xA, "0", 0.0, 0, 0, 1, 0, 0),
                Line(0x10, 0xA, "0", negativeZero, 0, 0, 1, 0, 0));
        }

        private static void FiniteEdgeValuesAreAccepted()
        {
            var subnormal = BitConverter.Int64BitsToDouble(1);
            var snapshot = CanonicalSelectionHash.Build(new[]
            {
                Line(0x10, 0xA, "0", subnormal, double.MaxValue, -double.MaxValue, 1, 2, 3),
            });
            Equal(1, snapshot.Entities.Count);
        }

        private static void NanIsRejected()
        {
            Throws("non-finite-double", delegate
            {
                CanonicalSelectionHash.Build(new[]
                {
                    Line(0x10, 0xA, "0", double.NaN, 0, 0, 1, 0, 0),
                });
            });
        }

        private static void InfinityIsRejected()
        {
            Throws("non-finite-double", delegate
            {
                CanonicalSelectionHash.Build(new[]
                {
                    Circle(0x20, 0xA, "0", 0, 0, 0, double.PositiveInfinity, 0, 0, 1),
                });
            });
        }

        private static void IsolatedSurrogateIsRejected()
        {
            Throws("isolated-surrogate", delegate
            {
                CanonicalSelectionHash.Build(new[] { DbText(0x40, "\uD800", 0, 0, 0, 1, 0) });
            });
        }

        private static void NulIsRejected()
        {
            Throws("nul-character", delegate
            {
                CanonicalSelectionHash.Build(new[] { DbText(0x40, "A\0B", 0, 0, 0, 1, 0) });
            });
        }

        private static void UnicodeFormatCharacterIsRejected()
        {
            Throws("unicode-format-character", delegate
            {
                CanonicalSelectionHash.Build(new[] { DbText(0x40, "A\u202EB", 0, 0, 0, 1, 0) });
            });
        }

        private static void TextControlsAreConstrained()
        {
            CanonicalSelectionHash.Build(new[] { DbText(0x40, "A\r\nB\tC", 0, 0, 0, 1, 0) });
            Throws("control-character", delegate
            {
                CanonicalSelectionHash.Build(new[] { DbText(0x40, "A\u0001B", 0, 0, 0, 1, 0) });
            });
        }

        private static void EntityLimitIsEnforced()
        {
            var drafts = new List<ContextEntityDraft>();
            for (var index = 0; index < 65; index++)
            {
                drafts.Add(Line((ulong)(0x100 + index), 0xA, "0", index, 0, 0, index + 1, 0, 0));
            }

            Throws("entity-limit", delegate { CanonicalSelectionHash.Build(drafts); });
        }

        private static void PolylineVertexLimitIsEnforced()
        {
            var vertices = new List<ContextPolylineVertex>();
            for (var index = 0; index < 257; index++)
            {
                vertices.Add(new ContextPolylineVertex(new ContextPoint2(index, 0), 0));
            }

            Throws("polyline-vertex-limit", delegate
            {
                CanonicalSelectionHash.Build(new[] { PolylineWithVertices(0x30, vertices) });
            });
        }

        private static void TextLimitIsEnforced()
        {
            Throws("utf16-character-limit", delegate
            {
                CanonicalSelectionHash.Build(new[]
                {
                    DbText(0x40, new string('文', 2049), 0, 0, 0, 1, 0),
                });
            });
        }

        private static void NameLimitIsEnforced()
        {
            Throws("utf16-character-limit", delegate
            {
                CanonicalSelectionHash.Build(new[]
                {
                    Line(0x10, 0xA, new string('L', 256), 0, 0, 0, 1, 0, 0),
                });
            });
        }

        private static void CanonicalBudgetIsEnforced()
        {
            var drafts = new List<ContextEntityDraft>();
            for (var index = 0; index < 64; index++)
            {
                drafts.Add(DbText(
                    (ulong)(0x200 + index),
                    new string('A', CanonicalSelectionHash.MaximumTextCharacters),
                    index,
                    0,
                    0,
                    1,
                    0));
            }

            Throws("canonical-byte-limit", delegate { CanonicalSelectionHash.Build(drafts); });
        }

        private static void CultureDoesNotAffectHashes()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                string expected = null;
                var names = new[] { "zh-CN", "tr-TR", "fr-FR" };
                for (var index = 0; index < names.Length; index++)
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(names[index]);
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(names[index]);
                    var actual = CanonicalSelectionHash.Build(Mixed()).SnapshotHash;
                    expected = expected ?? actual;
                    Equal(expected, actual);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private static void SnapshotDefensivelyCopiesEntities()
        {
            var source = new List<ContextEntityDraft>
            {
                Line(0x10, 0xA, "0", 0, 0, 0, 1, 0, 0),
            };
            var snapshot = CanonicalSelectionHash.Build(source);
            source.Clear();
            Equal(1, snapshot.Entities.Count);
        }

        private static void HashesAreLowercaseSha256()
        {
            var snapshot = CanonicalSelectionHash.Build(Mixed());
            AssertLowerHash(snapshot.SnapshotHash);
            for (var index = 0; index < snapshot.Entities.Count; index++)
            {
                AssertLowerHash(snapshot.Entities[index].StateHash);
            }
        }

        private static IList<ContextEntityDraft> Mixed()
        {
            return new List<ContextEntityDraft>
            {
                Line(0x10, 0xA, "0", 1, 2, 3, 4, 5, 6),
                Circle(0x11, 0xA, "圆层", 10, 20, 30, 25, 0, 0, 1),
                Polyline(0x12, true, 3.5, 0.25),
                UnicodeText(),
                MText(0x14, "MText\n中文", 7, 8, 9, 2.5, 0.5),
                Block(0x15, "DYN-门", true, false, 5, 6, 7, 0.25, 1, 2, 1),
            };
        }

        private static ContextEntityDraft UnicodeText()
        {
            return DbText(0x13, "焊缝 Φ25\r\n第二行\t😀", 1, 2, 0, 3.5, 0.125);
        }

        private static ContextEntityDraft Line(
            ulong handle,
            ulong owner,
            string layer,
            double sx,
            double sy,
            double sz,
            double ex,
            double ey,
            double ez)
        {
            return Draft(
                ContextEntityKind.Line,
                handle,
                owner,
                layer,
                new ContextLineData(new ContextPoint3(sx, sy, sz), new ContextPoint3(ex, ey, ez)),
                null,
                null,
                null,
                null,
                null);
        }

        private static ContextEntityDraft Circle(
            ulong handle,
            ulong owner,
            string layer,
            double x,
            double y,
            double z,
            double radius,
            double nx,
            double ny,
            double nz)
        {
            return Draft(
                ContextEntityKind.Circle,
                handle,
                owner,
                layer,
                null,
                new ContextCircleData(
                    new ContextPoint3(x, y, z),
                    radius,
                    new ContextVector3(nx, ny, nz)),
                null,
                null,
                null,
                null);
        }

        private static ContextEntityDraft Polyline(
            ulong handle,
            bool closed,
            double elevation,
            double bulge,
            double firstX = 0.0)
        {
            return PolylineWithVertices(
                handle,
                new List<ContextPolylineVertex>
                {
                    new ContextPolylineVertex(new ContextPoint2(firstX, 0), bulge),
                    new ContextPolylineVertex(new ContextPoint2(10, 0), 0),
                    new ContextPolylineVertex(new ContextPoint2(10, 10), -0.25),
                },
                closed,
                elevation);
        }

        private static ContextEntityDraft PolylineWithVertices(
            ulong handle,
            IList<ContextPolylineVertex> vertices,
            bool closed = false,
            double elevation = 0.0)
        {
            return Draft(
                ContextEntityKind.Polyline,
                handle,
                0xA,
                "PL",
                null,
                null,
                new ContextPolylineData(
                    closed,
                    elevation,
                    new ContextVector3(0, 0, 1),
                    vertices),
                null,
                null,
                null);
        }

        private static ContextEntityDraft DbText(
            ulong handle,
            string text,
            double x,
            double y,
            double z,
            double height,
            double rotation)
        {
            return Draft(
                ContextEntityKind.DbText,
                handle,
                0xA,
                "TEXT",
                null,
                null,
                null,
                new ContextDbTextData(text, new ContextPoint3(x, y, z), height, rotation),
                null,
                null);
        }

        private static ContextEntityDraft MText(
            ulong handle,
            string text,
            double x,
            double y,
            double z,
            double height,
            double rotation)
        {
            return Draft(
                ContextEntityKind.MText,
                handle,
                0xA,
                "MTEXT",
                null,
                null,
                null,
                null,
                new ContextMTextData(text, new ContextPoint3(x, y, z), height, rotation),
                null);
        }

        private static ContextEntityDraft Block(
            ulong handle,
            string name,
            bool dynamic,
            bool xref,
            double x,
            double y,
            double z,
            double rotation,
            double sx,
            double sy,
            double sz)
        {
            return Draft(
                ContextEntityKind.BlockReference,
                handle,
                0xA,
                "BLOCK",
                null,
                null,
                null,
                null,
                null,
                new ContextBlockData(
                    new ContextPoint3(x, y, z),
                    rotation,
                    new ContextVector3(sx, sy, sz),
                    name,
                    dynamic,
                    xref));
        }

        private static ContextEntityDraft Draft(
            ContextEntityKind kind,
            ulong handle,
            ulong owner,
            string layer,
            ContextLineData line,
            ContextCircleData circle,
            ContextPolylineData polyline,
            ContextDbTextData dbText,
            ContextMTextData mText,
            ContextBlockData block)
        {
            return new ContextEntityDraft(
                kind,
                handle,
                owner,
                layer,
                line,
                circle,
                polyline,
                dbText,
                mText,
                block);
        }

        private static void AssertChanged(ContextEntityDraft baseline, params ContextEntityDraft[] variants)
        {
            var expected = CanonicalSelectionHash.Build(new[] { baseline }).Entities[0].StateHash;
            for (var index = 0; index < variants.Length; index++)
            {
                var actual = CanonicalSelectionHash.Build(new[] { variants[index] }).Entities[0].StateHash;
                NotEqual(expected, actual);
            }
        }

        private static void AssertLowerHash(string value)
        {
            Equal(64, value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                True((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
            }
        }

        private static void Throws(string expectedCode, Action action)
        {
            try
            {
                action();
            }
            catch (ContextValidationException exception)
            {
                Equal(expectedCode, exception.Code);
                return;
            }

            throw new InvalidOperationException("Expected ContextValidationException: " + expectedCode + ".");
        }

        private static void Run(string name, Action action)
        {
            action();
            Passed.Add(name);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected " + expected + " but got " + actual + ".");
            }
        }

        private static void NotEqual<T>(T left, T right)
        {
            if (EqualityComparer<T>.Default.Equals(left, right))
            {
                throw new InvalidOperationException("Values must differ: " + left + ".");
            }
        }

        private static void True(bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }
    }
}
