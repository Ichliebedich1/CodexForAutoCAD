#nullable enable

using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

const string ZeroHash =
    "0000000000000000000000000000000000000000000000000000000000000000";

var specs = new[]
{
    new SpecCase("HOST2016-V2-001", MixedSelectionIsExplicit),
    new SpecCase("HOST2016-V2-002", InputOrderIsDeterministic),
    new SpecCase("HOST2016-V2-003", DuplicateHandleIsRejected),
    new SpecCase("HOST2016-V2-004", NullEntityIsRejected),
    new SpecCase("HOST2016-V2-005", InvalidHandleIsRejected),
    new SpecCase("HOST2016-V2-006", RequiredNameBoundaryIsFailClosed),
    new SpecCase("HOST2016-V2-007", CountBoundaryIsFailClosed),
    new SpecCase("HOST2016-V2-008", CountAccumulationIsBounded),
    new SpecCase("HOST2016-V2-009", TextLimitProducesStructuredCode),
    new SpecCase("HOST2016-V2-010", CoordinateLimitProducesStructuredCode),
    new SpecCase("HOST2016-V2-011", ContractFailureClassificationIsNarrow),
    new SpecCase("HOST2016-V2-012", NameFailureClassificationIsNarrow),
    new SpecCase("HOST2016-V2-013", SelectionReadIssuesExposeActualTypes),
    new SpecCase("HOST2016-V2-014", ChineseTypeCatalogListsNineteenSupportedTypes),
    new SpecCase("HOST2016-V2-015", ReadableSummaryUsesActualTypeStatistics),
    new SpecCase("HOST2016-V2-016", HighValueDrawingIndexCategoriesRemainLimited),
};

var passed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Execute();
        Console.WriteLine("PASS " + spec.Id);
        passed++;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            "FAIL " + spec.Id + " " + exception.GetType().Name + " " + exception.Message);
    }
}

Console.WriteLine(passed + "/" + specs.Length + " specs passed");
return passed == specs.Length ? 0 : 1;

void MixedSelectionIsExplicit()
{
    var snapshot = CanonicalSelectionHashV2.Build(
        new[] { Unsupported("B"), Line("A", 0d) });

    Equal(2, snapshot.Selection.EntityCount, "entity count");
    Equal(1, snapshot.Selection.ParsedEntityCount, "parsed count");
    Equal(1, snapshot.Selection.UnsupportedEntityCount, "unsupported count");
    Equal(false, snapshot.Selection.Complete, "complete flag");
    Equal("A", snapshot.Selection.Entities[0].Handle, "numeric handle ordering first");
    Equal("B", snapshot.Selection.Entities[1].Handle, "numeric handle ordering second");
    NotEqual(ZeroHash, snapshot.Selection.Entities[0].StateHash, "line state hash");
    NotEqual(ZeroHash, snapshot.Selection.Entities[1].StateHash, "unsupported state hash");
    IsLowerSha256(snapshot.Selection.SnapshotHash, "snapshot hash");
    Equal(
        "0ba4970c01da7877a41c9de960f1decd090d0f6646e9eff7a979c71db5bb8990",
        snapshot.Selection.SnapshotHash,
        "frozen mixed-selection hash");
    Equal(147, snapshot.CanonicalLength, "frozen mixed-selection bytes");
    if (snapshot.CanonicalLength <= 0)
    {
        throw new InvalidOperationException("selection canonical length must be positive");
    }

    Console.WriteLine(
        "HOST2016_V2_SNAPSHOT sha256=" + snapshot.Selection.SnapshotHash
        + " bytes=" + snapshot.CanonicalLength);
}

void InputOrderIsDeterministic()
{
    var first = CanonicalSelectionHashV2.Build(
        new[] { Line("10", 1d), Unsupported("2") });
    var second = CanonicalSelectionHashV2.Build(
        new[] { Unsupported("2"), Line("10", 1d) });

    Equal(first.Selection.SnapshotHash, second.Selection.SnapshotHash, "snapshot hash");
    Equal(first.CanonicalLength, second.CanonicalLength, "canonical length");
    Equal("2", first.Selection.Entities[0].Handle, "numeric ordering");
    Equal("10", first.Selection.Entities[1].Handle, "numeric ordering");
    Equal(
        first.Selection.Entities[0].StateHash,
        second.Selection.Entities[0].StateHash,
        "unsupported state hash");
    Equal(
        first.Selection.Entities[1].StateHash,
        second.Selection.Entities[1].StateHash,
        "line state hash");
}

