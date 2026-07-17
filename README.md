# Codex for AutoCAD

公司内部使用的 AutoCAD 原生 Codex 侧边栏。目标版本为 AutoCAD 2016 x64 与 AutoCAD 2025 x64。

当前实施阶段聚焦 AutoCAD 2025 的安全纵向闭环：

- 原生 WPF `PaletteSet` 面板；
- 本机 `codex app-server` 进程协议；
- 版本化 CAD MCP 契约；
- 预览、一次性审批、`DocumentLock`、事务和单次 Undo；
- 默认拒绝的 Shell、文件、网络和 CAD 写入策略。

## 本地构建

```powershell
dotnet build Codex.AutoCAD.sln
dotnet run --project tests/Codex.AutoCAD.Contracts.Specs
```

AutoCAD 2025 API 默认从本机安装路径读取，也可通过 `AutoCad2025Dir` MSBuild 属性覆盖。

AutoCAD 2016 工程将在 `EnableAutoCad2016=true` 且提供 `AutoCad2016Dir` 与完整 .NET Framework 4.5 参考程序集时启用。Autodesk DLL 不提交到仓库。

## 安全不变量

1. 模型不能向活动 AutoCAD 发送命令字符串、LISP、脚本或任意 API 名称。
2. 活动 DWG 只能通过强类型操作计划、预览、一次性审批和事务修改。
3. CAD 写审批不能使用会话级永久授权。
4. 插件不自动保存或覆盖 DWG。
5. 断线、超时、图纸修订变化或结果不确定时默认拒绝并停止写入。
