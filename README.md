# Codex for AutoCAD

公司内部使用的 AutoCAD 原生 Codex 侧边栏。目标版本为 AutoCAD 2016 x64 与 AutoCAD 2025 x64。

当前实施优先适配 AutoCAD 2016：进程内保持 `net45` x64 薄宿主，Agent/Sandbox 运行在进程外 .NET 8；AutoCAD 2025 保留为次要目标。完整产品的目标安全边界包括：

- 原生 WPF `PaletteSet` 面板与只读 CAD 上下文；
- 本机认证 Bridge 与进程外 `codex app-server`；
- 版本化 CAD 上下文/操作契约、HMAC、序号、nonce 与防重放；
- 预览、一次性 CAD 审批、`DocumentLock` 内重校验、单事务和单次 Undo；
- 不自动保存，Shell、文件、网络和 CAD 写入默认拒绝。

以上是目标边界，不代表当前全部能力已经接通。实际完成状态和真机证据以 `handoff/autocad2016/CURRENT_STATE.md`、`handoff/autocad2016/README_FIRST.md` 及对应阶段证据为准。

## 当前状态（2026-07-24）

当前已在原版 AutoCAD 2016 R20.1 中建立 `0.3.2.0` 的 CadContextJson v2 只读 AI
实机基线，并已完成 M2-A/M2-B `0.4.0.0` 图纸级只读索引、Codex 按需查询、确定性
1k/10k/50k 基准图和 Host 本地性能遥测的自动化候选冻结；`0.3.3.0` M1 稳定化候选仍等待
精确哈希实机绑定：

- Host/Doctor、Palette 和 v2 schema 已人工 `NETLOAD` 运行。
- 100% DPI 下的打开、停靠、浮动、隐藏重开、重建、中文输入和布局由用户确认通过。
- 一个 50 对象混合选区成功发布：44 个强类型实体、6 个受限 placeholder，
  `jsonBytes=23142`，`DBMOD 21 -> 21`；未知对象没有使整组选区失败。
- 本机 Codex 使用当前 v2 CAD 上下文完成两轮连续对话。
- M2-A 新增 `codex.autocad.drawing-index/1` 和 `codex.autocad.cad-query/1`，支持
  Selection/Current/Model/Layouts/Drawing 分片索引、受限占位、状态失效和游标分页。
- M2-B 已把同一索引以只读 `cad.query_drawing` 动态工具接入 AgentRuntime、认证
  AgentHost 反向 Bridge 和 Host；没有选择上下文时，只要存在有效索引也可 ASK。
- 查询绑定 Host 拥有的索引、文档和 revision 身份；Bridge 后台线程只读取纯托管冻结快照，
  不进入 Autodesk API。系统 request、Provider thread/turn 和 tool/query ID 保持分离。
- M2 保留 CadContext v2 的 64 实体/256 KiB 选择快照边界；整图索引不把整图 JSON 一次
  发送到 Codex。
- 三个 AC1009 脱敏 DXF fixture 在模型空间分别包含精确 1,000、10,000、50,000 个实体；
  双次生成、独立解析、哈希、拒绝覆盖和脱敏 evidence 记录门禁为 `6/6`。
- `CODEX16INDEXINFO` 现显示 Idle 分片次数/最大耗时、总扫描耗时、估算内存以及本地和
  Codex 反向查询耗时；遥测不扩展 DrawingIndex/CadQuery wire 契约。
- M3 `0.4.1.0` 开发纵切已在同一只读调用链中增加实际 placeholder 类型/原因统计：选择
  摘要、`CODEX16CTXINFO`、`CODEX16INDEXINFO` 和 Palette 不再只显示笼统的
  `unsupported` 数量，`CODEX16TYPEINFO` 还会输出 19 类现有强类型对象的中文目录。
  该版本尚未冻结候选、未取得实机 `NETLOAD`，不能继承 M2 或 P1 的实机结论。
- M3 当前自动门禁为 Contracts `85/85`、Host v2 net45/net8 各 `15/15`、Host MVP
  `53/53`、完整 Phase 2 `309/309`，R20.1 Release 为 0 warning / 0 error；禁止 API、
  AgentHost doctor、敏感信息和 diff 门禁均通过。同一依赖闭包下 Host A/B 逐字节一致，
  编译门禁 Host SHA-256 为
   `EC1C6174893DC47DABE5C55C28D020FD607881BE435DBBD1FE9079BC8C0DB51B`。这是
   `artifacts/m3-r201-compile-only-d67432c0d40d4aed8710f24b30602955/` 中 `compile-summary.json`
   记录的编译证据；不是冻结候选，没有候选 manifest 或 AutoCAD 实机证据。
- 显式 CAD 上下文清除和文档激活清除旧缓存通过；CAD 写入和插件保存仍禁用。
- P0 停止生命周期已有独立实机证据：重复 STOP、DBMOD 不变和 AgentHost 残留为零。
- M1 已实现 Bridge 断线 fail-closed、结构化脱敏错误、request_id/唯一终态、幂等取消、
  覆盖 Provider 启动阶段的 10 分钟总超时、新建对话、清除全部和按图纸隔离对话；
  图纸切换会立即清空旧回答。
