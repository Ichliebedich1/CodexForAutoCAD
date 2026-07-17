using Codex.AutoCAD.Contracts;

var specs = new (string Name, Action Run)[]
{
    ("有效直线计划通过", ValidLineBatchPasses),
    ("零长度直线被拒绝", ZeroLengthLineFails),
    ("NaN坐标被拒绝", NonFiniteCoordinateFails),
    ("低报风险被拒绝", UnderstatedRiskFails),
    ("重复Handle被拒绝", DuplicateHandleFails),
    ("协议版本不匹配被拒绝", ProtocolMismatchFails)
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine("PASS " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception.Message);
    }
}

Console.WriteLine($"{specs.Length - failed}/{specs.Length} specs passed");
return failed == 0 ? 0 : 1;

static void ValidLineBatchPasses()
{
    var failures = CadContractValidator.Validate(CreateLineBatch());
    Equal(0, failures.Length, string.Join("; ", failures.Select(static failure => failure.Code)));
}

static void ZeroLengthLineFails()
{
    var batch = CreateLineBatch();
    var line = (CreateLineOperation)batch.Operations[0];
    line.End = new CadPoint3(line.Start.X, line.Start.Y, line.Start.Z);
    Contains(CadContractValidator.Validate(batch), "line_zero_length");
}

static void NonFiniteCoordinateFails()
{
    var batch = CreateLineBatch();
    ((CreateLineOperation)batch.Operations[0]).End.X = double.NaN;
    Contains(CadContractValidator.Validate(batch), "end_finite");
}

static void UnderstatedRiskFails()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.Preview;
    Contains(CadContractValidator.Validate(batch), "risk_understated");
}

static void DuplicateHandleFails()
{
    var batch = CreateLineBatch();
    batch.DeclaredRisk = CadRiskLevel.DestructiveWrite;
    batch.Operations =
    [
        new EraseEntitiesOperation
        {
            OperationId = "erase-1",
            Handles = ["1A", "1a"]
        }
    ];
    Contains(CadContractValidator.Validate(batch), "handle_duplicate");
}

static void ProtocolMismatchFails()
{
    var batch = CreateLineBatch();
    batch.ProtocolVersion = ProtocolConstants.CurrentVersion + 1;
    Contains(CadContractValidator.Validate(batch), "protocol_version");
}

static CadOperationBatch CreateLineBatch()
{
    return new CadOperationBatch
    {
        BatchId = "batch-1",
        ThreadId = "thread-1",
        TurnId = "turn-1",
        Document = new CadDocumentRef
        {
            DocumentId = "doc-1",
            DrawingFingerprint = "sha256:drawing",
            Revision = 7
        },
        SelectionSnapshotHash = "sha256:selection",
        DeclaredRisk = CadRiskLevel.ReversibleWrite,
        Operations =
        [
            new CreateLineOperation
            {
                OperationId = "line-1",
                Start = new CadPoint3(0, 0, 0),
                End = new CadPoint3(100, 0, 0),
                Layer = "0"
            }
        ]
    };
}

static void Contains(IEnumerable<CadValidationFailure> failures, string expectedCode)
{
    if (!failures.Any(failure => string.Equals(failure.Code, expectedCode, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Expected failure code: " + expectedCode);
    }
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}. {message}");
    }
}
