# M9.1 Windows 托管核心 CI 检查点

最后更新：2026-07-27（北京时间）

## 目标

从 M4.16 干净候选提交
`cef82772bbafebd161f5c9d3af0c3aa32ddd0084` 建立独立 M9 后继线，使每个提交在
GitHub `windows-2022` Runner 上同时接受 PowerShell 7 与 Windows PowerShell 5.1 的托管核心
门禁，并且不把 GitHub Runner 缺少本机 Codex、凭据和 Autodesk 安装误写成已验证。

## 真实调用链

- `.github/workflows/windows-core.yml`
  - `windows-2022`
  - `pwsh` / `powershell` 双矩阵
  - `scripts/verify-m9-windows-ci.ps1`
  - `scripts/verify-build-safety.ps1`
  - `scripts/verify-phase2.ps1 -SkipLiveCodexHandshake`
  - `scripts/verify-m9-net45-x64.ps1`
  - `scripts/verify-m9-sbom-and-licenses.ps1`
- `verify-phase2.ps1` 默认不跳过 doctor；CI-only 开关只跳过本机 AgentHost/Codex doctor。
- `verify-all-gates.ps1` 禁止引用 CI-only 开关，仍保持正式 readiness 的真实本机 doctor。

## 供应链与权限边界

- `actions/checkout` 固定到
  `11bd71901bbe5b1630ceea73d27597364c9af683`（v4.2.2）。
- `actions/setup-dotnet` 固定到
  `67a3573c9a986a3f9c594539f4ab511d57bb3ce9`（v4.3.1）。
- 工作流权限只有 `contents: read`。
- checkout 使用 `persist-credentials: false`。
- SDK 由 `global.json` 固定为 `8.0.319`。
- 产物根使用 `${{ runner.temp }}`，并显式设置
  `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0`。
- 本机脚本默认磁盘门槛仍为 `40 GiB`；工作流只对标准 Runner 临时卷显式传入
  `-MinimumFreeGiB 5`，防止 40 GiB 本机门槛让标准 Runner 在首步必然失败。
- Phase 2 显式传入仓库内 `<clear />`、单一离线 feed、强制签名验证的
  `src/Codex.AutoCAD.Host.2016/NuGet.Config`，不合并 Runner 用户 NuGet 配置。
- `verify-m9-net45-x64.ps1` 用同一离线配置构建 AgentLauncher、Bridge.Client、
  Contracts 和 IPC 的 `net45/x64` 产物，并验证 PE Machine 为 `0x8664`；临时 cache、
  lock 和输出都在 E 盘/Runner 临时产物根。
- 工作流不得访问 Secrets、`CODEX_HOME`、AutoCAD、NETLOAD、Core Console 或 CAD 写入。

## 本地验证

- 工作流定义门禁：
  - PowerShell 7：通过。
  - Windows PowerShell 5.1：通过。
  - 双 Shell 负向自检 `17/17`：除原有 `contents: write`、可移动 Action 标签、持久化
    checkout 凭据、`pull_request_target`、系统盘产物、缺少 CI-only 边界和正式统一门禁
    跳过 doctor 外，额外只读权限、job 级 `write-all`、方括号 Secrets、local action、
    缺失矩阵 Shell、`continue-on-error`、尾接 `exit 0`、额外 job、短格式额外 run 和取消
    Runner 磁盘门槛也全部被拒绝。
- CI-only Phase 2：
  - PowerShell 7：Release `0 warning / 0 error`，`469/469`。
  - Windows PowerShell 5.1：Release `0 warning / 0 error`，`469/469`。
- net45/x64 托管边界：
  - PowerShell 7：4 个 AMD64 程序集，`0 warning / 0 error`。
  - Windows PowerShell 5.1：4 个 AMD64 程序集，`0 warning / 0 error`。
  - 该结果不包含依赖 Autodesk 程序集的 Host.2016；R20.1 Host 仍由本机正式门禁验证。
- 默认 Phase 2：
  - PowerShell 7：本机 Codex `0.144.4` doctor 通过，`469/469`。
- build-safety：
  - 双 Shell 通过。
  - PowerShell 文件 `34`、C# 文件 `173`、`DOTNET_CLI_HOME` 位置 `25`、违规 `0`。
  - User PATH 长度 `661`、条目 `13`、SHA-256
    `05DF0D2FFC86D41186216560D37CC16FA0159ED5CEF9A89F61042964C196BE59`。
- SBOM/许可证：
  - 双 Shell 通过。
  - 外部组件 `1`、内部项目 `4`、锁文件 `5`。

## 提交后状态

- 本检查点已按精确 10 文件边界提交为
  `9afaaafcdf24028d984bd1b3ca81a5ea013e59ba`。
- 提交后双 Shell 工作流定义自检和 build-safety 复核通过，Worktree 干净。
- 后继 M9.2 Worktree 对工作流增加工具链锁步骤；这属于新的未提交检查点，不回写为
  M9.1 提交当时已经具备的能力。

## 明确未验证

- 当前提交尚未推送。
- GitHub Actions 远端 workflow run 尚不存在。
- CI 没有执行本机 Codex doctor、R20.1 Host 构建、候选打包或 AutoCAD 实机。
- M9.3 尚缺 AgentLauncher Specs、manifest 和候选 doctor 的远端汇总；当前 469 项不能冒充
  完整 M9.3。
- M9.4 覆盖率、M9.5 属性/模糊测试、M9.6 soak、M9.7 性能回归、漏洞库与人工 IL 审查仍未完成。
- 该检查点不改变 M4、M5 或 CAD 写入状态。

## 下一步

1. 经用户授权后推送分支并观察两个远端 job；没有远端 run 不得把 M9.1 标成完成。
2. 先完成独立 M9.2 工具链锁检查点，再设计 M9.3 动态汇总和 M9.4 覆盖率门禁，避免把
   现有 console specs 误当作 VSTest 覆盖率。
