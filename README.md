# Codex for AutoCAD

公司内部使用的 AutoCAD 原生 Codex 侧边栏。目标版本为 AutoCAD 2016 x64 与 AutoCAD 2025 x64。

当前实施优先适配 AutoCAD 2016：进程内保持 `net45` x64 薄宿主，Agent/Sandbox 运行在进程外 .NET 8；AutoCAD 2025 保留为次要目标。完整产品的目标安全边界包括：

- 原生 WPF `PaletteSet` 面板与只读 CAD 上下文；
- 本机认证 Bridge 与进程外 `codex app-server`；
- 版本化 CAD 上下文/操作契约、HMAC、序号、nonce 与防重放；
- 预览、一次性 CAD 审批、`DocumentLock` 内重校验、单事务和单次 Undo；
- 不自动保存，Shell、文件、网络和 CAD 写入默认拒绝。

以上是目标边界，不代表当前全部能力已经接通。实际完成状态和真机证据以 `handoff/autocad2016/CURRENT_STATE.md`、`handoff/autocad2016/README_FIRST.md` 及对应阶段证据为准。

## 当前状态（2026-07-22）

当前已在原版 AutoCAD 2016 R20.1 中建立 `0.3.2.0` 的 CadContextJson v2 只读 AI
实机基线，并已完成 M2-A `0.4.0.0` 图纸级只读索引的自动化候选冻结；`0.3.3.0` M1
稳定化候选仍等待精确哈希实机绑定：

- Host/Doctor、Palette 和 v2 schema 已人工 `NETLOAD` 运行。
- 100% DPI 下的打开、停靠、浮动、隐藏重开、重建、中文输入和布局由用户确认通过。
- 一个 50 对象混合选区成功发布：44 个强类型实体、6 个受限 placeholder，
  `jsonBytes=23142`，`DBMOD 21 -> 21`；未知对象没有使整组选区失败。
- 本机 Codex 使用当前 v2 CAD 上下文完成两轮连续对话。
- M2-A 新增 `codex.autocad.drawing-index/1` 和 `codex.autocad.cad-query/1`，支持
  Selection/Current/Model/Layouts/Drawing 分片索引、受限占位、状态失效和游标分页。
- M2-A 保留 CadContext v2 的 64 实体/256 KiB 选择快照边界；整图索引不把整图 JSON 一次
  发送到 Codex。
- 显式 CAD 上下文清除和文档激活清除旧缓存通过；CAD 写入和插件保存仍禁用。
- P0 停止生命周期已有独立实机证据：重复 STOP、DBMOD 不变和 AgentHost 残留为零。
- M1 已实现 Bridge 断线 fail-closed、结构化脱敏错误、request_id/唯一终态、幂等取消、
  10 分钟回合超时、新建对话、清除全部和按图纸隔离对话；图纸切换会立即清空旧回答。
- `0.3.3.0` 冻结候选为
  `artifacts/autocad2016-m1-readonly-v033-c3478920-a47d86a6-7fc17895/`；Host SHA-256
  前缀 `C3478920`、AgentHost 前缀 `A47D86A6`、manifest 前缀 `2702D4F1`。
- 该候选通过 Host MVP `40/40`、完整 Phase 2 `275/275`、Host.2016 只读 Compile 闭包、
  R20.1/net45/x64 双构建位级一致、敏感信息扫描和候选包自身 AgentHost doctor。

M2-A `0.4.0.0` 自动化候选为：

```text
C:\tmp\CodexForAutoCAD-m2-drawing-index\artifacts\autocad2016-m2-drawing-index-v040-2cfbadd8-4028850a-8af00fa8
Host SHA-256: 2CFBADD8FF57F6DAAA4727F1B6DE871D509B92E47A680ECCA669A024CBA786A5
AgentHost SHA-256: 4028850AD9B9EECB8812B07CF3C401AE5287744D839AE66C57AD193C1DB3CE0C
Manifest SHA-256: 3CF194EB69B8C33E8D6B3C7B7D33838D6CB847036819CAC074D9DB7E1AFEF20A
```

它通过 Contracts `83/83`、完整 Phase 2 `287/287`、R20.1/net45/x64 Host 构建、A/B
位级一致和只读扫描；尚未在 AutoCAD 2016 中按精确哈希 `NETLOAD`。

`0.3.2.0` 脱敏实机范围证据见
`handoff/autocad2016/evidence/cad-context-v2-live-observation-20260722.json`；`0.3.3.0`
自动化冻结证据见
`handoff/autocad2016/evidence/cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-c3478920-a47d86a6-7fc17895.json`。
后者尚未在 AutoCAD 中按精确哈希 `NETLOAD`，不能继承前者的实机结论。

当前选择快照仍有明确的 `64` 实体和 `256 KiB` canonical JSON 上限。M2-A 没有简单放大
常量，而是保留 v2 兼容快照并新增独立整图扫描、索引和分页查询。

当前活动路线为：

1. M0 已完成 P0/P1 受控集成、自动化复验和统一候选冻结；精确结果见
   `handoff/autocad2016/M0_BASELINE_RELEASE_20260722.md`。
2. M1 代码、自动化和 `0.3.3.0` 候选冻结已完成，精确候选实机矩阵仍待用户绑定。
3. M2-A 图纸级索引垂直切片已完成自动化候选；实机入口为
   `handoff/autocad2016/M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`。
4. M2-B 将把同一索引通过现有认证 Bridge 接入 Codex 结构化只读查询；不复制扫描器。
5. M2 完成后再做 M3 对象语义、M4 沙箱审计，随后才启用 AutoCAD 2016 强类型安全写入。
6. 随后完成长期记忆、安装签名、企业部署和 AutoCAD 2025 适配。

完整阶段与完成定义见 `handoff/autocad2016/LONG_TERM_MEMORY_TODO.md`。

Provider-neutral 抽象、Direct API Provider 和自研 Agent Loop 已冻结，不属于当前产品
结束条件；除非用户以后单独重新立项，不创建对应空接口或第二套调用链。

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

构建或 Specs 通过只证明对应的静态/自动化门禁，不替代 AutoCAD 2016 人工 `NETLOAD`。M2-A
候选的完整 DLL、依赖和人工步骤见 `M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md` 与
`M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`。Codex 不启动、唤醒、关闭、重启或操作
AutoCAD；实机步骤由用户在现有 CAD 环境中执行。

## 安全不变量

1. 模型不能向活动 AutoCAD 发送命令字符串、LISP、脚本或任意 API 名称。
2. 活动 DWG 只能通过强类型操作计划、预览、一次性审批和事务修改。
3. CAD 写审批不能使用会话级永久授权。
4. 插件不自动保存或覆盖 DWG。
5. 断线、超时、图纸修订变化或结果不确定时默认拒绝并停止写入。
