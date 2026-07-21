using Codex.AutoCAD.Contracts;

internal static class CadContextJsonV2Specs
{
    internal static void CanonicalVectorIsFrozen()
    {
        var context = CreateFullContext();
        var failures = CadContextJsonV2Validator.Validate(context);
        Equal(0, failures.Length, JoinCodes(failures));

        var bytes = CadContextJsonV2Codec.SerializeCanonicalUtf8(context);
        var sha256 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
        Console.WriteLine(
            "CAD_CONTEXT_JSON_V2 sha256=" + sha256 + " bytes=" + bytes.Length);

        Equal(
            "21cc9378a618022c5bc21cb35c58db7818272c33d0adc5b5bd8618b4a638c3b4",
            sha256,
            "CadContextJson v2规范向量发生变化时必须显式升级schema或更新审计证据。");
    }

    internal static void EntityOrderingIsCanonical()
    {
        var context = CreateFullContext();
        var canonical = CadContextJsonV2Codec.SerializeCanonical(context);
        var hash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        context.Selection.Entities = context.Selection.Entities.Reverse().ToArray();
        var table = context.Selection.Entities.Single(
            entity => entity.EntityType == CadContextEntityTypesV2.Table);
        table.Table!.Cells = table.Table.Cells.Reverse().ToArray();

        Equal(canonical, CadContextJsonV2Codec.SerializeCanonical(context),
            "输入图元和表格单元格顺序不应改变规范JSON。");
        Equal(hash, CadContextJsonV2Codec.ComputeCanonicalSha256(context),
            "输入图元和表格单元格顺序不应改变规范哈希。");
    }

    internal static void MixedSelectionIsExplicit()
    {
        var context = CreateFullContext();
        context.Selection.Entities =
        [
            context.Selection.Entities.Single(
                entity => entity.EntityType == CadContextEntityTypesV2.Line),
            context.Selection.Entities.Single(
                entity => entity.EntityType == CadContextEntityTypesV2.Unsupported),
        ];
        context.Selection.EntityCount = 2;
        context.Selection.ParsedEntityCount = 1;
        context.Selection.UnsupportedEntityCount = 1;
        context.Selection.Complete = false;
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "支持对象与未知对象的混合选区应完整发布。");

