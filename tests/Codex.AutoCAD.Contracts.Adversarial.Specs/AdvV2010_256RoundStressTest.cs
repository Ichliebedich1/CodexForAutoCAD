using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

/// <summary>
/// ADV-V2-010: 固定种子256轮压力；每轮合法且连续序列化3次一致，
/// 拼接每轮hash计算aggregate，并输出：
/// CAD_CONTEXT_JSON_V2_ADVERSARIAL seed=C0D3CA16 rounds=256 sha256=lowerhex
/// </summary>
public static class AdvV2010_256RoundStressTest
{
    public static void Run()
    {
        const uint seed = 0xC0D3CA16;
        const int rounds = 256;

        var rng = new XorShift32(seed);
        var aggregateBuilder = new StringBuilder();

        for (var round = 0; round < rounds; round++)
        {
            var context = CreateRandomContext(rng);

            // 验证每轮合法
            var failures = CadContextJsonV2Validator.Validate(context);
            if (failures.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Round {round}: validation failed: {failures[0].Code}");
            }

            // 连续序列化3次一致
            var json1 = CadContextJsonV2Codec.SerializeCanonical(context);
            var json2 = CadContextJsonV2Codec.SerializeCanonical(context);
            var json3 = CadContextJsonV2Codec.SerializeCanonical(context);

            if (json1 != json2 || json2 != json3)
            {
                throw new InvalidOperationException(
                    $"Round {round}: canonical JSON not deterministic.");
            }

            var hash = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
            aggregateBuilder.Append(hash);
        }

        // 计算aggregate hash
        var aggregateBytes = Encoding.UTF8.GetBytes(aggregateBuilder.ToString());
        byte[] sha256Bytes;
        using (var sha256 = SHA256.Create())
        {
            sha256Bytes = sha256.ComputeHash(aggregateBytes);
        }

        var sha256Hex = ToLowerHex(sha256Bytes);

        // 输出要求的格式
        Console.WriteLine(
            $"CAD_CONTEXT_JSON_V2_ADVERSARIAL seed=C0D3CA16 rounds=256 sha256={sha256Hex}");
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

    private static CadContextJsonV2 CreateRandomContext(XorShift32 rng)
    {
        var entityCount = (int)(rng.Next() % 10) + 1;
        var entities = new CadContextEntityV2[entityCount];

        for (var i = 0; i < entityCount; i++)
        {
            entities[i] = CreateRandomEntity(rng, i);
        }

        var parsedCount = entities.Count(e => e.EntityType != CadContextEntityTypesV2.Unsupported);
        var unsupportedCount = entityCount - parsedCount;

        return new CadContextJsonV2
        {
            CapturedAtUtc = "2026-07-21T04:00:00.000Z",
            Document = new CadContextDocumentV2
            {
                DocumentId = "doc-adv-010-" + rng.Next().ToString("X"),
                DrawingFingerprint = new string('a', 64),
                Revision = (long)(rng.Next() % 100),
                CurrentSpace = rng.Next() % 2 == 0
                    ? CadContextJsonV2Constants.ModelSpace
                    : CadContextJsonV2Constants.PaperSpace,
                DrawingVersion = "AC1027",
                Units = "Millimeters",
            },
            Selection = new CadContextSelectionV2
            {
                SnapshotHash = new string('b', 64),
                EntityCount = entityCount,
                ParsedEntityCount = parsedCount,
                UnsupportedEntityCount = unsupportedCount,
                Complete = unsupportedCount == 0,
                Entities = entities,
            },
        };
    }

    private static CadContextEntityV2 CreateRandomEntity(XorShift32 rng, int index)
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
            CadContextEntityTypesV2.Unsupported,
        };

        var type = types[rng.Next() % types.Length];
        var handle = index.ToString("X");

        var entity = new CadContextEntityV2
        {
            Handle = handle,
            OwnerSpaceHandle = "1F",
            EntityType = type,
            StateHash = GenerateHash(rng),
            Layer = "0",
        };

        switch (type)
        {
            case CadContextEntityTypesV2.Line:
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(rng.Next() % 100, rng.Next() % 100, 0),
                    End = new CadPoint3(rng.Next() % 100 + 100, rng.Next() % 100, 0),
                };
                break;
            case CadContextEntityTypesV2.Circle:
                entity.Circle = new CadContextCircleV2
                {
                    Center = new CadPoint3(rng.Next() % 100, rng.Next() % 100, 0),
                    Radius = (rng.Next() % 50) + 1,
                    Normal = new CadPoint3(0, 0, 1),
                };
                break;
            case CadContextEntityTypesV2.DbText:
                entity.DbText = new CadContextDbTextV2
                {
                    Text = "text-" + rng.Next().ToString("X"),
                    Position = new CadPoint3(rng.Next() % 100, rng.Next() % 100, 0),
                    Height = 2.5,
                    Rotation = 0,
                };
                break;
            case CadContextEntityTypesV2.Unsupported:
                entity.Unsupported = new CadContextUnsupportedV2
                {
                    DxfName = "ACAD_PROXY_ENTITY",
                    Reason = CadContextUnsupportedReasonsV2.UnknownEntityType,
                };
                break;
            default:
                // 简化：其他类型使用Line
                entity.EntityType = CadContextEntityTypesV2.Line;
                entity.Line = new CadContextLineV2
                {
                    Start = new CadPoint3(0, 0, 0),
                    End = new CadPoint3(10, 0, 0),
                };
                break;
        }

        return entity;
    }

    private static string GenerateHash(XorShift32 rng)
    {
        const string alphabet = "0123456789abcdef";
        var chars = new char[64];
        for (var i = 0; i < 64; i++)
        {
            chars[i] = alphabet[(int)(rng.Next() % 16)];
        }
        return new string(chars);
    }

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
