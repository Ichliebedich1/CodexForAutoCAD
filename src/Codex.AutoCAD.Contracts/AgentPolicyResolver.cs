using System.Collections.Generic;

namespace Codex.AutoCAD.Contracts;

/// <summary>
/// M4.1 分层策略合并与请求校验。合并顺序固定为机器策略 &gt; 管理员 &gt; 用户：
/// 低优先级层只能进一步收窄白名单，永远不能扩大；被高优先级层锁定的项，低优先级层
/// 不得改变。任何未知、越界、冲突或缺省缺失的配置都 fail-closed，不返回部分策略。
/// </summary>
public static class AgentPolicyResolver
{
    /// <summary>按固定优先级合并三层配置。任一层为 null 表示该层不存在。</summary>
    public static AgentPolicyResolution Resolve(
        AgentPolicyLayerDocument? machinePolicy,
        AgentPolicyLayerDocument? administrator,
        AgentPolicyLayerDocument? user)
    {
        var ordered = new List<AgentPolicyLayerDocument>();
        if (machinePolicy != null)
        {
            machinePolicy.Layer = AgentPolicyLayers.MachinePolicy;
            ordered.Add(machinePolicy);
        }
        if (administrator != null)
        {
            administrator.Layer = AgentPolicyLayers.Administrator;
            ordered.Add(administrator);
        }
        if (user != null)
        {
            user.Layer = AgentPolicyLayers.User;
            ordered.Add(user);
        }

        if (ordered.Count == 0)
        {
            return AgentPolicyResolution.Reject(
                AgentPolicyErrorCodes.NoEffectiveLayer, string.Empty);
        }

        string[]? allowedModels = null;
        string[]? allowedEfforts = null;
        var defaultModel = string.Empty;
        var defaultEffort = string.Empty;
        var modelLockedBy = string.Empty;
        var effortLockedBy = string.Empty;

        foreach (var layer in ordered)
        {
            if (layer.Schema != AgentPolicyConstants.Schema ||
                layer.SchemaVersion != AgentPolicyConstants.SchemaVersion)
            {
                return AgentPolicyResolution.Reject(
                    AgentPolicyErrorCodes.SchemaUnsupported, layer.Layer);
            }
            if (!AgentPolicyLayers.IsKnown(layer.Layer))
            {
                return AgentPolicyResolution.Reject(
                    AgentPolicyErrorCodes.LayerUnknown, layer.Layer);
            }

            var modelLocked = modelLockedBy.Length != 0;
            var effortLocked = effortLockedBy.Length != 0;

            // ---- 模型白名单 ----
            if (layer.AllowedModels.Length != 0)
            {
                if (modelLocked)
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LockedByHigherLayer, layer.Layer);
                }
                var narrowed = NormalizeAllowList(
                    layer.AllowedModels, requireKnownEffort: false, out var error);
                if (error.Length != 0)
                {
                    return AgentPolicyResolution.Reject(error, layer.Layer);
                }
                if (allowedModels != null && !IsSubset(narrowed, allowedModels))
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LayerWidensAllowList, layer.Layer);
                }
                allowedModels = narrowed;
            }

            // ---- 默认模型 ----
            if (layer.DefaultModel.Length != 0)
            {
                if (modelLocked)
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LockedByHigherLayer, layer.Layer);
                }
                if (!IsValidModel(layer.DefaultModel))
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.ModelInvalid, layer.Layer);
                }
                defaultModel = layer.DefaultModel;
            }

            // ---- 思考强度白名单 ----
            if (layer.AllowedReasoningEfforts.Length != 0)
            {
                if (effortLocked)
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LockedByHigherLayer, layer.Layer);
                }
                var narrowed = NormalizeAllowList(
                    layer.AllowedReasoningEfforts, requireKnownEffort: true, out var error);
                if (error.Length != 0)
                {
                    return AgentPolicyResolution.Reject(error, layer.Layer);
                }
                if (allowedEfforts != null && !IsSubset(narrowed, allowedEfforts))
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LayerWidensAllowList, layer.Layer);
                }
                allowedEfforts = narrowed;
            }

            // ---- 默认思考强度 ----
            if (layer.DefaultReasoningEffort.Length != 0)
            {
                if (effortLocked)
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.LockedByHigherLayer, layer.Layer);
                }
                if (!AgentReasoningEfforts.IsKnown(layer.DefaultReasoningEffort))
                {
                    return AgentPolicyResolution.Reject(
                        AgentPolicyErrorCodes.ReasoningEffortInvalid, layer.Layer);
                }
                defaultEffort = layer.DefaultReasoningEffort;
            }

            // 锁定在本层生效之后记录，使同层可以同时设置值并锁定。
            if (layer.LockModel && modelLockedBy.Length == 0)
            {
                modelLockedBy = layer.Layer;
            }
            if (layer.LockReasoningEffort && effortLockedBy.Length == 0)
            {
                effortLockedBy = layer.Layer;
            }
        }

        // fail-closed：白名单必须被显式配置，不存在"未配置即全部允许"。
        if (allowedModels == null || allowedModels.Length == 0)
        {
            return AgentPolicyResolution.Reject(
                AgentPolicyErrorCodes.AllowListEmpty, AgentPolicyLayers.MachinePolicy);
        }
        if (allowedEfforts == null || allowedEfforts.Length == 0)
        {
            return AgentPolicyResolution.Reject(
                AgentPolicyErrorCodes.AllowListEmpty, AgentPolicyLayers.MachinePolicy);
        }
        if (defaultModel.Length == 0 || defaultEffort.Length == 0)
        {
            return AgentPolicyResolution.Reject(
                AgentPolicyErrorCodes.DefaultMissing, AgentPolicyLayers.MachinePolicy);
        }
        if (!Contains(allowedModels, defaultModel) || !Contains(allowedEfforts, defaultEffort))
        {
            return AgentPolicyResolution.Reject(
                AgentPolicyErrorCodes.DefaultNotAllowed, AgentPolicyLayers.MachinePolicy);
        }

        return AgentPolicyResolution.Accept(new ResolvedAgentPolicy
        {
            AllowedModels = allowedModels,
            DefaultModel = defaultModel,
            AllowedReasoningEfforts = allowedEfforts,
            DefaultReasoningEffort = defaultEffort,
            ModelLocked = modelLockedBy.Length != 0,
            ReasoningEffortLocked = effortLockedBy.Length != 0,
            ModelLockedByLayer = modelLockedBy,
            ReasoningEffortLockedByLayer = effortLockedBy,
        });
    }

    /// <summary>
    /// 校验一次实际请求。空请求回落到策略默认值；非法或不在白名单的值一律拒绝，
    /// 不静默降级，从而保证任意字符串不会穿透到 AgentHost/Codex。
    /// </summary>
    public static AgentPolicySelection Select(
        ResolvedAgentPolicy policy,
        string? requestedModel,
        string? requestedReasoningEffort)
    {
        if (policy == null)
        {
            return AgentPolicySelection.Reject(AgentPolicyErrorCodes.NoEffectiveLayer);
        }

        var model = policy.DefaultModel;
        var modelCoerced = false;
        if (!string.IsNullOrEmpty(requestedModel))
        {
            if (!IsValidModel(requestedModel!))
            {
                return AgentPolicySelection.Reject(AgentPolicyErrorCodes.ModelInvalid);
            }
            if (policy.ModelLocked && requestedModel != policy.DefaultModel)
            {
                return AgentPolicySelection.Reject(AgentPolicyErrorCodes.LockedByHigherLayer);
            }
            if (!Contains(policy.AllowedModels, requestedModel!))
            {
                return AgentPolicySelection.Reject(AgentPolicyErrorCodes.ModelNotAllowed);
            }
            model = requestedModel!;
        }
        else
        {
            modelCoerced = true;
        }

        var effort = policy.DefaultReasoningEffort;
        var effortCoerced = false;
        if (!string.IsNullOrEmpty(requestedReasoningEffort))
        {
            if (!AgentReasoningEfforts.IsKnown(requestedReasoningEffort))
            {
                return AgentPolicySelection.Reject(
                    AgentPolicyErrorCodes.ReasoningEffortInvalid);
            }
            if (policy.ReasoningEffortLocked &&
                requestedReasoningEffort != policy.DefaultReasoningEffort)
            {
                return AgentPolicySelection.Reject(AgentPolicyErrorCodes.LockedByHigherLayer);
            }
            if (!Contains(policy.AllowedReasoningEfforts, requestedReasoningEffort!))
            {
                return AgentPolicySelection.Reject(
                    AgentPolicyErrorCodes.ReasoningEffortNotAllowed);
            }
            effort = requestedReasoningEffort!;
        }
        else
        {
            effortCoerced = true;
        }

        return new AgentPolicySelection
        {
            Accepted = true,
            AcceptedModel = model,
            AcceptedReasoningEffort = effort,
            ModelCoercedToDefault = modelCoerced,
            ReasoningEffortCoercedToDefault = effortCoerced,
        };
    }

    /// <summary>
    /// 模型标识只允许有界 ASCII 子集，杜绝控制字符、空白、引号和路径分隔符进入
    /// 进程参数或诊断输出。
    /// </summary>
    public static bool IsValidModel(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > AgentPolicyConstants.MaximumModelLength)
        {
            return false;
        }
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var ok = (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '-' || c == '.' || c == '_';
            if (!ok)
            {
                return false;
            }
        }
        return true;
    }

    private static string[] NormalizeAllowList(
        string[] entries, bool requireKnownEffort, out string errorCode)
    {
        errorCode = string.Empty;
        if (entries.Length > AgentPolicyConstants.MaximumAllowedEntryCount)
        {
            errorCode = AgentPolicyErrorCodes.AllowListTooLarge;
            return new string[0];
        }

        var unique = new List<string>();
        foreach (var entry in entries)
        {
            if (requireKnownEffort)
            {
                if (!AgentReasoningEfforts.IsKnown(entry))
                {
                    errorCode = AgentPolicyErrorCodes.ReasoningEffortInvalid;
                    return new string[0];
                }
            }
            else if (!IsValidModel(entry))
            {
                errorCode = AgentPolicyErrorCodes.ModelInvalid;
                return new string[0];
            }

            if (!unique.Contains(entry))
            {
                unique.Add(entry);
            }
        }

        if (unique.Count == 0)
        {
            errorCode = AgentPolicyErrorCodes.AllowListEmpty;
            return new string[0];
        }

        // 稳定顺序，保证跨进程与跨 Shell 的确定性诊断与比较。
        unique.Sort(System.StringComparer.Ordinal);
        return unique.ToArray();
    }

    private static bool IsSubset(string[] candidate, string[] allowed)
    {
        foreach (var entry in candidate)
        {
            if (!Contains(allowed, entry))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Contains(string[] values, string value)
    {
        foreach (var entry in values)
        {
            if (string.Equals(entry, value, System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
