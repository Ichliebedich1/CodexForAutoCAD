using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext.Specs
{
    internal sealed class ReferenceEncodingResult
    {
        internal ReferenceEncodingResult(byte[] canonical, string hash)
        {
            Canonical = canonical;
            Hash = hash;
        }

        internal byte[] Canonical { get; private set; }

        internal string Hash { get; private set; }
    }

    internal static class ReferenceSelectionEncoder
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static ReferenceEncodingResult EncodeSelection(IList<ContextEntityDraft> drafts)
        {
            var ordered = new List<ContextEntityDraft>(drafts);
            ordered.Sort(delegate(ContextEntityDraft left, ContextEntityDraft right)
            {
                return left.Handle < right.Handle ? -1 : left.Handle > right.Handle ? 1 : 0;
            });

            var records = new List<byte[]>(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                records.Add(EncodeEntity(ordered[index]));
            }

            using (var stream = new MemoryStream())
            {
                WriteAscii(stream, "CDXCTXS1");
                WriteUInt32(stream, 1);
                WriteUInt32(stream, checked((uint)records.Count));
                for (var index = 0; index < records.Count; index++)
                {
                    WriteUInt32(stream, checked((uint)records[index].Length));
                    stream.Write(records[index], 0, records[index].Length);
                }

                var canonical = stream.ToArray();
                return new ReferenceEncodingResult(canonical, ComputeHash(canonical));
            }
        }

        internal static byte[] EncodeEntity(ContextEntityDraft draft)
        {
            using (var stream = new MemoryStream())
            {
                WriteAscii(stream, "CDXCTXE1");
                WriteUInt32(stream, 1);
                stream.WriteByte((byte)draft.Kind);
                WriteUInt64(stream, draft.Handle);
                WriteUInt64(stream, draft.OwnerSpaceHandle);
                WriteString(stream, draft.Layer);

                switch (draft.Kind)
                {
                    case ContextEntityKind.Line:
                        WritePoint3(stream, draft.Line.Start);
                        WritePoint3(stream, draft.Line.End);
                        break;
                    case ContextEntityKind.Circle:
                        WritePoint3(stream, draft.Circle.Center);
                        WriteDouble(stream, draft.Circle.Radius);
                        WriteVector3(stream, draft.Circle.Normal);
                        break;
                    case ContextEntityKind.Polyline:
                        stream.WriteByte(draft.Polyline.Closed ? (byte)1 : (byte)0);
                        WriteDouble(stream, draft.Polyline.Elevation);
                        WriteVector3(stream, draft.Polyline.Normal);
                        WriteUInt32(stream, checked((uint)draft.Polyline.Vertices.Count));
                        for (var index = 0; index < draft.Polyline.Vertices.Count; index++)
                        {
                            WritePoint2(stream, draft.Polyline.Vertices[index].Position);
                            WriteDouble(stream, draft.Polyline.Vertices[index].Bulge);
                        }

                        break;
                    case ContextEntityKind.DbText:
                        WriteString(stream, draft.DbText.Text);
                        WritePoint3(stream, draft.DbText.Position);
                        WriteDouble(stream, draft.DbText.Height);
                        WriteDouble(stream, draft.DbText.Rotation);
                        break;
                    case ContextEntityKind.MText:
                        WriteString(stream, draft.MText.Text);
                        WritePoint3(stream, draft.MText.Location);
                        WriteDouble(stream, draft.MText.TextHeight);
                        WriteDouble(stream, draft.MText.Rotation);
                        break;
                    case ContextEntityKind.BlockReference:
                        WritePoint3(stream, draft.Block.Position);
                        WriteDouble(stream, draft.Block.Rotation);
                        WriteVector3(stream, draft.Block.Scale);
                        WriteString(stream, draft.Block.EffectiveName);
                        stream.WriteByte(draft.Block.Dynamic ? (byte)1 : (byte)0);
                        stream.WriteByte(draft.Block.Xref ? (byte)1 : (byte)0);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown reference kind.");
                }

                return stream.ToArray();
            }
        }

        internal static string ToHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = hex[bytes[index] >> 4];
                characters[(index * 2) + 1] = hex[bytes[index] & 0x0F];
            }

            return new string(characters);
        }

        internal static string ComputeHash(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = StrictUtf8.GetBytes(value);
            WriteUInt32(stream, checked((uint)bytes.Length));
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WritePoint2(Stream stream, ContextPoint2 point)
        {
            WriteDouble(stream, point.X);
            WriteDouble(stream, point.Y);
        }

        private static void WritePoint3(Stream stream, ContextPoint3 point)
        {
            WriteDouble(stream, point.X);
            WriteDouble(stream, point.Y);
            WriteDouble(stream, point.Z);
        }

        private static void WriteVector3(Stream stream, ContextVector3 vector)
        {
            WriteDouble(stream, vector.X);
            WriteDouble(stream, vector.Y);
            WriteDouble(stream, vector.Z);
        }

        private static void WriteDouble(Stream stream, double value)
        {
            WriteUInt64(stream, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteUInt64(Stream stream, ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8)
            {
                stream.WriteByte((byte)(value >> shift));
            }
        }
    }
}