        context.Selection.Complete = true;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_complete");
        context.Selection.Complete = false;
        context.Selection.UnsupportedEntityCount = 0;
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_unsupported_count");
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_count_sum");
    }

    internal static void PayloadMustBeUniqueAndMatching()
    {
        var context = CreateFullContext();
        var line = context.Selection.Entities.Single(
            entity => entity.EntityType == CadContextEntityTypesV2.Line);
        line.Circle = new CadContextCircleV2
        {
            Center = new CadPoint3(0, 0, 0),
            Radius = 1,
            Normal = new CadPoint3(0, 0, 1),
        };
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_shape_count");

        line.Circle = null;
        line.EntityType = CadContextEntityTypesV2.Circle;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_shape_mismatch");
    }

    internal static void LimitsFailClosed()
    {
        var context = CreateFullContext();
        var spline = context.Selection.Entities.Single(
            entity => entity.EntityType == CadContextEntityTypesV2.Spline).Spline!;
        spline.ControlPoints = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumSplinePoints + 1)
            .Select(index => new CadPoint3(index, 0, 0))
            .ToArray();
        spline.FitPoints = new CadPoint3[0];
        spline.HasFitData = false;
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_spline_point_limit");

        context = CreateFullContext();
        var table = context.Selection.Entities.Single(
            entity => entity.EntityType == CadContextEntityTypesV2.Table).Table!;
        table.Rows = 9;
        table.Columns = 8;
        table.Cells = Enumerable.Range(0, 72)
            .Select(index => new CadContextTableCellV2
            {
                Row = index / 8,
                Column = index % 8,
                Text = string.Empty,
            })
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_table_cell_limit");

        context = CreateFullContext();
        var unsupported = context.Selection.Entities.Single(
            entity => entity.EntityType == CadContextEntityTypesV2.Unsupported).Unsupported!;
        unsupported.Reason = "raw-exception-message";
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_unsupported_reason");
    }

    internal static void PrivacyBoundaryIsPreserved()
    {
        var json = CadContextJsonV2Codec.SerializeCanonical(CreateFullContext());
        ContainsText(json, "\"schemaVersion\":2");
        ContainsText(json, "\"entityType\":\"arc\"");
        ContainsText(json, "\"entityType\":\"unsupported\"");
        ContainsText(json, "\"reason\":\"unknown-entity-type\"");
        ContainsText(json, "中文表格");
        DoesNotContainText(json, "\"displayName\"");
        DoesNotContainText(json, "\"path\"");
        DoesNotContainText(json, "\"exception\"");
        DoesNotContainText(json, "\"stackTrace\"");
        DoesNotContainText(json, "D:\\");
    }

    internal static void SchemaVersionIsIndependent()
    {
        Equal(1, CadContextJsonV1Constants.SchemaVersion,
            "CadContextJson v1版本必须保持不变。");
        Equal(2, CadContextJsonV2Constants.SchemaVersion,
            "CadContextJson v2版本必须独立冻结为2。");

        var context = CreateFullContext();
        context.SchemaVersion = 1;
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_schema_version");
    }

    private static CadContextJsonV2 CreateFullContext()
    {
        var entities = new[]
        {
            Base("1", CadContextEntityTypesV2.Line, 1, entity =>
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(0, 0, 0),
                    End = new CadPoint3(10, 0, 0),
                }),
            Base("2", CadContextEntityTypesV2.Circle, 2, entity =>
                entity.Circle = new CadContextCircleV2
                {
                    Center = new CadPoint3(5, 5, 0),
                    Radius = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                }),
            Base("3", CadContextEntityTypesV2.Polyline, 3, entity =>
                entity.Polyline = new CadContextPolylineV2
                {
                    Closed = true,
                    Elevation = 0,
                    Normal = new CadPoint3(0, 0, 1),
                    Vertices =
                    [
                        new CadContextPolylineVertexV2
                        {
                            Position = new CadPoint2(0, 0),
                            Bulge = 0,
                        },
                        new CadContextPolylineVertexV2
                        {
                            Position = new CadPoint2(4, 0),
                            Bulge = 0.25,
                        },
                    ],
                }),
            Base("4", CadContextEntityTypesV2.DbText, 4, entity =>
                entity.DbText = new CadContextDbTextV2
                {
                    Text = "文字A",
                    Position = new CadPoint3(1, 2, 0),
                    Height = 2.5,
                    Rotation = 0.1,
                }),
            Base("5", CadContextEntityTypesV2.MText, 5, entity =>
                entity.MText = new CadContextMTextV2
                {
                    Text = "第一行\n第二行\t🙂",
                    Location = new CadPoint3(2, 3, 0),
                    TextHeight = 3,
                    Rotation = 0.2,
                }),
            Base("6", CadContextEntityTypesV2.BlockReference, 6, entity =>
                entity.BlockReference = new CadContextBlockReferenceV2
                {
                    Position = new CadPoint3(3, 4, 0),
                    Rotation = 0.3,
                    Scale = new CadPoint3(1, 2, 1),
                    EffectiveName = "动态块_A",
                    IsDynamic = true,
                    IsExternalReference = false,
                }),
            Base("7", CadContextEntityTypesV2.Arc, 7, entity =>
                entity.Arc = new CadContextArcV2
                {
                    Center = new CadPoint3(10, 10, 0),
                    Radius = 5,
                    StartAngle = 0.25,
                    EndAngle = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                }),
            Base("8", CadContextEntityTypesV2.Ellipse, 8, entity =>
                entity.Ellipse = new CadContextEllipseV2
                {
                    Center = new CadPoint3(20, 10, 0),
                    MajorAxis = new CadPoint3(6, 0, 0),
                    RadiusRatio = 0.5,
                    StartParameter = 0,
                    EndParameter = 6.283185307179586,
                    Normal = new CadPoint3(0, 0, 1),
                }),
            Base("9", CadContextEntityTypesV2.Spline, 9, entity =>
                entity.Spline = new CadContextSplineV2
                {
                    Degree = 3,
                    IsRational = false,
                    HasFitData = true,
                    ControlPoints =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(2, 4, 0),
                        new CadPoint3(5, 5, 0),
                        new CadPoint3(8, 0, 0),
                    ],
                    FitPoints =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(4, 3, 0),
                        new CadPoint3(8, 0, 0),
                    ],
                }),
            Base("A", CadContextEntityTypesV2.Point, 10, entity =>
                entity.Point = new CadContextPointV2
                {
                    Position = new CadPoint3(7, 8, 9),
                    Normal = new CadPoint3(0, 0, 1),
                    EcsRotation = 0.5,
                }),
            Base("B", CadContextEntityTypesV2.Ray, 11, entity =>
                entity.Ray = new CadContextRayV2
                {
                    BasePoint = new CadPoint3(0, 0, 0),
                    SecondPoint = new CadPoint3(1, 1, 0),
                }),
            Base("C", CadContextEntityTypesV2.Xline, 12, entity =>
                entity.Xline = new CadContextXlineV2
                {
                    BasePoint = new CadPoint3(1, 0, 0),
                    SecondPoint = new CadPoint3(1, 2, 0),
                }),
            Base("D", CadContextEntityTypesV2.Polyline2d, 13, entity =>
                entity.Polyline2d = new CadContextPolyline2dV2
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
                        new CadContextPolyline2dVertexV2
                        {
                            Position = new CadPoint3(5, 0, 1),
                            Bulge = -0.2,
                            StartWidth = 0.2,
                            EndWidth = 0.1,
                        },
                    ],
                }),
            Base("E", CadContextEntityTypesV2.Polyline3d, 14, entity =>
                entity.Polyline3d = new CadContextPolyline3dV2
                {
                    Closed = false,
                    Vertices =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(1, 2, 3),
                        new CadPoint3(4, 5, 6),
                    ],
                }),
            Base("F", CadContextEntityTypesV2.Dimension, 15, entity =>
                entity.Dimension = new CadContextDimensionV2
                {
                    DimensionType = "AlignedDimension",
                    Measurement = 12.5,
                    DimensionText = "<>",
                    TextPosition = new CadPoint3(6, 2, 0),
                    TextRotation = 0,
                    Normal = new CadPoint3(0, 0, 1),
                    StyleName = "ISO-25",
                }),
            Base("10", CadContextEntityTypesV2.Hatch, 16, entity =>
                entity.Hatch = new CadContextHatchV2
                {
                    Associative = true,
                    IsGradient = false,
                    IsSolidFill = false,
                    PatternName = "ANSI31",
                    PatternAngle = 0.7853981633974483,
                    PatternScale = 1.5,
                    Elevation = 0,
                    Normal = new CadPoint3(0, 0, 1),
                    LoopTypes = ["External", "Polyline"],
                }),
            Base("11", CadContextEntityTypesV2.Leader, 17, entity =>
                entity.Leader = new CadContextLeaderV2
                {
                    IsSplined = false,
                    HasArrowHead = true,
                    AnnotationType = "MText",
                    Normal = new CadPoint3(0, 0, 1),
                    Vertices =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(2, 2, 0),
                        new CadPoint3(4, 2, 0),
                    ],
                }),
            Base("12", CadContextEntityTypesV2.MLeader, 18, entity =>
                entity.MLeader = new CadContextMLeaderV2
                {
                    ContentType = "MTextContent",
                    Normal = new CadPoint3(0, 0, 1),
                    Text = "多重引线",
                    LeaderLines =
                    [
                        new CadContextMLeaderLineV2
                        {
                            Vertices =
                            [
                                new CadPoint3(0, 0, 0),
                                new CadPoint3(3, 3, 0),
                                new CadPoint3(6, 3, 0),
                            ],
                        },
                    ],
                }),
            Base("13", CadContextEntityTypesV2.Table, 19, entity =>
                entity.Table = new CadContextTableV2
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
                }),
            Base("14", CadContextEntityTypesV2.Unsupported, 20, entity =>
                entity.Unsupported = new CadContextUnsupportedV2
                {
                    DxfName = "ACAD_PROXY_ENTITY",
                    Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
                }),
        };

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-v2-test",
                DrawingFingerprint = new string('a', 64),
                Revision = 8,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('f', 64),
                EntityCount = entities.Length,
                ParsedEntityCount = entities.Length - 1,
                UnsupportedEntityCount = 1,
                Complete = false,
                Entities = entities,
            },
        };
    }

    private static CadContextEntityV2 Base(
        string handle,
        string entityType,
        int stateIndex,
        Action<CadContextEntityV2> populate)
    {
        const string hashAlphabet = "0123456789abcdef";
        var entity = new CadContextEntityV2
        {
            Handle = handle,
            OwnerSpaceHandle = "1F",
            EntityType = entityType,
            StateHash = new string(hashAlphabet[stateIndex % hashAlphabet.Length], 64),
            Layer = "0",
        };
        populate(entity);
        return entity;
    }

    private static string JoinCodes(IEnumerable<CadValidationFailure> failures)
    {
        return string.Join("; ", failures.Select(failure => failure.Code + "@" + failure.Path));
    }

    private static void Contains(
        IEnumerable<CadValidationFailure> failures,
        string expectedCode)
    {
        if (!failures.Any(failure =>
                string.Equals(failure.Code, expectedCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Expected failure code: " + expectedCode + "; actual: " + JoinCodes(failures));
        }
    }

    private static void ContainsText(string value, string expected)
    {
        if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("Expected text: " + expected);
        }
    }

    private static void DoesNotContainText(string value, string unexpected)
    {
        if (value.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Unexpected text: " + unexpected);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                "Expected " + expected + ", actual " + actual + ". " + message);
        }
    }
}
