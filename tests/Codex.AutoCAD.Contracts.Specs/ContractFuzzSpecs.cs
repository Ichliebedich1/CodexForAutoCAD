using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Codex.AutoCAD.Contracts;

// 与本项目其他规格类一致，放在全局命名空间：Program.cs 直接按类型名引用。

/// <summary>
/// M9.5：契约校验入口的属性与模糊测试。
/// </summary>
/// <remarks>
/// 验收目标是"崩溃、无限循环和超大分配为 0"，因此这里断言的不是"某个输入被拒绝"，
/// 而是三条对**任意**输入都必须成立的性质：
///
/// 1. 校验函数只以返回失败数组的方式表达拒绝，不抛出异常。畸形输入让校验器抛出，
///    等于把输入校验变成了调用方的异常处理问题，而调用方往往在更靠外的边界上。
/// 2. 每次调用在有界时间内返回。目标文件明确要求"无限循环为 0"。
/// 3. 失败数组本身有界。一个对每个字符都追加一条失败的校验器，可以被一段超长输入
///    变成内存放大器。
///
/// 种子固定：模糊测试的价值一半在于发现问题，另一半在于失败能被原样复现。用随机种子
/// 会让一次红色变成无法追查的传闻。
/// </remarks>
internal static class ContractFuzzSpecs
{
    private const int CaseCount = 512;
    private const int PerCallTimeoutMilliseconds = 2000;
    private const int MaximumFailureCount = 4096;

    /// <summary>畸形输入的构造素材：每一项都对应一类真实出现过的解析陷阱。</summary>
    private static readonly string[] HostileFragments =
    {
        string.Empty,
        " ",
        "\0",
        "\u0000\u0001\u0002",
        "\r\n",
        "\u202E",                       // 从右至左覆写，能让日志显示与实际值不符
        "\uD800",                       // 落单的高代理项，容易在编码往返中炸开
        "\uDFFF",                       // 落单的低代理项
        "\uFFFD",
        "../../etc/passwd",
        "\\\\?\\C:\\Windows",
        "{\"a\":",                      // 截断的 JSON
        "]]]]]]]]",
        "%00%2e%2e",
        "'; DROP TABLE --",
        "${jndi:ldap://x}",
        new string('A', 70000),         // 超过任何合理字段上限
        "codex.autocad.cad-context/2",  // 合法值也要混进去，避免只测拒绝路径
    };

    private static string BuildHostileString(Random random)
    {
        var builder = new StringBuilder();
        var pieces = random.Next(1, 5);
        for (var index = 0; index < pieces; index++)
        {
            builder.Append(HostileFragments[random.Next(HostileFragments.Length)]);
        }

        return builder.ToString();
    }

    private static double BuildHostileDouble(Random random)
    {
        switch (random.Next(8))
        {
            case 0: return double.NaN;
            case 1: return double.PositiveInfinity;
            case 2: return double.NegativeInfinity;
            case 3: return double.MaxValue;
            case 4: return double.MinValue;
            case 5: return double.Epsilon;
            case 6: return 0.0;
            default: return (random.NextDouble() - 0.5) * 1e12;
        }
    }

    private static int BuildHostileInt(Random random)
    {
        switch (random.Next(6))
        {
            case 0: return int.MinValue;
            case 1: return int.MaxValue;
            case 2: return -1;
            case 3: return 0;
            case 4: return 1;
            default: return random.Next();
        }
    }

    /// <summary>
    /// 对一个校验委托施加三条性质检查。返回失败描述，通过时返回 null。
    /// </summary>
    private static string? CheckValidationProperties(
        string label,
        int seed,
        Func<CadValidationFailure[]> validate)
    {
        CadValidationFailure[] failures;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            failures = validate();
        }
        catch (Exception exception)
        {
            return label + " seed=" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 抛出了 " + exception.GetType().Name + "，校验器必须以失败数组表达拒绝。";
        }
        finally
        {
            stopwatch.Stop();
        }

        if (stopwatch.ElapsedMilliseconds > PerCallTimeoutMilliseconds)
        {
            return label + " seed=" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 用时 " + stopwatch.ElapsedMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + " ms，超过有界时间要求。";
        }

        if (failures is null)
        {
            return label + " 返回了 null 而不是空数组。";
        }

        if (failures.Length > MaximumFailureCount)
        {
            return label + " seed=" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 返回 " + failures.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 条失败，失败集合必须有界。";
        }

