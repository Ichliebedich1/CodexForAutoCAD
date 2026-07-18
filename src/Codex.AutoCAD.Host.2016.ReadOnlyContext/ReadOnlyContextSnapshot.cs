using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    internal enum ContextEntityKind : byte
    {
        Line = 1,
        Circle = 2,
        Polyline = 3,
        DbText = 4,
        MText = 5,
        BlockReference = 6
    }

    internal sealed class ContextPoint2
    {
        internal ContextPoint2(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal double X { get; private set; }

        internal double Y { get; private set; }
    }

    internal sealed class ContextPoint3
    {
        internal ContextPoint3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal double X { get; private set; }

        internal double Y { get; private set; }

        internal double Z { get; private set; }
    }

    internal sealed class ContextVector3
    {
        internal ContextVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal double X { get; private set; }

        internal double Y { get; private set; }

        internal double Z { get; private set; }
    }

    internal sealed class ContextLineData
    {
        internal ContextLineData(ContextPoint3 start, ContextPoint3 end)
        {
            Start = start;
            End = end;
        }

        internal ContextPoint3 Start { get; private set; }

        internal ContextPoint3 End { get; private set; }
    }

    internal sealed class ContextCircleData
    {
        internal ContextCircleData(ContextPoint3 center, double radius, ContextVector3 normal)
        {
            Center = center;
            Radius = radius;
            Normal = normal;
        }

        internal ContextPoint3 Center { get; private set; }

        internal double Radius { get; private set; }

        internal ContextVector3 Normal { get; private set; }
    }

    internal sealed class ContextPolylineVertex
    {
        internal ContextPolylineVertex(ContextPoint2 position, double bulge)
        {
            Position = position;
            Bulge = bulge;
        }

        internal ContextPoint2 Position { get; private set; }

        internal double Bulge { get; private set; }
    }

    internal sealed class ContextPolylineData
    {
        private readonly ReadOnlyCollection<ContextPolylineVertex> vertices;

        internal ContextPolylineData(
            bool closed,
            double elevation,
            ContextVector3 normal,
            IList<ContextPolylineVertex> vertices)
        {
            Closed = closed;
            Elevation = elevation;
            Normal = normal;
            this.vertices = new ReadOnlyCollection<ContextPolylineVertex>(
                new List<ContextPolylineVertex>(vertices));
        }

        internal bool Closed { get; private set; }

        internal double Elevation { get; private set; }

        internal ContextVector3 Normal { get; private set; }

        internal IReadOnlyList<ContextPolylineVertex> Vertices
        {
            get { return vertices; }
        }
    }

    internal sealed class ContextDbTextData
    {
        internal ContextDbTextData(string text, ContextPoint3 position, double height, double rotation)
        {
            Text = text;
            Position = position;
            Height = height;
            Rotation = rotation;
        }

        internal string Text { get; private set; }

        internal ContextPoint3 Position { get; private set; }

        internal double Height { get; private set; }

        internal double Rotation { get; private set; }
    }

    internal sealed class ContextMTextData
    {
        internal ContextMTextData(string text, ContextPoint3 location, double textHeight, double rotation)
        {
            Text = text;
            Location = location;
            TextHeight = textHeight;
            Rotation = rotation;
        }

        internal string Text { get; private set; }

        internal ContextPoint3 Location { get; private set; }

        internal double TextHeight { get; private set; }

        internal double Rotation { get; private set; }
    }

    internal sealed class ContextBlockData
    {
        internal ContextBlockData(
            ContextPoint3 position,
            double rotation,
            ContextVector3 scale,
            string effectiveName,
            bool dynamic,
            bool xref)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            EffectiveName = effectiveName;
            Dynamic = dynamic;
            Xref = xref;
        }

        internal ContextPoint3 Position { get; private set; }

        internal double Rotation { get; private set; }

        internal ContextVector3 Scale { get; private set; }

        internal string EffectiveName { get; private set; }

        internal bool Dynamic { get; private set; }

        internal bool Xref { get; private set; }
    }

    internal sealed class ContextEntityDraft
    {
        internal ContextEntityDraft(
            ContextEntityKind kind,
            ulong handle,
            ulong ownerSpaceHandle,
            string layer,
            ContextLineData line,
            ContextCircleData circle,
            ContextPolylineData polyline,
            ContextDbTextData dbText,
            ContextMTextData mText,
            ContextBlockData block)
        {
            Kind = kind;
            Handle = handle;
            OwnerSpaceHandle = ownerSpaceHandle;
            Layer = layer;
            Line = line;
            Circle = circle;
            Polyline = polyline;
            DbText = dbText;
            MText = mText;
            Block = block;
        }

        internal ContextEntityKind Kind { get; private set; }

        internal ulong Handle { get; private set; }

        internal ulong OwnerSpaceHandle { get; private set; }

        internal string Layer { get; private set; }

        internal ContextLineData Line { get; private set; }

        internal ContextCircleData Circle { get; private set; }

        internal ContextPolylineData Polyline { get; private set; }

        internal ContextDbTextData DbText { get; private set; }

        internal ContextMTextData MText { get; private set; }

        internal ContextBlockData Block { get; private set; }
    }

    internal sealed class ContextEntitySnapshot
    {
        internal ContextEntitySnapshot(ContextEntityDraft draft, string stateHash)
        {
            Draft = draft;
            StateHash = stateHash;
        }

        internal ContextEntityDraft Draft { get; private set; }

        internal string StateHash { get; private set; }
    }

    internal sealed class ContextSelectionSnapshot
    {
        private readonly ReadOnlyCollection<ContextEntitySnapshot> entities;

        internal ContextSelectionSnapshot(
            IList<ContextEntitySnapshot> entities,
            string snapshotHash,
            int canonicalLength)
        {
            this.entities = new ReadOnlyCollection<ContextEntitySnapshot>(
                new List<ContextEntitySnapshot>(entities));
            SnapshotHash = snapshotHash;
            CanonicalLength = canonicalLength;
        }

        internal IReadOnlyList<ContextEntitySnapshot> Entities
        {
            get { return entities; }
        }

        internal string SnapshotHash { get; private set; }

        internal int CanonicalLength { get; private set; }
    }

    internal sealed class ContextRuntimeState
    {
        internal ContextRuntimeState(
            string status,
            int generation,
            int selectedCount,
            int? dbmodBefore,
            int? dbmodAfter,
            ContextSelectionSnapshot snapshot)
        {
            Status = status;
            Generation = generation;
            SelectedCount = selectedCount;
            DbmodBefore = dbmodBefore;
            DbmodAfter = dbmodAfter;
            Snapshot = snapshot;
        }

        internal string Status { get; private set; }

        internal int Generation { get; private set; }

        internal int SelectedCount { get; private set; }

        internal int? DbmodBefore { get; private set; }

        internal int? DbmodAfter { get; private set; }

        internal ContextSelectionSnapshot Snapshot { get; private set; }

        internal bool Published
        {
            get { return Snapshot != null; }
        }

        internal bool DbmodUnchanged
        {
            get
            {
                return DbmodBefore.HasValue
                    && DbmodAfter.HasValue
                    && DbmodBefore.Value == DbmodAfter.Value;
            }
        }
    }

    internal sealed class ContextValidationException : Exception
    {
        internal ContextValidationException(string code)
            : base(code)
        {
            Code = code;
        }

        internal string Code { get; private set; }
    }
}
