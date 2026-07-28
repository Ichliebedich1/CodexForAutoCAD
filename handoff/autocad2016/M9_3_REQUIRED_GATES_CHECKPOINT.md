# M9.3 必过门禁动态汇总检查点

最后更新：2026-07-27（北京时间）

## 目标

在 M9.1 Windows CI 与 M9.2 工具链锁之上，建立一个可复现的本地最终入口，覆盖
Contracts、IPC、Bridge、Launcher、AppServer、Runtime、Host MVP、Security、禁用 API、
秘密扫描、候选 manifest 和 candidate AgentHost doctor。测试总数必须来自同一次运行的
结构化 evidence，不能依赖会随代码增长而过期的硬编码总数。

本阶段只读取经审查的 AutoCAD 2016 R20.1 程序集并生成只读候选；不启动或控制 AutoCAD，
不执行 NETLOAD，不启用 CAD 写入，不改变 M4/M5 状态。

## 审计发现

1. 原 `verify-all-gates.ps1` 是 M4 readiness 和候选打包器已经消费的相关联 `9/9`
   套件，直接改变其门禁数量会破坏冻结契约。
2. Host ReadOnlyContext `25` 项、Host V2 `16` 项和 AgentService `7` 项规格已经存在，
   但没有全部进入正式 solution、Phase 2 或 Agent bootstrap evidence。
3. AgentService 的 bootstrap-serve 生命周期必须接收 FakeAgentHost 路径，不能把普通
   Launcher 启停规格当成该调用链的替代证明。
4. SDK artifacts 输出规则对单目标项目使用 `release`，对多目标项目使用
   `release_<tfm>`；验证器必须使用实际项目输出规则。
5. 候选 manifest/doctor 只能在一个干净提交上与 suite、readiness、源码提交和候选哈希
   一一绑定，脏工作树上的局部测试不能冒充最终 M9.3 evidence。

## 实现

- `Codex.AutoCAD.sln`
  - 纳入 Host ReadOnlyContext、Host V2 和 AgentService 三个规格项目。
- `scripts/verify-phase2.ps1`
  - 动态运行 11 个规格项目，不保存固定总数。
  - Host ReadOnlyContext 摘要统一为可解析的 `N/N specs passed`。
- `scripts/verify-autocad2016-agent-bootstrap.ps1`
  - 隔离还原和构建 AgentService Specs。
  - 先还原单目标 net8 AgentService，再还原双目标 Launcher Specs，防止共享
    `project.assets.json` 丢失 net45 target。
  - 运行 7 项 FakeAgentHost bootstrap-serve 生命周期规格。
  - evidence schema 升为 17，记录两组规格 ID、动态摘要和
    `BootstrapServeLifecycleVerified=true`。
- `scripts/verify-m4-automated-readiness.ps1`
  - 严格消费 schema 17、AgentService 摘要和 bootstrap-serve 布尔值。
  - Phase 2 改为验证精确 11 项名称集合，不依赖旧项目数量。
- `scripts/verify-m9-required-gates.ps1`
  - 要求干净已提交工作树、AutoCAD 未运行和明确的 R20.1 输入目录。
  - 依次运行 M9.1 工作流定义、M9.2 完整工具链锁、net45/x64、M4 相关联套件和
    M4 live 只读候选打包。
  - 严格验证 JSON 类型、精确项目/门禁集合、Run ID、源码提交和每份 evidence SHA-256。
  - 重新哈希候选 manifest，并验证其 source/suite/readiness 绑定与 CAD 写入关闭状态。
  - 动态求和 Phase 2、Launcher 和 AgentService 的唯一逻辑规格；重复的跨 Shell/门禁
    运行不重复计入唯一总数。
  - 输出 `codex.autocad.m9-required-gates/1`，明确保留远端 CI、NETLOAD、M4 实机矩阵、
    M4.16 冻结和 CAD 写入均未验证。
- `scripts/verify-m9-windows-ci.ps1`
  - 静态验证 M9.3 汇总器确实消费工具链、net45/x64、相关联套件、候选打包和动态求和契约。

## 当前自动化结果

