using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    internal sealed class V2SelectionSnapshot
    {
        internal V2SelectionSnapshot(CadContextSelectionV2 selection, int canonicalLength)
        {
            Selection = selection;
            CanonicalLength = canonicalLength;
        }

        internal CadContextSelectionV2 Selection { get; private set; }

        internal int CanonicalLength { get; private set; }
    }

    internal static class CanonicalSelectionHashV2
    {
        private const string ZeroHash =
            "0000000000000000000000000000000000000000000000000000000000000000";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static V2SelectionSnapshot Build(IList<CadContextEntityV2> drafts)
        {
            if (drafts == null || drafts.Count == 0)
            {
                throw new ContextValidationException("v2-empty-selection");
            }

            if (drafts.Count > CadContextJsonV2Constants.MaximumEntities)
            {
                throw new ContextValidationException("v2-entity-limit");
            }

            var ordered = new List<CadContextEntityV2>(drafts);
            for (var index = 0; index < ordered.Count; index++)
            {
                if (ordered[index] == null)
                {
                    throw new ContextValidationException("v2-null-entity");
                }
            }
            ordered.Sort(CompareEntities);

            ulong? previousHandle = null;
            var unsupportedCount = 0;
            for (var index = 0; index < ordered.Count; index++)
            {
                var entity = ordered[index];
                var handle = ParseHandle(entity.Handle);
                if (previousHandle.HasValue && previousHandle.Value == handle)
                {
                    throw new ContextValidationException("v2-duplicate-handle");
                }
                previousHandle = handle;

                entity.StateHash = ZeroHash;
                entity.StateHash = ComputeEntityStateHash(entity);
                if (string.Equals(
                        entity.EntityType,
                        CadContextEntityTypesV2.Unsupported,
                        StringComparison.Ordinal))
                {
                    unsupportedCount++;
                }
            }

            var selectionCanonical = BuildSelectionCanonical(ordered);
            string snapshotHash;
            try
            {
                snapshotHash = ComputeSha256Lower(selectionCanonical);
            }
            finally
            {
                Array.Clear(selectionCanonical, 0, selectionCanonical.Length);
            }

            var selection = new CadContextSelectionV2
            {
                SnapshotHash = snapshotHash,
                EntityCount = ordered.Count,
                ParsedEntityCount = ordered.Count - unsupportedCount,
                UnsupportedEntityCount = unsupportedCount,
                Complete = unsupportedCount == 0,
                Entities = ordered.ToArray(),
            };

            ValidateSelection(selection);
            var canonicalLength = GetSelectionCanonicalLength(ordered);
            return new V2SelectionSnapshot(selection, canonicalLength);
        }

        private static string ComputeEntityStateHash(CadContextEntityV2 entity)
        {
            var unsupported = string.Equals(
                entity.EntityType,
                CadContextEntityTypesV2.Unsupported,
                StringComparison.Ordinal);
            var context = new CadContextJsonV2
            {
                CapturedAtUtc = "2000-01-01T00:00:00.000Z",
                Document = new CadContextDocumentV2
                {
                    DocumentId = "entity-state-v2",
                    DrawingFingerprint = ZeroHash,
                    Revision = 0,
                    CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                    DrawingVersion = "R20.1",
                    Units = "Unitless",
                },
                Selection = new CadContextSelectionV2
                {
                    SnapshotHash = ZeroHash,
                    EntityCount = 1,
                    ParsedEntityCount = unsupported ? 0 : 1,
                    UnsupportedEntityCount = unsupported ? 1 : 0,
                    Complete = !unsupported,
                    Entities = new[] { entity },
                },
            };

            var failures = CadContextJsonV2Validator.Validate(context);
            if (failures.Length != 0)
            {
                throw new ContextValidationException(
                    "v2-" + failures[0].Code);
            }
            return CadContextJsonV2Codec.ComputeCanonicalSha256(context);
        }

        private static void ValidateSelection(CadContextSelectionV2 selection)
        {
            var context = new CadContextJsonV2
            {
                CapturedAtUtc = "2000-01-01T00:00:00.000Z",
                Document = new CadContextDocumentV2
                {
                    DocumentId = "selection-state-v2",
                    DrawingFingerprint = ZeroHash,
                    Revision = 0,
                    CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                    DrawingVersion = "R20.1",
                    Units = "Unitless",
                },
                Selection = selection,
            };
            var failures = CadContextJsonV2Validator.Validate(context);
            if (failures.Length != 0)
            {
                throw new ContextValidationException(
                    "v2-" + failures[0].Code);
            }
        }

        private static byte[] BuildSelectionCanonical(IList<CadContextEntityV2> ordered)
        {
            var builder = new StringBuilder(256 + (ordered.Count * 96));
            builder.Append("CDXCTXS2");
            builder.Append('\n');
            builder.Append("2");
            builder.Append('\n');
            builder.Append(ordered.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
            for (var index = 0; index < ordered.Count; index++)
            {
                var entity = ordered[index];
                builder.Append(entity.Handle);
                builder.Append(':');
                builder.Append(entity.StateHash);
                builder.Append('\n');
            }
            try
            {
                return StrictUtf8.GetBytes(builder.ToString());
            }
            catch (EncoderFallbackException)
            {
                throw new ContextValidationException("v2-selection-unicode");
            }
        }

        private static int GetSelectionCanonicalLength(IList<CadContextEntityV2> ordered)
        {
            var bytes = BuildSelectionCanonical(ordered);
            try
            {
                return bytes.Length;
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static int CompareEntities(CadContextEntityV2 left, CadContextEntityV2 right)
        {
            var leftValue = ParseHandle(left.Handle);
            var rightValue = ParseHandle(right.Handle);
            return leftValue < rightValue ? -1 : leftValue > rightValue ? 1 : 0;
        }

        private static ulong ParseHandle(string value)
        {
            ulong parsed;
            if (string.IsNullOrEmpty(value)
                || !ulong.TryParse(
                    value,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out parsed)
                || parsed == 0)
            {
                throw new ContextValidationException("v2-invalid-handle");
            }
            return parsed;
        }

        private static string ComputeSha256Lower(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                try
                {
                    const string alphabet = "0123456789abcdef";
                    var characters = new char[hash.Length * 2];
                    for (var index = 0; index < hash.Length; index++)
                    {
                        characters[index * 2] = alphabet[hash[index] >> 4];
                        characters[(index * 2) + 1] = alphabet[hash[index] & 0x0F];
                    }
                    return new string(characters);
                }
                finally
                {
                    Array.Clear(hash, 0, hash.Length);
                }
            }
        }
    }
}
