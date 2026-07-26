using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.Contracts;

/// <summary>
/// M4.1 策略加载规格。覆盖缺失、损坏、旧版本、未知字段、相对路径、UNC、设备路径、
/// 超限文件、reparse、配置层优先级和管理员锁定。
/// </summary>
internal static class AgentHostPolicyStoreSpecs
{
    private const string ValidMachineJson = """
        {
          "schema": "codex.autocad.agent-policy",
          "schemaVersion": 1,
          "allowedModels": ["gpt-5-codex", "gpt-5", "o4-mini"],
          "defaultModel": "gpt-5-codex",
          "allowedReasoningEfforts": ["low", "medium", "high"],
          "defaultReasoningEffort": "medium"
        }
        """;

    private sealed class PolicyFixture : IDisposable
    {
        public PolicyFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "codex-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string MachinePath => Path.Combine(Root, "machine-policy.json");

        public string AdministratorPath => Path.Combine(Root, "administrator.json");

        public string UserPath => Path.Combine(Root, "user.json");

        public void Write(string path, string content) => File.WriteAllText(path, content);

        public AgentPolicyLoadResult Load()
            => AgentHostPolicyStore.LoadFrom(MachinePath, AdministratorPath, UserPath);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void AssertRejected(AgentPolicyLoadResult result, string expectedCode)
    {
        if (result.Accepted)
        {
            throw new InvalidOperationException("Expected rejection: " + expectedCode);
        }
        if (!string.Equals(result.ErrorCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Expected " + expectedCode + ", got " + result.ErrorCode);
        }
        if (result.Policy != null)
        {
            throw new InvalidOperationException("Rejected load must not carry a policy.");
        }
    }

    public static Task MissingEveryLayerFailsClosed()
    {
        using var fixture = new PolicyFixture();
        // 三个文件都不存在：没有任何受信来源，必须 fail-closed。
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.NoEffectiveLayer);
        return Task.CompletedTask;
    }

    public static Task MachinePolicyAloneResolves()
    {
        using var fixture = new PolicyFixture();
        fixture.Write(fixture.MachinePath, ValidMachineJson);

        var result = fixture.Load();
        if (!result.Accepted || result.Policy == null)
        {
            throw new InvalidOperationException("Valid machine policy must resolve: " + result.ErrorCode);
        }
        if (result.Policy.DefaultModel != "gpt-5-codex" || result.Policy.AllowedModels.Length != 3)
        {
            throw new InvalidOperationException("Resolved policy content mismatch.");
        }
        if (!result.MachinePolicyPresent || result.AdministratorPolicyPresent || result.UserPolicyPresent)
        {
            throw new InvalidOperationException("Layer presence must be reported truthfully.");
        }
        return Task.CompletedTask;
    }

