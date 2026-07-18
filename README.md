# Codex for AutoCAD

公司内部使用的 AutoCAD 原生 Codex 侧边栏。目标版本为 AutoCAD 2016 x64 与 AutoCAD 2025 x64。

当前实施优先适配 AutoCAD 2016：进程内保持 `net45` x64 薄宿主，Agent/Sandbox 运行在进程外 .NET 8；AutoCAD 2025 保留为次要目标。完整产品的目标安全边界包括：

- 原生 WPF `PaletteSet` 面板与只读 CAD 上下文；
- 本机认证 Bridge 与进程外 `codex app-server`；
- 版本化 CAD MCP 契约、HMAC、序号、nonce 与防重放；
- 预览、一次性 CAD 审批、`DocumentLock` 内重校验、单事务和单次 Undo；
- 不自动保存，Shell、文件、网络和 CAD 写入默认拒绝。

以上是目标边界，不代表当前全部能力已经接通。实际完成状态与真机证据分别以 `docs/phase2-security-status.md` 和 `handoff/autocad2016/README_FIRST.md` 为准；在 Palette、真实 Agent/Bridge、审批事务写入和发布验收完成前，不得宣称完整支持 AutoCAD 2016。

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

## 安全不变量

1. 模型不能向活动 AutoCAD 发送命令字符串、LISP、脚本或任意 API 名称。
2. 活动 DWG 只能通过强类型操作计划、预览、一次性审批和事务修改。
3. CAD 写审批不能使用会话级永久授权。
4. 插件不自动保存或覆盖 DWG。
5. 断线、超时、图纸修订变化或结果不确定时默认拒绝并停止写入。
