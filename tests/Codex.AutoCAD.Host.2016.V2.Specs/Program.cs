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

CadContextEntityV2 Unsupported(string handle)
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
            DxfName = "ACAD_PROXY_ENTITY",
            Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
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
