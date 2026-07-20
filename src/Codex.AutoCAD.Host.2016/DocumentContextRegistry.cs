using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Maintains only opaque, in-memory identity for open documents. It never reads or stores a DWG
    /// name or path. Database change events advance a monotonic, fail-safe revision counter.
    /// </summary>
    internal sealed class DocumentContextRegistry : IDisposable
    {
        private readonly Dictionary<Document, DocumentState> states =
            new Dictionary<Document, DocumentState>();
        private bool disposed;

        internal CadContextDocumentMetadata Capture(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            EnsureNotDisposed();
            DocumentState state;
            if (!states.TryGetValue(document, out state))
            {
                state = new DocumentState(document);
                states.Add(document, state);
            }

            return state.Capture();
        }

        internal void Remove(Document document)
        {
            if (document == null)
            {
                return;
            }

            DocumentState state;
            if (!states.TryGetValue(document, out state))
            {
                return;
            }

            states.Remove(document);
            state.Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var state in states.Values)
            {
                state.Dispose();
            }

            states.Clear();
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DocumentContextRegistry));
            }
        }

        private sealed class DocumentState : IDisposable
        {
            private readonly Database database;
            private readonly string documentId;
            private readonly string drawingFingerprint;
            private long revision;
            private bool disposed;

            internal DocumentState(Document document)
            {
                database = document.Database;
                documentId = Guid.NewGuid().ToString("N");
                drawingFingerprint = BuildDrawingFingerprint(database, documentId);
                revision = ReadInitialRevision();

                database.ObjectAppended += OnObjectAppended;
                database.ObjectModified += OnObjectModified;
                database.ObjectErased += OnObjectErased;
            }

            internal CadContextDocumentMetadata Capture()
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(DocumentState));
                }

                return new CadContextDocumentMetadata(
                    documentId,
                    drawingFingerprint,
                    Math.Max(0L, Interlocked.Read(ref revision)),
                    ReadCurrentSpace(database),
                    ReadDrawingVersion(database),
                    ReadUnits(database));
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                database.ObjectAppended -= OnObjectAppended;
                database.ObjectModified -= OnObjectModified;
                database.ObjectErased -= OnObjectErased;
            }

            private long ReadInitialRevision()
            {
                try
                {
                    var raw = AutoCadApplication.GetSystemVariable("DBMOD");
                    return raw == null
                        ? 0L
                        : Math.Max(0L, Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return 0L;
                }
                catch (FormatException)
                {
                    return 0L;
                }
                catch (InvalidCastException)
                {
                    return 0L;
                }
                catch (OverflowException)
                {
                    return 0L;
                }
            }

            private void OnObjectAppended(object sender, ObjectEventArgs eventArgs)
            {
                AdvanceRevision();
            }

            private void OnObjectModified(object sender, ObjectEventArgs eventArgs)
            {
                AdvanceRevision();
            }

            private void OnObjectErased(object sender, ObjectErasedEventArgs eventArgs)
            {
                AdvanceRevision();
            }

            private void AdvanceRevision()
            {
                while (true)
                {
                    var current = Interlocked.Read(ref revision);
                    if (current == long.MaxValue)
                    {
                        return;
                    }

                    if (Interlocked.CompareExchange(ref revision, current + 1L, current) == current)
                    {
                        return;
                    }
                }
            }
        }

        private static string BuildDrawingFingerprint(Database database, string documentId)
        {
            var fingerprint = database.FingerprintGuid;
            var material = string.IsNullOrWhiteSpace(fingerprint)
                ? "codex.autocad.volatile-drawing.v1|" + documentId
                : "codex.autocad.drawing.v1|" + fingerprint;
            var bytes = Encoding.ASCII.GetBytes(material);
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(bytes);
                    try
                    {
                        return ToLowerHex(hash);
                    }
                    finally
                    {
                        Array.Clear(hash, 0, hash.Length);
                    }
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static string ReadCurrentSpace(Database database)
        {
            try
            {
                var raw = AutoCadApplication.GetSystemVariable("CVPORT");
                var cvport = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return !database.TileMode && cvport == 1
                    ? Codex.AutoCAD.Contracts.CadContextJsonV1Constants.PaperSpace
                    : Codex.AutoCAD.Contracts.CadContextJsonV1Constants.ModelSpace;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return database.TileMode
                    ? Codex.AutoCAD.Contracts.CadContextJsonV1Constants.ModelSpace
                    : Codex.AutoCAD.Contracts.CadContextJsonV1Constants.PaperSpace;
            }
            catch (FormatException)
            {
                return database.TileMode
                    ? Codex.AutoCAD.Contracts.CadContextJsonV1Constants.ModelSpace
                    : Codex.AutoCAD.Contracts.CadContextJsonV1Constants.PaperSpace;
            }
            catch (InvalidCastException)
            {
                return database.TileMode
                    ? Codex.AutoCAD.Contracts.CadContextJsonV1Constants.ModelSpace
                    : Codex.AutoCAD.Contracts.CadContextJsonV1Constants.PaperSpace;
            }
            catch (OverflowException)
            {
                return database.TileMode
                    ? Codex.AutoCAD.Contracts.CadContextJsonV1Constants.ModelSpace
                    : Codex.AutoCAD.Contracts.CadContextJsonV1Constants.PaperSpace;
            }
        }

        private static string ReadDrawingVersion(Database database)
        {
            return database.OriginalFileVersion.ToString();
        }

        private static string ReadUnits(Database database)
        {
            return database.Insunits == UnitsValue.Undefined
                ? "unitless"
                : database.Insunits.ToString().ToLowerInvariant();
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
            }

            return new string(characters);
        }
    }
}
