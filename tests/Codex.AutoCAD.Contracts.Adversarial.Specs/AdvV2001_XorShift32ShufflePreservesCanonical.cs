using System;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-001: 固定xorshift32种子0xC0D3CA16，至少128轮打乱19强类型+unsupported的实体顺序及Table cells，
/// canonical bytes/hash不变；不得打乱有CAD语义的几何数组。
/// </summary>
public static class AdvV2001_XorShift32ShufflePreservesCanonical
{
    public static void Run()
    {
        const uint seed = 0xC0D3CA16;
        const int rounds = 128;

        var context = CreateFullContext();
        var canonical = CadContextJsonV2Codec.SerializeCanonical(context);
        var hash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        var rng = new XorShift32(seed);

        for (var round = 0; round < rounds; round++)
        {
            // 打乱实体顺序
            Shuffle(rng, context.Selection.Entities);

            // 打乱 Table cells
            foreach (var entity in context.Selection.Entities)
            {
                if (entity.Table is not null)
                {
                    Shuffle(rng, entity.Table.Cells);
                }
            }

            var roundCanonical = CadContextJsonV2Codec.SerializeCanonical(context);
            var roundHash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

            if (roundCanonical != canonical)
            {
                throw new InvalidOperationException(
                    $"Round {round}: canonical JSON changed after shuffle.");
            }

            if (roundHash != hash)
            {
                throw new InvalidOperationException(
                    $"Round {round}: canonical hash changed after shuffle.");
            }
        }

        // 验证几何数组保持原始顺序
        var polyline = context.Selection.Entities
            .First(e => e.EntityType == CadContextEntityTypesV2.Polyline)
            .Polyline!;
        if (polyline.Vertices[0].Position.X != 0 || polyline.Vertices[1].Position.X != 4)
        {
            throw new InvalidOperationException(
                "Polyline vertices were incorrectly shuffled.");
        }
    }

    private static void Shuffle(XorShift32 rng, CadContextEntityV2[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = (int)(rng.Next() % (uint)(i + 1));
            var temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }

    private static void Shuffle(XorShift32 rng, CadContextTableCellV2[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = (int)(rng.Next() % (uint)(i + 1));
            var temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }

    private static CadContextJsonV2 CreateFullContext()
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-001",
                DrawingFingerprint = new string('a', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('f', 64),
                EntityCount = 20,
                ParsedEntityCount = 19,
                UnsupportedEntityCount = 1,
                Complete = false,
                Entities = CreateAllEntityTypes(),
            },
        };
    }

    private static CadContextEntityV2[] CreateAllEntityTypes()
    {
        return new[]
        {
            CreateLine("1"),
            CreateCircle("2"),
            CreatePolyline("3"),
            CreateDbText("4"),
            CreateMText("5"),
            CreateBlockReference("6"),
            CreateArc("7"),
            CreateEllipse("8"),
            CreateSpline("9"),
            CreatePoint("A"),
            CreateRay("B"),
            CreateXline("C"),
            CreatePolyline2d("D"),
            CreatePolyline3d("E"),
            CreateDimension("F"),
            CreateHatch("10"),
            CreateLeader("11"),
            CreateMLeader("12"),
            CreateTable("13"),
            CreateUnsupported("14"),
        };
    }

    private static CadContextEntityV2 CreateLine(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Line,
        StateHash = new string('1', 64),
        Layer = "0",
        Line = new CadContextLineV2
        {
            Start = new CadPoint3(0, 0, 0),
            End = new CadPoint3(10, 0, 0),
        },
    };

    private static CadContextEntityV2 CreateCircle(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Circle,
        StateHash = new string('2', 64),
        Layer = "0",
        Circle = new CadContextCircleV2
        {
            Center = new CadPoint3(5, 5, 0),
            Radius = 2.5,
            Normal = new CadPoint3(0, 0, 1),
        },
    };

