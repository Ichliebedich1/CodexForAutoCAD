# M4.15 实机矩阵结果契约

最后更新：2026-07-27（北京时间）

## 目的

权威目标允许 M4.15 的九项真实机器/企业矩阵采用两种明确处置：

- `verified`：绑定当前候选并取得真实证据；
- `deferred`：只允许除 `RealAbnormalExitMatrixVerified` 外的八项，必须写明理由，并同时约定
  在 M9 发布门禁和 M10 企业部署前重新评估。

`RealAbnormalExitMatrixVerified` 是进入 M5 CAD 写入的硬前置，不能延期。自动化测试、FakeAgentHost
或只强杀 AgentHost 都不能替代真实 AutoCAD、AgentHost、Codex 三类强制终止矩阵。

实际结果文件固定为：

```text
handoff/autocad2016/live-matrix-results.json
```

当前仓库故意不创建该文件，因为真实异常退出矩阵尚未执行。不得用示例值、自动化结果或人工猜测
生成一份看似通过的文件。

## 固定 schema

顶层字段：

```json
{
  "SchemaVersion": 1,
  "Scope": "m4-live-matrix-results",
  "Candidate": {
    "HeadCommit": "<40 位小写 Git commit>",
    "R201HostDllSha256": "<64 位大写 SHA-256>",
    "AgentHostDllSha256": "<64 位大写 SHA-256>"
  },
  "Items": []
}
```

`Candidate` 的三个值必须与同一次 M4 readiness evidence 完全一致。换提交、重编译或候选哈希变化
后，旧实机结果不得继续使用。

## 两提交模型

实机结果只能在候选已经构建和测试后产生，因此结果文件不可能把自己的提交哈希写进自己。固定
流程为：

1. 提交并清理候选源码，记为提交 C；
2. 从 C 运行统一门禁并生成 readiness，实机只测试 C 对应的 Host/AgentHost；
3. 写入 `live-matrix-results.json`，其中 `Candidate.HeadCommit` 必须是 C；
4. 单独提交该 JSON，形成 evidence-only 提交 E；E 相对 C 只能新增或修改这一份文件；
5. 回滚 ref 必须指向真正测试过的 C，不是 E；
6. 从干净的 E 运行冻结前置检查。

脚本会验证 C 是 E 的祖先，且 `C..E` 的 name/status 差异精确只有
`handoff/autocad2016/live-matrix-results.json`。若夹带源码、脚本、其他文档、重命名或删除，
冻结失败。这样既保存实机结果，又不产生“文件内容必须包含自身提交哈希”的循环依赖。

`Items` 必须精确包含以下九个 ID，不能缺少、重复或增加：

1. `RealCredentialManagerVerified`
2. `RealCodexLoginAndKeyringVerified`
3. `RealRestrictedTokenProductChainVerified`
4. `RealFixedCapacityVolumeVerified`
5. `RealDiskFullVerified`
6. `RealPowerLossVerified`
7. `RealAbnormalExitMatrixVerified`
8. `EnterpriseAppLockerWacEdRMatrixVerified`
9. `EnterpriseRetentionArchiveMatrixVerified`

schema 是严格白名单：顶层、`Candidate`、每个 item 和异常退出 `Outcome` 都不允许额外字段。
不要加入事件日志原文、路径、PID、机器名、账户、域、证书、策略 GUID、URL、邮件地址或秘密。
`Reason` 会拒绝控制/双向字符、路径、UNC、URI、邮件地址、环境变量和常见 secret 键值形态。
`EvidenceSha256` 必须来自真实脱敏报告，64 个相同十六进制字符等占位值会被拒绝。
文件必须是普通非 reparse 文件、严格 UTF-8、有效 JSON，大小为 2–65,536 字节；校验器使用
共享只读锁一次读入，并对同一字节序列解析和计算 SHA-256。布尔值必须是真正 JSON boolean，
计数必须是真正 JSON integer，不能用 `"false"`、`"0"` 等字符串冒充。

普通 `verified` 项至少包含：

```json
{
  "Id": "RealCredentialManagerVerified",
  "Disposition": "verified",
  "EvidenceSha256": "<脱敏实机结果文件的 64 位大写 SHA-256>"
}
```

普通 `deferred` 项必须包含单行、最多 256 字符的理由，以及两个重评点：

```json
{
  "Id": "RealCredentialManagerVerified",
  "Disposition": "deferred",
  "Reason": "测试机暂不具备企业凭据环境；不影响 M5 的异常退出保护。",
  "ReassessAt": ["M9", "M10"]
}
```

`RealAbnormalExitMatrixVerified` 只接受以下 `verified` 形态：

```json
{
  "Id": "RealAbnormalExitMatrixVerified",
  "Disposition": "verified",
  "EvidenceSha256": "<脱敏实机结果文件的 64 位大写 SHA-256>",
  "Outcome": {
    "AutoCadForcedTerminationVerified": true,
    "AgentHostForcedTerminationVerified": true,
    "CodexForcedTerminationVerified": true,
    "UniqueTerminal": true,
    "ResidualProcessCount": 0,
    "SubsequentRequestsFailClosed": true,
    "SensitiveDataExposed": false
  }
}
```

## 验证入口

先从候选提交 C 生成统一 9/9 gate evidence，再准备真实矩阵文件和指向 C 的人工回滚 ref，并按
上面的两提交模型形成 evidence-only 提交 E。
随后运行：

```powershell
pwsh -NoProfile -NonInteractive -File .\scripts\verify-m4-16-freeze-preconditions.ps1 `
  -ReadinessEvidencePath <同一次门禁的 m4-readiness.json> `
  -LiveMatrixResultsPath .\handoff\autocad2016\live-matrix-results.json `
  -RollbackRef <由用户确认并创建、指向候选提交 C 的 ref> `
  -EvidencePath <build-safety Worktree 产物根>\gate-evidence\m4-16-freeze-preconditions.json
```

脚本只读 Git ref，不创建、移动或删除 ref；不启动 AutoCAD，不启用 CAD 写入或插件保存。输出
schema 2 会分别列出 `Verified` 与 `Deferred`，并以 `LiveMatrixSha256` 绑定原始处置文件。
`EvidencePath` 必须是 build-safety 已选定 Worktree 产物根下的 `.json`，不能写入系统盘任意目录、
仓库或其他 Worktree。

当前预期结果仍是 `freeze_refused`：真实异常退出尚未验证、实际矩阵文件尚不存在，也尚未由用户
建立冻结回滚点。这是正确的 fail-closed 状态。

2026-07-27 已在独立临时 Git 仓库完成正向契约演练：候选提交 C、仅新增
`live-matrix-results.json` 的提交 E、指向 C 的回滚 ref、1 项 verified + 8 项 deferred，
最终得到 `preconditions_met`、`BlockerCount=0`、`M4Complete=true`，同时继续
`M416Frozen=false`。该演练只使用 synthetic 数据，证明判定路径可达，不是项目实机矩阵证据。
