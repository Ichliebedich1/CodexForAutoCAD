# AutoCAD 2016 Worktree 清单

生成日期：2026-07-22

目的：在 M0 集成完成前区分活动线、已有提交的历史线、待复核候选和包含未提交内容的
Worktree。本文只建立删除前置条件，不执行删除。

## 删除规则

删除 Worktree 前必须同时满足：

1. `git status --porcelain` 为空。
2. 必要提交已进入 M0/main，或存在明确保留分支/标签。
3. 未跟踪的 handoff、evidence、测试和 patch 已审查。
4. 冻结 artifact 和哈希已有归档位置。
5. 用户所有的 Host.2025/Kimi 工作不在删除范围。

## 活动或明确保留

| 分支 | HEAD | 状态 | 处理 |
| --- | --- | --- | --- |
| `main` | `ecaff6b` | 脏；含用户 Host.2025 原型和文档 | 必须保留，不清理、不切换、不强制合并 |
| `codex/m0-baseline` | M0 merge in progress | 当前集成线 | 保留至 M0 完成并进入 main |
| `codex/cad-context-v2` | `3b0bff0` | 干净；P1 权威来源 | M0 完成、打标签和重冻候选前保留 |
| `codex/bridge-client-net45` | `8a4ee57` | 干净；P0 实机权威来源 | M0/P0 证据完成归档前保留 |
| `codex/kimi-palette-ui` | `7f10d60` | 干净；用户明确暂停 | 保留，不由 M0 删除 |

## M0 完成后可优先移除 Worktree

这些 HEAD 已是 P1/main 祖先，且当前工作树干净。移除 Worktree 后分支仍可保留：

| 分支 | HEAD | 依据 |
| --- | --- | --- |
| `codex/agenthost-live2016` | `a82ea66` | 已进入 P1 |
| `codex/cad-context-contract-v1` | `336f190` | 已进入 P1 |
| `codex/selection2016-readonly-v2` | `c9280f3` | 已进入 main 与 P1 |

## 禁止删除：存在未提交或未跟踪内容

| 分支 | HEAD | 未提交内容摘要 |
| --- | --- | --- |
| `codex/host2016-unified-readonly-mvp` | `e596bb6` | 多份 handoff 文档修改 |
| `codex/mimo-v2-agent-protocol` | `589c8ea` | `.mimocode/`、`BRIDGE_V2_HANDOFF.md` |
| `codex/mimo-v2-contract-hardening` | `e7e2a70` | `CAD_CONTEXT_V2_TEST_HARDENING.md` |
| `codex/mimo-v2-r201-semantics` | `50f6cf3` | Probe/Specs 修改、语义验证器、evidence 和 handoff |
| `codex/mimo-v2-runtime-test-kit` | `39383fe` | runtime report 模板、验证脚本和 Specs 修改 |

上述内容必须逐项审查、提交/吸收或由用户明确放弃后，才能移除 Worktree。

## 干净但存在 P1 非祖先提交：先审查再决定

| 分支 | HEAD | 当前判断 |
| --- | --- | --- |
| `codex/collector-discovery-hardening` | `083f5f1` | 有独立失败回归提交；可能已由后续 Location 集成替代，需比较 |
| `codex/mimo-collector-location-integration` | `c1e3cac` | patch-equivalent 已进入 P1；分支另含 main 文档祖先 |
| `codex/mimo-p0-stop-evidence-v2` | `d03e824` | P0 evidence 候选；需与 `8a4ee57`/P1 evidence 比较 |
| `codex/mimo-v2-capability-failclosed` | `7d26638` | 需确认是否已被 P1 `MvpAgentCapabilityPolicy` 覆盖 |
| `codex/mimo-v2-compat-harness` | `89045b9` | 含两个非祖先测试提交，需复核是否仍有增量价值 |
| `codex/mimo-v2-contract-adversarial` | `e606640` | adversarial 测试提交未成为 P1 祖先 |
| `codex/mimo-v2-contract-limit-gaps` | `4956ccb` | limit-gap 测试提交未成为 P1 祖先 |
| `codex/mimo-v2-host-r201` | `bdede8e` | R20.1 Host 候选提交未成为 P1 祖先 |
| `codex/mimo-v2-probe-evidence` | `a791210` | Probe evidence 提交未成为 P1 祖先 |
| `codex/mimo-v2-r201-signatures` | `d72df8a` | R20.1 signature 提交未成为 P1 祖先 |

## 当前结论

- M0 期间不删除任何 Worktree。
- M0 完成后，先移除三个“已进入 P1/main 且干净”的 Worktree。
- Kimi、主工作树和所有脏 Worktree继续保留。
- 干净但非祖先分支需要一次差异审计；不能只因 MiMo 已报告完成就删除。