- `0.3.3.0` 冻结候选为
  `artifacts/autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3/`；Host SHA-256
  前缀 `E6701A77`、AgentHost 前缀 `4B602965`、manifest 前缀 `B081B93A`。
- 该候选通过 Host MVP `41/41`、完整 Phase 2 `276/276`、Host.2016 只读 Compile 闭包、
  R20.1/net45/x64 双构建位级一致、敏感信息扫描和候选包自身 AgentHost doctor。

当前 M2 `0.4.0.0` 自动化候选由源码提交
`34cef1214ad22822996db4e4ad33013f855751e3` 精确生成：

```text
C:\tmp\CodexForAutoCAD-m2-integration\artifacts\autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024
Host SHA-256: BC6011D3C0C00222BE266E27A26770B87FC4CE542A9516640AEC1A959950C5D5
AgentHost SHA-256: 6DE30DB91C466CA0CA87E6202926FB893165CE8950B1CCAB9E0E3C49650CDD89
Manifest SHA-256: CDE0E31D9B2342B322D1850224B6DE78755B97EAEF7802C7D609F86E58E7D917
```

它通过 Contracts net8/net45 `88/88`、Bridge Client net8/net45 `29/29`、Bridge/AgentHost
`39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2 `314/314`、benchmark
fixture/evidence `6/6`、R20.1/net45/x64 Host A/B 位级一致、30 文件只读扫描和候选
AgentHost doctor。查询对象身份现为不泄露 AutoCAD Handle 的 `obj-########` 令牌；分页
使用 Host 随机生成、五分钟过期并绑定索引、revision、查询形状和 offset 的 `dq1_...`
游标。尚未在 AutoCAD 2016 中按精确哈希 `NETLOAD`；M2.3、M2.13、M2.14 仍未完成。
旧 `E85D97EC...` 和 `597A7A3D...` 候选仅保留为历史冻结点，均不是当前实机入口。

当前 M2 自动化冻结证据为
`handoff/autocad2016/evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`。

`0.3.2.0` 脱敏实机范围证据见
`handoff/autocad2016/evidence/cad-context-v2-live-observation-20260722.json`；`0.3.3.0`
自动化冻结证据见
`handoff/autocad2016/evidence/cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3.json`。
后者尚未在 AutoCAD 中按精确哈希 `NETLOAD`，不能继承前者的实机结论。

当前选择快照仍有明确的 `64` 实体和 `256 KiB` canonical JSON 上限。M2 没有简单放大
常量，而是保留 v2 兼容快照并新增独立整图扫描、索引和分页查询。

当前活动路线为：

1. M0 已完成 P0/P1 受控集成、自动化复验和统一候选冻结；精确结果见
   `handoff/autocad2016/M0_BASELINE_RELEASE_20260722.md`。
2. M1 代码、自动化和 `0.3.3.0` 候选冻结已完成，精确候选实机矩阵仍待用户绑定。
3. M2-A 图纸级索引和 M2-B Codex 动态查询均已进入同一真实调用链并冻结候选；实机入口为
   `handoff/autocad2016/M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`。
4. M2 的 1k/10k/50k fixture、采集字段和脱敏 evidence 写入器已完成；仍等待 AutoCAD 2016
   五种范围、无选择集 ASK、失效/取消及三档真实性能证据。
5. M2 的实机/性能证据仍待完成；M3 已开始只读对象语义纵切，但不替代 M2 验收。M3 中文
   目录和未来字段核对模板见 `handoff/autocad2016/M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`。
6. 完成 M3 后关闭 M4 沙箱审计，随后才启用 AutoCAD 2016 强类型安全写入。
7. 随后完成长期记忆、安装签名、企业部署和 AutoCAD 2025 适配。

完整阶段与完成定义见 `handoff/autocad2016/LONG_TERM_MEMORY_TODO.md`。

Provider-neutral 抽象、Direct API Provider 和自研 Agent Loop 已冻结，不属于当前产品
结束条件；除非用户以后单独重新立项，不创建对应空接口或第二套调用链。

## 本地构建

```powershell
dotnet build Codex.AutoCAD.sln
dotnet run --project tests/Codex.AutoCAD.Contracts.Specs
.\scripts\verify-autocad2016-drawing-index-benchmarks.ps1
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

构建或 Specs 通过只证明对应的静态/自动化门禁，不替代 AutoCAD 2016 人工 `NETLOAD`。M2
候选的完整 DLL、依赖和人工步骤见 `M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md` 与
`M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`；M3 的对象目录和核对模板见
`M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`。Codex 不启动、唤醒、关闭、重启或操作
AutoCAD；实机步骤由用户在现有 CAD 环境中执行。

## 安全不变量

1. 模型不能向活动 AutoCAD 发送命令字符串、LISP、脚本或任意 API 名称。
2. 活动 DWG 只能通过强类型操作计划、预览、一次性审批和事务修改。
3. CAD 写审批不能使用会话级永久授权。
4. 插件不自动保存或覆盖 DWG。
5. 断线、超时、图纸修订变化或结果不确定时默认拒绝并停止写入。
