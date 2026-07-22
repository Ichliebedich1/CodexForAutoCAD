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

    internal static void EachTypedPayloadValidatesIndividually()
    {
        var types = new[]
        {
            CadContextEntityTypesV2.Line,
            CadContextEntityTypesV2.Circle,
            CadContextEntityTypesV2.Polyline,
            CadContextEntityTypesV2.DbText,
            CadContextEntityTypesV2.MText,
            CadContextEntityTypesV2.BlockReference,
            CadContextEntityTypesV2.Arc,
            CadContextEntityTypesV2.Ellipse,
            CadContextEntityTypesV2.Spline,
            CadContextEntityTypesV2.Point,
            CadContextEntityTypesV2.Ray,
            CadContextEntityTypesV2.Xline,
            CadContextEntityTypesV2.Polyline2d,
            CadContextEntityTypesV2.Polyline3d,
            CadContextEntityTypesV2.Dimension,
            CadContextEntityTypesV2.Hatch,
            CadContextEntityTypesV2.Leader,
            CadContextEntityTypesV2.MLeader,
            CadContextEntityTypesV2.Table,
        };
        Equal(19, types.Length, "v2必须覆盖19个强类型payload。");

        foreach (var entityType in types)
        {
            var context = MakeMinimalContext(entityType);
            var failures = CadContextJsonV2Validator.Validate(context);
            Equal(0, failures.Length,
                entityType + "单独验证应通过: " + JoinCodes(failures));
        }

        var unsupported = MakeMinimalContext(CadContextEntityTypesV2.Unsupported);
        unsupported.Selection.Entities[0].Unsupported = new CadContextUnsupportedV2
        {
            DxfName = "ACAD_PROXY_ENTITY",
            Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
        };
        Equal(0, CadContextJsonV2Validator.Validate(unsupported).Length,
            "unsupported单独验证应通过。");
    }

    internal static void ThreeUnsupportedReasonsAreAccepted()
    {
        var reasons = new[]
        {
            CadContextUnsupportedReasonsV2.UnknownEntityType,
            CadContextUnsupportedReasonsV2.EntityReadFailed,
            CadContextUnsupportedReasonsV2.EntityDataLimit,
        };
        foreach (var reason in reasons)
        {
            var context = MakeMinimalContext(CadContextEntityTypesV2.Unsupported);
            context.Selection.Entities[0].Unsupported = new CadContextUnsupportedV2
            {
                DxfName = "3DSOLID",
                Reason = reason,
            };
            var failures = CadContextJsonV2Validator.Validate(context);
            Equal(0, failures.Length,
                "reason=" + reason + "应通过: " + JoinCodes(failures));
        }
    }

    internal static void CountInconsistenciesAreRejected()
    {
        var context = CreateFullContext();
        context.Selection.EntityCount = 999;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_entity_count");

        context = CreateFullContext();
        context.Selection.ParsedEntityCount = 999;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_parsed_count");

        context = CreateFullContext();
        context.Selection.UnsupportedEntityCount = 999;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_unsupported_count");

        context = CreateFullContext();
        context.Selection.ParsedEntityCount = context.Selection.EntityCount;
        context.Selection.UnsupportedEntityCount = 1;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_count_sum");

        context = CreateFullContext();
        context.Selection.ParsedEntityCount = 0;
        context.Selection.UnsupportedEntityCount = context.Selection.EntityCount;
        context.Selection.Complete = true;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_complete");
    }

    internal static void EntityLimitAndLimitPlusOne()
    {
        const string hex = "0123456789abcdef";
        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumEntities)
            .Select(i => new CadContextEntityV2
            {
                Handle = i.ToString("X"),
                OwnerSpaceHandle = "1F",
                EntityType = CadContextEntityTypesV2.Line,
                StateHash = new string(hex[i % hex.Length], 64),
                Layer = "0",
                Line = new CadContextLineV2
                {
                    Start = new CadPoint3(i, 0, 0),
                    End = new CadPoint3(i + 1, 0, 0),
                },
            })
            .ToArray();
        context.Selection.EntityCount = CadContextJsonV2Constants.MaximumEntities;
        context.Selection.ParsedEntityCount = CadContextJsonV2Constants.MaximumEntities;
        context.Selection.UnsupportedEntityCount = 0;
        context.Selection.Complete = true;
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到实体上限应通过。");

        var extra = new CadContextEntityV2[context.Selection.Entities.Length + 1];
        Array.Copy(context.Selection.Entities, extra, context.Selection.Entities.Length);
        extra[extra.Length - 1] = new CadContextEntityV2
        {
            Handle = "FFFF",
            OwnerSpaceHandle = "1F",
            EntityType = CadContextEntityTypesV2.Line,
            StateHash = new string('a', 64),
            Layer = "0",
            Line = new CadContextLineV2
            {
                Start = new CadPoint3(999, 0, 0),
                End = new CadPoint3(1000, 0, 0),
            },
        };
        context.Selection.Entities = extra;
        context.Selection.EntityCount = extra.Length;
        context.Selection.ParsedEntityCount = extra.Length;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_entity_limit");
    }

    internal static void PolylineVertexLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Polyline);
        var polyline = context.Selection.Entities[0].Polyline!;
        polyline.Vertices = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumPolylineVertices)
            .Select(i => new CadContextPolylineVertexV2
            {
                Position = new CadPoint2(i, 0),
                Bulge = 0,
            })
            .ToArray();
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到多段线顶点上限应通过。");

        polyline.Vertices = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumPolylineVertices + 1)
            .Select(i => new CadContextPolylineVertexV2
            {
                Position = new CadPoint2(i, 0),
                Bulge = 0,
            })
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_polyline_vertex_limit");
    }

    internal static void TextLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text =
            new string('测', CadContextJsonV2Constants.MaximumTextCharacters);
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到文本字符上限应通过。");

        context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text =
            new string('测', CadContextJsonV2Constants.MaximumTextCharacters + 1);
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_characters");
    }

    internal static void NameLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.BlockReference);
        context.Selection.Entities[0].BlockReference!.EffectiveName =
            new string('B', CadContextJsonV2Constants.MaximumNameCharacters);
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到名称字符上限应通过。");

        context = MakeMinimalContext(CadContextEntityTypesV2.BlockReference);
        context.Selection.Entities[0].BlockReference!.EffectiveName =
            new string('B', CadContextJsonV2Constants.MaximumNameCharacters + 1);
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_block_name_characters");
    }

    internal static void TableCellLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Table);
        var table = context.Selection.Entities[0].Table!;
        table.Rows = 8;
        table.Columns = 8;
        table.Cells = Enumerable.Range(0, 64)
            .Select(i => new CadContextTableCellV2
            {
                Row = i / 8,
                Column = i % 8,
                Text = string.Empty,
            })
            .ToArray();
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到表格单元格上限应通过。");

        table.Rows = 9;
        table.Columns = 8;
        table.Cells = Enumerable.Range(0, 72)
            .Select(i => new CadContextTableCellV2
            {
                Row = i / 8,
                Column = i % 8,
                Text = string.Empty,
            })
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_table_cell_limit");
    }

    internal static void HatchLoopLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Hatch);
        var hatch = context.Selection.Entities[0].Hatch!;
        hatch.LoopTypes = Enumerable.Range(0, CadContextJsonV2Constants.MaximumHatchLoops)
            .Select(_ => "External")
            .ToArray();
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到填充环上限应通过。");

        hatch.LoopTypes = Enumerable.Range(0, CadContextJsonV2Constants.MaximumHatchLoops + 1)
            .Select(_ => "External")
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_hatch_loop_limit");
    }

    internal static void MLeaderLineLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.MLeader);
        var mleader = context.Selection.Entities[0].MLeader!;
        mleader.LeaderLines = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumMLeaderLines)
            .Select(_ => new CadContextMLeaderLineV2
            {
                Vertices = [new CadPoint3(0, 0, 0), new CadPoint3(1, 1, 0)],
            })
            .ToArray();
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到多重引线上限应通过。");

        mleader.LeaderLines = Enumerable.Range(
                0, CadContextJsonV2Constants.MaximumMLeaderLines + 1)
            .Select(_ => new CadContextMLeaderLineV2
            {
                Vertices = [new CadPoint3(0, 0, 0), new CadPoint3(1, 1, 0)],
            })
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_mleader_line_limit");
    }

    internal static void HandleNumericSortBoundary()
    {
        var handles = new[] { "1", "A", "F", "10", "100" };
        var entities = handles.Select((h, i) =>
        {
            var entity = new CadContextEntityV2
            {
                Handle = h,
                OwnerSpaceHandle = "1F",
                EntityType = CadContextEntityTypesV2.Line,
                StateHash = new string((char)('a' + i), 64),
                Layer = "0",
                Line = new CadContextLineV2
                {
                    Start = new CadPoint3(i, 0, 0),
                    End = new CadPoint3(i + 1, 0, 0),
                },
            };
            return entity;
        }).ToArray();

        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities = entities;
        context.Selection.EntityCount = entities.Length;
        context.Selection.ParsedEntityCount = entities.Length;
        context.Selection.UnsupportedEntityCount = 0;
        context.Selection.Complete = true;
        var json = CadContextJsonV2Codec.SerializeCanonical(context);

        var pos1 = json.IndexOf("\"handle\":\"1\"", StringComparison.Ordinal);
        var posA = json.IndexOf("\"handle\":\"A\"", StringComparison.Ordinal);
        var posF = json.IndexOf("\"handle\":\"F\"", StringComparison.Ordinal);
        var pos10 = json.IndexOf("\"handle\":\"10\"", StringComparison.Ordinal);
        var pos100 = json.IndexOf("\"handle\":\"100\"", StringComparison.Ordinal);
        Equal(true,
            pos1 >= 0 && pos1 < posA && posA < posF && posF < pos10 && pos10 < pos100,
            "Handle必须按数值排序: 1 < A(10) < F(15) < 10(16) < 100(256)。");
    }

    internal static void EntityInputOrderDoesNotChangeCanonical()
    {
        var context = CreateFullContext();
        var canonical = CadContextJsonV2Codec.SerializeCanonical(context);
        var hash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        context.Selection.Entities = context.Selection.Entities.Reverse().ToArray();

        Equal(canonical, CadContextJsonV2Codec.SerializeCanonical(context),
            "输入图元顺序不应改变规范JSON。");
        Equal(hash, CadContextJsonV2Codec.ComputeCanonicalSha256(context),
            "输入图元顺序不应改变规范哈希。");
    }

    internal static void GeometryArraysPreserveOriginalOrder()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Polyline);
        var polyline = context.Selection.Entities[0].Polyline!;
        polyline.Vertices =
        [
            new CadContextPolylineVertexV2
            {
                Position = new CadPoint2(9, 0), Bulge = 0,
            },
            new CadContextPolylineVertexV2
            {
                Position = new CadPoint2(1, 0), Bulge = 0,
            },
            new CadContextPolylineVertexV2
            {
                Position = new CadPoint2(5, 0), Bulge = 0,
            },
        ];
        var json = CadContextJsonV2Codec.SerializeCanonical(context);
        var pos9 = json.IndexOf("\"x\":9", StringComparison.Ordinal);
        var pos1 = json.IndexOf("\"x\":1,", StringComparison.Ordinal);
        var pos5 = json.IndexOf("\"x\":5", StringComparison.Ordinal);
        Equal(true, pos9 >= 0 && pos9 < pos1 && pos1 < pos5,
            "多段线顶点应保持原始顺序而非被排序。");
    }

    internal static void RejectsUnsafeValuesNanInfinityMagnitude()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities[0].Line!.Start.X = double.NaN;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_point3");

        context = MakeMinimalContext(CadContextEntityTypesV2.Circle);
        context.Selection.Entities[0].Circle!.Center.Y = double.PositiveInfinity;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_point3");

        context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities[0].Line!.End.Z = double.NegativeInfinity;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_point3");

        context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities[0].Line!.Start.X =
            CadContextJsonV2Constants.MaximumCoordinateMagnitude + 1;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_point3");

        context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Height = double.NaN;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_height");
    }

    internal static void RejectsControlCharactersAndBidiFormats()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text = "text\u0007bell";
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_unicode");

        context = MakeMinimalContext(CadContextEntityTypesV2.MText);
        context.Selection.Entities[0].MText!.Text = "before\u202Ehidden";
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_unicode");

        context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text = "embed\u200Bzero";
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_unicode");

        context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text = "has\u2029para";
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_unicode");
    }

    internal static void RejectsIllegalSurrogates()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.DbText);
        context.Selection.Entities[0].DbText!.Text = "abc\uDC00def";
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_text_unicode");
    }

    internal static void RejectsNullInNameField()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.BlockReference);
        context.Selection.Entities[0].BlockReference!.EffectiveName = null!;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_block_name");

        context = MakeMinimalContext(CadContextEntityTypesV2.Dimension);
        context.Selection.Entities[0].Dimension!.StyleName = null!;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_dimension_style");
    }

    internal static void RejectsNullInDocumentFields()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Document.DocumentId = null!;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_document_id");

        context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Document.Units = null!;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_units");
    }

    internal static void SelectionMustNotBeEmpty()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities = new CadContextEntityV2[0];
        context.Selection.EntityCount = 0;
        context.Selection.ParsedEntityCount = 0;
        context.Selection.UnsupportedEntityCount = 0;
        Contains(CadContextJsonV2Validator.Validate(context), "context_v2_entities_required");
    }

    internal static void PrivacyBoundaryIsComprehensive()
    {
        var json = CadContextJsonV2Codec.SerializeCanonical(CreateFullContext());
        ContainsText(json, "\"schema\":\"codex.autocad.cad-context\"");
        ContainsText(json, "\"schemaVersion\":2");
        ContainsText(json, "\"entityType\":\"line\"");
        ContainsText(json, "\"entityType\":\"unsupported\"");
        ContainsText(json, "\"reason\":\"unknown-entity-type\"");
        ContainsText(json, "中文表格");
        DoesNotContainText(json, "\"displayName\"");
        DoesNotContainText(json, "\"path\"");
        DoesNotContainText(json, "\"exception\"");
        DoesNotContainText(json, "\"stackTrace\"");
        DoesNotContainText(json, "\"TRUSTEDPATHS\"");
        DoesNotContainText(json, "C:\\");
        DoesNotContainText(json, "D:\\");
        DoesNotContainText(json, "\\\\server\\");
        DoesNotContainText(json, "\"password\"");
        DoesNotContainText(json, "\"secret\"");
        DoesNotContainText(json, "\"token\"");
        DoesNotContainText(json, "\"credential\"");
        DoesNotContainText(json, "\"apiKey\"");
        DoesNotContainText(json, "Bearer ");
        DoesNotContainText(json, "Authorization:");
    }

    internal static void V1FrozenVectorIsUnchanged()
    {
        var context = CreateCadContextV1();
        var failures = CadContextJsonV1Validator.Validate(context);
        Equal(0, failures.Length, JoinCodes(failures));

        var bytes = CadContextJsonV1Codec.SerializeCanonicalUtf8(context);
        var sha256 = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
        Equal(2225, bytes.Length, "v1规范向量必须保持2225字节。");
        Equal(
            "c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423",
            sha256,
            "v1 SHA-256必须保持不变。");
    }

    internal static void V2CanonicalVectorIsDeterministic()
    {
        var context = CreateFullContext();
        var hash1 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
        var hash2 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
        var hash3 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
        Equal(hash1, hash2, "多次运行应产生相同的规范哈希。");
        Equal(hash2, hash3, "多次运行应产生相同的规范哈希。");

        var json1 = CadContextJsonV2Codec.SerializeCanonical(context);
        var json2 = CadContextJsonV2Codec.SerializeCanonical(context);
        Equal(json1, json2, "多次运行应产生相同的规范JSON。");
    }

    internal static void NumberFormatIsDeterministicAcrossRuntimes()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Line);
        context.Selection.Entities[0].Line!.Start.X = 0.1d;
        context.Selection.Entities[0].Line!.Start.Y = -0d;
        context.Selection.Entities[0].Line!.Start.Z = 0.0000001d;
        context.Selection.Entities[0].Line!.End.X = 1e9;
        context.Selection.Entities[0].Line!.End.Y = 1e-9;
        context.Selection.Entities[0].Line!.End.Z = 123.456789012345678d;
        var json = CadContextJsonV2Codec.SerializeCanonical(context);
        ContainsText(json, "\"x\":0.10000000000000001");
        ContainsText(json, "\"y\":0,\"z\":9.9999999999999995e-8");
    }

    internal static void SplineTotalPointLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Spline);
        var spline = context.Selection.Entities[0].Spline!;
        spline.ControlPoints = Enumerable.Range(0, 128)
            .Select(i => new CadPoint3(i, 0, 0))
            .ToArray();
        spline.FitPoints = Enumerable.Range(0, 128)
            .Select(i => new CadPoint3(i, 1, 0))
            .ToArray();
        spline.HasFitData = true;
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "128+128=256个样条曲线点应恰好通过。");

        spline.ControlPoints = Enumerable.Range(0, 129)
            .Select(i => new CadPoint3(i, 0, 0))
            .ToArray();
        spline.FitPoints = Enumerable.Range(0, 128)
            .Select(i => new CadPoint3(i, 1, 0))
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_spline_point_limit");
    }

    internal static void LeaderVertexLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.Leader);
        var leader = context.Selection.Entities[0].Leader!;
        leader.Vertices = Enumerable.Range(0, 256)
            .Select(i => new CadPoint3(i, 0, 0))
            .ToArray();
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "恰好达到引线顶点上限应通过。");

        leader.Vertices = Enumerable.Range(0, 257)
            .Select(i => new CadPoint3(i, 0, 0))
            .ToArray();
        Contains(CadContextJsonV2Validator.Validate(context),
            "context_v2_leader_vertices_limit");
    }

    internal static void MLeaderTotalVertexLimitAndPlusOne()
    {
        var context = MakeMinimalContext(CadContextEntityTypesV2.MLeader);
        var mleader = context.Selection.Entities[0].MLeader!;
        mleader.LeaderLines =
        [
            new CadContextMLeaderLineV2
            {
                Vertices = Enumerable.Range(0, 128)
                    .Select(i => new CadPoint3(i, 0, 0))
                    .ToArray(),
            },
            new CadContextMLeaderLineV2
            {
                Vertices = Enumerable.Range(0, 128)
                    .Select(i => new CadPoint3(i, 1, 0))
                    .ToArray(),
            },
        ];
        Equal(0, CadContextJsonV2Validator.Validate(context).Length,
            "多条引线总计256个顶点应恰好通过。");

        mleader.LeaderLines =
        [
            new CadContextMLeaderLineV2
            {
                Vertices = Enumerable.Range(0, 129)
                    .Select(i => new CadPoint3(i, 0, 0))
                    .ToArray(),
            },
            new CadContextMLeaderLineV2
            {
                Vertices = Enumerable.Range(0, 128)
                    .Select(i => new CadPoint3(i, 1, 0))
                    .ToArray(),
            },
        ];
        var failures = CadContextJsonV2Validator.Validate(context);
        Contains(failures, "context_v2_mleader_vertex_limit");

        context = MakeMinimalContext(CadContextEntityTypesV2.MLeader);
        mleader = context.Selection.Entities[0].MLeader!;
        mleader.LeaderLines =
        [
            new CadContextMLeaderLineV2
            {
                Vertices = Enumerable.Range(0, 257)
                    .Select(i => new CadPoint3(i, 0, 0))
                    .ToArray(),
            },
        ];
        failures = CadContextJsonV2Validator.Validate(context);
        Contains(failures, "context_v2_mleader_vertices_limit");
        Contains(failures, "context_v2_mleader_vertex_limit");
    }

    internal static void FrozenLegalBoundaryFixtureIsDeterministic()
    {
        var context = CreateLegalBoundaryFixture();
        var failures = CadContextJsonV2Validator.Validate(context);
        Equal(0, failures.Length, "合法边界fixture验证应通过: " + JoinCodes(failures));

        var utf8 = CadContextJsonV2Codec.SerializeCanonicalUtf8(context);
        var sha256 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        var utf8_2 = CadContextJsonV2Codec.SerializeCanonicalUtf8(context);
        var sha256_2 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        var utf8_3 = CadContextJsonV2Codec.SerializeCanonicalUtf8(context);
        var sha256_3 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);

        Equal(sha256, sha256_2, "连续序列化第1/2次SHA-256必须一致。");
        Equal(sha256_2, sha256_3, "连续序列化第2/3次SHA-256必须一致。");
        Equal(utf8.Length, utf8_2.Length, "连续序列化第1/2次字节数必须一致。");
        Equal(utf8_2.Length, utf8_3.Length, "连续序列化第2/3次字节数必须一致。");

        for (var index = 0; index < utf8.Length; index++)
        {
            var b = utf8[index];
            Equal(true,
                b == 0x09 || b == 0x0A || b == 0x0D || (b >= 0x20 && b <= 0x7E),
                "字节偏移" + index + "值0x" + b.ToString("X2") + "不是纯ASCII。");
        }

        var asciiLine = "CAD_CONTEXT_JSON_V2_LIMITS sha256=" + sha256
            + " bytes=" + utf8.Length;
        foreach (var c in asciiLine)
        {
            Equal(true, c >= ' ' && c <= '~',
                "输出行含非ASCII字符: U+" + ((int)c).ToString("X4"));
        }
        Console.WriteLine(asciiLine);

        const string expectedSha256 =
            "fb532a9c3932f400d6fa093cab4d5b2f9abef3a65bb0b2eb890fbe2d1bbf629e";
        const int expectedBytes = 17721;
        Equal(expectedSha256, sha256, "边界fixture SHA-256必须与固定期望一致。");
        Equal(expectedBytes, utf8.Length, "边界fixture字节数必须与固定期望一致。");
    }

    private static CadContextJsonV2 CreateLegalBoundaryFixture()
    {
        const string hex = "0123456789abcdef";
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T12:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "fixture-limits-v1",
                DrawingFingerprint = new string('f', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('e', 64),
                EntityCount = 3,
                ParsedEntityCount = 3,
                UnsupportedEntityCount = 0,
                Complete = true,
                Entities =
                [
                    new CadContextEntityV2
                    {
                        Handle = "1",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Spline,
                        StateHash = new string(hex[1 % hex.Length], 64),
                        Layer = "0",
                        Spline = new CadContextSplineV2
                        {
                            Degree = 3,
                            IsRational = false,
                            HasFitData = true,
                            ControlPoints = Enumerable.Range(0, 128)
                                .Select(i => new CadPoint3(i, 0, 0))
                                .ToArray(),
                            FitPoints = Enumerable.Range(0, 128)
                                .Select(i => new CadPoint3(i, 1, 0))
                                .ToArray(),
                        },
                    },
                    new CadContextEntityV2
                    {
                        Handle = "2",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.Leader,
                        StateHash = new string(hex[2 % hex.Length], 64),
                        Layer = "0",
                        Leader = new CadContextLeaderV2
                        {
                            IsSplined = false,
                            HasArrowHead = true,
                            AnnotationType = "MText",
                            Normal = new CadPoint3(0, 0, 1),
                            Vertices = Enumerable.Range(0, 256)
                                .Select(i => new CadPoint3(i, 0, 0))
                                .ToArray(),
                        },
                    },
                    new CadContextEntityV2
                    {
                        Handle = "3",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypesV2.MLeader,
                        StateHash = new string(hex[3 % hex.Length], 64),
                        Layer = "0",
                        MLeader = new CadContextMLeaderV2
                        {
                            ContentType = "MTextContent",
                            Normal = new CadPoint3(0, 0, 1),
                            Text = "limit-test",
                            LeaderLines =
                            [
                                new CadContextMLeaderLineV2
                                {
                                    Vertices = Enumerable.Range(0, 128)
                                        .Select(i => new CadPoint3(i, 0, 0))
                                        .ToArray(),
                                },
                                new CadContextMLeaderLineV2
                                {
                                    Vertices = Enumerable.Range(0, 128)
                                        .Select(i => new CadPoint3(i, 1, 0))
                                        .ToArray(),
                                },
                            ],
                        },
                    },
                ],
            },
        };
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

    private static void Fail(string message)
    {
        throw new InvalidOperationException(message);
    }

    private static void AnyFailures(CadValidationFailure[] failures)
    {
        if (failures.Length == 0)
        {
            throw new InvalidOperationException("Expected at least one validation failure.");
        }
    }

    private static CadContextJsonV2 MakeMinimalContext(string entityType)
    {
        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-min",
                DrawingFingerprint = new string('b', 64),
                Revision = 1,
                CurrentSpace = CadContextJsonV2Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('c', 64),
                EntityCount = 1,
                ParsedEntityCount = entityType == CadContextEntityTypesV2.Unsupported ? 0 : 1,
                UnsupportedEntityCount = entityType == CadContextEntityTypesV2.Unsupported ? 1 : 0,
                Complete = entityType != CadContextEntityTypesV2.Unsupported,
                Entities = [MakeEntity(entityType)],
            },
        };
    }

    private static CadContextEntityV2 MakeEntity(string entityType)
    {
        var entity = new CadContextEntityV2
        {
            Handle = "1",
            OwnerSpaceHandle = "1F",
            EntityType = entityType,
            StateHash = new string('d', 64),
            Layer = "0",
        };
        PopulatePayload(entity, entityType);
        return entity;
    }

    private static void PopulatePayload(CadContextEntityV2 entity, string entityType)
    {
        switch (entityType)
        {
            case CadContextEntityTypesV2.Line:
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(0, 0, 0),
                    End = new CadPoint3(10, 0, 0),
                };
                break;
            case CadContextEntityTypesV2.Circle:
                entity.Circle = new CadContextCircleV2
                {
                    Center = new CadPoint3(5, 5, 0),
                    Radius = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
            case CadContextEntityTypesV2.Polyline:
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
                    ],
                };
                break;
            case CadContextEntityTypesV2.DbText:
                entity.DbText = new CadContextDbTextV2
                {
                    Text = "文字",
                    Position = new CadPoint3(1, 2, 0),
                    Height = 2.5,
                    Rotation = 0.1,
                };
                break;
            case CadContextEntityTypesV2.MText:
                entity.MText = new CadContextMTextV2
                {
                    Text = "多行文字",
                    Location = new CadPoint3(2, 3, 0),
                    TextHeight = 3,
                    Rotation = 0.2,
                };
                break;
            case CadContextEntityTypesV2.BlockReference:
                entity.BlockReference = new CadContextBlockReferenceV2
                {
                    Position = new CadPoint3(3, 4, 0),
                    Rotation = 0.3,
                    Scale = new CadPoint3(1, 1, 1),
                    EffectiveName = "TestBlock",
                    IsDynamic = false,
                    IsExternalReference = false,
                };
                break;
            case CadContextEntityTypesV2.Arc:
                entity.Arc = new CadContextArcV2
                {
                    Center = new CadPoint3(10, 10, 0),
                    Radius = 5,
                    StartAngle = 0.25,
                    EndAngle = 2.5,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
            case CadContextEntityTypesV2.Ellipse:
                entity.Ellipse = new CadContextEllipseV2
                {
                    Center = new CadPoint3(20, 10, 0),
                    MajorAxis = new CadPoint3(6, 0, 0),
                    RadiusRatio = 0.5,
                    StartParameter = 0,
                    EndParameter = 6.283185307179586,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
            case CadContextEntityTypesV2.Spline:
                entity.Spline = new CadContextSplineV2
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
                    FitPoints = new CadPoint3[0],
                };
                break;
            case CadContextEntityTypesV2.Point:
                entity.Point = new CadContextPointV2
                {
                    Position = new CadPoint3(7, 8, 9),
                    Normal = new CadPoint3(0, 0, 1),
                    EcsRotation = 0.5,
                };
                break;
            case CadContextEntityTypesV2.Ray:
                entity.Ray = new CadContextRayV2
                {
                    BasePoint = new CadPoint3(0, 0, 0),
                    SecondPoint = new CadPoint3(1, 1, 0),
                };
                break;
            case CadContextEntityTypesV2.Xline:
                entity.Xline = new CadContextXlineV2
                {
                    BasePoint = new CadPoint3(1, 0, 0),
                    SecondPoint = new CadPoint3(1, 2, 0),
                };
                break;
            case CadContextEntityTypesV2.Polyline2d:
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
                    ],
                };
                break;
            case CadContextEntityTypesV2.Polyline3d:
                entity.Polyline3d = new CadContextPolyline3dV2
                {
                    Closed = false,
                    Vertices =
                    [
                        new CadPoint3(0, 0, 0),
                        new CadPoint3(1, 2, 3),
                    ],
                };
                break;
            case CadContextEntityTypesV2.Dimension:
                entity.Dimension = new CadContextDimensionV2
                {
                    DimensionType = "AlignedDimension",
                    Measurement = 12.5,
                    DimensionText = "<>",
                    TextPosition = new CadPoint3(6, 2, 0),
                    TextRotation = 0,
                    Normal = new CadPoint3(0, 0, 1),
                    StyleName = "ISO-25",
                };
                break;
            case CadContextEntityTypesV2.Hatch:
                entity.Hatch = new CadContextHatchV2
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
                };
                break;
            case CadContextEntityTypesV2.Leader:
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
                    ],
                };
                break;
            case CadContextEntityTypesV2.MLeader:
                entity.MLeader = new CadContextMLeaderV2
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
                };
                break;
            case CadContextEntityTypesV2.Table:
                entity.Table = new CadContextTableV2
                {
                    Position = new CadPoint3(30, 20, 0),
                    Direction = new CadPoint3(1, 0, 0),
                    Rows = 1,
                    Columns = 1,
                    Width = 10,
                    Height = 5,
                    StyleName = "Standard",
                    Cells =
                    [
                        new CadContextTableCellV2 { Row = 0, Column = 0, Text = "X" },
                    ],
                };
                break;
            case CadContextEntityTypesV2.Unsupported:
                entity.Unsupported = new CadContextUnsupportedV2
                {
                    DxfName = "ACAD_PROXY_ENTITY",
                    Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
                };
                break;
        }
    }

    private static CadContextJsonV1 CreateCadContextV1()
    {
        return new CadContextJsonV1
        {
            CapturedAtUtc = "2026-07-19T08:30:45.123Z",
            Document = new CadContextDocumentV1
            {
                DocumentId = "doc-session-01",
                DrawingFingerprint = new string('a', 64),
                Revision = 42,
                CurrentSpace = CadContextJsonV1Constants.ModelSpace,
                DrawingVersion = "AC1027",
                Units = "millimeters",
            },
            Selection = new CadContextSelectionV1
            {
                SnapshotHash = new string('b', 64),
                EntityCount = 6,
                Entities =
                [
                    new CadContextEntityV1
                    {
                        Handle = "20",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.Line,
                        StateHash = new string('1', 64),
                        Layer = "结构层",
                        Line = new CadContextLineV1
                        {
                            Start = new CadPoint3(0, -3.5, 0),
                            End = new CadPoint3(100.25, 7.125, 0),
                        },
                    },
                    new CadContextEntityV1
                    {
                        Handle = "A",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.Circle,
                        StateHash = new string('2', 64),
                        Layer = "圆层",
                        Circle = new CadContextCircleV1
                        {
                            Center = new CadPoint3(1, 2, 3),
                            Radius = 12.5,
                            Normal = new CadPoint3(0, 0, 1),
                        },
                    },
                    new CadContextEntityV1
                    {
                        Handle = "30",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.Polyline,
                        StateHash = new string('3', 64),
                        Layer = "轮廓层",
                        Polyline = new CadContextPolylineV1
                        {
                            Closed = true,
                            Elevation = 5,
                            Normal = new CadPoint3(0, 0, 1),
                            Vertices =
                            [
                                new CadContextPolylineVertexV1
                                {
                                    Position = new CadPoint2(0, 0),
                                    Bulge = 0,
                                },
                                new CadContextPolylineVertexV1
                                {
                                    Position = new CadPoint2(10.5, 0),
                                    Bulge = 0.25,
                                },
                                new CadContextPolylineVertexV1
                                {
                                    Position = new CadPoint2(10.5, 20),
                                    Bulge = -0.125,
                                },
                            ],
                        },
                    },
                    new CadContextEntityV1
                    {
                        Handle = "B",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.DbText,
                        StateHash = new string('4', 64),
                        Layer = "文字层",
                        DbText = new CadContextDbTextV1
                        {
                            Text = "设备A",
                            Position = new CadPoint3(8, 9, 0),
                            Height = 2.5,
                            Rotation = 0.5,
                        },
                    },
                    new CadContextEntityV1
                    {
                        Handle = "40",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.MText,
                        StateHash = new string('5', 64),
                        Layer = "说明层",
                        MText = new CadContextMTextV1
                        {
                            Text = "第一行\n第二行\t🙂",
                            Location = new CadPoint3(-2, 4.25, 0),
                            TextHeight = 3,
                            Rotation = 0,
                        },
                    },
                    new CadContextEntityV1
                    {
                        Handle = "C",
                        OwnerSpaceHandle = "1F",
                        EntityType = CadContextEntityTypes.BlockReference,
                        StateHash = new string('6', 64),
                        Layer = "设备层",
                        BlockReference = new CadContextBlockReferenceV1
                        {
                            Position = new CadPoint3(-1, 2.5, 0),
                            Rotation = 1.5707963267948966,
                            Scale = new CadPoint3(1, -1, 2),
                            EffectiveName = "动态块_A",
                            IsDynamic = true,
                            IsExternalReference = false,
                        },
                    },
                ],
            },
        };
    }
}
