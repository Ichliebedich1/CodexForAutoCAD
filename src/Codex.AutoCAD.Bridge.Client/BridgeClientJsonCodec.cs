using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.Text;
using System.Xml;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Bridge.Client;

internal static class BridgeClientJsonCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private static readonly JsonFieldSpec[] EnvelopeShape =
    {
        new JsonFieldSpec("protocolVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("messageId", JsonFieldKind.String),
        new JsonFieldSpec("correlationId", JsonFieldKind.String),
        new JsonFieldSpec("sessionId", JsonFieldKind.String),
        new JsonFieldSpec("sequence", JsonFieldKind.Integer),
        new JsonFieldSpec("messageType", JsonFieldKind.String),
        new JsonFieldSpec("payloadJson", JsonFieldKind.String),
        new JsonFieldSpec("nonce", JsonFieldKind.String),
        new JsonFieldSpec("mac", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] RequestPayloadShape =
    {
        new JsonFieldSpec("method", JsonFieldKind.String),
        new JsonFieldSpec("bodyJson", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] ResponsePayloadShape =
    {
        new JsonFieldSpec("bodyJson", JsonFieldKind.String),
        new JsonFieldSpec("errorCode", JsonFieldKind.String),
        new JsonFieldSpec("errorMessage", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] CapabilitiesRequestShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("clientName", JsonFieldKind.String),
        new JsonFieldSpec("clientVersion", JsonFieldKind.String),
        new JsonFieldSpec("hostTarget", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] CapabilitiesResponseShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("minimumCompatibleVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("agentInstanceId", JsonFieldKind.String),
        new JsonFieldSpec("cadContextSchema", JsonFieldKind.String),
        new JsonFieldSpec("cadContextSchemaVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("methods", JsonFieldKind.StringArray),
        new JsonFieldSpec("eventKinds", JsonFieldKind.StringArray),
        new JsonFieldSpec("approvalDecisions", JsonFieldKind.StringArray),
        new JsonFieldSpec("cadWriteAvailable", JsonFieldKind.Boolean),
    };

    private static readonly JsonFieldSpec[] CapabilitiesResponseV2ExtendedShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("minimumCompatibleVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("agentInstanceId", JsonFieldKind.String),
        new JsonFieldSpec("cadContextSchema", JsonFieldKind.String),
        new JsonFieldSpec("cadContextSchemaVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("supportedCadContextSchemas", JsonFieldKind.SchemaArray),
        new JsonFieldSpec("methods", JsonFieldKind.StringArray),
        new JsonFieldSpec("eventKinds", JsonFieldKind.StringArray),
        new JsonFieldSpec("approvalDecisions", JsonFieldKind.StringArray),
        new JsonFieldSpec("cadWriteAvailable", JsonFieldKind.Boolean),
    };

    private static readonly JsonFieldSpec[] ThreadStartRequestShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("conversationId", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] ThreadStartResponseShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] TurnStartResponseShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("acceptedContextSha256", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] TurnStartV2ResponseShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("acceptedContextV2Sha256", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] TurnInterruptRequestShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] ApprovalResolveRequestShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("approvalId", JsonFieldKind.String),
        new JsonFieldSpec("decision", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] DrawingQueryRequestShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("requestId", JsonFieldKind.String),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("toolCallId", JsonFieldKind.String),
        new JsonFieldSpec("queryId", JsonFieldKind.String),
        new JsonFieldSpec("filter", JsonFieldKind.Object),
        new JsonFieldSpec("pageSize", JsonFieldKind.Integer),
        new JsonFieldSpec("cursor", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] DrawingQueryResponseShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("requestId", JsonFieldKind.String),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("toolCallId", JsonFieldKind.String),
        new JsonFieldSpec("queryId", JsonFieldKind.String),
        new JsonFieldSpec("query", JsonFieldKind.Object),
    };

    private static readonly JsonFieldSpec[] CancelPayloadShape =
    {
        new JsonFieldSpec("reason", JsonFieldKind.String),
    };

    private static readonly JsonFieldSpec[] AgentEventShape =
    {
        new JsonFieldSpec("contractVersion", JsonFieldKind.Integer),
        new JsonFieldSpec("kind", JsonFieldKind.String),
        new JsonFieldSpec("eventId", JsonFieldKind.String),
        new JsonFieldSpec("sequence", JsonFieldKind.Integer),
        new JsonFieldSpec("threadId", JsonFieldKind.String),
        new JsonFieldSpec("turnId", JsonFieldKind.String),
        new JsonFieldSpec("itemId", JsonFieldKind.String),
        new JsonFieldSpec("messageId", JsonFieldKind.String),
        new JsonFieldSpec("content", JsonFieldKind.String),
        new JsonFieldSpec("delta", JsonFieldKind.String),
        new JsonFieldSpec("toolName", JsonFieldKind.String),
        new JsonFieldSpec("category", JsonFieldKind.String),
        new JsonFieldSpec("summary", JsonFieldKind.String),
        new JsonFieldSpec("details", JsonFieldKind.String),
        new JsonFieldSpec("error", JsonFieldKind.String),
        new JsonFieldSpec("errorCode", JsonFieldKind.String),
        new JsonFieldSpec("retryable", JsonFieldKind.Boolean),
        new JsonFieldSpec("connectionState", JsonFieldKind.String),
        new JsonFieldSpec("contextSha256", JsonFieldKind.String),
        new JsonFieldSpec("approvalId", JsonFieldKind.String),
        new JsonFieldSpec("approvalKind", JsonFieldKind.String),
        new JsonFieldSpec("risk", JsonFieldKind.String),
        new JsonFieldSpec("allowedDecisions", JsonFieldKind.StringArray),
        new JsonFieldSpec("decision", JsonFieldKind.String),
        new JsonFieldSpec("occurredAtUtc", JsonFieldKind.String),
        new JsonFieldSpec("expiresAtUtc", JsonFieldKind.String),
    };

    public static byte[] SerializeEnvelope(IpcEnvelope envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        return Serialize(new EnvelopeWire
        {
            ProtocolVersion = envelope.ProtocolVersion,
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            SessionId = envelope.SessionId,
            Sequence = envelope.Sequence,
            MessageType = envelope.MessageType,
            PayloadJson = envelope.PayloadJson,
            Nonce = envelope.Nonce,
            Mac = envelope.Mac,
        });
    }

    public static IpcEnvelope DeserializeEnvelope(byte[] utf8)
    {
        StrictJsonShapeValidator.ValidateObject(utf8, EnvelopeShape);
        var wire = Deserialize<EnvelopeWire>(utf8);
        var envelope = new IpcEnvelope
        {
            ProtocolVersion = wire.ProtocolVersion,
            MessageId = RequireString(wire.MessageId, "messageId"),
            CorrelationId = RequireString(wire.CorrelationId, "correlationId"),
            SessionId = RequireString(wire.SessionId, "sessionId"),
            Sequence = wire.Sequence,
            MessageType = RequireString(wire.MessageType, "messageType"),
            PayloadJson = RequireString(wire.PayloadJson, "payloadJson"),
            Nonce = RequireUpperHex(wire.Nonce, 32, "nonce"),
            Mac = RequireUpperHex(wire.Mac, 64, "mac"),
        };
        return envelope;
    }

    public static string SerializeRequestPayload(string method, string bodyJson)
    {
        return StrictUtf8.GetString(Serialize(new RequestPayloadWire
        {
            Method = method,
            BodyJson = bodyJson,
        }));
    }

    public static RequestPayloadValue DeserializeRequestPayload(string json)
    {
        var utf8 = GetStrictUtf8(json, "bridge request payload");
        StrictJsonShapeValidator.ValidateObject(utf8, RequestPayloadShape);
        var wire = Deserialize<RequestPayloadWire>(utf8);
        return new RequestPayloadValue(
            RequireString(wire.Method, "method"),
            RequireString(wire.BodyJson, "bodyJson"));
    }

    public static ResponsePayloadValue DeserializeResponsePayload(string json)
    {
        var utf8 = GetStrictUtf8(json, "bridge response payload");
        StrictJsonShapeValidator.ValidateObject(utf8, ResponsePayloadShape);
        var wire = Deserialize<ResponsePayloadWire>(utf8);
        return new ResponsePayloadValue(
            RequireString(wire.BodyJson, "bodyJson"),
            RequireString(wire.ErrorCode, "errorCode"),
            RequireString(wire.ErrorMessage, "errorMessage"));
    }

    public static string SerializeResponsePayload(
        string bodyJson,
        string errorCode,
        string errorMessage)
    {
        var utf8 = Serialize(new ResponsePayloadWire
        {
            BodyJson = bodyJson ?? throw new ArgumentNullException(nameof(bodyJson)),
            ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode)),
            ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage)),
        });
        StrictJsonShapeValidator.ValidateObject(utf8, ResponsePayloadShape);
        return StrictUtf8.GetString(utf8);
    }

    public static string DeserializeCancelReason(string json)
    {
        var utf8 = GetStrictUtf8(json, "bridge cancel payload");
        StrictJsonShapeValidator.ValidateObject(utf8, CancelPayloadShape);
        var wire = Deserialize<CancelPayloadWire>(utf8);
        return RequireString(wire.Reason, "reason");
    }

    public static string SerializeCapabilitiesRequest(AgentCapabilitiesRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var failures = AgentBridgeContractValidator.Validate(request);
        if (failures.Length != 0)
        {
            throw new ArgumentException("Agent capabilities请求不符合冻结v1契约。", nameof(request));
        }

        var utf8 = Serialize(new CapabilitiesRequestWire
        {
            ContractVersion = request.ContractVersion,
            ClientName = request.ClientName,
            ClientVersion = request.ClientVersion,
            HostTarget = request.HostTarget,
        });
        StrictJsonShapeValidator.ValidateObject(utf8, CapabilitiesRequestShape);
        return StrictUtf8.GetString(utf8);
    }

    public static AgentCapabilitiesResponse DeserializeCapabilitiesResponse(string json)
    {
        var utf8 = GetStrictUtf8(json, "capabilities response");
        CapabilitiesResponseWire wire;
        var hasV2Schemas = false;
        try
        {
            StrictJsonShapeValidator.ValidateObject(utf8, CapabilitiesResponseV2ExtendedShape);
            wire = Deserialize<CapabilitiesResponseWire>(utf8);
            hasV2Schemas = wire.SupportedCadContextSchemas is { Length: > 0 };
        }
        catch (AgentBridgeClientException)
        {
            StrictJsonShapeValidator.ValidateObject(utf8, CapabilitiesResponseShape);
            wire = Deserialize<CapabilitiesResponseWire>(utf8);
        }

        var response = new AgentCapabilitiesResponse
        {
            ContractVersion = wire.ContractVersion,
            MinimumCompatibleVersion = wire.MinimumCompatibleVersion,
            AgentInstanceId = RequireString(wire.AgentInstanceId, "agentInstanceId"),
            CadContextSchema = RequireString(wire.CadContextSchema, "cadContextSchema"),
            CadContextSchemaVersion = wire.CadContextSchemaVersion,
            Methods = RequireArray(wire.Methods, "methods"),
            EventKinds = RequireArray(wire.EventKinds, "eventKinds"),
            ApprovalDecisions = RequireArray(wire.ApprovalDecisions, "approvalDecisions"),
            CadWriteAvailable = wire.CadWriteAvailable,
        };

        if (hasV2Schemas)
        {
            response.SupportedCadContextSchemas = wire.SupportedCadContextSchemas!
                .Where(s => s is not null)
                .Select(s => new Codex.AutoCAD.Contracts.CadContextSchemaVersionEntry
                {
                    Schema = s!.Schema ?? string.Empty,
                    SchemaVersion = s.SchemaVersion,
                })
                .ToArray();
        }

        if (AgentBridgeContractValidator.Validate(response).Length != 0)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.ContractMismatch,
                "Agent capabilities响应不符合冻结契约。");
        }

        return response;
    }

    public static string SerializeThreadStartRequest(AgentThreadStartRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new ArgumentException("Agent thread请求不符合冻结v1契约。", nameof(request));
        }

        var utf8 = Serialize(new ThreadStartRequestWire
        {
            ContractVersion = request.ContractVersion,
            ConversationId = request.ConversationId,
        });
        StrictJsonShapeValidator.ValidateObject(utf8, ThreadStartRequestShape);
        return StrictUtf8.GetString(utf8);
    }

    public static AgentThreadStartResponse DeserializeThreadStartResponse(string json)
    {
        var utf8 = GetStrictUtf8(json, "thread start response");
        StrictJsonShapeValidator.ValidateObject(utf8, ThreadStartResponseShape);
        var wire = Deserialize<ThreadStartResponseWire>(utf8);
        if (wire.ContractVersion != AgentBridgeContractConstants.CurrentVersion
            || !IsSafeIdentifier(wire.ThreadId))
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.ContractMismatch,
                "Agent thread响应不符合冻结v1契约。");
        }

        return new AgentThreadStartResponse
        {
            ContractVersion = wire.ContractVersion,
            ThreadId = wire.ThreadId!,
        };
    }

    public static string SerializeTurnStartRequest(AgentTurnStartRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new ArgumentException("Agent turn请求不符合冻结v1契约。", nameof(request));
        }

        var builder = new StringBuilder();
        builder.Append('{');
        AppendJsonPropertyName(builder, "contractVersion");
        builder.Append(request.ContractVersion.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendJsonProperty(builder, "threadId", request.ThreadId);
        builder.Append(',');
        AppendJsonProperty(builder, "clientTurnId", request.ClientTurnId);
        builder.Append(',');
        AppendJsonProperty(builder, "prompt", request.Prompt);
        builder.Append(',');
        AppendJsonPropertyName(builder, "context");
        builder.Append(request.Context is null
            ? "null"
            : CadContextJsonV1Codec.SerializeCanonical(request.Context));
        builder.Append(',');
        AppendJsonProperty(builder, "contextSha256", request.ContextSha256);
        builder.Append('}');

        var json = builder.ToString();
        if (GetStrictUtf8(json, "turn start request").Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new AgentBridgeClientException("request_invalid", "Agent turn请求超过安全上限。");
        }

        return json;
    }

    public static AgentTurnStartResponse DeserializeTurnStartResponse(
        string json,
        AgentTurnStartRequest request)
    {
        var utf8 = GetStrictUtf8(json, "turn start response");
        StrictJsonShapeValidator.ValidateObject(utf8, TurnStartResponseShape);
        var wire = Deserialize<TurnStartResponseWire>(utf8);
        var response = new AgentTurnStartResponse
        {
            ContractVersion = wire.ContractVersion,
            ThreadId = RequireString(wire.ThreadId, "threadId"),
            TurnId = RequireString(wire.TurnId, "turnId"),
            AcceptedContextSha256 = RequireString(
                wire.AcceptedContextSha256,
                "acceptedContextSha256"),
        };

        if (AgentBridgeContractValidator.ValidateTurnAcceptance(request, response).Length != 0)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.ResultIdentityMismatch,
                "Agent turn响应与thread/context身份不一致。");
        }

        return response;
    }

    public static string SerializeTurnStartV2Request(AgentTurnStartV2Request request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new ArgumentException("Agent turn v2请求不符合冻结契约。", nameof(request));
        }

        var builder = new StringBuilder();
        builder.Append('{');
        AppendJsonPropertyName(builder, "contractVersion");
        builder.Append(request.ContractVersion.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        AppendJsonProperty(builder, "threadId", request.ThreadId);
        builder.Append(',');
        AppendJsonProperty(builder, "clientTurnId", request.ClientTurnId);
        builder.Append(',');
        AppendJsonProperty(builder, "prompt", request.Prompt);
        builder.Append(',');
        AppendJsonPropertyName(builder, "contextV2");
        builder.Append(request.ContextV2 is null
            ? "null"
            : CadContextJsonV2Codec.SerializeCanonical(request.ContextV2));
        builder.Append(',');
        AppendJsonProperty(builder, "contextV2Sha256", request.ContextV2Sha256);
        builder.Append('}');

        var json = builder.ToString();
        if (GetStrictUtf8(json, "turn start v2 request").Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new AgentBridgeClientException("request_invalid", "Agent turn v2请求超过安全上限。");
        }

        return json;
    }

    public static AgentTurnStartV2Response DeserializeTurnStartV2Response(
        string json,
        AgentTurnStartV2Request request)
    {
        var utf8 = GetStrictUtf8(json, "turn start v2 response");
        StrictJsonShapeValidator.ValidateObject(utf8, TurnStartV2ResponseShape);
        var wire = Deserialize<TurnStartV2ResponseWire>(utf8);
        var response = new AgentTurnStartV2Response
        {
            ContractVersion = wire.ContractVersion,
            ThreadId = RequireString(wire.ThreadId, "threadId"),
            TurnId = RequireString(wire.TurnId, "turnId"),
            AcceptedContextV2Sha256 = RequireString(
                wire.AcceptedContextV2Sha256,
                "acceptedContextV2Sha256"),
        };

        if (AgentBridgeContractValidator.ValidateTurnV2Acceptance(request, response).Length != 0)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.ResultIdentityMismatch,
                "Agent turn v2响应与thread/context身份不一致。");
        }

        return response;
    }

    public static string SerializeTurnInterruptRequest(AgentTurnInterruptRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new ArgumentException("Agent interrupt请求不符合冻结v1契约。", nameof(request));
        }

        var utf8 = Serialize(new TurnInterruptRequestWire
        {
            ContractVersion = request.ContractVersion,
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
        });
        StrictJsonShapeValidator.ValidateObject(utf8, TurnInterruptRequestShape);
        return StrictUtf8.GetString(utf8);
    }

    public static string SerializeApprovalResolveRequest(AgentApprovalResolveRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new ArgumentException("Agent approval请求不符合冻结v1契约。", nameof(request));
        }

        var utf8 = Serialize(new ApprovalResolveRequestWire
        {
            ContractVersion = request.ContractVersion,
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            ApprovalId = request.ApprovalId,
            Decision = request.Decision,
        });
        StrictJsonShapeValidator.ValidateObject(utf8, ApprovalResolveRequestShape);
        return StrictUtf8.GetString(utf8);
    }

    public static AgentDrawingQueryRequest DeserializeDrawingQueryRequest(string json)
    {
        var utf8 = GetStrictUtf8(json, "drawing query request");
        StrictJsonShapeValidator.ValidateObject(utf8, DrawingQueryRequestShape);
        var wire = Deserialize<DrawingQueryRequestWire>(utf8);
        var request = new AgentDrawingQueryRequest
        {
            ContractVersion = wire.ContractVersion,
            RequestId = RequireString(wire.RequestId, "requestId"),
            ThreadId = RequireString(wire.ThreadId, "threadId"),
            TurnId = RequireString(wire.TurnId, "turnId"),
            ToolCallId = RequireString(wire.ToolCallId, "toolCallId"),
            QueryId = RequireString(wire.QueryId, "queryId"),
            Filter = FromWire(wire.Filter),
            PageSize = wire.PageSize,
            Cursor = RequireString(wire.Cursor, "cursor"),
        };
        if (AgentBridgeContractValidator.Validate(request).Length != 0)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.RequestInvalid,
                "反向整图查询请求不符合冻结契约。");
        }

        return request;
    }

    public static string SerializeDrawingQueryResponse(
        AgentDrawingQueryRequest request,
        AgentDrawingQueryResponse response)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (AgentBridgeContractValidator.ValidateDrawingQueryResponse(request, response).Length != 0)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.ResultIdentityMismatch,
                "反向整图查询响应未绑定请求身份或CadQuery契约无效。");
        }

        var utf8 = Serialize(ToWire(response));
        StrictJsonShapeValidator.ValidateObject(utf8, DrawingQueryResponseShape);
        return StrictUtf8.GetString(utf8);
    }

    private static CadQueryFilter FromWire(DrawingQueryFilterWire? wire)
    {
        if (wire is null)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.RequestInvalid,
                "反向整图查询缺少filter。");
        }

        return new CadQueryFilter
        {
            EntityTypes = RequireArray(wire.EntityTypes, "filter.entityTypes"),
            Layers = RequireArray(wire.Layers, "filter.layers"),
            Spaces = RequireArray(wire.Spaces, "filter.spaces"),
            BlockNames = RequireArray(wire.BlockNames, "filter.blockNames"),
            ObjectIds = RequireArray(wire.ObjectIds, "filter.objectIds"),
            TextContains = RequireString(wire.TextContains, "filter.textContains"),
            Bounds = wire.Bounds is null
                ? null
                : new CadQueryBounds
                {
                    Minimum = FromWire(wire.Bounds.Minimum, "filter.bounds.minimum"),
                    Maximum = FromWire(wire.Bounds.Maximum, "filter.bounds.maximum"),
                },
            IncludeUnsupported = wire.IncludeUnsupported,
        };
    }

    private static CadPoint3 FromWire(DrawingQueryPointWire? wire, string field)
    {
        if (wire is null)
        {
            throw new AgentBridgeClientException(
                AgentBridgeErrorCodes.RequestInvalid,
                "反向整图查询缺少JSON字段：" + field + "。");
        }

        return new CadPoint3(wire.X, wire.Y, wire.Z);
    }

    private static DrawingQueryResponseWire ToWire(AgentDrawingQueryResponse response)
    {
        var query = response.Query;
        return new DrawingQueryResponseWire
        {
            ContractVersion = response.ContractVersion,
            RequestId = response.RequestId,
            ThreadId = response.ThreadId,
            TurnId = response.TurnId,
            ToolCallId = response.ToolCallId,
            QueryId = response.QueryId,
            Query = new CadQueryResponseWire
            {
                Schema = query.Schema,
                SchemaVersion = query.SchemaVersion,
                IndexId = query.IndexId,
                DocumentId = query.DocumentId,
                DocumentRevision = query.DocumentRevision,
                QueryId = query.QueryId,
                Status = query.Status,
                Complete = query.Complete,
                TotalMatches = query.TotalMatches,
                ReturnedCount = query.ReturnedCount,
                Entities = (query.Entities ?? new CadQueryEntity[0])
                    .Select(ToWire)
                    .ToArray(),
                NextCursor = query.NextCursor,
                Message = query.Message,
            },
        };
    }

    private static CadQueryEntityWire ToWire(CadQueryEntity entity)
    {
        return new CadQueryEntityWire
        {
            ObjectId = entity.ObjectId,
            EntityType = entity.EntityType,
            ActualType = entity.ActualType,
            Layer = entity.Layer,
            Space = entity.Space,
            BlockName = entity.BlockName,
            BlockDetails = ToWire(entity.BlockDetails),
            TextExcerpt = entity.TextExcerpt,
            Bounds = entity.Bounds is null
                ? null
                : new CadQueryExtentsWire
                {
                    Minimum = ToWire(entity.Bounds.Minimum),
                    Maximum = ToWire(entity.Bounds.Maximum),
                },
            Unsupported = entity.Unsupported,
            ReadStatus = entity.ReadStatus,
        };
    }

    private static CadQueryBlockDetailsWire? ToWire(CadQueryBlockDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        return new CadQueryBlockDetailsWire
        {
            DetailStatus = details.DetailStatus,
            IsDynamic = details.IsDynamic,
            IsExternalReference = details.IsExternalReference,
            IsOverlayReference = details.IsOverlayReference,
            IsAnonymousDefinition = details.IsAnonymousDefinition,
            IsLayoutDefinition = details.IsLayoutDefinition,
            HasAttributeDefinitions = details.HasAttributeDefinitions,
            LayoutName = details.LayoutName,
            LayoutKind = details.LayoutKind,
            AttributeCount = details.AttributeCount,
            Attributes = (details.Attributes ?? new CadQueryBlockAttribute[0])
                .Select(attribute => new CadQueryBlockAttributeWire
                {
                    Tag = attribute == null ? string.Empty : attribute.Tag,
                    Value = attribute == null ? string.Empty : attribute.Value,
                    IsInvisible = attribute != null && attribute.IsInvisible,
                    IsMText = attribute != null && attribute.IsMText,
                })
                .ToArray(),
            DynamicPropertyCount = details.DynamicPropertyCount,
            DynamicProperties = (details.DynamicProperties ?? new CadQueryDynamicBlockProperty[0])
                .Select(property => new CadQueryDynamicBlockPropertyWire
                {
                    Name = property == null ? string.Empty : property.Name,
                    ValueKind = property == null ? string.Empty : property.ValueKind,
                    Value = property == null ? string.Empty : property.Value,
                    IsReadOnly = property != null && property.IsReadOnly,
                    IsVisible = property != null && property.IsVisible,
                })
                .ToArray(),
            NestedBlockReferenceCount = details.NestedBlockReferenceCount,
            MaximumNestedBlockDepth = details.MaximumNestedBlockDepth,
        };
    }

    private static DrawingQueryPointWire ToWire(CadPoint3 point)
        => new DrawingQueryPointWire { X = point.X, Y = point.Y, Z = point.Z };

    public static void ValidateNullResponse(string json)
    {
        var utf8 = GetStrictUtf8(json, "empty bridge response");
        if (utf8.Length != 4
            || utf8[0] != (byte)'n'
            || utf8[1] != (byte)'u'
            || utf8[2] != (byte)'l'
            || utf8[3] != (byte)'l')
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                "Agent Bridge空响应必须精确为JSON null。");
        }
    }

    public static AgentBridgeEvent DeserializeAgentEvent(string json)
    {
        var utf8 = GetStrictUtf8(json, "agent event");
        StrictJsonShapeValidator.ValidateObject(utf8, AgentEventShape);
        var wire = Deserialize<AgentEventWire>(utf8);
        var bridgeEvent = new AgentBridgeEvent
        {
            ContractVersion = wire.ContractVersion,
            Kind = RequireString(wire.Kind, "kind"),
            EventId = RequireString(wire.EventId, "eventId"),
            Sequence = wire.Sequence,
            ThreadId = RequireString(wire.ThreadId, "threadId"),
            TurnId = RequireString(wire.TurnId, "turnId"),
            ItemId = RequireString(wire.ItemId, "itemId"),
            MessageId = RequireString(wire.MessageId, "messageId"),
            Content = RequireString(wire.Content, "content"),
            Delta = RequireString(wire.Delta, "delta"),
            ToolName = RequireString(wire.ToolName, "toolName"),
            Category = RequireString(wire.Category, "category"),
            Summary = RequireString(wire.Summary, "summary"),
            Details = RequireString(wire.Details, "details"),
            Error = RequireString(wire.Error, "error"),
            ErrorCode = RequireString(wire.ErrorCode, "errorCode"),
            Retryable = wire.Retryable,
            ConnectionState = RequireString(wire.ConnectionState, "connectionState"),
            ContextSha256 = RequireString(wire.ContextSha256, "contextSha256"),
            ApprovalId = RequireString(wire.ApprovalId, "approvalId"),
            ApprovalKind = RequireString(wire.ApprovalKind, "approvalKind"),
            Risk = RequireString(wire.Risk, "risk"),
            AllowedDecisions = RequireArray(wire.AllowedDecisions, "allowedDecisions"),
            Decision = RequireString(wire.Decision, "decision"),
            OccurredAtUtc = RequireString(wire.OccurredAtUtc, "occurredAtUtc"),
            ExpiresAtUtc = RequireString(wire.ExpiresAtUtc, "expiresAtUtc"),
        };

        if (AgentBridgeContractValidator.Validate(bridgeEvent).Length != 0)
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                "Agent event不符合冻结v1契约。");
        }

        return bridgeEvent;
    }

    private static byte[] GetStrictUtf8(string value, string label)
    {
        if (value is null)
        {
            throw new AgentBridgeClientException("request_invalid", label + " cannot be null.");
        }

        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                label + " contains invalid Unicode.",
                exception);
        }
    }

    private static byte[] Serialize<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(T),
            new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 4096 });
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, value);
            return stream.ToArray();
        }
    }

    private static T Deserialize<T>(byte[] utf8)
    {
        try
        {
            var serializer = new DataContractJsonSerializer(
                typeof(T),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 4096 });
            using (var stream = new MemoryStream(utf8, false))
            {
                var value = serializer.ReadObject(stream);
                if (!(value is T typed))
                {
                    throw new SerializationException("JSON value did not produce the expected wire type.");
                }

                return typed;
            }
        }
        catch (Exception exception) when (
            exception is SerializationException
            || exception is XmlException
            || exception is InvalidDataContractException)
        {
            throw new AgentBridgeClientException(
                "request_invalid",
                "Agent Bridge JSON不符合冻结wire契约。",
                exception);
        }
    }

    private static string RequireString(string? value, string field)
    {
        if (value is null)
        {
            throw new AgentBridgeClientException("request_invalid", "缺少JSON字段：" + field + "。");
        }

        return value;
    }

    private static string[] RequireArray(string[]? value, string field)
    {
        if (value is null || value.Any(item => item is null))
        {
            throw new AgentBridgeClientException("request_invalid", "JSON数组无效：" + field + "。");
        }

        return value;
    }

    private static string RequireUpperHex(string? value, int length, string field)
    {
        if (value is null || value.Length != length)
        {
            throw new AgentBridgeClientException("authentication_failed", field + "长度无效。");
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9')
                || (character >= 'A' && character <= 'F')))
            {
                throw new AgentBridgeClientException("authentication_failed", field + "格式无效。");
            }
        }

        return value;
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, string value)
    {
        AppendJsonPropertyName(builder, name);
        AppendJsonString(builder, value);
    }

    private static void AppendJsonPropertyName(StringBuilder builder, string name)
    {
        AppendJsonString(builder, name);
        builder.Append(':');
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static bool IsSafeIdentifier(string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '-'
                || character == '_'
                || character == '.'
                || character == ':'))
            {
                return false;
            }
        }

        return true;
    }

    internal sealed class ResponsePayloadValue
    {
        public ResponsePayloadValue(string bodyJson, string errorCode, string errorMessage)
        {
            BodyJson = bodyJson;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public string BodyJson { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }
    }

    internal sealed class RequestPayloadValue
    {
        public RequestPayloadValue(string method, string bodyJson)
        {
            Method = method;
            BodyJson = bodyJson;
        }

        public string Method { get; }

        public string BodyJson { get; }
    }

    [DataContract]
    private sealed class EnvelopeWire
    {
        [DataMember(Name = "protocolVersion", Order = 1, IsRequired = true)]
        public int ProtocolVersion { get; set; }

        [DataMember(Name = "messageId", Order = 2, IsRequired = true)]
        public string MessageId { get; set; } = string.Empty;

        [DataMember(Name = "correlationId", Order = 3, IsRequired = true)]
        public string CorrelationId { get; set; } = string.Empty;

        [DataMember(Name = "sessionId", Order = 4, IsRequired = true)]
        public string SessionId { get; set; } = string.Empty;

        [DataMember(Name = "sequence", Order = 5, IsRequired = true)]
        public long Sequence { get; set; }

        [DataMember(Name = "messageType", Order = 6, IsRequired = true)]
        public string MessageType { get; set; } = string.Empty;

        [DataMember(Name = "payloadJson", Order = 7, IsRequired = true)]
        public string PayloadJson { get; set; } = string.Empty;

        [DataMember(Name = "nonce", Order = 8, IsRequired = true)]
        public string Nonce { get; set; } = string.Empty;

        [DataMember(Name = "mac", Order = 9, IsRequired = true)]
        public string Mac { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class RequestPayloadWire
    {
        [DataMember(Name = "method", Order = 1, IsRequired = true)]
        public string Method { get; set; } = string.Empty;

        [DataMember(Name = "bodyJson", Order = 2, IsRequired = true)]
        public string BodyJson { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class ResponsePayloadWire
    {
        [DataMember(Name = "bodyJson", Order = 1, IsRequired = true)]
        public string BodyJson { get; set; } = string.Empty;

        [DataMember(Name = "errorCode", Order = 2, IsRequired = true)]
        public string ErrorCode { get; set; } = string.Empty;

        [DataMember(Name = "errorMessage", Order = 3, IsRequired = true)]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class CancelPayloadWire
    {
        [DataMember(Name = "reason", Order = 1, IsRequired = true)]
        public string Reason { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class CapabilitiesRequestWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "clientName", Order = 2, IsRequired = true)]
        public string ClientName { get; set; } = string.Empty;

        [DataMember(Name = "clientVersion", Order = 3, IsRequired = true)]
        public string ClientVersion { get; set; } = string.Empty;

        [DataMember(Name = "hostTarget", Order = 4, IsRequired = true)]
        public string HostTarget { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class CapabilitiesResponseWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "minimumCompatibleVersion", Order = 2, IsRequired = true)]
        public int MinimumCompatibleVersion { get; set; }

        [DataMember(Name = "agentInstanceId", Order = 3, IsRequired = true)]
        public string AgentInstanceId { get; set; } = string.Empty;

        [DataMember(Name = "cadContextSchema", Order = 4, IsRequired = true)]
        public string CadContextSchema { get; set; } = string.Empty;

        [DataMember(Name = "cadContextSchemaVersion", Order = 5, IsRequired = true)]
        public int CadContextSchemaVersion { get; set; }

        [DataMember(Name = "methods", Order = 6, IsRequired = true)]
        public string[] Methods { get; set; } = new string[0];

        [DataMember(Name = "eventKinds", Order = 7, IsRequired = true)]
        public string[] EventKinds { get; set; } = new string[0];

        [DataMember(Name = "approvalDecisions", Order = 8, IsRequired = true)]
        public string[] ApprovalDecisions { get; set; } = new string[0];

        [DataMember(Name = "supportedCadContextSchemas", Order = 9, IsRequired = false)]
        public SchemaEntryWire[]? SupportedCadContextSchemas { get; set; }

        [DataMember(Name = "cadWriteAvailable", Order = 10, IsRequired = true)]
        public bool CadWriteAvailable { get; set; }
    }

    [DataContract]
    private sealed class SchemaEntryWire
    {
        [DataMember(Name = "schema", Order = 1, IsRequired = true)]
        public string Schema { get; set; } = string.Empty;

        [DataMember(Name = "schemaVersion", Order = 2, IsRequired = true)]
        public int SchemaVersion { get; set; }
    }

    [DataContract]
    private sealed class ThreadStartRequestWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "conversationId", Order = 2, IsRequired = true)]
        public string ConversationId { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class ThreadStartResponseWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "threadId", Order = 2, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class TurnStartResponseWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "threadId", Order = 2, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 3, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "acceptedContextSha256", Order = 4, IsRequired = true)]
        public string AcceptedContextSha256 { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class TurnStartV2ResponseWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "threadId", Order = 2, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 3, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "acceptedContextV2Sha256", Order = 4, IsRequired = true)]
        public string AcceptedContextV2Sha256 { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class TurnInterruptRequestWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "threadId", Order = 2, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 3, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class ApprovalResolveRequestWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "threadId", Order = 2, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 3, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "approvalId", Order = 4, IsRequired = true)]
        public string ApprovalId { get; set; } = string.Empty;

        [DataMember(Name = "decision", Order = 5, IsRequired = true)]
        public string Decision { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class DrawingQueryRequestWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "requestId", Order = 2, IsRequired = true)]
        public string RequestId { get; set; } = string.Empty;

        [DataMember(Name = "threadId", Order = 3, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 4, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "toolCallId", Order = 5, IsRequired = true)]
        public string ToolCallId { get; set; } = string.Empty;

        [DataMember(Name = "queryId", Order = 6, IsRequired = true)]
        public string QueryId { get; set; } = string.Empty;

        [DataMember(Name = "filter", Order = 7, IsRequired = true)]
        public DrawingQueryFilterWire? Filter { get; set; }

        [DataMember(Name = "pageSize", Order = 8, IsRequired = true)]
        public int PageSize { get; set; }

        [DataMember(Name = "cursor", Order = 9, IsRequired = true)]
        public string Cursor { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class DrawingQueryFilterWire
    {
        [DataMember(Name = "entityTypes", Order = 1, IsRequired = true)]
        public string[] EntityTypes { get; set; } = new string[0];

        [DataMember(Name = "layers", Order = 2, IsRequired = true)]
        public string[] Layers { get; set; } = new string[0];

        [DataMember(Name = "spaces", Order = 3, IsRequired = true)]
        public string[] Spaces { get; set; } = new string[0];

        [DataMember(Name = "blockNames", Order = 4, IsRequired = true)]
        public string[] BlockNames { get; set; } = new string[0];

        [DataMember(Name = "objectIds", Order = 5, IsRequired = true)]
        public string[] ObjectIds { get; set; } = new string[0];

        [DataMember(Name = "textContains", Order = 6, IsRequired = true)]
        public string TextContains { get; set; } = string.Empty;

        [DataMember(Name = "bounds", Order = 7, IsRequired = true)]
        public DrawingQueryBoundsWire? Bounds { get; set; }

        [DataMember(Name = "includeUnsupported", Order = 8, IsRequired = true)]
        public bool IncludeUnsupported { get; set; }
    }

    [DataContract]
    private sealed class DrawingQueryBoundsWire
    {
        [DataMember(Name = "minimum", Order = 1, IsRequired = true)]
        public DrawingQueryPointWire? Minimum { get; set; }

        [DataMember(Name = "maximum", Order = 2, IsRequired = true)]
        public DrawingQueryPointWire? Maximum { get; set; }
    }

    [DataContract]
    private sealed class DrawingQueryPointWire
    {
        [DataMember(Name = "x", Order = 1, IsRequired = true)]
        public double X { get; set; }

        [DataMember(Name = "y", Order = 2, IsRequired = true)]
        public double Y { get; set; }

        [DataMember(Name = "z", Order = 3, IsRequired = true)]
        public double Z { get; set; }
    }

    [DataContract]
    private sealed class DrawingQueryResponseWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "requestId", Order = 2, IsRequired = true)]
        public string RequestId { get; set; } = string.Empty;

        [DataMember(Name = "threadId", Order = 3, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 4, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "toolCallId", Order = 5, IsRequired = true)]
        public string ToolCallId { get; set; } = string.Empty;

        [DataMember(Name = "queryId", Order = 6, IsRequired = true)]
        public string QueryId { get; set; } = string.Empty;

        [DataMember(Name = "query", Order = 7, IsRequired = true)]
        public CadQueryResponseWire Query { get; set; } = new CadQueryResponseWire();
    }

    [DataContract]
    private sealed class CadQueryResponseWire
    {
        [DataMember(Name = "schema", Order = 1, IsRequired = true)]
        public string Schema { get; set; } = string.Empty;

        [DataMember(Name = "schemaVersion", Order = 2, IsRequired = true)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "indexId", Order = 3, IsRequired = true)]
        public string IndexId { get; set; } = string.Empty;

        [DataMember(Name = "documentId", Order = 4, IsRequired = true)]
        public string DocumentId { get; set; } = string.Empty;

        [DataMember(Name = "documentRevision", Order = 5, IsRequired = true)]
        public long DocumentRevision { get; set; }

        [DataMember(Name = "queryId", Order = 6, IsRequired = true)]
        public string QueryId { get; set; } = string.Empty;

        [DataMember(Name = "status", Order = 7, IsRequired = true)]
        public string Status { get; set; } = string.Empty;

        [DataMember(Name = "complete", Order = 8, IsRequired = true)]
        public bool Complete { get; set; }

        [DataMember(Name = "totalMatches", Order = 9, IsRequired = true)]
        public int TotalMatches { get; set; }

        [DataMember(Name = "returnedCount", Order = 10, IsRequired = true)]
        public int ReturnedCount { get; set; }

        [DataMember(Name = "entities", Order = 11, IsRequired = true)]
        public CadQueryEntityWire[] Entities { get; set; } = new CadQueryEntityWire[0];

        [DataMember(Name = "nextCursor", Order = 12, IsRequired = true)]
        public string NextCursor { get; set; } = string.Empty;

        [DataMember(Name = "message", Order = 13, IsRequired = true)]
        public string Message { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class CadQueryEntityWire
    {
        [DataMember(Name = "objectId", Order = 1, IsRequired = true)]
        public string ObjectId { get; set; } = string.Empty;

        [DataMember(Name = "entityType", Order = 2, IsRequired = true)]
        public string EntityType { get; set; } = string.Empty;

        [DataMember(Name = "actualType", Order = 3, IsRequired = true)]
        public string ActualType { get; set; } = string.Empty;

        [DataMember(Name = "layer", Order = 4, IsRequired = true)]
        public string Layer { get; set; } = string.Empty;

        [DataMember(Name = "space", Order = 5, IsRequired = true)]
        public string Space { get; set; } = string.Empty;

        [DataMember(Name = "blockName", Order = 6, IsRequired = true)]
        public string BlockName { get; set; } = string.Empty;

        [DataMember(Name = "blockDetails", Order = 7, IsRequired = false)]
        public CadQueryBlockDetailsWire? BlockDetails { get; set; }

        [DataMember(Name = "textExcerpt", Order = 8, IsRequired = true)]
        public string TextExcerpt { get; set; } = string.Empty;

        [DataMember(Name = "bounds", Order = 9, IsRequired = true)]
        public CadQueryExtentsWire? Bounds { get; set; }

        [DataMember(Name = "unsupported", Order = 10, IsRequired = true)]
        public bool Unsupported { get; set; }

        [DataMember(Name = "readStatus", Order = 11, IsRequired = true)]
        public string ReadStatus { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class CadQueryBlockDetailsWire
    {
        [DataMember(Name = "detailStatus", Order = 1, IsRequired = true)]
        public string DetailStatus { get; set; } = string.Empty;

        [DataMember(Name = "isDynamic", Order = 2, IsRequired = true)]
        public bool IsDynamic { get; set; }

        [DataMember(Name = "isExternalReference", Order = 3, IsRequired = true)]
        public bool IsExternalReference { get; set; }

        [DataMember(Name = "isOverlayReference", Order = 4, IsRequired = true)]
        public bool IsOverlayReference { get; set; }

        [DataMember(Name = "isAnonymousDefinition", Order = 5, IsRequired = true)]
        public bool IsAnonymousDefinition { get; set; }

        [DataMember(Name = "isLayoutDefinition", Order = 6, IsRequired = true)]
        public bool IsLayoutDefinition { get; set; }

        [DataMember(Name = "hasAttributeDefinitions", Order = 7, IsRequired = true)]
        public bool HasAttributeDefinitions { get; set; }

        [DataMember(Name = "layoutName", Order = 8, IsRequired = true)]
        public string LayoutName { get; set; } = string.Empty;

        [DataMember(Name = "layoutKind", Order = 9, IsRequired = true)]
        public string LayoutKind { get; set; } = string.Empty;

        [DataMember(Name = "attributeCount", Order = 10, IsRequired = true)]
        public int AttributeCount { get; set; }

        [DataMember(Name = "attributes", Order = 11, IsRequired = true)]
        public CadQueryBlockAttributeWire[] Attributes { get; set; } = new CadQueryBlockAttributeWire[0];

        [DataMember(Name = "dynamicPropertyCount", Order = 12, IsRequired = true)]
        public int DynamicPropertyCount { get; set; }

        [DataMember(Name = "dynamicProperties", Order = 13, IsRequired = true)]
        public CadQueryDynamicBlockPropertyWire[] DynamicProperties { get; set; } =
            new CadQueryDynamicBlockPropertyWire[0];

        [DataMember(Name = "nestedBlockReferenceCount", Order = 14, IsRequired = true)]
        public int NestedBlockReferenceCount { get; set; }

        [DataMember(Name = "maximumNestedBlockDepth", Order = 15, IsRequired = true)]
        public int MaximumNestedBlockDepth { get; set; }
    }

    [DataContract]
    private sealed class CadQueryBlockAttributeWire
    {
        [DataMember(Name = "tag", Order = 1, IsRequired = true)]
        public string Tag { get; set; } = string.Empty;

        [DataMember(Name = "value", Order = 2, IsRequired = true)]
        public string Value { get; set; } = string.Empty;

        [DataMember(Name = "isInvisible", Order = 3, IsRequired = true)]
        public bool IsInvisible { get; set; }

        [DataMember(Name = "isMText", Order = 4, IsRequired = true)]
        public bool IsMText { get; set; }
    }

    [DataContract]
    private sealed class CadQueryDynamicBlockPropertyWire
    {
        [DataMember(Name = "name", Order = 1, IsRequired = true)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "valueKind", Order = 2, IsRequired = true)]
        public string ValueKind { get; set; } = string.Empty;

        [DataMember(Name = "value", Order = 3, IsRequired = true)]
        public string Value { get; set; } = string.Empty;

        [DataMember(Name = "isReadOnly", Order = 4, IsRequired = true)]
        public bool IsReadOnly { get; set; }

        [DataMember(Name = "isVisible", Order = 5, IsRequired = true)]
        public bool IsVisible { get; set; }
    }

    [DataContract]
    private sealed class CadQueryExtentsWire
    {
        [DataMember(Name = "minimum", Order = 1, IsRequired = true)]
        public DrawingQueryPointWire Minimum { get; set; } = new DrawingQueryPointWire();

        [DataMember(Name = "maximum", Order = 2, IsRequired = true)]
        public DrawingQueryPointWire Maximum { get; set; } = new DrawingQueryPointWire();
    }

    [DataContract]
    private sealed class AgentEventWire
    {
        [DataMember(Name = "contractVersion", Order = 1, IsRequired = true)]
        public int ContractVersion { get; set; }

        [DataMember(Name = "kind", Order = 2, IsRequired = true)]
        public string Kind { get; set; } = string.Empty;

        [DataMember(Name = "eventId", Order = 3, IsRequired = true)]
        public string EventId { get; set; } = string.Empty;

        [DataMember(Name = "sequence", Order = 4, IsRequired = true)]
        public long Sequence { get; set; }

        [DataMember(Name = "threadId", Order = 5, IsRequired = true)]
        public string ThreadId { get; set; } = string.Empty;

        [DataMember(Name = "turnId", Order = 6, IsRequired = true)]
        public string TurnId { get; set; } = string.Empty;

        [DataMember(Name = "itemId", Order = 7, IsRequired = true)]
        public string ItemId { get; set; } = string.Empty;

        [DataMember(Name = "messageId", Order = 8, IsRequired = true)]
        public string MessageId { get; set; } = string.Empty;

        [DataMember(Name = "content", Order = 9, IsRequired = true)]
        public string Content { get; set; } = string.Empty;

        [DataMember(Name = "delta", Order = 10, IsRequired = true)]
        public string Delta { get; set; } = string.Empty;

        [DataMember(Name = "toolName", Order = 11, IsRequired = true)]
        public string ToolName { get; set; } = string.Empty;

        [DataMember(Name = "category", Order = 12, IsRequired = true)]
        public string Category { get; set; } = string.Empty;

        [DataMember(Name = "summary", Order = 13, IsRequired = true)]
        public string Summary { get; set; } = string.Empty;

        [DataMember(Name = "details", Order = 14, IsRequired = true)]
        public string Details { get; set; } = string.Empty;

        [DataMember(Name = "error", Order = 15, IsRequired = true)]
        public string Error { get; set; } = string.Empty;

        [DataMember(Name = "errorCode", Order = 16, IsRequired = true)]
        public string ErrorCode { get; set; } = string.Empty;

        [DataMember(Name = "retryable", Order = 17, IsRequired = true)]
        public bool Retryable { get; set; }

        [DataMember(Name = "connectionState", Order = 18, IsRequired = true)]
        public string ConnectionState { get; set; } = string.Empty;

        [DataMember(Name = "contextSha256", Order = 19, IsRequired = true)]
        public string ContextSha256 { get; set; } = string.Empty;

        [DataMember(Name = "approvalId", Order = 20, IsRequired = true)]
        public string ApprovalId { get; set; } = string.Empty;

        [DataMember(Name = "approvalKind", Order = 21, IsRequired = true)]
        public string ApprovalKind { get; set; } = string.Empty;

        [DataMember(Name = "risk", Order = 22, IsRequired = true)]
        public string Risk { get; set; } = string.Empty;

        [DataMember(Name = "allowedDecisions", Order = 23, IsRequired = true)]
        public string[] AllowedDecisions { get; set; } = new string[0];

        [DataMember(Name = "decision", Order = 24, IsRequired = true)]
        public string Decision { get; set; } = string.Empty;

        [DataMember(Name = "occurredAtUtc", Order = 25, IsRequired = true)]
        public string OccurredAtUtc { get; set; } = string.Empty;

        [DataMember(Name = "expiresAtUtc", Order = 26, IsRequired = true)]
        public string ExpiresAtUtc { get; set; } = string.Empty;
    }

    private enum JsonFieldKind
    {
        String,
        Integer,
        Boolean,
        Object,
        StringArray,
        SchemaArray,
    }

    private sealed class JsonFieldSpec
    {
        public JsonFieldSpec(string name, JsonFieldKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }

        public JsonFieldKind Kind { get; }
    }

    private static class StrictJsonShapeValidator
    {
        public static void ValidateObject(byte[] utf8, JsonFieldSpec[] fields)
        {
            if (utf8 is null)
            {
                throw new ArgumentNullException(nameof(utf8));
            }

            if (utf8.Length == 0 || utf8.Length > ProtocolConstants.MaximumMessageBytes)
            {
                throw new AgentBridgeClientException("request_invalid", "JSON大小无效。");
            }

            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            {
                throw new AgentBridgeClientException("request_invalid", "JSON不得包含UTF-8 BOM。");
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(utf8);
            }
            catch (DecoderFallbackException exception)
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON包含无效UTF-8。",
                    exception);
            }

            EnsureSingleTopLevelObject(json);

            var expected = fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var quotas = new XmlDictionaryReaderQuotas
            {
                MaxDepth = 8,
                MaxStringContentLength = ProtocolConstants.MaximumMessageBytes,
                MaxArrayLength = 4096,
                MaxBytesPerRead = 4096,
                MaxNameTableCharCount = 4096,
            };

            try
            {
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                    utf8,
                    0,
                    utf8.Length,
                    StrictUtf8,
                    quotas,
                    null))
                {
                    reader.MoveToContent();
                    if (reader.NodeType != XmlNodeType.Element
                        || !string.Equals(reader.GetAttribute("type"), "object", StringComparison.Ordinal))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON根节点必须是object。");
                    }

                    var rootDepth = reader.Depth;
                    reader.ReadStartElement();
                    reader.MoveToContent();
                    while (reader.NodeType == XmlNodeType.Element && reader.Depth == rootDepth + 1)
                    {
                        JsonFieldSpec field;
                        if (!expected.TryGetValue(reader.LocalName, out field!))
                        {
                            throw new AgentBridgeClientException(
                                "request_invalid",
                                "JSON包含未知字段。" );
                        }

                        if (!seen.Add(field.Name))
                        {
                            throw new AgentBridgeClientException(
                                "request_invalid",
                                "JSON包含重复字段：" + field.Name + "。" );
                        }

                        ReadExpectedValue(reader, field);
                        reader.MoveToContent();
                    }

                    if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != rootDepth)
                    {
                        throw new AgentBridgeClientException("request_invalid", "JSON object结构无效。");
                    }

                    reader.ReadEndElement();
                    reader.MoveToContent();
                    if (!reader.EOF)
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON后存在额外数据。" );
                    }
                }
            }
            catch (AgentBridgeClientException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is XmlException
                || exception is SerializationException
                || exception is FormatException)
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON结构无效。",
                    exception);
            }

            if (seen.Count != fields.Length)
            {
                throw new AgentBridgeClientException("request_invalid", "JSON缺少必需字段。");
            }
        }

        private static void EnsureSingleTopLevelObject(string json)
        {
            var index = 0;
            while (index < json.Length && IsJsonWhitespace(json[index]))
            {
                index++;
            }

            if (index == json.Length || json[index] != '{')
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON根节点必须是object。");
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (; index < json.Length; index++)
            {
                var character = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }

                if (character == '{' || character == '[')
                {
                    depth++;
                    continue;
                }

                if (character != '}' && character != ']')
                {
                    continue;
                }

                depth--;
                if (depth < 0)
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON object结构无效。");
                }

                if (depth != 0)
                {
                    continue;
                }

                for (index++; index < json.Length; index++)
                {
                    if (!IsJsonWhitespace(json[index]))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON后存在额外数据。");
                    }
                }

                return;
            }

            throw new AgentBridgeClientException(
                "request_invalid",
                "JSON object结构无效。");
        }

        private static bool IsJsonWhitespace(char character)
        {
            return character == ' '
                || character == '\t'
                || character == '\r'
                || character == '\n';
        }

        private static void ReadExpectedValue(XmlDictionaryReader reader, JsonFieldSpec field)
        {
            var actualType = reader.GetAttribute("type") ?? string.Empty;
            switch (field.Kind)
            {
                case JsonFieldKind.String:
                    RequireType(actualType, "string", field.Name);
                    reader.ReadElementContentAsString();
                    return;
                case JsonFieldKind.Integer:
                    RequireType(actualType, "number", field.Name);
                    var integer = reader.ReadElementContentAsString();
                    if (!IsStrictInteger(integer))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON整数字段格式无效：" + field.Name + "。" );
                    }

                    return;
                case JsonFieldKind.Boolean:
                    RequireType(actualType, "boolean", field.Name);
                    var boolean = reader.ReadElementContentAsString();
                    if (!string.Equals(boolean, "true", StringComparison.Ordinal)
                        && !string.Equals(boolean, "false", StringComparison.Ordinal))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON布尔字段格式无效：" + field.Name + "。" );
                    }

                    return;
                case JsonFieldKind.Object:
                    RequireType(actualType, "object", field.Name);
                    reader.Skip();
                    return;
                case JsonFieldKind.StringArray:
                    RequireType(actualType, "array", field.Name);
                    ReadStringArray(reader, field.Name);
                    return;
                case JsonFieldKind.SchemaArray:
                    if (!string.Equals(actualType, "array", StringComparison.Ordinal)
                        && !string.Equals(actualType, "null", StringComparison.Ordinal))
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON字段类型无效：" + field.Name + "。");
                    }
                    if (string.Equals(actualType, "array", StringComparison.Ordinal))
                    {
                        ReadSchemaArray(reader, field.Name);
                    }
                    else
                    {
                        reader.ReadElementContentAsString();
                    }
                    return;
                default:
                    throw new AgentBridgeClientException("request_invalid", "未知JSON字段类型。");
            }
        }

        private static void ReadStringArray(XmlDictionaryReader reader, string fieldName)
        {
            var arrayDepth = reader.Depth;
            var itemCount = 0;
            reader.ReadStartElement();
            reader.MoveToContent();
            while (reader.NodeType == XmlNodeType.Element && reader.Depth == arrayDepth + 1)
            {
                if (!string.Equals(reader.LocalName, "item", StringComparison.Ordinal)
                    || !string.Equals(reader.GetAttribute("type"), "string", StringComparison.Ordinal))
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON数组只能包含string：" + fieldName + "。" );
                }

                reader.ReadElementContentAsString();
                itemCount++;
                if (itemCount > 256)
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON数组超过安全上限：" + fieldName + "。" );
                }

                reader.MoveToContent();
            }

            if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != arrayDepth)
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON数组结构无效：" + fieldName + "。" );
            }

            reader.ReadEndElement();
        }

        private static void ReadSchemaArray(XmlDictionaryReader reader, string fieldName)
        {
            var arrayDepth = reader.Depth;
            var itemCount = 0;
            reader.ReadStartElement();
            reader.MoveToContent();
            while (reader.NodeType == XmlNodeType.Element && reader.Depth == arrayDepth + 1)
            {
                if (!string.Equals(reader.GetAttribute("type"), "object", StringComparison.Ordinal))
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON schema数组只能包含object：" + fieldName + "。");
                }

                var objDepth = reader.Depth;
                reader.ReadStartElement();
                reader.MoveToContent();
                var hasSchema = false;
                var hasVersion = false;
                while (reader.NodeType == XmlNodeType.Element && reader.Depth == objDepth + 1)
                {
                    var propName = reader.LocalName;
                    if (string.Equals(propName, "schema", StringComparison.Ordinal))
                    {
                        RequireType(reader.GetAttribute("type") ?? "", "string", fieldName + ".schema");
                        reader.ReadElementContentAsString();
                        hasSchema = true;
                    }
                    else if (string.Equals(propName, "schemaVersion", StringComparison.Ordinal))
                    {
                        RequireType(reader.GetAttribute("type") ?? "", "number", fieldName + ".schemaVersion");
                        reader.ReadElementContentAsString();
                        hasVersion = true;
                    }
                    else
                    {
                        throw new AgentBridgeClientException(
                            "request_invalid",
                            "JSON schema object包含未知字段：" + propName + "。");
                    }
                    reader.MoveToContent();
                }
                if (!hasSchema || !hasVersion)
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON schema object缺少必需字段：" + fieldName + "。");
                }
                if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != objDepth)
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON schema object结构无效：" + fieldName + "。");
                }
                reader.ReadEndElement();
                itemCount++;
                if (itemCount > 16)
                {
                    throw new AgentBridgeClientException(
                        "request_invalid",
                        "JSON schema数组超过安全上限：" + fieldName + "。");
                }
                reader.MoveToContent();
            }

            if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != arrayDepth)
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON schema数组结构无效：" + fieldName + "。");
            }

            reader.ReadEndElement();
        }

        private static void RequireType(string actual, string expected, string fieldName)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new AgentBridgeClientException(
                    "request_invalid",
                    "JSON字段类型无效：" + fieldName + "。" );
            }
        }

        private static bool IsStrictInteger(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var index = value[0] == '-' ? 1 : 0;
            if (index == value.Length)
            {
                return false;
            }

            if (value[index] == '0')
            {
                return index == value.Length - 1;
            }

            if (value[index] < '1' || value[index] > '9')
            {
                return false;
            }

            for (index++; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