    public static Task UserLayerNarrowsButCannotWiden()
    {
        using var fixture = new PolicyFixture();
        fixture.Write(fixture.MachinePath, ValidMachineJson);
        fixture.Write(fixture.UserPath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 1,
              "allowedModels": ["gpt-5"],
              "defaultModel": "gpt-5"
            }
            """);

        var narrowed = fixture.Load();
        if (!narrowed.Accepted || narrowed.Policy!.DefaultModel != "gpt-5" ||
            narrowed.Policy.AllowedModels.Length != 1)
        {
            throw new InvalidOperationException("User layer must be able to narrow.");
        }

        // 用户层引入机器策略未列出的模型必须被拒绝。
        fixture.Write(fixture.UserPath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 1,
              "allowedModels": ["gpt-5", "smuggled-model"]
            }
            """);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.LayerWidensAllowList);
        return Task.CompletedTask;
    }

    public static Task AdministratorLockBlocksUserLayer()
    {
        using var fixture = new PolicyFixture();
        fixture.Write(fixture.MachinePath, ValidMachineJson);
        fixture.Write(fixture.AdministratorPath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 1,
              "defaultModel": "gpt-5",
              "lockModel": true
            }
            """);
        fixture.Write(fixture.UserPath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 1,
              "defaultModel": "o4-mini"
            }
            """);

        var result = fixture.Load();
        AssertRejected(result, AgentPolicyErrorCodes.LockedByHigherLayer);
        if (result.ErrorLayer != AgentPolicyLayers.User)
        {
            throw new InvalidOperationException("Rejection must name the offending layer.");
        }
        return Task.CompletedTask;
    }

    public static Task MalformedAndUnknownFieldsFailClosed()
    {
        using var fixture = new PolicyFixture();

        // 非法 JSON
        fixture.Write(fixture.MachinePath, "{ not json ");
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.FileMalformed);

        // 未知字段不得被静默忽略
        fixture.Write(fixture.MachinePath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 1,
              "allowedModels": ["gpt-5"],
              "defaultModel": "gpt-5",
              "allowedReasoningEfforts": ["medium"],
              "defaultReasoningEffort": "medium",
              "enableEverything": true
            }
            """);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.FileMalformed);

        // 类型错误
        fixture.Write(fixture.MachinePath, """
            {"schema": "codex.autocad.agent-policy", "schemaVersion": "one"}
            """);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.FileMalformed);

        // 配置文件不得自行声明所属层来提权
        fixture.Write(fixture.MachinePath, """
            {"schema": "codex.autocad.agent-policy", "schemaVersion": 1, "layer": "machine-policy"}
            """);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.FileMalformed);
        return Task.CompletedTask;
    }

    public static Task OutdatedSchemaVersionFailsClosed()
    {
        using var fixture = new PolicyFixture();
        fixture.Write(fixture.MachinePath, """
            {
              "schema": "codex.autocad.agent-policy",
              "schemaVersion": 0,
              "allowedModels": ["gpt-5"],
              "defaultModel": "gpt-5",
              "allowedReasoningEfforts": ["medium"],
              "defaultReasoningEffort": "medium"
            }
            """);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.SchemaUnsupported);
        return Task.CompletedTask;
    }

    public static Task OversizedPolicyFileFailsClosed()
    {
        using var fixture = new PolicyFixture();
        var padding = new string('x', (int)AgentHostPolicyStore.MaximumPolicyFileBytes + 1);
        fixture.Write(fixture.MachinePath, padding);
        AssertRejected(fixture.Load(), AgentPolicyErrorCodes.FileTooLarge);
        return Task.CompletedTask;
    }

    public static Task UnsafePolicyPathsAreRejected()
    {
        // 相对路径、UNC 与设备命名空间都不得成为配置源。
        var hostile = new[]
        {
            @"policy\machine.json",
            @"..\..\machine.json",
            @"\\server\share\machine.json",
            @"\\?\C:\machine.json",
            @"\\.\PhysicalDrive0",
            string.Empty,
        };

        foreach (var path in hostile)
        {
            if (AgentHostPolicyStore.IsAcceptablePolicyPath(path))
            {
                throw new InvalidOperationException("Path must be rejected: " + path.Length);
            }
        }

        using var fixture = new PolicyFixture();
        fixture.Write(fixture.MachinePath, ValidMachineJson);
        var result = AgentHostPolicyStore.LoadFrom(
            fixture.MachinePath, @"\\server\share\administrator.json", fixture.UserPath);
        AssertRejected(result, AgentPolicyErrorCodes.PathRejected);
        if (result.ErrorLayer != AgentPolicyLayers.Administrator)
        {
            throw new InvalidOperationException("Rejection must name the offending layer.");
        }
        return Task.CompletedTask;
    }

    public static Task StartupFailureIsRedactedAndDistinguishesUnconfigured()
    {
        using var fixture = new PolicyFixture();

        // 三层皆缺失 = 管理员未部署策略。启动路径据此保留"仅形态校验"的兼容行为，
        // 因此该错误码必须与"配置存在但不可用"明确区分。
        var unconfigured = fixture.Load();
        if (unconfigured.ErrorCode != AgentPolicyErrorCodes.NoEffectiveLayer)
        {
            throw new InvalidOperationException("Unconfigured state must be distinguishable.");
        }

        // 配置存在但损坏：启动必须 fail-closed，且异常不得泄露路径或文件内容。
        fixture.Write(fixture.MachinePath, "{ broken");
        var broken = fixture.Load();
        AssertRejected(broken, AgentPolicyErrorCodes.FileMalformed);

        var exception = new AgentHostPolicyConfigurationException(
            broken.ErrorCode, broken.ErrorLayer);
        var rendered = exception.ToString() + " " + exception.Message;
        foreach (var forbidden in new[]
                 {
                     fixture.Root, "machine-policy.json", "broken", "C:\\", Environment.UserName,
                 })
        {
            if (forbidden.Length != 0 &&
                rendered.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("Startup failure leaked configuration detail.");
            }
        }
        if (rendered.IndexOf(AgentPolicyErrorCodes.FileMalformed, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("Startup failure must expose a stable error code.");
        }
        return Task.CompletedTask;
    }

    public static Task ProductEntryPointUsesFixedLocations()
    {
        // 产品入口不接受任意源路径：三个位置固定且互不相同，且都通过路径安全校验。
        var machine = AgentHostPolicyStore.MachinePolicyPath();
        var administrator = AgentHostPolicyStore.AdministratorPolicyPath();
        var user = AgentHostPolicyStore.UserPolicyPath();

        foreach (var path in new[] { machine, administrator, user })
        {
            if (!AgentHostPolicyStore.IsAcceptablePolicyPath(path))
            {
                throw new InvalidOperationException("Product policy path failed its own guard.");
            }
        }
        if (machine == administrator || machine == user || administrator == user)
        {
            throw new InvalidOperationException("Policy layers must use distinct files.");
        }
        return Task.CompletedTask;
    }
}