    private static CadContextEntityV2 CreatePolyline(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Polyline,
        StateHash = new string('3', 64),
        Layer = "0",
        Polyline = new CadContextPolylineV2
        {
            Closed = true,
            Elevation = 0,
            Normal = new CadPoint3(0, 0, 1),
            Vertices =
            [
                new CadContextPolylineVertexV2 { Position = new CadPoint2(0, 0), Bulge = 0 },
                new CadContextPolylineVertexV2 { Position = new CadPoint2(4, 0), Bulge = 0.25 },
            ],
        },
    };

    private static CadContextEntityV2 CreateDbText(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.DbText,
        StateHash = new string('4', 64),
        Layer = "0",
        DbText = new CadContextDbTextV2
        {
            Text = "文字A",
            Position = new CadPoint3(1, 2, 0),
            Height = 2.5,
            Rotation = 0.1,
        },
    };

    private static CadContextEntityV2 CreateMText(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.MText,
        StateHash = new string('5', 64),
        Layer = "0",
        MText = new CadContextMTextV2
        {
            Text = "第一行\n第二行",
            Location = new CadPoint3(2, 3, 0),
            TextHeight = 3,
            Rotation = 0.2,
        },
    };

    private static CadContextEntityV2 CreateBlockReference(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.BlockReference,
        StateHash = new string('6', 64),
        Layer = "0",
        BlockReference = new CadContextBlockReferenceV2
        {
            Position = new CadPoint3(3, 4, 0),
            Rotation = 0.3,
            Scale = new CadPoint3(1, 1, 1),
            EffectiveName = "TestBlock",
            IsDynamic = false,
            IsExternalReference = false,
        },
    };

    private static CadContextEntityV2 CreateArc(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Arc,
        StateHash = new string('7', 64),
        Layer = "0",
        Arc = new CadContextArcV2
        {
            Center = new CadPoint3(10, 10, 0),
            Radius = 5,
            StartAngle = 0.25,
            EndAngle = 2.5,
            Normal = new CadPoint3(0, 0, 1),
        },
    };

    private static CadContextEntityV2 CreateEllipse(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Ellipse,
        StateHash = new string('8', 64),
        Layer = "0",
        Ellipse = new CadContextEllipseV2
        {
            Center = new CadPoint3(20, 10, 0),
            MajorAxis = new CadPoint3(6, 0, 0),
            RadiusRatio = 0.5,
            StartParameter = 0,
            EndParameter = 6.283185307179586,
            Normal = new CadPoint3(0, 0, 1),
        },
    };

    private static CadContextEntityV2 CreateSpline(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Spline,
        StateHash = new string('9', 64),
        Layer = "0",
        Spline = new CadContextSplineV2
        {
            Degree = 3,
            IsRational = false,
            HasFitData = false,
            ControlPoints =
            [
                new CadPoint3(0, 0, 0),
                new CadPoint3(2, 4, 0),
                new CadPoint3(5, 5, 0),
                new CadPoint3(8, 0, 0),
            ],
            FitPoints = [],
        },
    };

    private static CadContextEntityV2 CreatePoint(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Point,
        StateHash = new string('a', 64),
        Layer = "0",
        Point = new CadContextPointV2
        {
            Position = new CadPoint3(7, 8, 9),
            Normal = new CadPoint3(0, 0, 1),
            EcsRotation = 0.5,
        },
    };

    private static CadContextEntityV2 CreateRay(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Ray,
        StateHash = new string('b', 64),
        Layer = "0",
        Ray = new CadContextRayV2
        {
            BasePoint = new CadPoint3(0, 0, 0),
            SecondPoint = new CadPoint3(1, 1, 0),
        },
    };

    private static CadContextEntityV2 CreateXline(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Xline,
        StateHash = new string('c', 64),
        Layer = "0",
        Xline = new CadContextXlineV2
        {
            BasePoint = new CadPoint3(1, 0, 0),
            SecondPoint = new CadPoint3(1, 2, 0),
        },
    };

