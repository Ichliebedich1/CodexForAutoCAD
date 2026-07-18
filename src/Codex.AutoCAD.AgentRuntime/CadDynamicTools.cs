using System.Collections.ObjectModel;
using System.Text.Json;

namespace Codex.AutoCAD.AgentRuntime;

public readonly record struct AgentCadPoint3d(double X, double Y, double Z);

public abstract record AgentCadOperationProposal(string Type);

public sealed record AgentCadCreateLineProposal(
    AgentCadPoint3d Start,
    AgentCadPoint3d End,
    string? Layer) : AgentCadOperationProposal("create_line");

/// <summary>
/// Unbound CAD proposal emitted for the trusted AutoCAD host. Document identity, revision and
/// selection preconditions are intentionally absent; the host must bind those values itself.
/// </summary>
public sealed record AgentCadOperationBatchProposal
{
    public AgentCadOperationBatchProposal(
        string proposalId,
        string callId,
        string threadId,
        string turnId,
        IReadOnlyList<AgentCadOperationProposal> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ProposalId = proposalId;
        CallId = callId;
        ThreadId = threadId;
        TurnId = turnId;
        Operations = CloneOperations(operations);
    }

    public string ProposalId { get; }

    public string CallId { get; }

    public string ThreadId { get; }

    public string TurnId { get; }

    public IReadOnlyList<AgentCadOperationProposal> Operations { get; }

    internal AgentCadOperationBatchProposal DeepClone()
        => new(ProposalId, CallId, ThreadId, TurnId, Operations);

    private static ReadOnlyCollection<AgentCadOperationProposal> CloneOperations(
        IReadOnlyList<AgentCadOperationProposal> operations)
    {
        var clone = new AgentCadOperationProposal[operations.Count];
        for (var index = 0; index < operations.Count; index++)
        {
            clone[index] = operations[index] switch
            {
                AgentCadCreateLineProposal line => new AgentCadCreateLineProposal(
                    line.Start,
                    line.End,
                    line.Layer),
                null => throw new ArgumentException(
                    "CAD proposal operations cannot contain null entries.",
                    nameof(operations)),
                _ => throw new ArgumentException(
                    "CAD proposal contains an unsupported operation type.",
                    nameof(operations)),
            };
        }

        return Array.AsReadOnly(clone);
    }
}

internal static class CadDynamicToolCatalog
{
    public const string Namespace = "cad";
    public const string ProposeOperations = "propose_operations";
    public const int MaximumOperations = 500;
    private const double MaximumCoordinateMagnitude = 1_000_000_000d;

    private static readonly JsonElement ProposeOperationsInputSchema = CreateInputSchema();

    public static IReadOnlyList<DynamicToolNamespaceWire> CreateWireTools()
        => new[]
        {
            new DynamicToolNamespaceWire(
                "namespace",
                Namespace,
                "Create declaration-only CAD proposals. The trusted AutoCAD host performs document binding, preview and approval.",
                new[]
                {
                    new DynamicToolFunctionWire(
                        "function",
                        ProposeOperations,
                        "Propose CAD operations without changing the active drawing. Currently only create_line is accepted.",
                        ProposeOperationsInputSchema.Clone()),
                }),
        };

    public static AgentCadOperationBatchProposal ParseProposal(
        string proposalId,
        string callId,
        string threadId,
        string turnId,
        JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new CadProposalValidationException("arguments must be an object.");
        }

        EnsureOnlyProperties(arguments, "arguments", "operations");
        if (!arguments.TryGetProperty("operations", out var operationsElement)
            || operationsElement.ValueKind != JsonValueKind.Array)
        {
            throw new CadProposalValidationException("operations must be an array.");
        }

        var count = operationsElement.GetArrayLength();
        if (count is < 1 or > MaximumOperations)
        {
            throw new CadProposalValidationException(
                $"operations must contain between 1 and {MaximumOperations} entries.");
        }

        var operations = new AgentCadOperationProposal[count];
        var index = 0;
        foreach (var operationElement in operationsElement.EnumerateArray())
        {
            operations[index] = ParseOperation(operationElement, index);
            index++;
        }

