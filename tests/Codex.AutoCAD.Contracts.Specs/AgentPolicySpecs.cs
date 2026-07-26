using Codex.AutoCAD.Contracts;

/// <summary>
/// M4.1 分层 Agent 策略规格。覆盖配置层优先级、白名单收窄、管理员锁定、非法模型、
/// 非法思考强度、缺省缺失、schema 损坏与旧版本，以及请求侧的字符串穿透边界。
/// </summary>
internal static class AgentPolicySpecs
{
    private static AgentPolicyLayerDocument Layer(string layer)
        => new AgentPolicyLayerDocument
        {
            Schema = AgentPolicyConstants.Schema,
            SchemaVersion = AgentPolicyConstants.SchemaVersion,
            Layer = layer,
        };

    private static AgentPolicyLayerDocument BaselineMachineLayer()
    {
        var machine = Layer(AgentPolicyLayers.MachinePolicy);
        machine.AllowedModels = new[] { "gpt-5-codex", "gpt-5", "o4-mini" };
        machine.DefaultModel = "gpt-5-codex";
        machine.AllowedReasoningEfforts = new[]
        {
            AgentReasoningEfforts.Low,
            AgentReasoningEfforts.Medium,
            AgentReasoningEfforts.High,
        };
        machine.DefaultReasoningEffort = AgentReasoningEfforts.Medium;
        return machine;
    }

    private static ResolvedAgentPolicy ResolveBaseline()
    {
        var resolution = AgentPolicyResolver.Resolve(BaselineMachineLayer(), null, null);
        AssertAccepted(resolution);
        return resolution.Policy!;
    }

    private static void AssertAccepted(AgentPolicyResolution resolution)
    {
        if (!resolution.Accepted || resolution.Policy == null)
        {
            throw new InvalidOperationException(
                "Expected policy resolution to be accepted, got: " + resolution.ErrorCode);
        }
    }

    private static void AssertRejected(AgentPolicyResolution resolution, string expectedCode)
    {
        if (resolution.Accepted)
        {
            throw new InvalidOperationException("Expected rejection with code: " + expectedCode);
        }
        if (!string.Equals(resolution.ErrorCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Expected code " + expectedCode + ", got " + resolution.ErrorCode);
        }
        if (resolution.Policy != null)
        {
            throw new InvalidOperationException("Rejected resolution must not carry a policy.");
        }
    }

    private static void AssertSelectionRejected(AgentPolicySelection selection, string expectedCode)
    {
        if (selection.Accepted)
        {
            throw new InvalidOperationException("Expected selection rejection: " + expectedCode);
        }
        if (!string.Equals(selection.ErrorCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Expected code " + expectedCode + ", got " + selection.ErrorCode);
        }
        if (selection.AcceptedModel.Length != 0 || selection.AcceptedReasoningEffort.Length != 0)
        {
            throw new InvalidOperationException("Rejected selection must not carry accepted values.");
        }
    }

    // ---------------- 配置层优先级与收窄 ----------------

    public static void LayerPrecedenceNarrowsFromMachineToUser()
    {
        var machine = BaselineMachineLayer();
        var admin = Layer(AgentPolicyLayers.Administrator);
        admin.AllowedModels = new[] { "gpt-5-codex", "gpt-5" };
        var user = Layer(AgentPolicyLayers.User);
        user.AllowedModels = new[] { "gpt-5" };
        user.DefaultModel = "gpt-5";

        var resolution = AgentPolicyResolver.Resolve(machine, admin, user);
        AssertAccepted(resolution);
        var policy = resolution.Policy!;
        if (policy.AllowedModels.Length != 1 || policy.AllowedModels[0] != "gpt-5")
        {
            throw new InvalidOperationException("User layer must narrow the model allow list.");
        }
        if (policy.DefaultModel != "gpt-5")
        {
            throw new InvalidOperationException("Lowest layer default must win when not locked.");
        }
    }

    public static void LowerLayerCannotWidenAllowList()
    {
        var machine = BaselineMachineLayer();
        var user = Layer(AgentPolicyLayers.User);
        // 机器策略未列出的模型不得由用户层引入。
        user.AllowedModels = new[] { "gpt-5-codex", "attacker-model" };

        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, user),
            AgentPolicyErrorCodes.LayerWidensAllowList);
    }

