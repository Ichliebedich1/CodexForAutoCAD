using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    internal static class CanonicalSelectionHash
    {
        internal const int MaximumEntities = 64;
        internal const int MaximumPolylineVertices = 256;
        internal const int MaximumTextCharacters = 2048;
        internal const int MaximumCanonicalBytes = 64 * 1024;

        private const int MaximumNameCharacters = 255;
        private const int MaximumNameBytes = 1024;
        private const int MaximumTextBytes = 8192;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static ContextSelectionSnapshot Build(IList<ContextEntityDraft> drafts)
        {
            if (drafts == null || drafts.Count == 0)
            {
                throw new ContextValidationException("empty-selection");
            }

            if (drafts.Count > MaximumEntities)
            {
                throw new ContextValidationException("entity-limit");
            }

            var ordered = new List<ContextEntityDraft>(drafts);
            ordered.Sort(CompareDrafts);

            var snapshots = new List<ContextEntitySnapshot>(ordered.Count);
            var records = new List<byte[]>(ordered.Count);
            ulong? previousHandle = null;

            for (var index = 0; index < ordered.Count; index++)
            {
                var draft = ordered[index];
                if (draft.Handle == 0 || draft.OwnerSpaceHandle == 0)
                {
                    throw new ContextValidationException("invalid-handle");
                }

                if (previousHandle.HasValue && previousHandle.Value == draft.Handle)
                {
                    throw new ContextValidationException("duplicate-handle");
                }

                previousHandle = draft.Handle;
                var record = EncodeEntity(draft);
                var stateHash = ComputeSha256Lower(record);
                records.Add(record);
                snapshots.Add(new ContextEntitySnapshot(draft, stateHash));
            }

            var selection = new CanonicalBuffer(MaximumCanonicalBytes);
            selection.WriteAscii("CDXCTXS1");
            selection.WriteUInt32(1);
            selection.WriteUInt32(checked((uint)records.Count));
            for (var index = 0; index < records.Count; index++)
            {
                selection.WriteLengthPrefixedBytes(records[index]);
            }

            var canonical = selection.ToArray();
            try
            {
                return new ContextSelectionSnapshot(
                    snapshots,
                    ComputeSha256Lower(canonical),
                    canonical.Length);
            }
            finally
            {
                Array.Clear(canonical, 0, canonical.Length);
                for (var index = 0; index < records.Count; index++)
                {
                    Array.Clear(records[index], 0, records[index].Length);
                }
            }
        }

        private static byte[] EncodeEntity(ContextEntityDraft draft)
        {
            ValidateShape(draft);

            var buffer = new CanonicalBuffer(MaximumCanonicalBytes);
            buffer.WriteAscii("CDXCTXE1");
            buffer.WriteUInt32(1);
            buffer.WriteByte((byte)draft.Kind);
            buffer.WriteUInt64(draft.Handle);
            buffer.WriteUInt64(draft.OwnerSpaceHandle);
            buffer.WriteString(draft.Layer, MaximumNameCharacters, MaximumNameBytes, false);

            switch (draft.Kind)
            {
                case ContextEntityKind.Line:
                    WritePoint3(buffer, draft.Line.Start);
                    WritePoint3(buffer, draft.Line.End);
                    break;
                case ContextEntityKind.Circle:
                    WritePoint3(buffer, draft.Circle.Center);
                    WriteDouble(buffer, draft.Circle.Radius);
                    WriteVector3(buffer, draft.Circle.Normal);
                    break;
                case ContextEntityKind.Polyline:
                    buffer.WriteByte(draft.Polyline.Closed ? (byte)1 : (byte)0);
                    WriteDouble(buffer, draft.Polyline.Elevation);
                    WriteVector3(buffer, draft.Polyline.Normal);
                    if (draft.Polyline.Vertices.Count == 0
                        || draft.Polyline.Vertices.Count > MaximumPolylineVertices)
                    {
                        throw new ContextValidationException("polyline-vertex-limit");
                    }

                    buffer.WriteUInt32(checked((uint)draft.Polyline.Vertices.Count));
                    for (var index = 0; index < draft.Polyline.Vertices.Count; index++)
                    {
                        var vertex = draft.Polyline.Vertices[index];
                        WritePoint2(buffer, vertex.Position);
                        WriteDouble(buffer, vertex.Bulge);
                    }

                    break;
                case ContextEntityKind.DbText:
                    buffer.WriteString(
                        draft.DbText.Text,
                        MaximumTextCharacters,
                        MaximumTextBytes,
                        true);
                    WritePoint3(buffer, draft.DbText.Position);
                    WriteDouble(buffer, draft.DbText.Height);
                    WriteDouble(buffer, draft.DbText.Rotation);
                    break;
                case ContextEntityKind.MText:
                    buffer.WriteString(
                        draft.MText.Text,
                        MaximumTextCharacters,
                        MaximumTextBytes,
                        true);
                    WritePoint3(buffer, draft.MText.Location);
                    WriteDouble(buffer, draft.MText.TextHeight);
                    WriteDouble(buffer, draft.MText.Rotation);
                    break;
                case ContextEntityKind.BlockReference:
                    WritePoint3(buffer, draft.Block.Position);
                    WriteDouble(buffer, draft.Block.Rotation);
                    WriteVector3(buffer, draft.Block.Scale);
                    buffer.WriteString(
                        draft.Block.EffectiveName,
                        MaximumNameCharacters,
                        MaximumNameBytes,
                        false);
                    buffer.WriteByte(draft.Block.Dynamic ? (byte)1 : (byte)0);
                    buffer.WriteByte(draft.Block.Xref ? (byte)1 : (byte)0);
                    break;
                default:
                    throw new ContextValidationException("unsupported-kind");
            }

            return buffer.ToArray();
        }

        private static void ValidateShape(ContextEntityDraft draft)
        {
            var shapeCount = 0;
            shapeCount += draft.Line == null ? 0 : 1;
            shapeCount += draft.Circle == null ? 0 : 1;
            shapeCount += draft.Polyline == null ? 0 : 1;
            shapeCount += draft.DbText == null ? 0 : 1;
            shapeCount += draft.MText == null ? 0 : 1;
            shapeCount += draft.Block == null ? 0 : 1;
            if (shapeCount != 1)
            {
                throw new ContextValidationException("invalid-shape-count");
            }

            var matchesKind =
                (draft.Kind == ContextEntityKind.Line && draft.Line != null)
                || (draft.Kind == ContextEntityKind.Circle && draft.Circle != null)
                || (draft.Kind == ContextEntityKind.Polyline && draft.Polyline != null)
                || (draft.Kind == ContextEntityKind.DbText && draft.DbText != null)
                || (draft.Kind == ContextEntityKind.MText && draft.MText != null)
                || (draft.Kind == ContextEntityKind.BlockReference && draft.Block != null);
            if (!matchesKind)
            {
                throw new ContextValidationException("kind-shape-mismatch");
            }
        }

        private static void WritePoint2(CanonicalBuffer buffer, ContextPoint2 point)
        {
            if (point == null)
            {
                throw new ContextValidationException("null-point2");
            }

            WriteDouble(buffer, point.X);
            WriteDouble(buffer, point.Y);
        }

        private static void WritePoint3(CanonicalBuffer buffer, ContextPoint3 point)
        {
            if (point == null)
            {
                throw new ContextValidationException("null-point3");
            }

            WriteDouble(buffer, point.X);
            WriteDouble(buffer, point.Y);
            WriteDouble(buffer, point.Z);
        }

        private static void WriteVector3(CanonicalBuffer buffer, ContextVector3 vector)
        {
            if (vector == null)
            {
                throw new ContextValidationException("null-vector3");
            }

            WriteDouble(buffer, vector.X);
            WriteDouble(buffer, vector.Y);
            WriteDouble(buffer, vector.Z);
        }

        private static void WriteDouble(CanonicalBuffer buffer, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ContextValidationException("non-finite-double");
            }

            buffer.WriteUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        }

        private static int CompareDrafts(ContextEntityDraft left, ContextEntityDraft right)
        {
            return left.Handle < right.Handle ? -1 : left.Handle > right.Handle ? 1 : 0;
        }

        private static string ComputeSha256Lower(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                try
                {
                    const string hex = "0123456789abcdef";
                    var characters = new char[hash.Length * 2];
                    for (var index = 0; index < hash.Length; index++)
                    {
                        characters[index * 2] = hex[hash[index] >> 4];
                        characters[(index * 2) + 1] = hex[hash[index] & 0x0F];
                    }

                    return new string(characters);
                }
                finally
                {
                    Array.Clear(hash, 0, hash.Length);
                }
            }
        }

        private sealed class CanonicalBuffer
        {
            private readonly int maximumLength;
            private byte[] bytes;
            private int length;

            internal CanonicalBuffer(int maximumLength)
            {
                this.maximumLength = maximumLength;
                bytes = new byte[Math.Min(256, maximumLength)];
            }

            internal void WriteAscii(string value)
            {
                var encoded = Encoding.ASCII.GetBytes(value);
                WriteBytes(encoded);
            }

            internal void WriteByte(byte value)
            {
                EnsureCapacity(1);
                bytes[length++] = value;
            }

            internal void WriteUInt32(uint value)
            {
                EnsureCapacity(4);
                bytes[length++] = (byte)value;
                bytes[length++] = (byte)(value >> 8);
                bytes[length++] = (byte)(value >> 16);
                bytes[length++] = (byte)(value >> 24);
            }

            internal void WriteUInt64(ulong value)
            {
                EnsureCapacity(8);
                for (var shift = 0; shift < 64; shift += 8)
                {
                    bytes[length++] = (byte)(value >> shift);
                }
            }

            internal void WriteLengthPrefixedBytes(byte[] value)
            {
                WriteUInt32(checked((uint)value.Length));
                WriteBytes(value);
            }

            internal void WriteString(
                string value,
                int maximumCharacters,
                int maximumBytes,
                bool allowTextControls)
            {
                ValidateString(value, maximumCharacters, allowTextControls);
                byte[] encoded;
                try
                {
                    encoded = StrictUtf8.GetBytes(value);
                }
                catch (EncoderFallbackException)
                {
                    throw new ContextValidationException("invalid-unicode");
                }

                try
                {
                    if (encoded.Length > maximumBytes)
                    {
                        throw new ContextValidationException("utf8-byte-limit");
                    }

                    WriteLengthPrefixedBytes(encoded);
                }
                finally
                {
                    Array.Clear(encoded, 0, encoded.Length);
                }
            }

            internal byte[] ToArray()
            {
                var result = new byte[length];
                Buffer.BlockCopy(bytes, 0, result, 0, length);
                return result;
            }

            private static void ValidateString(
                string value,
                int maximumCharacters,
                bool allowTextControls)
            {
                if (value == null)
                {
                    throw new ContextValidationException("null-string");
                }

                if (value.Length > maximumCharacters)
                {
                    throw new ContextValidationException("utf16-character-limit");
                }

                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    if (character == '\0')
                    {
                        throw new ContextValidationException("nul-character");
                    }

                    if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        {
                            throw new ContextValidationException("isolated-surrogate");
                        }
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        if (index == 0 || !char.IsHighSurrogate(value[index - 1]))
                        {
                            throw new ContextValidationException("isolated-surrogate");
                        }

                        continue;
                    }

                    var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                    if (category == UnicodeCategory.Format)
                    {
                        throw new ContextValidationException("unicode-format-character");
                    }

                    if (category == UnicodeCategory.Control)
                    {
                        var allowed = allowTextControls
                            && (character == '\r' || character == '\n' || character == '\t');
                        if (!allowed)
                        {
                            throw new ContextValidationException("control-character");
                        }
                    }
                }
            }

            private void WriteBytes(byte[] value)
            {
                EnsureCapacity(value.Length);
                Buffer.BlockCopy(value, 0, bytes, length, value.Length);
                length += value.Length;
            }

            private void EnsureCapacity(int additional)
            {
                var required = checked(length + additional);
                if (required > maximumLength)
                {
                    throw new ContextValidationException("canonical-byte-limit");
                }

                if (required <= bytes.Length)
                {
                    return;
                }

                var nextLength = Math.Min(maximumLength, Math.Max(required, bytes.Length * 2));
                var next = new byte[nextLength];
                Buffer.BlockCopy(bytes, 0, next, 0, length);
                Array.Clear(bytes, 0, bytes.Length);
                bytes = next;
            }
        }
    }
}