        return new AgentCadOperationBatchProposal(
            proposalId,
            callId,
            threadId,
            turnId,
            operations);
    }

    private static AgentCadOperationProposal ParseOperation(JsonElement operation, int index)
    {
        if (operation.ValueKind != JsonValueKind.Object)
        {
            throw new CadProposalValidationException($"operations[{index}] must be an object.");
        }

        EnsureOnlyProperties(operation, $"operations[{index}]", "type", "start", "end", "layer");
        var type = RequiredString(operation, "type", $"operations[{index}]");
        if (!string.Equals(type, "create_line", StringComparison.Ordinal))
        {
            throw new CadProposalValidationException(
                $"operations[{index}].type '{type}' is not allowed; only create_line is supported.");
        }

        var start = ParsePoint(operation, "start", $"operations[{index}]");
        var end = ParsePoint(operation, "end", $"operations[{index}]");
        if (start == end)
        {
            throw new CadProposalValidationException($"operations[{index}] line endpoints must differ.");
        }

        var layer = OptionalString(operation, "layer");
        if (layer is not null)
        {
            if (string.IsNullOrWhiteSpace(layer) || layer.Length > 255 || layer.Any(char.IsControl))
            {
                throw new CadProposalValidationException($"operations[{index}].layer is invalid.");
            }
        }

        return new AgentCadCreateLineProposal(start, end, layer);
    }

    private static AgentCadPoint3d ParsePoint(JsonElement parent, string property, string context)
    {
        if (!parent.TryGetProperty(property, out var point) || point.ValueKind != JsonValueKind.Object)
        {
            throw new CadProposalValidationException($"{context}.{property} must be an object.");
        }

        EnsureOnlyProperties(point, $"{context}.{property}", "x", "y", "z");
        return new AgentCadPoint3d(
            RequiredCoordinate(point, "x", $"{context}.{property}"),
            RequiredCoordinate(point, "y", $"{context}.{property}"),
            OptionalCoordinate(point, "z", $"{context}.{property}") ?? 0d);
    }

    private static double RequiredCoordinate(JsonElement point, string property, string context)
        => OptionalCoordinate(point, property, context)
            ?? throw new CadProposalValidationException($"{context}.{property} is required.");

    private static double? OptionalCoordinate(JsonElement point, string property, string context)
    {
        if (!point.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || !double.IsFinite(number) || Math.Abs(number) > MaximumCoordinateMagnitude)
        {
            throw new CadProposalValidationException(
                $"{context}.{property} must be a finite coordinate within {MaximumCoordinateMagnitude}.");
        }

        return number;
    }

    private static string RequiredString(JsonElement parent, string property, string context)
    {
        var value = OptionalString(parent, property);
        return value ?? throw new CadProposalValidationException($"{context}.{property} is required.");
    }

    private static string? OptionalString(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void EnsureOnlyProperties(
        JsonElement value,
        string context,
        params string[] allowedProperties)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new CadProposalValidationException(
                    $"{context} contains unsupported property '{property.Name}'.");
            }
        }
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["operations"],
              "properties": {
                "operations": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 500,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["type", "start", "end"],
                    "properties": {
                      "type": { "type": "string", "enum": ["create_line"] },
                      "start": { "$ref": "#/$defs/point3d" },
                      "end": { "$ref": "#/$defs/point3d" },
                      "layer": { "type": "string", "minLength": 1, "maxLength": 255 }
                    }
                  }
                }
              },
              "$defs": {
                "point3d": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["x", "y"],
                  "properties": {
                    "x": { "type": "number", "minimum": -1000000000, "maximum": 1000000000 },
                    "y": { "type": "number", "minimum": -1000000000, "maximum": 1000000000 },
                    "z": { "type": "number", "minimum": -1000000000, "maximum": 1000000000 }
                  }
                }
              }
            }
            """);
        return document.RootElement.Clone();
    }
}

internal sealed class CadProposalValidationException(string message) : Exception(message);