        return null;
    }

    private static void RunFuzzCampaign(
        string label,
        Func<Random, Func<CadValidationFailure[]>> buildCase)
    {
        var problems = new List<string>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            // 种子即序号：失败信息里的 seed 可以直接复现那一个用例。
            var random = new Random(seed);
            var problem = CheckValidationProperties(label, seed, buildCase(random));
            if (problem is not null)
            {
                problems.Add(problem);
                if (problems.Count >= 5)
                {
                    break;
                }
            }
        }

        if (problems.Count != 0)
        {
            throw new InvalidOperationException(string.Join(" | ", problems));
        }
    }

    internal static void TurnStartV2RequestSurvivesHostileInput()
    {
        RunFuzzCampaign(
            "AgentTurnStartV2Request",
            random => () => AgentBridgeContractValidator.Validate(new AgentTurnStartV2Request
            {
                ThreadId = BuildHostileString(random),
                ClientTurnId = BuildHostileString(random),
                Prompt = BuildHostileString(random),
                ContextV2Sha256 = BuildHostileString(random),
            }));
    }

    internal static void DrawingQueryRequestSurvivesHostileInput()
    {
        RunFuzzCampaign(
            "AgentDrawingQueryRequest",
            random => () => AgentBridgeContractValidator.Validate(new AgentDrawingQueryRequest
            {
                ThreadId = BuildHostileString(random),
                TurnId = BuildHostileString(random),
                RequestId = BuildHostileString(random),
                ToolCallId = BuildHostileString(random),
                QueryId = BuildHostileString(random),
                Cursor = BuildHostileString(random),
                PageSize = BuildHostileInt(random),
            }));
    }

    internal static void LineProposalSurvivesHostileInput()
    {
        RunFuzzCampaign(
            "CadLineProposalRequest",
            random => () => AgentBridgeContractValidator.Validate(new CadLineProposalRequest
            {
                ThreadId = BuildHostileString(random),
                TurnId = BuildHostileString(random),
                ProposalId = BuildHostileString(random),
                ToolCallId = BuildHostileString(random),
                Layer = BuildHostileString(random),
                Start = new CadPoint3
                {
                    X = BuildHostileDouble(random),
                    Y = BuildHostileDouble(random),
                    Z = BuildHostileDouble(random),
                },
                End = new CadPoint3
                {
                    X = BuildHostileDouble(random),
                    Y = BuildHostileDouble(random),
                    Z = BuildHostileDouble(random),
                },
            }));
    }

    internal static void ApprovalResolveSurvivesHostileInput()
    {
        RunFuzzCampaign(
            "AgentApprovalResolveRequest",
            random => () => AgentBridgeContractValidator.Validate(new AgentApprovalResolveRequest
            {
                ThreadId = BuildHostileString(random),
                TurnId = BuildHostileString(random),
                ApprovalId = BuildHostileString(random),
                Decision = BuildHostileString(random),
            }));
    }

    internal static void CapabilitiesSurviveHostileInput()
    {
        RunFuzzCampaign(
            "AgentCapabilitiesRequest",
            random => () => AgentBridgeContractValidator.Validate(new AgentCapabilitiesRequest
            {
                ClientName = BuildHostileString(random),
                ClientVersion = BuildHostileString(random),
                HostTarget = BuildHostileString(random),
            }));
    }

    /// <summary>
    /// 同一个种子必须产生同一个结论。模糊测试若不可复现，红色就无法追查。
    /// </summary>
    internal static void FuzzCampaignIsReproducible()
    {
        static int CountFailures(int seed)
        {
            var random = new Random(seed);
            return AgentBridgeContractValidator.Validate(new AgentTurnStartV2Request
            {
                ThreadId = BuildHostileString(random),
                ClientTurnId = BuildHostileString(random),
                Prompt = BuildHostileString(random),
                ContextV2Sha256 = BuildHostileString(random),
            }).Length;
        }

        for (var seed = 0; seed < 32; seed++)
        {
            if (CountFailures(seed) != CountFailures(seed))
            {
                throw new InvalidOperationException(
                    "同一种子产生了不同的校验结论，模糊测试不可复现。");
            }
        }
    }

    /// <summary>
    /// null 输入必须以失败数组表达，而不是 NullReferenceException。
    /// </summary>
    internal static void NullRequestsFailClosedWithoutThrowing()
    {
        var checks = new Func<CadValidationFailure[]>[]
        {
            () => AgentBridgeContractValidator.Validate((AgentCapabilitiesRequest?)null),
            () => AgentBridgeContractValidator.Validate((AgentTurnStartV2Request?)null),
            () => AgentBridgeContractValidator.Validate((AgentDrawingQueryRequest?)null),
            () => AgentBridgeContractValidator.Validate((CadLineProposalRequest?)null),
            () => AgentBridgeContractValidator.Validate((AgentApprovalResolveRequest?)null),
        };

        foreach (var check in checks)
        {
            var problem = CheckValidationProperties("null input", -1, check);
            if (problem is not null)
            {
                throw new InvalidOperationException(problem);
            }

            if (check().Length == 0)
            {
                throw new InvalidOperationException(
                    "null 请求返回了空失败集合，等于被当成有效输入。");
            }
        }
    }
}