void DuplicateHandleIsRejected()
{
    ExpectCode(
        "v2-duplicate-handle",
        () => CanonicalSelectionHashV2.Build(new[] { Line("A", 0d), Line("A", 1d) }));
}

void NullEntityIsRejected()
{
    ExpectCode(
        "v2-null-entity",
        () => CanonicalSelectionHashV2.Build(new CadContextEntityV2[] { null! }));
}

void InvalidHandleIsRejected()
{
    ExpectCode(
        "v2-invalid-handle",
        () => CanonicalSelectionHashV2.Build(new[] { Line("0", 0d) }));
}

void RequiredNameBoundaryIsFailClosed()
{
    Equal(true, CadContextV2CapturePolicy.IsSafeRequiredName("结构层"), "Chinese layer");
    Equal(
        true,
        CadContextV2CapturePolicy.IsSafeRequiredName(
            new string('A', CadContextJsonV2Constants.MaximumNameCharacters)),
        "name at limit");
    Equal(false, CadContextV2CapturePolicy.IsSafeRequiredName(string.Empty), "empty name");
    Equal(false, CadContextV2CapturePolicy.IsSafeRequiredName("A\nB"), "control name");
    Equal(false, CadContextV2CapturePolicy.IsSafeRequiredName("A\u200BB"), "format name");
    Equal(
        false,
        CadContextV2CapturePolicy.IsSafeRequiredName(
            new string('A', CadContextJsonV2Constants.MaximumNameCharacters + 1)),
        "name over limit");
}

void CountBoundaryIsFailClosed()
{
    Equal(true, CadContextV2CapturePolicy.IsWithinCountLimit(64, 64), "count at limit");
    Equal(false, CadContextV2CapturePolicy.IsWithinCountLimit(65, 64), "count over limit");
    Equal(false, CadContextV2CapturePolicy.IsWithinCountLimit(-1, 64), "negative count");
    Equal(false, CadContextV2CapturePolicy.IsWithinCountLimit(0, -1), "negative maximum");
}

void CountAccumulationIsBounded()
{
    int total;
    Equal(
        true,
        CadContextV2CapturePolicy.TryAccumulateCount(128, 128, 256, out total),
        "accumulation at limit");
    Equal(256, total, "accumulated total");
    Equal(
        false,
        CadContextV2CapturePolicy.TryAccumulateCount(256, 1, 256, out total),
        "accumulation over limit");
    Equal(0, total, "failed accumulation output");
    Equal(
        false,
        CadContextV2CapturePolicy.TryAccumulateCount(int.MaxValue, 1, int.MaxValue, out total),
        "overflow-safe accumulation");
}

void TextLimitProducesStructuredCode()
{
    ExpectCode(
        "v2-context_v2_text_characters",
        () => CanonicalSelectionHashV2.Build(
            new[]
            {
                DbText(
                    "A",
                    new string('T', CadContextJsonV2Constants.MaximumTextCharacters + 1)),
            }));
}

void CoordinateLimitProducesStructuredCode()
{
    var entity = Line("A", 0d);
    entity.Line!.End.X = CadContextJsonV2Constants.MaximumCoordinateMagnitude + 1d;
    ExpectCode(
        "v2-context_v2_point3",
        () => CanonicalSelectionHashV2.Build(new[] { entity }));
}

void ContractFailureClassificationIsNarrow()
{
    Equal(
        CadContextUnsupportedReasonsV2.EntityDataLimit,
        CadContextV2CapturePolicy.ClassifyContractFailure(
            "v2-context_v2_text_characters"),
        "text character limit");
    Equal(
        CadContextUnsupportedReasonsV2.EntityDataLimit,
        CadContextV2CapturePolicy.ClassifyContractFailure("v2-context_v2_point3"),
        "coordinate limit");
    Equal(
        CadContextUnsupportedReasonsV2.EntityReadFailed,
        CadContextV2CapturePolicy.ClassifyContractFailure(
            "v2-context_v2_shape_mismatch"),
        "shape mismatch");
    Equal(
        CadContextUnsupportedReasonsV2.EntityReadFailed,
        CadContextV2CapturePolicy.ClassifyContractFailure(
            "v2-context_v2_text_unicode"),
        "invalid Unicode");
}

void NameFailureClassificationIsNarrow()
{
    var atLimit = new string('A', CadContextJsonV2Constants.MaximumNameCharacters);
    var overLimit = new string('A', CadContextJsonV2Constants.MaximumNameCharacters + 1);
    Equal(false, CadContextV2CapturePolicy.IsNameDataLimit(atLimit), "name at limit");
    Equal(true, CadContextV2CapturePolicy.IsNameDataLimit(overLimit), "name over limit");
    Equal(false, CadContextV2CapturePolicy.IsNameDataLimit("A\nB"), "invalid control is not a size limit");
    Equal(false, CadContextV2CapturePolicy.IsSafeRequiredName("A\nB"), "invalid control rejected");
}