    public static void AllowListIsDeterministicallyOrdered()
    {
        var machine = BaselineMachineLayer();
        machine.AllowedModels = new[] { "o4-mini", "gpt-5", "gpt-5-codex", "gpt-5" };

        var resolution = AgentPolicyResolver.Resolve(machine, null, null);
        AssertAccepted(resolution);
        var models = resolution.Policy!.AllowedModels;
        // 去重且按 Ordinal 稳定排序，保证跨进程与跨 Shell 的确定性。
        if (models.Length != 3 ||
            models[0] != "gpt-5" || models[1] != "gpt-5-codex" || models[2] != "o4-mini")
        {
            throw new InvalidOperationException("Allow list must be deduplicated and ordered.");
        }
    }

    // ---------------- 管理员锁定 ----------------

    public static void AdministratorLockBlocksUserModelOverride()
    {
        var machine = BaselineMachineLayer();
        var admin = Layer(AgentPolicyLayers.Administrator);
        admin.DefaultModel = "gpt-5";
        admin.LockModel = true;
        var user = Layer(AgentPolicyLayers.User);
        user.DefaultModel = "o4-mini";

        var resolution = AgentPolicyResolver.Resolve(machine, admin, user);
        AssertRejected(resolution, AgentPolicyErrorCodes.LockedByHigherLayer);
        if (resolution.ErrorLayer != AgentPolicyLayers.User)
        {
            throw new InvalidOperationException("Rejection must name the offending lower layer.");
        }
    }

    public static void AdministratorLockBlocksUserEffortOverride()
    {
        var machine = BaselineMachineLayer();
        var admin = Layer(AgentPolicyLayers.Administrator);
        admin.DefaultReasoningEffort = AgentReasoningEfforts.Low;
        admin.LockReasoningEffort = true;
        var user = Layer(AgentPolicyLayers.User);
        user.DefaultReasoningEffort = AgentReasoningEfforts.High;

        AssertRejected(
            AgentPolicyResolver.Resolve(machine, admin, user),
            AgentPolicyErrorCodes.LockedByHigherLayer);
    }

    public static void SameLayerMaySetValueAndLockIt()
    {
        var machine = BaselineMachineLayer();
        machine.LockModel = true;

        var resolution = AgentPolicyResolver.Resolve(machine, null, null);
        AssertAccepted(resolution);
        var policy = resolution.Policy!;
        if (!policy.ModelLocked || policy.ModelLockedByLayer != AgentPolicyLayers.MachinePolicy)
        {
            throw new InvalidOperationException("A layer must be able to set and lock in one pass.");
        }
    }

    // ---------------- fail-closed 边界 ----------------

    public static void MissingEveryLayerFailsClosed()
    {
        AssertRejected(
            AgentPolicyResolver.Resolve(null, null, null),
            AgentPolicyErrorCodes.NoEffectiveLayer);
    }