- M9.3 聚合器自检：
  - PowerShell 7：通过，11 个 Phase 2 项目、12 个覆盖类别。
  - Windows PowerShell 5.1：通过，结果一致。
- Windows CI、M4 readiness、统一门禁自检：
  - 双 Shell 全部通过。
- Phase 2 CI-only 当前局部运行：
  - 11 个项目，`510/510`。
  - Release solution build：0 warning / 0 error。
  - Host 禁用 API、Host.2016 CAD 写入关闭、Git diff 和基础秘密扫描通过。
- Agent bootstrap：
  - net8 Launcher：`65/65`。
  - net45 Launcher：`65/65`。
  - AgentService bootstrap-serve：`7/7`。
  - 双隔离构建：0 warning / 0 error，规格运行后输出树仍一致。
  - 新增相关残留进程：0。
- M9.2 正式输入：
  - 15 文件工具链锁已在独立提交
    `1e969e2da702af459e1a76b9df0b7c58b49425cb` 冻结。
- M9.3 临时提交完整验证：
  - 项目分支基于 M9.2，验证前精确增量为 10 个修改和 2 个新增。
  - 临时 validation 分支提交
    `34f842dee33d447812acaeda8583d80e3c6e9214` 仅用于物化未提交实现。
  - 未传入 `MinimumFreeGiB`，按脚本生产默认 `40 GiB` 运行完整入口。
  - Windows CI definition、完整 M9.2 工具链锁、R20.1 输入、双缓存复现、
    net45/x64、相关联套件 `9/9`、SBOM/许可证以及候选 manifest/doctor 全部通过。
  - Phase 2 `510/510`、Launcher net8/net45 各 `65/65`、AgentService `7/7`；
    12 个覆盖类别均为 true，动态唯一逻辑规格为 `582`。
  - 新增相关残留进程 0、AutoCAD 未启动、`M4Complete=false`、`M416Frozen=false`、
    `CadWriteEnabled=false`、`RemoteWorkflowRunVerified=false`。
  - 最终 evidence 位于
    `E:cfam9r-d072d224m9r-1e969e2am9-required-gatesm9-required-gates.json`，
    schema 为 `codex.autocad.m9-required-gates/1`，状态为 `required_gates_verified`，
    SHA-256 为 `9F2456A56BCBEE1DF504E8B6BDAD9DD784F8CB71FC66E62A06C106A89901AA25`。
- 构建期间用户 PATH SHA-256 前后均为
  `05DF0D2FFC86D41186216560D37CC16FA0159ED5CEF9A89F61042964C196BE59`。
- 本文档在完整验证之后刷新，因此临时提交证明的是刷新前的实现状态；更新后的项目工作树
  不再与 `34f842d` 逐字节完全一致，不能把该临时提交冒充正式 M9.3 evidence。

## 尚未完成

1. M9.3 当前仍是未提交 Worktree，临时 validation 提交不属于项目分支。
2. 正式 M9.3 提交后，必须从该精确提交按默认 `40 GiB` 再次运行完整入口并生成正式绑定
   evidence；当前 E 盘空间接近门槛，运行前必须先通过磁盘安全检查，不能降低门槛。
3. 分支未推送，GitHub Actions PowerShell 7/Windows PowerShell 5.1 真实远端 job 未运行。
4. 未执行 AutoCAD NETLOAD、M4 实机异常退出矩阵、M4.16 冻结或任何 CAD 写入。
5. M9.4 覆盖率、M9.5 属性/模糊测试、M9.6 并发/soak、M9.7 性能回归和后续供应链
   里程碑仍未完成。

## 后续顺序

1. 复核 M9.3 的 12 文件边界、双 Shell build-safety、Windows CI/required-gates 自检、
   M4 readiness/all-gates 自检、diff 和脱敏扫描。
2. 用户授权后形成独立 M9.3 项目提交，不夹带临时 validation 分支或历史产物。
3. 从正式精确 M9.3 提交按默认 `40 GiB` 运行完整 `verify-m9-required-gates.ps1`。
4. 推送后取得两个真实远端 Windows job，再评估 M9.1–M9.3 的完成状态。