void SelectionReadIssuesExposeActualTypes()
{
    var selection = new CadContextSelectionV2
    {
        Entities = new[]
        {
            Unsupported("1", "ACAD_PROXY_ENTITY", CadContextUnsupportedReasonsV2.UnknownEntityType),
            Unsupported("2", "acad_proxy_entity", CadContextUnsupportedReasonsV2.UnknownEntityType),
            Unsupported("3", "3DSOLID", CadContextUnsupportedReasonsV2.EntityDataLimit),
            Unsupported("4", "HATCH", CadContextUnsupportedReasonsV2.EntityReadFailed),
            Line("5", 0d),
        },
    };

    var statistics = CadReadTypeStatistics.FromSelection(selection);
    Equal(4, statistics.TotalCount, "read issue total");
    Equal(2, statistics.UnknownTypeCount, "unsupported type count");
    Equal(1, statistics.DataLimitedCount, "data limited count");
    Equal(1, statistics.ReadFailedCount, "read failed count");
    Equal(3, statistics.ActualTypeCounts.Length, "actual type bucket count");

    var summary = CadReadTypeStatistics.FormatSummary(statistics, 8);
    Contains(summary, "代理对象(ACAD_PROXY_ENTITY) x2", "proxy type summary");
    Contains(summary, "三维实体(3DSOLID) x1", "solid type summary");
    Contains(summary, "图案填充(HATCH) x1", "hatch type summary");
    Contains(summary, "未支持类型 2，数据超限 1，读取失败 1", "reason summary");
    NotContains(summary, "Layer", "summary must not contain layer data");
    NotContains(summary, "Handle", "summary must not contain handles");
}

void ChineseTypeCatalogListsNineteenSupportedTypes()
{
    var catalog = CadReadTypeStatistics.BuildSupportedTypeCatalog();
    Contains(catalog, "01. 直线 (Line)", "first supported type");
    Contains(catalog, "19. 表格 (Table)", "last supported type");
    Contains(catalog, "整图索引受限类别", "limited read categories");
    var numberedEntries = 0;
    foreach (var line in catalog.Replace("\r", string.Empty).Split('\n'))
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(line, "^[0-9]{2}\\. "))
        {
            numberedEntries++;
        }
    }
    Equal(19, numberedEntries, "catalog must contain exactly nineteen numbered types");
    NotContains(catalog, "C:\\", "catalog must not contain a local path");
}

void ReadableSummaryUsesActualTypeStatistics()
{
    var snapshot = CanonicalSelectionHashV2.Build(new[]
    {
        Line("1", 0d),
        Unsupported(
            "2",
            "ACAD_PROXY_ENTITY",
            CadContextUnsupportedReasonsV2.UnknownEntityType),
        Unsupported(
            "3",
            "3DSOLID",
            CadContextUnsupportedReasonsV2.EntityDataLimit),
    });
    var context = CadContextJsonV2Mapper.Build(
        new CadContextDocumentMetadata(
            "document-0123456789abcdef",
            new string('a', 64),
            42,
            CadContextJsonV2Constants.ModelSpace,
            "AC1027",
            "millimeters"),
        snapshot,
        DateTimeOffset.Parse(
            "2026-07-22T12:34:56.789Z",
            System.Globalization.CultureInfo.InvariantCulture));
    var canonicalJson = CadContextJsonV2Codec.SerializeCanonical(context);
    var summary = CadContextJsonV2Mapper.BuildReadableSummary(
        context,
        CadContextJsonV2Codec.ComputeCanonicalSha256(context),
        System.Text.Encoding.UTF8.GetByteCount(canonicalJson));

    Equal(0, CadContextJsonV2Validator.Validate(context).Length, "mapped context validation");
    Contains(summary, "选择图元：3  成功解析：1  未解析：2  完整：否", "selection completeness");
    Contains(summary, "未支持类型 1，数据超限 1，读取失败 0", "mapped reason summary");
    Contains(summary, "代理对象(ACAD_PROXY_ENTITY) x1", "mapped proxy summary");
    Contains(summary, "三维实体(3DSOLID) x1", "mapped solid summary");
    Contains(summary, "[2] 未解析对象", "mapped unsupported entity summary");
}

