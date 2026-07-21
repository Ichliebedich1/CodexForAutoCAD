# Codex for AutoCAD

公司内部使用的 AutoCAD 原生 Codex 侧边栏。目标版本为 AutoCAD 2016 x64 与 AutoCAD 2025 x64。

当前实施优先适配 AutoCAD 2016：进程内保持 `net45` x64 薄宿主，Agent/Sandbox 运行在进程外 .NET 8；AutoCAD 2025 保留为次要目标。完整产品的目标安全边界包括：

- 原生 WPF `PaletteSet` 面板与只读 CAD 上下文；
- 本机认证 Bridge 与进程外 `codex app-server`；
- 版本化 CAD 上下文/操作契约、HMAC、序号、nonce 与防重放；
- 预览、一次性 CAD 审批、`DocumentLock` 内重校验、单事务和单次 Undo；
- 不自动保存，Shell、文件、网络和 CAD 写入默认拒绝。

以上是目标边界，不代表当前全部能力已经接通。实际完成状态和真机证据以 `handoff/autocad2016/CURRENT_STATE.md`、`handoff/autocad2016/README_FIRST.md` 及对应阶段证据为准。

## 当前状态（2026-07-21）

以下结论严格区分真实 AutoCAD 2016 运行证据、自动化验证和仅存在于代码中的候选能力。

### 已在原版 AutoCAD 2016 中运行通过

- `net45`/x64 Host 使用目标机原版 R20.1 托管程序集构建，并由用户人工 `NETLOAD`。
- 原生 `PaletteSet` 已验证打开、停靠、浮动、隐藏重开、重建、中文输入与换行；已验证样本为 96 DPI。
- 统一只读 Host 已从真实选择集读取受支持图元，生成 `CadContextJson v1`，在 Palette 显示摘要与 canonical JSON；捕获、清除和 Palette 重建过程中 `DBMOD` 保持不变。
- Agent MVP `0.3.1`（提交 `7f10d60`）已实机验证：一条 `Line` 经
  `CadContextJson v1 -> Palette -> 认证 AgentHost -> 本机 Codex` 返回回答，并在同一 Codex thread 中完成两轮连续对话。

这些证据证明了受支持图元的最小只读 AI 链路，不等于完整对象覆盖、稳定发布版或 CAD 写入支持。

### 已实现基础，但尚未进入实机产品链

- AgentHost 停止生命周期候选 `0.3.2` 尚未完成 AutoCAD 2016 实机验证，也尚未形成阶段提交；失败后的重复 `STOP` 状态仍有误报“已停止”的风险。
- `CadContextJson v2` 已完成 19 种强类型对象和 3 种受限占位的契约基础；Contracts net45/net8 为 `71/71`，Phase 2 总门禁为 `231/231`，相关基础提交截至 `50f6cf3`。
- 当前 Host.2016 产品运行时、Palette 文案和 Agent Client 仍使用 v1。v2 捕获、状态、能力协商和 `StartTurnV2Async` 尚未接入并完成真实认证端到端验证。

### 仍未完成

- `0.3.2` 的启动/停止双循环、异常路径、无残留进程和 `DBMOD` 不变的实机收口。
- v2 统一 Host 产品接入、原版 R20.1 Release 构建、冻结 DLL，以及“多个支持对象 + 一个未知对象 + Codex 回答”的人工 `NETLOAD` 验证。
- AutoCAD 退出、Agent 异常退出/断线/超时/取消、文档切换和 125%/150% DPI 等稳定性验收。
- CAD 写入的预览、一次性审批、锁内重校验、单事务、Undo/回滚和不自动保存的 AutoCAD 2016 端到端验证。
- 完整 OS 沙箱、长期记忆、审计链、签名、安装和企业发布验收。

旧审计中的 `25%` 结论已经失效；项目进度不得按代码量或旧百分比判断，只能按上述实际运行证据逐项更新。未经原版 R20.1 编译和用户人工 `NETLOAD`，不得宣称某个新候选支持 AutoCAD 2016。

## 本地构建

```powershell
dotnet build Codex.AutoCAD.sln
dotnet run --project tests/Codex.AutoCAD.Contracts.Specs
```

主解决方案默认构建托管核心、AgentHost、Bridge、AgentRuntime 和全部 Specs；两个进程内 CAD Host 都按目标版本独立构建，避免某一版本未安装时破坏核心构建。

AutoCAD 2025 Host 保留在主解决方案中但不参与默认 Build。目标机提供原版托管程序集后，直接构建项目并传入 `AutoCad2025Dir`。

AutoCAD 2016 Host 位于独立解决方案 `Codex.AutoCAD.2016.sln`，并由专用脚本使用经典 MSBuild、目标机原版程序集和隔离输出验证：

```powershell
.\scripts\verify-autocad2016-host.ps1 `
  -AutoCad2016Dir 'D:\AutoCAD 2016' `
  -Configuration Release `
  -MsBuildPath 'D:\DevTools\VS2022BuildTools\MSBuild\Current\Bin\MSBuild.exe'
```

Host.2016 必须保持 `net45`/x64，Autodesk 引用保持 `Private=false`。net45 参考程序集由仓库内经过哈希、签名和锁文件验证的离线 NuGet 包恢复，不读取用户或网络 NuGet 源；Autodesk DLL 不提交到仓库，也不复制到插件输出。

构建或 Specs 通过只证明对应的静态/自动化门禁，不替代 AutoCAD 2016 人工 `NETLOAD`。Codex 不启动、唤醒、关闭、重启或操作 AutoCAD；实机步骤由用户在现有 CAD 环境中执行。

## 安全不变量

1. 模型不能向活动 AutoCAD 发送命令字符串、LISP、脚本或任意 API 名称。
2. 活动 DWG 只能通过强类型操作计划、预览、一次性审批和事务修改。
3. CAD 写审批不能使用会话级永久授权。
4. 插件不自动保存或覆盖 DWG。
5. 断线、超时、图纸修订变化或结果不确定时默认拒绝并停止写入。