    private static CadContextEntityV2 CreatePolyline2d(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Polyline2d,
        StateHash = new string('d', 64),
        Layer = "0",
        Polyline2d = new CadContextPolyline2dV2
        {
            Closed = false,
            Elevation = 1,
            Normal = new CadPoint3(0, 0, 1),
            Vertices =
            [
                new CadContextPolyline2dVertexV2
                {
                    Position = new CadPoint3(0, 0, 1),
                    Bulge = 0,
                    StartWidth = 0.1,
                    EndWidth = 0.2,
                },
            ],
        },
    };

    private static CadContextEntityV2 CreatePolyline3d(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Polyline3d,
        StateHash = new string('e', 64),
        Layer = "0",
        Polyline3d = new CadContextPolyline3dV2
        {
            Closed = false,
            Vertices =
            [
                new CadPoint3(0, 0, 0),
                new CadPoint3(1, 2, 3),
            ],
        },
    };

    private static CadContextEntityV2 CreateDimension(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Dimension,
        StateHash = new string('f', 64),
        Layer = "0",
        Dimension = new CadContextDimensionV2
        {
            DimensionType = "AlignedDimension",
            Measurement = 12.5,
            DimensionText = "<>",
            TextPosition = new CadPoint3(6, 2, 0),
            TextRotation = 0,
            Normal = new CadPoint3(0, 0, 1),
            StyleName = "ISO-25",
        },
    };

    private static CadContextEntityV2 CreateHatch(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Hatch,
        StateHash = new string('1', 64),
        Layer = "0",
        Hatch = new CadContextHatchV2
        {
            Associative = true,
            IsGradient = false,
            IsSolidFill = false,
            PatternName = "ANSI31",
            PatternAngle = 0.785,
            PatternScale = 1.5,
            Elevation = 0,
            Normal = new CadPoint3(0, 0, 1),
            LoopTypes = ["External"],
        },
    };

    private static CadContextEntityV2 CreateLeader(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Leader,
        StateHash = new string('2', 64),
        Layer = "0",
        Leader = new CadContextLeaderV2
        {
            IsSplined = false,
            HasArrowHead = true,
            AnnotationType = "MText",
            Normal = new CadPoint3(0, 0, 1),
            Vertices =
            [
                new CadPoint3(0, 0, 0),
                new CadPoint3(2, 2, 0),
            ],
        },
    };

    private static CadContextEntityV2 CreateMLeader(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.MLeader,
        StateHash = new string('3', 64),
        Layer = "0",
        MLeader = new CadContextMLeaderV2
        {
            ContentType = "MTextContent",
            Normal = new CadPoint3(0, 0, 1),
            Text = "引线",
            LeaderLines =
            [
                new CadContextMLeaderLineV2
                {
                    Vertices =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(3, 3, 0),
                    ],
                },
            ],
        },
    };

    private static CadContextEntityV2 CreateTable(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Table,
        StateHash = new string('4', 64),
        Layer = "0",
        Table = new CadContextTableV2
        {
            Position = new CadPoint3(30, 20, 0),
            Direction = new CadPoint3(1, 0, 0),
            Rows = 2,
            Columns = 2,
            Width = 20,
            Height = 10,
            StyleName = "Standard",
            Cells =
            [
                new CadContextTableCellV2 { Row = 0, Column = 0, Text = "名称" },
                new CadContextTableCellV2 { Row = 0, Column = 1, Text = "数量" },
                new CadContextTableCellV2 { Row = 1, Column = 0, Text = "中文表格" },
                new CadContextTableCellV2 { Row = 1, Column = 1, Text = "2" },
            ],
        },
    };

    private static CadContextEntityV2 CreateUnsupported(string handle) => new()
    {
        Handle = handle,
        OwnerSpaceHandle = "1F",
        EntityType = CadContextEntityTypesV2.Unsupported,
        StateHash = new string('5', 64),
        Layer = "0",
        Unsupported = new CadContextUnsupportedV2
        {
            DxfName = "ACAD_PROXY_ENTITY",
            Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
        },
    };

    private sealed class XorShift32
    {
        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0 ? 1 : seed;
        }

        public uint Next()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }
    }
}