    public static void EmptyAllowListFailsClosed()
    {
        var machine = Layer(AgentPolicyLayers.MachinePolicy);
        machine.DefaultModel = "gpt-5";
        machine.DefaultReasoningEffort = AgentReasoningEfforts.Medium;
        // 没有任何白名单：不存在"未配置即全部允许"。
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.AllowListEmpty);
    }

    public static void CorruptSchemaFailsClosed()
    {
        var machine = BaselineMachineLayer();
        machine.Schema = "codex.autocad.agent-policy.tampered";
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.SchemaUnsupported);
    }

    public static void OutdatedSchemaVersionFailsClosed()
    {
        var machine = BaselineMachineLayer();
        machine.SchemaVersion = AgentPolicyConstants.SchemaVersion - 1;
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.SchemaUnsupported);
    }

    public static void DefaultOutsideAllowListFailsClosed()
    {
        var machine = BaselineMachineLayer();
        machine.DefaultModel = "model-not-in-list";
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.DefaultNotAllowed);
    }

    public static void MissingDefaultFailsClosed()
    {
        var machine = BaselineMachineLayer();
        machine.DefaultReasoningEffort = string.Empty;
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.DefaultMissing);
    }

    public static void OversizedAllowListFailsClosed()
    {
        var machine = BaselineMachineLayer();
        var many = new string[AgentPolicyConstants.MaximumAllowedEntryCount + 1];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = "model-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        machine.AllowedModels = many;
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.AllowListTooLarge);
    }

    // ---------------- 非法模型与思考强度 ----------------

    public static void InvalidModelShapesFailClosed()
    {
        var hostile = new[]
        {
            "model with space",
            "model\"quoted",
            "model\nnewline",
            "model\u0000null",
            "..\\..\\escape",
            "C:\\Windows\\System32",
            "\\\\server\\share",
            "model;rm -rf",
            new string('m', AgentPolicyConstants.MaximumModelLength + 1),
            string.Empty,
        };

        foreach (var candidate in hostile)
        {
            if (AgentPolicyResolver.IsValidModel(candidate))
            {
                throw new InvalidOperationException("Model must be rejected: " + candidate.Length);
            }
            var machine = BaselineMachineLayer();
            machine.DefaultModel = candidate;
            var resolution = AgentPolicyResolver.Resolve(machine, null, null);
            if (resolution.Accepted)
            {
                throw new InvalidOperationException("Hostile default model was accepted.");
            }
        }
    }

    public static void InvalidReasoningEffortFailsClosed()
    {
        var machine = BaselineMachineLayer();
        machine.DefaultReasoningEffort = "ultra";
        AssertRejected(
            AgentPolicyResolver.Resolve(machine, null, null),
            AgentPolicyErrorCodes.ReasoningEffortInvalid);

        var machine2 = BaselineMachineLayer();
        machine2.AllowedReasoningEfforts = new[] { AgentReasoningEfforts.Low, "turbo" };
        AssertRejected(
            AgentPolicyResolver.Resolve(machine2, null, null),
            AgentPolicyErrorCodes.ReasoningEffortInvalid);
    }

    // ---------------- 请求侧字符串穿透边界 ----------------

    public static void EmptyRequestFallsBackToDefaults()
    {
        var policy = ResolveBaseline();
        var selection = AgentPolicyResolver.Select(policy, null, null);
        if (!selection.Accepted)
        {
            throw new InvalidOperationException("Empty request must fall back to defaults.");
        }
        if (selection.AcceptedModel != "gpt-5-codex" ||
            selection.AcceptedReasoningEffort != AgentReasoningEfforts.Medium)
        {
            throw new InvalidOperationException("Fallback must use the resolved defaults.");
        }
        if (!selection.ModelCoercedToDefault || !selection.ReasoningEffortCoercedToDefault)
        {
            throw new InvalidOperationException("Fallback must be reported truthfully.");
        }
    }

    public static void ArbitraryRequestStringCannotPassThrough()
    {
        var policy = ResolveBaseline();
        AssertSelectionRejected(
            AgentPolicyResolver.Select(policy, "gpt-5-unlisted", null),
            AgentPolicyErrorCodes.ModelNotAllowed);
        AssertSelectionRejected(
            AgentPolicyResolver.Select(policy, "model with space", null),
            AgentPolicyErrorCodes.ModelInvalid);
        AssertSelectionRejected(
            AgentPolicyResolver.Select(policy, null, "ultra"),
            AgentPolicyErrorCodes.ReasoningEffortInvalid);
    }

    public static void AllowedRequestIsReturnedAsAcceptedValue()
    {
        var policy = ResolveBaseline();
        var selection = AgentPolicyResolver.Select(policy, "o4-mini", AgentReasoningEfforts.High);
        if (!selection.Accepted)
        {
            throw new InvalidOperationException("Allowed request must be accepted.");
        }
        if (selection.AcceptedModel != "o4-mini" ||
            selection.AcceptedReasoningEffort != AgentReasoningEfforts.High)
        {
            throw new InvalidOperationException("Accepted values must echo the allowed request.");
        }
        if (selection.ModelCoercedToDefault || selection.ReasoningEffortCoercedToDefault)
        {
            throw new InvalidOperationException("Explicit allowed request is not a coercion.");
        }
    }

    public static void LockedPolicyRejectsDivergentRequest()
    {
        var machine = BaselineMachineLayer();
        machine.LockModel = true;
        machine.LockReasoningEffort = true;
        var resolution = AgentPolicyResolver.Resolve(machine, null, null);
        AssertAccepted(resolution);
        var policy = resolution.Policy!;

        // 锁定后即便请求值在白名单内，也不得偏离受信默认值。
        AssertSelectionRejected(
            AgentPolicyResolver.Select(policy, "o4-mini", null),
            AgentPolicyErrorCodes.LockedByHigherLayer);
        AssertSelectionRejected(
            AgentPolicyResolver.Select(policy, null, AgentReasoningEfforts.High),
            AgentPolicyErrorCodes.LockedByHigherLayer);

        // 与默认值一致的显式请求仍然接受。
        var same = AgentPolicyResolver.Select(policy, "gpt-5-codex", AgentReasoningEfforts.Medium);
        if (!same.Accepted)
        {
            throw new InvalidOperationException("Request equal to the locked default must pass.");
        }
    }
}