void HighValueDrawingIndexCategoriesRemainLimited()
{
    var types = new[]
    {
        DrawingIndexEntityTypes.Region,
        DrawingIndexEntityTypes.Solid,
        DrawingIndexEntityTypes.Mesh,
        DrawingIndexEntityTypes.Surface,
        DrawingIndexEntityTypes.RasterImage,
        DrawingIndexEntityTypes.Underlay,
        DrawingIndexEntityTypes.Proxy,
        DrawingIndexEntityTypes.Wipeout,
    };
    foreach (var entityType in types)
    {
        Equal(true, DrawingIndexEntityTypes.IsHighValueLimited(entityType),
            "high-value DrawingIndex category");
    }
    Equal(false, DrawingIndexEntityTypes.IsHighValueLimited(CadContextEntityTypesV2.Line),
        "strong v2 entity type must not become data_limited");
    Equal(false, DrawingIndexEntityTypes.IsHighValueLimited("unknown"),
        "unrecognized category must remain unsupported rather than limited");

    var selection = new CadContextSelectionV2
    {
        Entities = new[]
        {
            Unsupported("1", "SOLID"),
            Unsupported("2", "FACE"),
            Unsupported("3", "POLYFACEMESH"),
            Unsupported("4", "WIPEOUT"),
        },
    };
    var summary = CadReadTypeStatistics.FormatSummary(
        CadReadTypeStatistics.FromSelection(selection),
        8);
    Contains(summary, "二维实体(SOLID) x1", "solid display name");
    Contains(summary, "三维面(FACE) x1", "face display name");
    Contains(summary, "网格(POLYFACEMESH) x1", "mesh display name");
    Contains(summary, "遮罩(WIPEOUT) x1", "wipeout display name");
}

CadContextEntityV2 Line(string handle, double offset)
{
    return new CadContextEntityV2
    {
        Handle = handle,
        OwnerSpaceHandle = "1",
        EntityType = CadContextEntityTypesV2.Line,
        StateHash = ZeroHash,
        Layer = "0",
        Line = new CadContextLineV2
        {
            Start = new CadPoint3(offset, 0d, 0d),
            End = new CadPoint3(offset + 10d, 0d, 0d),
        },
    };
}

CadContextEntityV2 DbText(string handle, string text)
{
    return new CadContextEntityV2
    {
        Handle = handle,
        OwnerSpaceHandle = "1",
        EntityType = CadContextEntityTypesV2.DbText,
        StateHash = ZeroHash,
        Layer = "0",
        DbText = new CadContextDbTextV2
        {
            Text = text,
            Position = new CadPoint3(0d, 0d, 0d),
            Height = 1d,
            Rotation = 0d,
        },
    };
}

CadContextEntityV2 Unsupported(
    string handle,
    string dxfName = "ACAD_PROXY_ENTITY",
    string reason = CadContextUnsupportedReasonsV2.UnknownEntityType)
{
    return new CadContextEntityV2
    {
        Handle = handle,
        OwnerSpaceHandle = "1",
        EntityType = CadContextEntityTypesV2.Unsupported,
        StateHash = ZeroHash,
        Layer = "0",
        Unsupported = new CadContextUnsupportedV2
        {
            DxfName = dxfName,
            Reason = reason,
        },
    };
}

void ExpectCode(string expected, Action action)
{
    try
    {
        action();
    }
    catch (ContextValidationException exception)
    {
        Equal(expected, exception.Code, "validation code");
        return;
    }

    throw new InvalidOperationException("Expected ContextValidationException: " + expected);
}

void IsLowerSha256(string value, string name)
{
    if (value.Length != 64
        || value.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
    {
        throw new InvalidOperationException(name + " is not a lower-case SHA-256");
    }
}

void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            name + ": expected=" + expected + ", actual=" + actual);
    }
}

void NotEqual<T>(T unexpected, T actual, string name)
{
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
    {
        throw new InvalidOperationException(name + ": unexpected=" + unexpected);
    }
}

void Contains(string value, string expected, string name)
{
    if (value == null || value.IndexOf(expected, StringComparison.Ordinal) < 0)
    {
        throw new InvalidOperationException(name + ": missing=" + expected);
    }
}

void NotContains(string value, string unexpected, string name)
{
    if (value != null && value.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
    {
        throw new InvalidOperationException(name + ": unexpected=" + unexpected);
    }
}

sealed class SpecCase
{
    internal SpecCase(string id, Action execute)
    {
        Id = id;
        Execute = execute;
    }

    internal string Id { get; }

    internal Action Execute { get; }
}
