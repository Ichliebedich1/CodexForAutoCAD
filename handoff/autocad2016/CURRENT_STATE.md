# AutoCAD 2016 当前状态索引

最后更新：2026-07-27（北京时间）

本文件是项目的长期“当前状态索引”。它不替代 `README_FIRST.md`、
`COMPANY_PC_RUNBOOK.md`、测试报告、证据 JSON 或 Git 历史；只把当前成立的结论、
证据边界、活动阶段和待验证队列集中在一处。

## 证据使用顺序

1. 真实 AutoCAD 命令记录与冻结候选 SHA-256。
2. 对应阶段的验证脚本输出和脱敏 evidence JSON。
3. 已验证后单独产生的 Git 提交。
4. 本索引及交接文档中的摘要。

若摘要与原始证据冲突，以更具体、更新且可复现的原始证据为准。没有真实编译和
NETLOAD 证据的能力一律视为未支持。

## 当前活动快照（2026-07-27）

- M4.16 正式实机候选已绑定干净候选提交
  `cef82772bbafebd161f5c9d3af0c3aa32ddd0084`，统一门禁 `9/9`、双 Shell Phase 2
  `469/469`，候选 ID 为
  `autocad2016-m4-live-v042-9827dc32-a3334d72-f41e24ee`。真实异常退出 A–E 尚未执行，
  `live-matrix-results.json` 和回滚点均不存在，因此 `M4Complete=false`、
  `M416Frozen=false`，M5 继续硬阻断。
- M9.1 已在候选 C 的独立后继 Worktree 建立 Windows CI 切口，并于提交
  `9afaaafcdf24028d984bd1b3ca81a5ea013e59ba` 形成独立检查点：新增
  `windows-2022`、PowerShell 7/Windows PowerShell 5.1 双矩阵工作流，第三方 Action 绑定
  精确提交、权限只读、checkout 不保留凭据、产物只进 Runner 临时目录。标准 Runner 显式
  使用 `5 GiB` 门槛，本机默认仍为 `40 GiB`；Phase 2 显式使用仓库离线 NuGet 配置，
  不读取用户配置。CI-only Phase 2 双 Shell 保持 `469/469`，额外 net45/x64 门禁在双 Shell
  均构建 4 个 AMD64 托管程序集、0 warning / 0 error；默认 Phase 2 的真实本机 Codex
  `0.144.4` doctor 已单独回归通过。工作流尚未推送或取得远端 run，故 M9.1 仍为
  进行中，不得把本地验证写成 CI 已通过。
- M9.2 已在独立提交 `1e969e2da702af459e1a76b9df0b7c58b49425cb` 冻结 15 文件工具链锁：
  `global.json` 禁止 SDK 补丁滚动，`eng/toolchain-lock.json` 和双 Shell 门禁锁定 SDK
  `8.0.319`、NuGet `6.10.2.8`、MSBuild `17.10.46.46604`、离线 net45 包及签名、全部
  NuGet 配置/锁文件、R20.1 Probe 源输入和 4 个批准的 Autodesk 二进制输入。门禁负向
  自检双 Shell 均为 `18/18`；两个全新缓存的 Probe A/B 构建 AMD64 DLL 哈希一致，API
  Probe 为 `29 passed / 8 expected failed`。提交后双 Shell 完整工具链锁已通过；远端
  工作流尚未运行，因此 M9.2 的本地 Git 检查点已完成，远端完成条件仍未满足。
- M9.3 在 `codex/m9-required-gates` Worktree 基于上述 M9.2 提交继续实现，当前精确增量为
  10 个修改和 2 个新增，未暂存、未提交。Host ReadOnlyContext、Host V2 和 AgentService
  Specs 已接入正式 solution/Phase 2/Agent bootstrap 调用链；Phase 2 为 `510/510`、
  AgentLauncher net8/net45 各 `65/65`、AgentService `7/7`，相关 bootstrap evidence
  schema 为 17、构建 0 warning / 0 error、残留进程为 0。`verify-m9-required-gates.ps1`
  从 evidence 动态聚合 11 个 Phase 2 项目、双运行时 Launcher 与 AgentService 规格，
  并把 M9.1、M9.2、M4 相关联套件、候选 manifest 和 candidate doctor 绑定到同一提交。
  临时 validation 提交 `34f842dee33d447812acaeda8583d80e3c6e9214` 已按生产默认
  `40 GiB` 门槛完成完整入口：相关联套件 `9/9`、12 个覆盖类别、候选 manifest/doctor、
  动态唯一逻辑规格 `582`，新增残留 0、AutoCAD 未启动、PATH 不变。最终 evidence schema
  为 `codex.autocad.m9-required-gates/1`，SHA-256 为
  `9F2456A56BCBEE1DF504E8B6BDAD9DD784F8CB71FC66E62A06C106A89901AA25`。该验证只证明本次
  文档刷新前的实现状态；项目分支仍未提交，正式 M9.3 提交后必须从该精确提交重新运行默认
  `40 GiB` 完整入口。远端 CI 也尚未运行，因此 M9.3 仍为进行中。
- P0 `codex/bridge-client-net45` 的 0.3.2 停止生命周期候选已由用户在 AutoCAD 2016
  中完成人工启停、重复 STOP、DBMOD 和残留检查；独立提交为 `8a4ee57`，实机证据为
  `agent-stop-live-observation-20260722.json`，该证据不继承给 P1。
- P1 `codex/cad-context-v2` 已在隔离 Worktree 完成 CadContextJson v2 的产品 Runtime、
  Palette、Bridge/AgentHost v2 测试接入；v1 固定向量未修改，源码级回归与托管门禁为
  `259/259`，R20.1 Host 编译和双 Shell v2 API Probe 证据已保留。
- P1 当前明确支持的协议标识为 `codex.autocad.cad-context/2`，Agent 回合方法为
  `agent.turn.start.v2`；未知/读取失败/超限对象使用受限占位，并通过
  `entityCount`、`parsedEntityCount`、`unsupportedEntityCount`、`complete` 表达完整性。
- P1 冻结候选现已取得 AutoCAD 2016 live 基线：模块版本 `0.3.2.0`、Doctor v2、
  100% DPI Palette、50 对象混合选区、6 个受限 placeholder、真实 v2 Codex 两轮对话、
  显式清除和文档激活清除均由用户实测。混合选区为 `selected=50`、
  `jsonBytes=23142`、`DBMOD 21 -> 21`；未知对象没有中断整体发布。
- 该 live 基线的脱敏范围证据为
  `evidence/cad-context-v2-live-observation-20260722.json`。它不证明全部 19 类对象已逐类
  核对，也不证明 AutoCAD 正常退出、125%/150% DPI、Bridge 断线、请求超时或取消。
- P1 候选：`artifacts/autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30/`；
  Host SHA-256 `0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7`，
  AgentHost EXE SHA-256 `10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442`，
  manifest SHA-256 `A16831703985906F724B8EB93BDB0BC801A5781A3228F0694CB1A20A4AC5960F`。
- M0 已在合并提交 `e66ef1e` 和构建稳定化提交 `c96e9a3` 上冻结统一自动化候选
  `artifacts/autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54/`。Host SHA-256
  为 `37C1953D9AD996F9892486300295E69043F8E020D506E0683FC1301F8FC4C532`，AgentHost EXE
  为 `AB1CE675EF48947F670E0A4FC013E09108AF9A91D5D14F49874039F42018CD3A`，manifest 为
  `FF11069F766A055D3F2DEA7D9D320CB1B4A5D874260FB4E47EE083D42E12F8BD`。
- 该 M0 候选已从精确提交通过 Phase 2 `259/259`、Host MVP `24/24`、R20.1 A/B、双 Shell
  API Probe、真实本机 Codex v2 两轮 `2/2`、manifest 和候选 doctor。其精确哈希尚未在
  AutoCAD 内 NETLOAD，不能把 P1 的实机哈希绑定自动迁移过来；边界见
  `evidence/m0-baseline-verification-20260722.json`。
- 候选包内 AgentHost 已单独运行 `doctor` 并完成本机 Codex app-server 初始化；脱敏证据见
  `evidence/cad-context-v2-candidate-package-doctor-20260722-refresh.json`。该证据仍不替代
  AutoCAD `NETLOAD` 或真实 v2 对话。
- P1 候选 AgentHost 已通过真实本机 Codex v2 live 规格：认证 capability 明确包含
  `agent.turn.start.v2` 与 `codex.autocad.cad-context/2`，同一 thread 使用两份合成 Line v2
  上下文完成两轮回答，上下文哈希在接收响应和 assistant 事件中一致回显，停止后残留
  AgentHost 为 `0`。最新证据见 `evidence/agenthost-v2-live-two-turns-20260722-refresh.json`；该规格没有
  启动 AutoCAD，因此仍不替代 P1 `NETLOAD`、真实选区和 Palette v2 实机证据。
- 采集器集成和退出清理重试回归后当前线完整 Phase 2 门禁通过 `259/259`、Release `0/0`、
  Host 禁用 API、doctor、秘密扫描和 diff；新候选构建证据为
  `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30.json`。
- `MvpAgentRuntime.Terminate()` 现在最多执行两次有界清理；首次同步/异步失败或空任务时
  自动重试，连续失败才报告脱敏错误。退出清理自动化为 `24/24`，证据见
  `evidence/host2016-terminate-exit-retry-20260722.json`。
- 本轮已修正 P1 Host 的 Doctor 和 `CODEXCAD` 命令文案，使其显示 v2，不改变 v1 契约或
  历史验证记录。
- Host 的 v2 能力判定已抽成独立 fail-closed 策略，并增加 `6/6` 回归：只有同时声明
  `agent.turn.start.v2` 与 `codex.autocad.cad-context/2` 才接受；null、空 schema、缺方法或
  只有 v1 schema 均拒绝。目标机 R20.1 net45/x64 Release 复编译为 0 错误。
- M0 已在独立 `codex/m0-baseline` Worktree 中完成 P1、P0 和主分支文档的受控整合、
  自动化复验和候选冻结；本地 `main` 已安全快进并吸收冻结提交 `4833e76`。主工作树中的
  Host.2025 UI/选择/写入原型不属于本阶段，仍保留且未被清理、覆盖或误提交；远端尚未
  因本次收尾自动推送。
- M1 已在干净 `codex/m1-integration` Worktree 从 `main@9edc83e` 受控吸收 10 个提交；
  集成树与来源 `codex/m1-readonly-stability@88c0a29` 完全一致，且未夹带 Host.2025、
  Kimi UI、M4 或主工作区补丁。该线已完成 Bridge 断线 fail-closed、结构化脱敏错误、Host
  自有 request_id、唯一终态、幂等取消、10 分钟回合超时和迟到事件拒绝；相关运行时代码
  提交为 `eb4e36c`、`9f1ffb6`、`8455000`、`41d184b`。
- M1 已新增 `CODEX16NEWCHAT` 和 `CODEX16CLEARALL`；`CODEX16CTXCLEAR` 明确只清 CAD
  上下文。系统 conversation ID 与 Provider thread ID 分离，活动回合期间新建/清除返回
  结构化 `busy`。相关提交为 `ba84047`、`924cab2`。
- 对话现按图纸隔离；图纸切换会终止旧活动回合、清空旧可见回答，并使下一次 ASK 建立新
  Provider thread。图 A 的迟到事件不能更新图 B，即使 Provider turn ID 碰撞。相关提交为
  `621b057`、`8b695fb`。
- M1 已冻结 `0.3.3.0` 集成候选
  `artifacts/autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3/`。Host SHA-256 为
  `E6701A771D17EC3EC8B2CA7DA78B553E27897639DC48B3BC0435F07249C9B5F6`，AgentHost EXE 为
  `4B60296581224ADCDF1E8B0C8F1C766AE896796DA2DCF0B73E5EEFE6BBFE6966`，manifest 为
  `B081B93A6BE99D8D16304A3A1B2EABD93D352E92613F370C5450E448E8507E40`。
- 该候选通过 Host MVP `41/41`、PowerShell 7 与 Windows PowerShell 5.1 各自
  Phase 2 `276/276`、25 文件 Host.2016 只读 Compile
  闭包、R20.1/net45/x64 双构建位级一致、敏感信息扫描、diff 和候选包自身 AgentHost
  doctor。证据为
  `evidence/cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3.json`。
  它尚未按精确哈希在 AutoCAD 内 NETLOAD，不能继承 `0.3.2.0` 的实机结论。
- M2-A `codex/m2-drawing-index` 已实现独立 `codex.autocad.drawing-index/1` 和
  `codex.autocad.cad-query/1`：支持 Selection/Current/Model/Layouts/Drawing 范围、Idle
  分片、进度、取消、2 分钟超时、64 MiB 估算预算、100,000 实体索引、2,000,000 实体
  报告上限、类型/图层/空间/块/文字/范围/对象令牌过滤和稳定游标分页。
- M2-A 未放大 v2 选择快照；`64` 实体和 `256 KiB` canonical JSON 仍是既有对话快照硬
  上限，整图能力走独立内存索引和分页查询。未知、代理、读取失败和数据受限对象发布
  受限占位；文档、revision、DBMOD、当前空间或对象事件变化会使旧索引 `stale`。
- M2-B 已把只读 `cad.query_drawing` 动态工具接入现有 CodexAgentRuntime、AgentHost、
  认证反向 Bridge 和 Host。无有效选择上下文但有有效 DrawingIndex 时允许 ASK；两者都
  没有时 fail-closed。模型只能提交过滤器、页大小和游标，不能提交 index/document/revision
  身份。
- Host 在 AutoCAD 文档线程冻结纯托管 DrawingIndex 快照；Bridge worker 查询该快照时
  不进入 Autodesk API。系统 request_id、Provider thread/turn、tool call 和 query ID 分离，
  每一层逐项绑定。早于 `agent.turn.start.v2` 响应到达的合法反向查询也按精确
  request/thread 身份绑定；启动失败、STOP、断线、取消和终态会清理临时绑定。
- 文档修改、撤销、切图、索引替换、回合取消或终态使旧快照/结果拒绝；Bridge 停止会取消
  并排空反向查询。50k 索引发布使用冻结实体数组所有权转移，避免第二次数组深拷贝。
- 当前 Host 命令为 `CODEX16INDEX`、`CODEX16INDEXINFO`、`CODEX16INDEXCANCEL`、
  `CODEX16QUERY`、`CODEX16QUERYNEXT`；Doctor 和加载横幅明确声明动态查询已接入。
- M2 性能准备分支 `codex/m2-benchmark-fixtures` 已在 M2-A/M2-B 真实调用链上增加 Host 本地
  性能遥测。`CODEX16INDEXINFO` 记录 Idle 总/准备/读取片数、最大分片耗时、总扫描耗时、
  估算内存、本地查询与 Codex 反向查询耗时，并明确显示查询页 `200` 与 IPC 单帧
  `8,388,608` 字节硬上限；这些字段不进入 wire 契约。
- 已建立确定性 AC1009 脱敏 DXF：模型空间精确 1,000、10,000、50,000 个实体，覆盖
  Line/Circle/Arc/Text/Insert 和 8 个图层。生成器拒绝覆盖已有目录，不启动 AutoCAD；
  独立流式解析、双次哈希、脱敏 evidence 和 fail-closed 测试为 `6/6`。
- 当前 M2 `0.4.0.0` 自动化候选由源码提交
  `34cef1214ad22822996db4e4ad33013f855751e3` 精确生成，目录为
  `C:\tmp\CodexForAutoCAD-m2-integration\artifacts\autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024`。
  Host SHA-256 为 `BC6011D3C0C00222BE266E27A26770B87FC4CE542A9516640AEC1A959950C5D5`，
  AgentHost EXE 为 `6DE30DB91C466CA0CA87E6202926FB893165CE8950B1CCAB9E0E3C49650CDD89`，
  manifest 为 `CDE0E31D9B2342B322D1850224B6DE78755B97EAEF7802C7D609F86E58E7D917`。
- 当前候选通过 Contracts net8/net45 `88/88`、Bridge Client net8/net45 `29/29`、
  Bridge/AgentHost `39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2
  `314/314`、benchmark `6/6`、30 文件 Host.2016 只读 Compile 闭包、R20.1/net45/x64
  双构建位级一致、敏感信息门禁和候选 AgentHost doctor。证据为
  `evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`。
  对象查询身份为不泄露 Handle 的 `obj-########` 令牌；`dq1_...` 分页游标由 Host 随机
  生成，五分钟过期，并绑定索引 ID、文档 revision、查询形状和 offset。
- 枚举器已在同一个有效只读 transaction 内创建、遍历并释放；当前真实剩余风险是每个
  space 的 ObjectId 仍会在一个 preparation Idle 回调内形成托管数组，尚未证明 50k 最大
  preparation 分片低于 20 ms。候选也未启动或操作 AutoCAD，因此不能证明动态查询实机
  行为和真实性能；M2.3、M2.13、M2.14 保持未完成。
- 旧 `E85D97EC...` 与 `597A7A3D...` 候选只保留为历史冻结点，均不可作为当前测试入口。
  Provider-neutral 抽象、Direct API 和自研 Agent Loop 继续冻结。
- M3 当前自动化冻结候选将 Host 版本推进到 `0.4.2.0`：选择快照、整图索引、
  `CODEX16CTXINFO`、`CODEX16INDEXINFO` 和 Palette 均按实际类型统计未支持、数据超限和
  读取失败对象；`CODEX16TYPEINFO` 提供 19 类现有强类型对象的中文名称与人工创建入口。
  类型统计受 `4,096` 个桶限制，且不包含图层、Handle、路径或对象内容。
- M3 的块读取纵切已把受限 `blockDetails` 接入 DrawingIndex、CadQuery、认证 Bridge 和
  Agent 工具。它包含属性/动态属性、嵌套块计数与深度、布局标志及安全 Xref 布尔元数据；
  外部 Xref 定义和真实路径不会读取或传播，任何受限情况以 `limited` 降级。
- Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy 和 Wipeout 已成为
  DrawingIndex 中可查询但明确 `data_limited` 的安全类别；它们没有被伪装为完整强类型
  payload，也没有修改冻结的 CadContextJson v2。
- 已加入含 14 个实体记录的确定性、脱敏 M3 核心 DXF fixture；双次生成、独立解析、哈希和
  预期 manifest 门禁为 `6/6`。
- source-bound M3 候选由提交 `00fe879a0ac056fab48c955e71d63c51ef3577d9` 生成，候选 ID
  为 `autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc`。Host SHA-256 为
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`，AgentHost EXE 为
  `44CD544883F7BA7B790044220FAE3C5DDD2515C589CE3CC6910260F6C6795EF5`，manifest 为
  `02B5AE218CAFC19892F7CF086330D46EB237131A67BA61700D644E6A7E74D520`。
- 该候选已通过完整 Phase 2 `323/323`、M3 fixture `6/6`、R20.1 双 Shell API Probe
  `29 passed / 8 expected failed`、Host x64 A/B 位级一致、只读源码扫描和 AgentHost
  发布门禁。证据为
  `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc.json`。
- 这些是自动化冻结证据，不是 AutoCAD 实机通过。M3 的 19 类字段、复杂块/Xref、受限对象
  降级和 M2 的 1k/10k/50k 性能仍需使用精确候选人工验证；中文对象目录、字段矩阵和边界见
  `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`。

## 已验证检查点

### 诊断薄宿主

- 提交：`2d2ad3738095794c8374e916559c0c5d13702ba1`。
- 目标：AutoCAD 2016 R20.1 x64，进程内 .NET Framework 4.5 薄宿主。
- 已使用目标机原版 Autodesk `20.1.0.0` 托管程序集真实编译并由用户手工
  `NETLOAD`。
- `CODEXCADDOCTOR` 与 `CODEXCAD` 可执行；首次记录为 `DBMOD 21 -> 21`。
- 该历史记录没有取得已加载 DLL 的现场路径/哈希绑定，不得由后续构建哈希回填。

### Palette 运行时检查点

- 提交：`56115e4`（`feat(host2016): add verified palette runtime checkpoint`）。
- 冻结候选 DLL SHA-256：
  `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`。
- 用户已在原本打开的 AutoCAD 2016 中手工加载并验证 Palette。
- 100% DPI 下打开、停靠、浮动、隐藏后重开、释放重建、中文输入和换行正常。
- 干净样本中 Palette 操作及文档切换前后 `DBMOD=4`；插件未读取选择、未写入
  CAD、未保存图纸。
- 125%/150% DPI 与 AutoCAD 退出生命周期仍未验证。

### 跨运行时认证与 Bootstrap 原语

- 阶段证据：`handoff/autocad2016/evidence/auth-bootstrap-verification-20260719.json`；
  阶段提交以包含该 evidence、源码、Specs 和验证器的同一 Git 提交为准。
- 基线为已验证 Palette 提交 `56115e4`；.NET SDK 固定为 `8.0.319`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁。
- 托管核心 Release 隔离构建为 `0` warning / `0` error；Bridge 回归 `29/29`。
- IPC/Bootstrap 在 net45 与 net8 均为 `35/35`，固定 frame、KDF、Host→Agent 与
  Agent→Host HMAC 字节完全一致；两次隔离构建的六个主产物逐字节一致。
- 公共 API 规范化面在 net45/net8 均为 `103` 项且 SHA-256 相同；MemberRef、关键
  状态机方法和完整 Bootstrap 实现 IL 已分别按目标框架冻结。
- 已验证发送 payload 只能尝试写一次，部分写入或 Flush 失败都会永久消费；接收
  payload 禁止转发；端点角色由材料来源固定；入站 Guard 与出站 Authenticator 各只能
  领取一次；认证后解析使用内部 frame 副本以避免调用者 TOCTOU。
- 该检查点自身只证明内存/Stream 协议原语。后续“真实进程外 AgentHost 安全引导”
  检查点已补齐有界的真实密钥交付、确认身份、启动截止 fail-closed 中止和最多 `5` 秒
  有界终止清理证据，但长运行 live
  Bridge 与 AutoCAD 集成仍不属于本原语检查点。

### 真实进程外 AgentHost 安全引导

- 阶段证据：`handoff/autocad2016/evidence/agent-bootstrap-verification-20260719.json`；
  阶段提交以包含 Launcher、AgentHost、Specs、验证器、evidence 和本次文档更新的同一
  Git 提交为准。
- 最终阶段入口为 `scripts/verify-autocad2016-agent-bootstrap-stage.ps1`；它自动运行双
  PowerShell 的 bootstrap、认证兼容与 Phase2 门禁，将 raw evidence/log 留在 ignored
  `artifacts/`，并在 Git evidence 中只保存其 SHA-256、规范化比较结果、计数和限制。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁；每次
  均完成两次隔离 Release 构建，完整可运行输出树 `106` 个文件按相对路径、长度和
  SHA-256 逐字节一致，Specs 执行后再次复核未变化。
- net45 与 net8 Launcher Specs 均为固定 ID 集 `15/15`；验证器拒绝缺失、重复、未知
  或两个运行时同时删除的测试。
- 真实 AgentHost bootstrap-doctor 已通过受限继承的 stdin/stdout/stderr 句柄完成认证；
  命令行和环境变量不携带 bootstrap 密钥或 frame。子进程领取句柄后清除继承位，父进程
  可继承 canary 句柄未进入子进程。
- 启动前要求批准的 EXE SHA-256；父进程以 `CREATE_SUSPENDED` 创建后校验 PID、创建
  FILETIME、映像路径、卷/文件 ID 和第二次 SHA-256，再恢复主线程。批准 SHA-256 不匹配
  和确认 PID/创建时间不匹配均动态 fail-closed。
- 未确认挂起、有效确认后继续挂起两条路径均由启动截止触发 fail-closed 中止，随后在
  最多 `5` 秒有界清理窗口内证明子进程终止；取消路径也执行相同的有界终止清理。这里
  不声称终止本身严格完成于配置的启动截止内。重复真实引导 `5` 次通过，相关进程
  基线/终态均为 `0 -> 0`。stderr 始终排空且只公开受限字节数与
  截断标志，失败异常不公开原始文本。
- M4 第一诊断边界已在独立 `codex/m4-process-config` 分支完成：Codex App Server stderr
  不再以原文进入事件、退出异常或 AgentHost 控制台，改为有界 `bytes`/`truncated` 摘要；
  退出事件会等待该无内容摘要形成而不阻塞进程事件线程；normal doctor 也不再回显工作目录
  或 `CODEX_HOME`。该分支尚未冻结候选，环境白名单、
  独立凭据、资源配额和完整审计仍未完成，不能据此宣称完整 OS 沙箱。
- M4 第二启动配置和健康边界已接入 AgentHost：`--codex`、`CODEX_EXECUTABLE`、已知 npm 安装布局
  和绝对 PATH 候选会归一化为固定本地磁盘的绝对 `codex.exe`；显式无效配置 fail-closed，doctor
  只显示来源标签。产品兼容窗口固定为 `>=0.144.4 <0.145.0`，预检与 app-server initialize
  绑定同一可执行文件身份租约。自动化已通过，正式 M4 候选的 AutoCAD 2016 启动/停止/退出实机
  仍待验收，详见 `M4_2_CODEX_HEALTH_PREFLIGHT_20260724.md`。
- M4.3 基础提交 `6d99bb9` 已让真实 Codex transport 使用显式环境白名单，并提供可选、严格验证的
  session `CODEX_HOME` 与活动租约；home 固定为空 MCP、禁用插件，正常清理不跟随重解析点。
  生产 bootstrap 尚未启用该 home，因为空 home 没有现有 ChatGPT 登录；M4.8 ACL/lease 自动化
  切口已完成，但 M4.11 凭据 Broker 完成前仍不得复制或解析全局 profile。详见
  `M4_3_CODEX_SESSION_HOME_BASELINE_20260724.md`。
- M4 第三进程树边界已接入真实 AgentHost 启动链：校验后的 AgentHost 会在恢复前进入具有
  `KILL_ON_JOB_CLOSE` 的未命名 Windows Job Object；普通后代由该 Job 统一回收。
  M4.6–M4.9 自动化检查点已提交为 `15352ff`，包含分配前任意 Job 成员检测、分配后目标 Job
  成员反查和结构化隔离失败；本机
  Windows 已真实验证外层/内层嵌套 Job 分配。Stop、AgentHost 异常退出和拥有 Job 的启动器
  退出后进程树回收、资源限制、累计用户时间终止和连续 `500` 次 service 启停回收均进入
  net45/net8 专项门禁。企业组策略、Windows 版本和宿主 Job 组合矩阵仍未验证，不能据此
  宣布 M4.6 全部完成。
- M4.8 自动化切口已接入真实 AgentHost 启动链：系统 session ID 生成后、进程启动前创建
  `sessions/{sessionId}`，包含 `workspace`、`audit`、`codex-home`、`.active` 和固定 schema
  marker。根、会话及子目录使用受保护 DACL，只允许当前用户、SYSTEM 和 Administrators；
  固定本地磁盘、owner、ACL、最终句柄路径和 reparse 边界均在使用前验证。活动租约阻止目录
  移动/替换，不同 session 可并发；STOP 在进程、stderr 和 I/O 全部收口后删除工作区，失败可由
  后续 STOP 重试。默认过期阈值 `24 h`、单次最多扫描 `64` 个候选，只清理带合法 marker、
  合法 ACL、已过期且 `.active` 可独占打开的目录；无 marker legacy 目录和活动 lease 保留。
  自动化切口已完成并包含在 `15352ff`，企业/AutoCAD 实机矩阵仍缺；`codex-home` 已创建但 M4.11
  凭据 Broker 完成前不得作为生产隔离登录入口。
- M4.9 当前自动化切口已进入真实默认启动路径：Job 默认限制为最多 `16` 个进程、总提交内存
  `4 GiB`、CPU hard cap `75%`、累计用户时间 `8 h`，认证服务墙钟上限为 `24 h`；停止前自然
  退出宽限现由 `GracefulStopTimeout` 控制，允许 `0–30 s`、默认 `1 s`，在启动前校验并快照。
  非法值 fail-closed，配置值实际传入 Stop 等待函数。Windows Job completion port 现对进程数、
  Job 总提交内存和累计用户时间命中提供权威通知，watchdog 对服务墙钟提供终态；Host 在有界
  仲裁窗口内优先使用这些原因，不再让普通 Bridge 断线覆盖活动 request。四类终态分别为
  `agenthost_process_limit_exceeded`、`agenthost_memory_limit_exceeded`、
  `agenthost_user_time_limit_exceeded` 和 `agenthost_session_runtime_limit_exceeded`，
  `error_stage=agenthost_runtime`、`retryable=false`。真实 Job 内存与用户时间组合耗尽也只提交
  第一个权威终态，不伪造固定优先级。当前明确不启用 `JOB_OBJECT_LIMIT_WORKINGSET`：Job 总
  提交内存继续作为硬边界，working set 只作为外部性能 telemetry 和发布预算。真实
  Codex/AutoCAD 耗尽矩阵和企业配置策略仍未完成，因此 M4.9 仍是进行中。技术说明见
  `M4_9_RESOURCE_LIMIT_TERMINALS_20260724.md`。
- M4.11 当前未提交切口已完成默认禁用凭据配置、产品专属 Windows Credential Manager
  target 校验、Generic Credential 的 `4 KiB` 有界二进制读取、稳定
  `agenthost_credential_unavailable`、幂等 Dispose 原位清零、认证一次性凭据帧以及
  AgentHost 的隔离 `CODEX_HOME` + stdin 登录调用链。fake Codex 已覆盖成功、非零退出、
  `auth.json`、超时、取消和 argv/环境不含 token；Bridge 为 `53/53`，AgentLauncher
  net45/net8 各 `63/63`，完整 bootstrap 门禁为 `63/63`。真实 Credential Manager、
  Codex/keyring、RestrictedToken 全链和 AutoCAD/企业矩阵仍缺，因此不能称为生产 Broker
  完成。详见 `M4_11_CREDENTIAL_BROKER_BOUNDARY_20260725.md`。
- M4.12 bounded JSONL 已升级为 `codex.autocad.agenthost.audit/2`：并发记录保持完整、
  部分写入留下可检测截断尾部并永久 fail-closed；CAD 写入事件仍等 M5 调用链接入。
- M4.13 当前未提交切口已进入真实 `bootstrap-serve`：生产审计不再位于会被 STOP 删除的
  session workspace，而是写入当前用户独立持久根的受保护 `segments` 与 `anchors` 子目录。
  JSONL 和 anchor 分目录、CreateNew、耐久更新；session workspace 删除后仍可验证。单段达到
  `10,000` 条或 `4 MiB` 时自动轮转，新段继承上一段 hash，默认最多 `64` 段。删除、
  插入、修改、截断、anchor 篡改、跨段重排、旧文件防覆盖和 STOP 后保留均有规格。新增只读
  `AgentHostAuditCatalog`，仅在真实受保护审计根上枚举并分类 `complete`、`incomplete`、
  `corrupt`、`anchor_mismatch`；只有 `session_stopped/session_failed` 终态链才是 `complete`，
  强杀留下的哈希/anchor 一致但无终态前缀标为 `incomplete/session_not_terminal` 并禁止导出。
  临时 anchor、缺段、缺 anchor、链损坏和身份/锚点不一致均不自动修复、删除或覆盖。Bridge Specs `73/73`，双 Shell Phase 2 `392/392`，
  bootstrap net8/net45 各 `63/63`，Release `0 warning / 0 error`，残留 AgentHost/FakeAgentHost
  为 `0`。受控 `audit-export --session <system-session-id>` 已接入 AgentHost：不接受任意路径或
  `--output`，固定读取当前用户受保护审计根，只导出 Catalog 的 `complete` 会话；先在内存中
  完成验链和脱敏 JSON，再写标准输出，失败只返回稳定错误码且不产生半份 JSON。完整、缺
  anchor、链损坏、anchor mismatch、无终态崩溃前缀、非法 session ID 和不可写目标均有规格。
  新增只读 `audit-retention-plan`：策略参数必须显式提供，固定读取受保护根，只计算年龄/容量
  候选且不删除文件；最低完整会话保留集不可覆盖，非终态/损坏/anchor mismatch 固定人工复核，
  未识别文件计入容量但不成为候选，计划不输出路径。规格还验证规划前后 artifact 数量、长度、
  时间和哈希不变，以及越界、非 UTC 和超大 artifact 被拒绝。`audit-retention-apply` 已接入
  AgentHost：要求显式策略和只读计划返回的 64 位小写 plan ID，执行前重新验链和重算计划；
  独立受保护 `retention-control` 目录保存排他锁、全计划耐久 journal 和完成 receipt。journal 在
  首个删除前原子提交，绑定每个会话的精确段数及各 artifact 的长度、UTC 时间和 SHA-256；恢复
  会重新哈希剩余文件。计划变化、文件变化、日志损坏、不同计划遗留日志和并发 apply 均失败关闭；
  中断可用同一 plan ID 恢复，完成后重复 apply 返回 `already_applied`。Bridge Specs 专用子进程
  现已执行真实 `Apply`，在 journal 耐久提交并删除首个 anchor 后由父进程强杀；新租约使用原
  plan ID 完成恢复，保留最低会话、清除 journal，且无残留工作器。已知 control artifact 现在
  有界收敛：最多保留最近 `256` 份 receipt；更旧 receipt 在删除前逐份耐久折叠到固定
  `audit-retention-receipt-checkpoint/1` 累计链，检查点记录最后 receipt 哈希和严格游标。检查点
  已提交但 receipt 尚未删除时可恢复且不重复累计；已有有效 final receipt 的 foreign temp 会被
  清除，没有 final 的 foreign temp 保持冲突并要求原计划恢复。当前仍不是后台自动清理或自动
  修复；企业默认策略、系统断电、真实生产 AgentHost/AutoCAD 异常退出、未知/恶意 control
  artifact 的企业归档流程、签名/HMAC 强化和企业/AutoCAD 实机仍缺，因此 M4.13 继续为进行中。
- M4.13 受控清理的命令、journal/receipt 协议、故障恢复和未完成边界见
  `M4_13_AUDIT_RETENTION_CLEANUP_20260725.md`。
- M4.14 已完成多个真实纵切：Contracts 新增按来源分类的有界 `DiagnosticSanitizer`，统一
  清除 Bearer/敏感键值、带引号 JSON secret、Windows/UNC 路径、URI、域账号/邮箱身份、
  控制字符和双向格式字符；输入上限 `4096`、公开输出上限 `512`，正则超时返回固定安全
  fallback。Bridge 服务端 `BridgeRemoteException`、客户端 `AgentBridgeClientException` 与
  `AgentBridgeRemoteException` 已接入该边界：合法稳定错误码保持兼容，非法远端码分别归一为
  `remote_error`/`internal_error`，消息、错误码和嵌套异常只贡献分类与数值脱敏证据，不再
  保留可能含 argv、环境、路径或凭据的原始 inner exception。反向整图查询
  的真实跨进程响应已证明只回传清洗后错误文本。AppServer stderr 原本即为只含字节数和截断位
  的无文本摘要；其 RPC 异常现在不再保留原始 JSON data，只公开 `DataWasPresent`、脱敏标志与
  清洗后消息，所有公开 AppServer 异常也不再保留任意原始 inner exception。AppServer 公开异常
  已显式携带诊断分类与数值脱敏计数：配置/版本预检为 `Configuration`、RPC 为 `RemoteError`、
  通用/协议异常为 `Exception`。AgentHost 未知命令不再原样回显任意首参数，只输出分类后的清洗
  命令、数值脱敏计数和固定 usage。Contracts 进一步覆盖设备命名空间路径、带空格/引号路径、
  转义 JSON secret、完整 URI 变体，以及最多 `16` 节点/深度 `8` 的嵌套与聚合异常图。
  `AgentBootstrapLaunchException` 已按配置/凭据、进程环境、stderr 和通用异常映射稳定分类，
  直接诊断和异常图只贡献数值脱敏证据，不保存原文、inner exception、堆栈或 `Data`。
  `doctor`/`run` 成功状态已改为最小公共 DTO，不再公开 App Server 原始 `userAgent`、
  `platformOs`、`platformFamily` 或 `codexHome`。AppServer `ProtocolFaulted` 事件也不再
  保留任意观察者原始异常、StackTrace、`Data` 或 inner graph，只公开固定消息安全快照、
  稳定分类和数值脱敏标志。AppServer 的服务端请求失败响应也已在唯一
  `WriteErrorAsync` 出站边界收口：JSON-RPC 数值 code 保留，message 按 `RemoteError`
  分类执行有界脱敏，处理器提供的任意原始 JSON data 不再写回本机 Codex 子进程，只保留
  `diagnosticClassification`、数值 `diagnosticRedactions` 和 `sourceDataWasPresent`。
  真实传输规格先 RED `36/37`，后 GREEN `37/37`。三个 AgentHost 审计 CLI 命令现在统一经过
  最外层失败边界；未预期异常只输出固定 `agenthost_audit_failure`、稳定 error code、
  `errorStage=agenthost_audit`、分类和数值脱敏标志，不再由 .NET 主机泄露异常类型、消息或堆栈，
  已有 `invalid_arguments`、`audit_*_rejected` 和闭集 ReasonCode 保持不变。Contracts
  `99/99`、AppServer `44/44`、Bridge `80/80`、AgentLauncher net8/net45 各 `63/63`、
  Bridge.Client `31/31`、AgentRuntime `39/39`、Host.2016 MVP `59/59`，双 Shell Phase 2
  `415/415`，
  Release `0 warning / 0 error`；禁用 API、doctor、敏感信息扫描和差异检查通过，相关进程残留
  为 `0`。Host.2016 Palette/Bridge 断线与 `CODEX16QUERY`/`CODEX16QUERYNEXT` 命令行错误
  已在最外层统一脱敏；邮箱或域账号紧邻中文时也不会绕过身份脱敏。AgentHost `doctor/run`
  通用 CLI 失败现在返回稳定 `agenthost_cli_failure`、`errorStage=agenthost_cli`、分类和数值
  脱敏标志；协议故障 stderr 与 `bootstrap-doctor/bootstrap-serve` CLI 失败也不再输出 CLR
  类型名。AppServer Client 与底层 transport 的 stderr 摘要观察者已逐项隔离，观察者异常不能
  中断 stderr 排空、退出传播或后续观察者；Client 只经固定安全 `ProtocolFaulted` 快照报告。
  AgentRuntime 的 projection/observer 公共诊断也不再保留原始异常图，动态工具校验失败原因在
  进入事件或回传 Codex 前按 `RemoteError` 脱敏；失败 turn 只保留 `id`、`status` 和脱敏后的
  `error.message`，observer 失败只保留事件类型安全快照，不再持有原始 Agent 事件。Bridge
  公共 `Completion`/`TerminalError` 也已改为固定 `BridgeTerminalException` 安全快照。
  DrawingIndex 启动、CadQuery 和 CadQuery 下一页三个 Host.2016 通用 catch 分支统一输出稳定
  code/stage、分类和数值脱敏标志，不再输出 CLR 类型名；目标 net45/x64 产品构建为
  `0 warning / 0 error`。配置请求和 AppServer 启动配置的 record 字符串不再展开路径、
  完整 PATH、参数或环境；AgentRuntime options/handle/input 不再展开路径、提示词、Provider
  标识或 schema；Bridge request/notification 不再展开完整 `BodyJson`。AppServer initialize
  response、notification、server request、RPC error、request resolution、turn interrupt 和
  approval event 包装器也只报告存在性、成功状态或数值错误码，不再递归输出 CodexHome、
  Provider ID、method、JSON、错误正文、任意 result 或审批 payload；wire JSON 与处理器字段
  保持不变。AgentRuntime 的 turn handle、item snapshot，以及消息增量、工具进度、turn、
  review、CAD proposal/rejection 和四类审批事件的字符串也已收敛为类型、枚举和存在性摘要，
  不再展开 Provider IDs、回复内容、工具 JSON、错误正文或审批 payload；真实事件字段、投影与
  审批转发保持不变。AppServer 的四类审批请求、嵌套权限/网络/文件系统模型、响应、CAD 文档
  身份、变更摘要和预览对象也只报告类型、存在性、枚举和数量，不再展开命令、工作目录、授权
  路径、Provider ID、理由、策略修订或预览 JSON；wire JSON 和审批决策保持不变。
  AppServer initialize 请求侧的 client info、capabilities 和 params 也只输出配置存在性、
  布尔能力与数量，不再展开任意客户端名称、标题、版本或方法列表；initialize wire JSON 不变。
  AgentRuntime 的 CAD 点、`create_line` 提案、提案批次与 Broker 结果 record 字符串也已收口，
  不再展开坐标、图层、Provider IDs 或结果正文；强类型属性、解析与 Broker 语义不变，CAD 写入
  仍禁用。
  `AgentHostAuditException`
  已追踪到生产 Bridge/CLI/导出/UI 边界，当前没有 raw inner 外逃路径，未做机械重构。
  当前 `Replace`/`Sanitize` 静态复核未发现另一套诊断清洗器，CAD
  文字摘要、cursor、命令行引用、哈希和原子文件替换保持原语义。
  AgentRuntime、Bridge、Host、AgentHost 审计导出/保留、CLI JSON、Doctor/Run、
  Host BuildInfo、DrawingIndex/CadQuery 和剩余公共 record/EventArgs 字符串出口已完成静态
  复核，未发现新的可复现公共泄漏。M4.14 的代码、自动化和静态公共出口审计已收口；真实
  Codex、AutoCAD、组策略、EDR、受限账户和系统断电故障验证转入 M4.15。详见
  `M4_14_DIAGNOSTIC_SANITIZATION_20260725.md`。
- M4.15.1 已把 Windows/企业策略阻止 AgentHost 启动接入正式失败链：当前用户
  `ERROR_ACCESS_DENIED`（5）、`ERROR_INVALID_IMAGE_HASH`（577）、
  `ERROR_ACCESS_DISABLED_BY_POLICY`（1260）和应用阻止错误 4551–4557 映射为稳定
  `agenthost_process_start_blocked`；RestrictedToken 的普通访问拒绝仍保持
  `process_isolation_failed`。Host UI 返回脱敏、不可自动重试的管理员检查提示，不公开原始
  Win32 正文、路径或异常图。该纵切只证明分类和调用链，不代表真实 AppLocker、WDAC、
  EDR/杀毒或企业组策略机器已验证。详见
  `M4_15_ENTERPRISE_POLICY_FAILURE_20260726.md`。
- M4.15.2a 已把嵌套 Job 分配拒绝从泛化隔离失败中分离：正式
  `AssignProcessToJobObject` 失败链在目标进程已属于父 Job 时返回不可自动重试的
  `agenthost_nested_job_assignment_failed`，Host 提示管理员检查父 Job 和进程隔离策略；
  原始 Win32 正文、路径和异常图仍不可见。既有本机正向嵌套 Job 运行规格继续通过，失败后
  挂起 AgentHost 由原启动清理链终止，绝不无 Job 回退。真实不可嵌套父 Job、企业启动器、
  EDR 和受限账户尚未验证。详见 `M4_15_NESTED_JOB_FAILURE_20260726.md`。
- M4.15.3a 已把 AgentHost 根进程意外退出接入独立结构化终态：正常 STOP 和资源限制保持
  `ProcessExit=None`，只有没有资源终态的自行退出才发布不可自动重试的
  `agenthost_unexpected_exit`。Host 在 Bridge fault 归因窗口内保持资源终态优先，再由进程
  退出胜过泛化断线；活动请求只进入一次 `failed`，后续 ASK fail-closed，原始 Bridge 诊断、
  stderr、路径和异常图不可见。真实 Codex/AgentHost/AutoCAD 强杀仍未验证。详见
  `M4_15_AGENTHOST_UNEXPECTED_EXIT_20260726.md`。
- M4.15.3b 已把进行中的 AgentHost 启动令牌纳入 Host STOP/退出生命周期：STOP 在后台先取消
  bootstrap/Bridge/thread 启动，再等待并清理已建立资源；预期中断不误报“启动失败”，不能在
  STOP 后上线，重复 STOP 不增加第二终态。真实 AutoCAD/Codex 分阶段启动中断仍未验证。详见
  `M4_15_STARTUP_INTERRUPTION_20260726.md`。
- M4.15.4 此前是 M4.15 中唯一没有交接文件的子项，实机范围、预期错误码和证据要求都没有
  落到纸面；现已补齐执行入口。该子项**没有可先做的自动化部分**：分类映射、`Retryable=false`
  和脱敏边界已由 M4.15.1 覆盖，M4.15.4 要验证的是真实 AppLocker/WDAC/代码签名/EDR 拦截
  是否确实落进那些已分类的 Win32 错误，只能由用户在具备相应策略的机器上执行。文件同时
  记下一个必须由实机回答的设计问题：当前用户身份下的普通错误 `5` 被归入
  `ProcessStartBlocked`，因此纯 NTFS ACL 拒绝也会显示“请让管理员检查 AppLocker、WDAC……”，
  可能把排查引向错误方向；在实机结论出来前不得凭猜测调整分类。详见
  `M4_15_RESTRICTED_ACCOUNT_EXECUTION_CONTROL_20260726.md`。
- M4.15.5a 已把 `retention-control` 顶层 inventory 纳入只读计划状态：合法中断文件报告
  `recovery_required`；未知文件/目录、reparse、超限/不可读或严格 schema 无效的控制 artifact
  报告 `manual_review_required`。计划只输出计数、闭集原因和必要 plan hash，不输出文件名、路径
  或内容；执行器持锁后重新检查，未知/危险/inventory 不完整时使用同名稳定原因码拒绝且不删除
  原证据。真实磁盘满、系统断电、企业归档和保留策略仍未验证。详见
  `M4_15_RETENTION_CONTROL_REVIEW_20260726.md`。
- M4.15.5b 已增加明确标注为 synthetic 的持久化 I/O 故障夹具：审计流写入或独立锚点提交失败
  后永久 fail-closed，Bridge 会话终止且不补写第二终态；retention 在 journal/receipt/checkpoint
  原子提交边界统一返回稳定 `cleanup_failed`。journal 提交前不删除 artifact，提交后保留
  `recovery_required`；同一 plan ID 重试只收敛一次，再次执行固定 `already_applied`。公共错误只
  输出稳定码、阶段、环境分类和数值脱敏标志。它不等同于真实磁盘满、卷离线或断电。详见
  `M4_15_PERSISTENCE_IO_FAILURE_20260726.md`。
- M4.15.6 自动化收口证据已完成：Phase 2 新增可选脱敏 JSON 输出并继续动态统计九个规格项目；
  新增 R20.1/.NET Framework 4.5/x64 Host 双隔离构建门禁；新增严格 fail-closed 的 readiness
  汇总器，绑定双 Shell Phase 2、Agent bootstrap、认证原语、R20.1 Host、源码 manifest、锁文件、
  用户 PATH 长度/哈希、秘密/API 扫描和相关进程残留。PowerShell 7 与 Windows PowerShell 5.1
  均通过汇总器自检和正式汇总，输出语义等价；状态固定为 `automated_readiness_only`，
  `M4Complete=false`、`M416Frozen=false`。真实凭据、受限身份、磁盘满、断电、异常退出、企业
  执行控制和企业归档全部保持未验证。详见 `M4_15_AUTOMATED_READINESS_20260726.md`。
- M4.1 分层策略模型已补齐并接入真实调用链（本轮新增，未提交）。Contracts 新增
  `AgentPolicyContracts.cs` 与 `AgentPolicyResolver.cs`：机器策略 > 管理员 > 用户三层合并，
  低优先级层只能收窄白名单不能扩大，被高层锁定的项低层不得改动，未知/损坏/旧版本/缺省缺失/
  白名单为空/超限/默认值越界全部 fail-closed 且不返回部分策略，错误码为稳定闭集。
  `CodexAgentRuntime` 的三处 `options.Model ?? _options.Model` 直接穿透已全部改走唯一出站边界
  `ResolveModelForWire`：配置策略时按白名单、锁定和默认值校验，未配置策略时仍拒绝危险形态
  （空白、引号、控制字符、路径分隔符、超长值），返回值即实际被下发的模型。
  AgentHost 新增 `AgentHostPolicyStore.cs` 从固定位置读取三层配置（ProgramData 机器/管理员、
  LocalAppData 用户），产品入口不接受任意源路径，拒绝相对路径、UNC、设备命名空间、非固定盘，
  并逐段检查路径链 reparse point，有界读取 `64 KiB`，未知字段与自行声明 `layer` 均 fail-closed。
  生产 `bootstrap-serve` 已在启动 Agent 运行时前加载策略：三层皆缺失表示管理员未部署策略，
  保留仅形态校验的兼容行为；任一层存在却不可用则以 `AgentHostPolicyConfigurationException`
  拒绝启动，绝不静默降级为无白名单。规格由 `421` 增至 `454`（Contracts `118`、
  AgentRuntime `43`、Bridge `93`），双 Shell Phase 2 均为 `454/454`。真实企业策略分发、
  组策略锁定和 AutoCAD 实机仍未验证。
- M4.16 完成条件的自动化保证已补上（本轮新增，未提交）。原 `Assert-NoForbiddenHostApi` 只扫描
  `src\Codex.AutoCAD.Host.2025`，而 M4.16 要求硬禁用的是生产宿主 Host.2016，该 Host 此前不在
  任何禁用 API 门禁覆盖内。新增 `Assert-NoCadWriteInHost2016` 并接入 Phase 2 主流程：不照搬
  Host.2025 全套规则（Host.2016 作为真实只读实现合法需要 Autodesk 类型、文件 IO 和
  `Assembly.Location`），只精确禁止 CAD 数据库写入、图纸保存导出、命令字符串执行和 LISP/脚本
  四类，按方法调用形态匹配并跳过纯注释行，避免把 `AppendEntitySummary` 这类文本格式化方法误判。
  门禁内建双向自检：`10` 个写入样例必须识别，`6` 个只读样例不得误报。端到端负向验证以临时探针
  精确报出 `5` 处写入（仅文件名与行号，不含绝对路径），双 Shell 消息一致，探针已移除。
  当前结论：Host.2016 `OpenMode` 全部为 `ForRead`，扫描 `31` 个源文件 `0` 处写入调用。
  M4.16 仍未完成——尚缺从已提交源码构建的候选、回滚点和资源/身份 evidence 绑定。
- 2026-07-27 审计发现冻结脚本仍使用过期规则，要求九项实机矩阵全部为 `true`，与权威目标
  “真实异常退出必须 verified、其余八项允许有理由 deferred”冲突。当前未提交修正引入固定
  `handoff/autocad2016/live-matrix-results.json` 契约：九项必须精确处置并绑定 readiness 的
  HEAD/Host/AgentHost 哈希；deferred 必须同时标注 M9/M10 重评；真实异常退出必须验证
  AutoCAD、AgentHost、Codex 强杀、唯一终态、残留 0、后续请求 fail-closed 和无敏感泄漏。
  冻结 evidence schema 2 分别列出 `Verified`/`Deferred`。双 Shell 自检通过；在当前缺少实际
  live matrix、回滚点且工作树 dirty 的状态下，真实拒绝检查正确输出 `freeze_refused`，
  `M4Complete=false`、`M416Frozen=false`，User PATH 不变。详见
  `M4_15_LIVE_MATRIX_RESULTS_CONTRACT.md`。
  为避免结果文件自引用自己的提交哈希，冻结采用两提交模型：候选提交 C 生成 readiness 并接受
  实机测试，随后 evidence-only 提交 E 只保存 `live-matrix-results.json`；脚本验证 C 是 E 的
  祖先且 `C..E` 没有任何其他差异，回滚 ref 仍指向 C。夹带源码、脚本或其他文档会拒绝冻结。
  live matrix 采用严格字段白名单；未知字段、路径/URI/邮件/环境变量/secret 形态的延期理由和
  全相同字符占位哈希均拒绝，避免 evidence-only 提交把原始事件或本机信息带入 Git。输入限制
  为普通非 reparse 文件、严格 UTF-8 和 64 KiB；解析与 SHA-256 绑定同一份加锁字节，JSON
  boolean/integer 类型严格校验，字符串 `"false"`/`"0"` 不能冒充真实结果。
  冻结 evidence 输出也只能位于 build-safety 的 Worktree 产物根内且必须为 `.json`，防止误写
  系统盘、仓库或其他 Worktree。
  用户实机入口已整理为 `M4_15_REAL_ABNORMAL_EXIT_RUNTIME_TEST.md`，覆盖正常 STOP、流式回答中
  强杀 Codex、流式回答中强杀 AgentHost、启动握手中断和强杀 AutoCAD；本轮没有执行这些场景。
  独立临时 Git 仓库中的正向契约演练已通过：候选 C、只新增矩阵的 E、回滚 ref 指向 C、
  1 verified + 8 deferred 得到 `preconditions_met`、阻塞 0、`M4Complete=true` 且
  `M416Frozen=false`。这是 synthetic 判定路径证据，不是 AutoCAD/Codex 实机通过。
- M4.12 CAD 执行事件 schema 已冻结（本轮新增）。目标文件已按受控拆分修订：M4.12 负责
  审计基础设施与全部事件 schema，CAD 执行事件的真实接线归 M5.13，从而解除"M4.12 需要 M5
  写入链、而 M4 又是 M5 硬前置"的死锁；该拆分不放松安全要求，写入链启用时若未按已冻结
  schema 接入哈希链，M5.13 即为未完成。新增 13 个事件类型覆盖提案、验证、预览、审批展示、
  用户决定、能力 token 消费、锁内重验、事务提交/中止与三类终态。字段白名单固定为
  `CadOperationKind`、`CadOperationCount`、`CadRiskLevel`、`CadRuleVersion`、`CadPlanHash`
  和 `CadDocumentRevision` 六项，明确禁止坐标、图层名、Handle、路径、选择哈希、审批 token
  和完整 CAD JSON；`CadPlanHash` 是规范化计划摘要，可证明批准对象但不能还原图纸。
  校验为双向：取值必须落在冻结闭集与格式内，且非 CAD 事件不得携带 CAD 字段。
  哈希链采用条件纳入：由于记录哈希使用长度前缀编码，无条件追加新字段会使既有记录
  哈希整体改变并让已持久化的生产链失效，因此只有确实携带 CAD 字段的记录才追加
  `cad/1:` 段。规格在测试内独立重算扩展前算法，逐事件类型断言既有哈希逐字节不变，
  并逐字段扰动证明 CAD 字段确实进入哈希；字段白名单本身也以反射冻结为断言。
  Bridge 由 `98/98` 增至 `102/102`。
- M4.13 审计链 MAC 密钥已落地（本轮新增，提交 `6b08d22`）：32 字节 HMAC-SHA256 密钥存于
  已受保护持久审计根，复用既有目录 ACL，未新增依赖；首次以 `CreateNew` 生成，并发启动
  只有一个成功、另一个回退加载并走完整校验；Dispose 原位清零，MAC 比较使用固定时间。
  防降级是本切口核心：密钥文件已存在但截断、超长、全零退化或不可读时一律 fail-closed
  并把原文件留在原处，绝不重新生成，否则破坏密钥即可让既有链无声失效。
  威胁模型如实固化：AgentHost 以当前用户运行，其可读的本地密钥同用户亦可读，因此本方案
  无法抵抗同用户蓄意篡改，该结论写为编译期常量 `SameUserTamperResistant=false` 并由规格
  断言，防止后续实现误称已解决；真正抵抗需管理员服务代签、锚点外发至用户不可写仲裁端或
  TPM 封装，属架构变更。
- M4.13 锚点 MAC 写入侧已接入（本轮新增）。`AgentHostAuditFileAnchorSink` 增加可选链密钥，
  非空时为锚点写出同名 `.mac` 伴随文件；MAC 覆盖锚点文件的完整落盘字节（含结尾换行），
  验证时直接读原始字节重算，因此不依赖 JSON 属性顺序在未来版本保持稳定。提交顺序为先
  MAC 后锚点：锚点是完成标志，它就位即代表 MAC 已在位，崩溃留下的中间态会验证失败而不会
  被误判为有效。MAC 以临时文件加同卷原子 rename 写出，避免留下截断伴随文件；构造时同时
  拒绝已存在的锚点与已存在的 MAC。
  降级防护为该切口核心：只要密钥存在，缺失、截断、长度正确但内容错误的 `.mac` 一律判失败，
  删除 `.mac` 无法把存储退回无保护状态；未配置密钥的既有存储保持向后兼容且不写出伴随文件；
  外来密钥不能验证本存储锚点；锚点重写时 MAC 必须同步更新。Bridge 由 `102/102` 增至
  `107/107`。
- M4.13 锚点 MAC 验证侧已接入（本轮新增）。`AgentHostAuditCatalog.ReadCompleteSession` 在
  链验证之前调用锚点 MAC 校验，受保护根由已验证的 anchor 目录父级推导，因此不需要把密钥
  贯穿传递。为此新增只加载不创建的 `AgentHostAuditChainKey.TryLoad`：只读分类路径若顺手
  生成密钥，会把"该存储从未启用 MAC"悄悄变成"已启用"，反而掩盖既有锚点缺少 MAC 的事实；
  密钥不存在返回 null 并放行以兼容既有存储，存在但损坏仍然 fail-closed。
  至此 M4.13 的 MAC 写入与验证在生产读写两侧均已接入，Bridge 为 `108/108`。
  尚缺：同用户篡改边界不变（见上），企业默认保留策略、系统断电与 AutoCAD 实机矩阵未验证，
  因此 M4.13 继续为进行中。
- M4.7 self-contained 尝试已回退（2026-07-26，用户决策暂缓）。`SelfContained=true` 本地
  构建通过（202 文件 / 71.9 MiB，候选必需 11 文件齐全，runtimeconfig 切换为固定 8.0.22 的
  `includedFrameworks`），但隔离构建门禁以 NU1101 失败：离线 feed 是刻意的供应链边界
  （`<clear />`、`signatureValidationMode=require`、单一受信签名者、单一已审查包），
  self-contained 需要的三个运行时包（NETCore/WindowsDesktop/AspNetCore App.Runtime.win-x64，
  约 80 MB）不在 feed 内且签名者不在信任列表。备选为扩充 feed（影响仓库体积与 M9.8
  SBOM/许可证）或暂缓；因 M4.7 即使完成 self-contained 仍被 M4.11 真实环境验证阻塞，
  用户选择暂缓，csproj 保持 `SelfContained=false`。回退后 agent-bootstrap 门禁已重新
  通过（EXIT=0）。重新启用前必须先决策离线 feed 扩充。
- M4.4/M4.5 已在当前集成分支提交为 `0763022`：产品公共配置、导出类型和公开结果不再
  暴露实验身份选择；RestrictedToken 只保留为 internal-only 可移植能力探针，且任何结果
  都禁止回退 CurrentUser。本机 net45/net8 原语均为 `available`，受限 FakeAgentHost
  均在认证前以 `child_exited` 退出；这不是生产受限身份成功。详情见
  `M4_4_M4_5_RESTRICTED_IDENTITY_PROBE_20260724.md`。
- 当前 `codex/m4-credential-broker` 已包含 `15352ff` 后的 M4.1、M4.11–M4.16 自动化准备、
  审计 MAC/Catalog、M9.8 SBOM/许可证，以及统一门禁入口 `scripts/verify-all-gates.ps1` 和
  构建安全 evidence 的 run correlation。精确提交以 `git rev-parse HEAD` 和 readiness
  `Source.HeadCommit` 为准；未经用户授权不得继续提交、合并或推送。
- 2026-07-27 从干净的已提交统一入口完成双 Shell 自检及一次完整 E 盘外置产物验证：
  `build-safety` PowerShell 7/Windows PowerShell 5.1、双 Shell Phase 2、agent-bootstrap、
  auth-compat、R20.1 Host 双构建、M9 SBOM/许可证和 M4 readiness 共 `9/9` 通过。
  双 Shell Phase 2 均为 `469/469`；每项 evidence 均绑定同一 `RunCorrelationId` 并在
  suite evidence 中记录独立 SHA-256。Host DLL SHA-256 为
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`，AgentHost DLL
  SHA-256 在当前精确提交 `4657aa7091c6a938bca28997acb9ef8a73f86e1f` 重跑后为
  `483E4CB438EB3436FB8C503920372D3001D0570C9D041F71896F2B5D0F26F52F`。
  readiness 记录精确源码 manifest、`Source.HeadCommit` 和
  `Source.WorkingTreeDirty=false`；各 evidence 的当前 SHA-256 以同一次 suite JSON 为准，
  不在本文复制易失值。
- 统一入口现在强制使用显式、短、非系统盘产物基目录；EvidenceDirectory 只能位于该
  Worktree 的产物根内。它不写 User/Machine 环境变量，任一门禁失败、PATH 指纹变化或
  evidence 不匹配都会 fail-fast；bootstrap/auth evidence 不再按“最新目录”选择，而是要求
  本次唯一 Run ID；残留检查按运行前后基线识别本次新增的 AgentHost/Fake/Bridge/Codex
  app-server，避免误判外部既有 Codex 会话。
- 本轮用户 PATH 保持 `661` 字符、`13` 项、`0` 污染项，UTF-8 SHA-256
  `05DF0D2FFC86D41186216560D37CC16FA0159ED5CEF9A89F61042964C196BE59` 全程不变；
  本次新增残留进程为 `0`，相关 `10` 份 correlated evidence 中敏感路径/用户名/秘密命中为
  `0`。readiness 仍固定 `automated_readiness_only`、`M4Complete=false`、
  `M416Frozen=false`、`RealAbnormalExitMatrixVerified=false`、`CadWriteEnabled=false`。
  M9.8 的漏洞库查询和人工/IL 审查、候选 manifest/doctor、CI/干净缓存、全部实机及企业矩阵
  明确不在这次 `9/9` 的完成声明内。
- 当前继续审查发现统一门禁只分别产生已验证 Host 和 AgentHost 输出，没有形成可直接
  `NETLOAD` 且 manifest 绑定提交/readiness 的完整 M4 实机候选。旧 M1 候选脚本的公共回归
  虽通过 Phase 2 `469/469`、R20.1 Host A/B、AgentHost doctor 和 manifest，但普通 publish
  得到的 AgentHost DLL 哈希为 `7FEDEF9A...FA11AE1`，与 readiness 的隔离构建哈希
  `483E4CB4...F26F52F` 不一致，故不能用于 M4 实机 evidence。当前未提交修正增加
  `CandidateProfile=m4-live`：干净提交、suite/readiness/Run ID、Host/AgentHost 哈希和 correlated
  bootstrap evidence 任一不一致都 fail-closed；M4 候选直接复制该次已验证 AgentHost 完整
  runnable 输出并生成 schema 2 manifest，候选/evidence 只写统一 E 盘产物根。该路径仍需
  进入新提交、从新提交重跑 `9/9` 后完成正向打包，当前不得开始实机矩阵。
- 后续安全复核已把 readiness、suite、bootstrap evidence 改为同一次加锁读取的字节同时用于
  严格 UTF-8 JSON 解析与 SHA-256，避免解析内容和绑定哈希来自两次读取；并按 bootstrap
  evidence 的真实口径重新比对 `out/bin` 双构建完整 `129` 文件树，再逐文件复制、复核其中
  AgentHost 的 `20` 文件 runnable 子树。基于当前正式 readiness 的辅助路径验证已唯一命中
  correlated bootstrap，`20/20` 文件复制一致；M1 兼容回归再次通过 Phase 2 `469/469`、
  Host A/B 和 AgentHost doctor，PATH 不变且 AutoCAD/AgentHost 前后均为 `0`。这些仅验证
  未提交脚本及旧模式，不替代修正提交后的正式 M4 正向候选，更不替代 AutoCAD 实机矩阵。
- 候选打包器现增加双 Shell `-SelfTestOnly`：在统一 E 盘产物根使用随机自有目录验证严格
  UTF-8、单次字节解析/哈希、大小限制、产物根逃逸、递归文件树复制、bootstrap A/B 完整树
  漂移和多个 correlated 输出均 fail-closed；PowerShell 7 与 Windows PowerShell 5.1 均通过，
  自测目录结束后精确清理。它不依赖或启动 AutoCAD，也不把 synthetic 数据写入 Git。
- 实机结果与冻结入口的一致性复核又发现 readiness 仍由普通 `Get-Content` 读取，且
  `AutomatedGatesPassed="false"` 等字符串存在被 PowerShell 强制转换为真值的风险。当前
  未提交修正使 readiness 与 live matrix 都采用普通非 reparse 文件、单次加锁字节、严格
  UTF-8 和有界 JSON；关键 readiness 开关必须是精确 JSON boolean，且要求有效 correlated
  Run ID。冻结 evidence 增加 `ReadinessSha256`。双 Shell自检通过；基于当前真实 readiness
  的缺矩阵/回滚点拒绝路径仍精确得到 `freeze_refused`、5 个 blocker、
  `M4Complete=false`、`M416Frozen=false`，PATH 不变且未启动 AutoCAD。
- 构建产物根已由 `<Worktree>\artifacts` 迁至 `E:\cxb\<Worktree>\`。历史产物迁移共
  `17` 个 Worktree、`249,268` 个文件、`44.465 GiB`，逐个校验文件数、总字节数、逐文件相对路径与
  长度，以及 `26,878` 个关键文件（`verification.json`、`manifest*.json`、`Codex.AutoCAD*.dll`）
  的 SHA-256 后才删除精确源目录；C 盘可用空间由 `26.45 GiB` 增至约 `71.4 GiB`。
  `C:\tmp\CodexForAutoCAD-docsync` 是指向 D 盘主仓库的 junction，已排除且全程未触碰。
  产物根路径长度现由 `Resolve-CodexArtifactRoot` 以 `60` 字符上限 fail-closed 预检：迁移初期
  使用较长基目录曾使 net45 隔离构建路径达到 `267` 字符，超过 MAX_PATH `260`
  （本机 `LongPathsEnabled=0`），导致 `agent-bootstrap` 与 `auth-compat` 以 `MSB3030` 失败。
  R20.1 双 Shell API Probe 为
  `29 passed / 8 expected failed`、Autodesk DLL 复制数 `0`。
  本轮 R20.1/.NET Framework 4.5/x64 Host A/B 重建也逐字节一致，Host SHA-256 为
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`。
  当前自动化 readiness 输入位于 `artifacts/m4-readiness-inputs/`，汇总状态为
  `automated_readiness_only`；这些 artifacts 未提交，也不替代真实机器或企业 evidence。
- M4 阶段编排器已升级为 bootstrap schema 16、九项目 Phase 2 冻结矩阵和条件锁文件异常
  路径恢复；Bridge Client 反向查询取消测试不再依赖负载敏感的 200 ms 调度窗口。PowerShell 7
  与 Windows PowerShell 5.1 最终阶段门禁均通过，正式 evidence 为
  `evidence/agent-bootstrap-verification-20260719.json`；最终 SHA-256 由门禁输出记录，避免在
  受该 evidence manifest 约束的文档中形成间接自引用。
- M4.4/M4.5 脱敏 evidence 为
  `evidence/m4-restricted-identity-probe-verification-20260724.json`。专项原始
  verification SHA-256 为
  `9F2828286E1259BC6B3FBB32A518638BABA40769D9353E922FCA9CF448A2BEA3`；完整双 Shell
  verification SHA-256 为
  `1DC258ABF33ECF9575E278352BEE103700E75EA3699ABC8FAF415A14706378D0`。
- 从该精确 M4 集成提交重跑 M3 候选脚本仅作为只读回归检查：Host `0.4.2.0` SHA-256
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D` 保持不变，
  AgentHost SHA-256 为
  `5DB1497A02B5C0F8E307C64A28D7EB4C589E233ABF04B9E98B1C73448E1EBB5A`，manifest 为
  `6CDE171520FDDA6E7FAE68E00038A90C8091E1D1EBAE0C7B63FAA199161E2CED`。该临时包不是新的
  正式 M3 候选，也不是 M4.16 安全候选；未启动 AutoCAD、未取得 NETLOAD 或真实 Codex
  进程树证据。脱敏边界见 `evidence/m4-integration-checkpoint-20260724.json`。
- 当前 M4.1 配置、M4.2 版本/健康预检和 M4.3 环境/session-home 基础均已进入代码；
  M4.4/M4.5 已受控提交，M4.6–M4.9 自动化检查点已提交为 `15352ff`。M4.2/M4.3 仍缺正式
  候选实机和生产隔离登录；M4.6/M4.8/M4.9 的企业/AutoCAD 实机矩阵仍缺。M4.7 生产身份
  隔离仍未完成。M4.10 固定容量卷只完成未提交的边界预检，M4.11 已完成未提交的自动化
  传输/登录纵切，但真实凭据、keyring、受限身份和实机验证仍缺。
  M4.11 后续组合/实机配额矩阵、磁盘硬配额、JSONL 哈希链审计、统一脱敏和
  故障/企业实机矩阵仍未完成，因此 M5 CAD 写入继续禁用。
- 冻结构建哈希：AgentHost EXE `8C39315A...CE2823A`，AgentHost DLL
  `8E0C3617...6797CF`，net45 Launcher `0DD7DA71...B1B8B`，net8 Launcher
  `1F5B289E...6904EB`；完整值保存在阶段 evidence。
- Phase 2 回归为 Release `0` warning / `0` error、九个 Specs `360/360`、
  AgentHost doctor、Host 禁止 API、秘密扫描和 diff 通过；认证兼容回归在两个 PowerShell
  下均保持 Bridge `49/49`、net45/net8 `35/35` 和固定向量一致。
- 本检查点未启动、重启或操作 AutoCAD。它不证明长运行 `IAgentBridgeClient`、Host.2016
  live handshake、外部进程复制句柄的对抗性、刻意替换 EXE 的 suspended-launch TOCTOU
  动态攻击、CAD 审批/写入或完整 AutoCAD 2016 支持。

### 只读选择上下文运行时检查点

- 当前阶段基线：`2036fd6`；分支：`codex/selection2016-readonly-v2`。阶段提交应由
  包含候选源码、Specs、验证器、evidence 和本次交接更新的同一 Git 提交记录。
- 冻结候选 DLL SHA-256：
  `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`；大小
  `31744` 字节，冻结副本保持只读。
- PowerShell 7 与 Windows PowerShell 5.1 的隔离 Release 双重构建、`25/25` Specs、
  IL、禁止 API、依赖和输出门禁已通过，四次构建逐字节一致。
- 用户在原本打开的 AutoCAD 2016 中手工 NETLOAD 该冻结候选。预选的 Line、Circle、
  Polyline、DBText、MText、BlockReference 各 `1` 个成功发布为只读上下文；
  `selected=6`、canonical bytes `738`，捕获前后 `DBMOD 4 -> 4`。
- `CODEX16CTXCLEAR` 后 `published=false`、`selected=0`，且 `DBMOD 4 -> 4`。
- 文档激活事件以 `cleared-document-activated` 清除旧缓存，事件计数为 `1`；切换前原图
  清除后的命令行 `DBMOD=21`，切换后目标图命令行 `DBMOD=21`，两值相等。这只证明
  缓存未跨图纸保留，不推断更多文档状态。
- 首次没有预选集时的 `validation-no-implied-selection` 是执行前置 `DBMOD` 后预选被
  AutoCAD 取消所触发的预期 fail-closed，不是候选运行失败。
- 选择哈希按脱敏策略不写入仓库；实体总数未单独计量，插件自动保存也未做独立
  运行时动作验证，因此对应 runtime 布尔值继续保持 `false`。

### CadContextJson v1 与 Host/Agent/UI 公共契约

- 规范：`handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md`；证据：
  `handoff/autocad2016/evidence/cad-context-contract-v1-verification-20260719.json`。
- CadContextJson schema 固定为 `codex.autocad.cad-context` / version `1`，与 IPC
  `protocolVersion` 和 Host/Agent/UI `contractVersion` 明确分离。
- Line、Circle、Polyline、DBText、MText、BlockReference 六类图元使用显式强类型
  payload；包含真实坐标、图层、文字、半径、顶点和有效块名。文档名称、路径和
  `pathHash` 不进入 v1。
- canonical JSON 固定为严格 UTF-8、无 BOM/空白、固定字段顺序、Handle 数值排序和
  invariant G17 数字格式。冻结向量为 `2225` 字节，SHA-256
  `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过：每个 Shell 两次隔离
  Release 构建逐字节一致，net45/net8 Contracts Specs 均为 `27/27` 且输出完全一致；
  当前 Phase 2 回归为 Release `0` warning / `0` error、`157/157`。
- 能力协商、方法/事件/错误闭集、thread/turn、assistant 文本事件、上下文哈希回显、
  离线/断线/超时 fail-closed 和审批仅 `allow_once`/拒绝已经冻结。UI 不得增加
  `allow_for_session`、未认证回退或隐式协议字段。
- 本检查点没有构建或 NETLOAD 统一 Host.2016，也没有操作 AutoCAD；它不证明 Palette
  JSON 展示、具体 `IAgentBridgeClient`、长运行 live Bridge、真实 Codex 对话或完整
  AutoCAD 2016 支持。

### 具体 IAgentBridgeClient 跨运行时检查点

- 阶段入口：`scripts/verify-autocad2016-bridge-client-stage.ps1`；阶段证据：
  `handoff/autocad2016/evidence/bridge-client-stage-verification-20260720.json`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁；每个
  Shell 都执行两次隔离 Release 构建，net45/net8 Client、Specs、TestServer 和 Bridge
  产物逐字节一致。
- net45 与 net8 Bridge Client Specs 均为 `22/22` 且输出一致；Bridge 回归 `34/34`；
  当前 Phase 2 为 Release `0` warning / `0` error、八个 Specs `184/184`。
- 已验证能力协商、thread start、携带 CadContextJson v1 身份的 turn start、assistant
  delta/completed 事件、interrupt 和仅一次审批响应；thread/turn/context 身份必须逐项
  回显并绑定。
- HMAC、严格递增 sequence、nonce、防重放、坏 MAC/序号间隙/nonce 重放拒绝均通过；
  未知字段、重复字段、错误大小写、尾随 JSON、非法 UTF-8 和超大帧在分配前均
  fail-closed。
- 合法 turn 终态事件在身份校验后消费活动 turn；同一 turn 的后续迟到事件不再具有活动
  身份并按 fail-closed 拒绝。离线、断线、请求超时、取消、并发 Stop 和重复 Dispose 均
  有有界终态；连接故障会发出 `ConnectionFaulted`，密钥副本清零，TestServer 无残留。
- 本检查点没有启动、重启或操作 AutoCAD，`NetLoadVerified=false`、
  `AutoCadLiveEvidence=false`。它不证明统一 Host.2016 已接入长运行 AgentHost，也不证明
  真实 Codex thread/turn、Palette 回答回传或两轮 CAD 对话。

## 历史演进检查点

以下两节保留 v1 到 v2 的形成过程，用于解释提交、候选和证据边界；它们不是当前状态。
当前有效结论以本文件顶部“当前活动快照（2026-07-22）”和末尾“下一步顺序”为准。

### 统一 Host.2016 只读 MVP（历史 v1 检查点）

- 公共契约决策门已通过，Codex 与 Kimi 均必须以 `MVP_PUBLIC_CONTRACT_V1.md` 为唯一
  wire/UI 数据基线；视觉实现可以并行，但不得反向改变协议。
- 诊断、Palette、只读选择捕获和 CadContextJson v1 已整合为唯一
  `Codex.AutoCAD.Host.2016` net45/x64 产品入口。用户已对上一统一候选人工 `NETLOAD`，
  验证 Palette 打开、四个真实图元生成 `1700` 字节 JSON、摘要展示和 `DBMOD 0 -> 0`。
- 上一 Agent 候选执行 `CODEX16AGENTSTART` 时在能力协商阶段以 `ArgumentException`
  fail-closed；没有进入 CAD 写入，相关 AgentHost 进程未残留。根因是 Host 将含空格和
  `/` 的显示文字用作 v1 机器标识符。
- 修复后的生产标识符固定为 `codex-autocad-2016-mvp`、`0.3.1.0` 和
  `autocad-r20.1-net45-x64`。精确回归 `HOST2016_CAPABILITIES_IDENTITY` 为 `1/1`。
- 当前实机候选 ID：`autocad2016-mvp-agent-v031-a7bff46f-8c74b95e`；Host 程序集版本
  `0.3.1.0`、SHA-256
  `A7BFF46F1BA4970818ACB03F51C09EEBF1DDB8A7093D0C4C615E2D877D9236D1`。AgentHost 为
  framework-dependent single-file，SHA-256
  `8C74B95ECD6680F9A35824DB1C2C543D42B52AB1E4D3565F5B7EE8DBB1DC900E`。
- 当前非 CAD 门禁：Release、Phase 2 `192/192`、精确候选 net45 Bridge Client
  `22/22`、精确候选 net45 AgentLauncher `15/15`、AgentService `7/7`、真实本机 Codex
  能力协商与同一 thread 两轮上下文问答 `2/2`；secret scan、`git diff --check` 和相关
  进程残留 `0` 均通过。
- 用户已在真实 AutoCAD 2016 中人工 `NETLOAD` 该精确候选，Palette 显示模块版本
  `0.3.1.0`；真实 Line 已发布为 CadContextJson v1 并显示于 Palette。AgentHost 完成
  认证启动，本机 Codex 返回第一轮回答，同一 thread 的第二轮问题正确复用了前一轮
  标记；用户另确认多次连续上下文对话正常。核心只读 AI 链路因此可记为
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`。脱敏证据见
  `handoff/autocad2016/evidence/agent-mvp-runtime-verification-20260721.json`。
- 核心只读 Agent MVP 已在自动化门禁和上述实机证据通过后单独提交：`7f10d60`
  (`feat(host2016): connect verified readonly Agent MVP`)。
- 用户确认执行了规定的停止与 `DBMOD` 检查，但随后独立只读进程检查仍发现 `1` 个由
  当前 AutoCAD 进程创建的候选 `AgentHost bootstrap-serve` 进程。因此核心问答通过不等于
  停止生命周期通过；`agentHostNoResidualProcessVerified=false`，在定位并复验前不得把
  “有界停止且无残留”写成已验证。
- CAD 写入和插件保存继续禁用；本阶段不触碰一次审批、事务写入或自动保存设置。

### P0 已完成检查点：AgentHost 停止生命周期

- 根因已限定在 Host 停止编排：旧实现先等待 Bridge 停止；Bridge 在 net45 管道读未及时
  解除时可能超时抛错，随后 AgentHost 会话终止被跳过，而命令层只观察异常、不显示结果。
  底层 AgentHost 终止器及测试进程清理仍保持 AgentService `7/7`、net45 Launcher
  `15/15` 和 Bridge Client `22/22`。
- 修复已改为分阶段、可重试的停止协调器：Bridge Stop、Bridge Dispose 与 AgentHost Stop
  分别记录；成功阶段不会重复，失败阶段可由下一次 STOP 重试；并发 STOP 共享同一 attempt，
  Palette/状态回调异常不能阻止资源清理。Host 停止规格为 `13/13`。
- 当前自动化门禁：Bridge Client net45/net8 `25/25`、Bridge `37/37`、Phase 2 `195/195`；
  AgentLauncher net45/net8 `26/26`；认证兼容 net45/net8 `35/35`。PowerShell 7 与 Windows
  PowerShell 5.1 均通过，目标机原版 R20.1 程序集 net45/x64 A/B 构建逐字节一致。
- 冻结并已完成实机验证的候选：`autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd`；
  Host `0.3.2.0`，SHA-256
  `1CC9D2943F1AB3C37395927B0E2EAF4189A0B3BE4B2E8FA4A61AE8470D3478DC`；AgentHost SHA-256
  `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`。用户已确认
  `NETLOAD`、模块版本、三次启停请求、Palette 的 `Agent Bridge 状态: online`、最终
  `AgentHost 已停止`、`DBMOD 20 -> 20`；本机只读进程检查为 AgentHost 残留 `0`。
  对应观察记录见 `evidence/agent-stop-live-observation-20260722.json`。当前为
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`。测试步骤见
  `MVP_AGENT_STOP_RUNTIME_TEST_20260722.md`。
- 旧候选 `autocad2016-mvp-agent-v032-884413f0-8c74b95e` 已撤销并标记
  `revoked-do-not-load`；其旧 evidence 时间早于最终产物，不得用于 P0 结论。
- 2026-07-22 重新运行候选冻结脚本后，`sourceSnapshotAtUtc`、`candidateFrozenAtUtc` 和
  `recordedAtUtc` 均晚于本次最终构建；候选哈希与固定目录保持一致。该证据仍明确保留
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`；该候选已完成本轮人工实机验证。

### CadContextJson v2 AutoCAD 实机验证（历史形成过程）

- 当前 CadContextJson v1 只对白名单中的 Line、Circle、Polyline、DBText、MText、
  BlockReference 提供强类型 payload；选区中任一其他实体会以
  `validation-unsupported-entity-kind` 整体 fail-closed。这是当前最常见的实际使用阻断。
- v1 已冻结，不在原 schema 上悄悄加入旧消费者无法理解的新实体。下一阶段先定义
  CadContextJson v2，新增高频实体强类型 payload，并为仍未知的实体提供显式、限界、
  脱敏的占位记录和 `complete=false`/计数；不得静默丢弃未知实体，也不得读取代理对象
  私有数据、图纸路径或外部资源路径。
- 第一批优先评估 Arc、Ellipse、Spline、Point、Ray/Xline、2D/3D legacy Polyline、
  Dimension、Hatch、Leader/MLeader 和 Table。按目标机 R20.1 原版 API 可编译性、固定
  白名单字段、数量/顶点/文字上限和真实图纸常见度逐项冻结，不一次承诺所有 AutoCAD
  或垂直产品代理对象。
- CadContextJson v2 独立契约、验证器和确定性 Codec 已在隔离分支
  `codex/cad-context-v2` 中冻结候选；v1 源文件未修改，原固定向量仍为 2225 字节、
  `c5a03d4c...4423`。v2 固定向量为 6678 字节、`21cc9378...c3b4`，
  net8 与 net45 Contracts Specs 均为 `39/39`。
- v2 覆盖原六类及 Arc、Ellipse、Spline、Point、Ray、Xline、Polyline2d、
  Polyline3d、Dimension、Hatch、Leader、MLeader、Table，共 19 个强类型 payload。
  未知、读取失败或超过单实体限额的对象使用仅含 `dxfName/reason` 的受限占位；
  `entityCount`、`parsedEntityCount`、`unsupportedEntityCount` 和 `complete`
  必须相互一致。
- 目标机原版 Acdbmgd 20.1.0.0 公共 API 字段审计已记录在
  `MVP_CAD_CONTEXT_V2.md`；契约证据见
  `evidence/cad-context-contract-v2-verification-20260721.json`。
- Host.2016 的真实对象读取、v2 JSON 映射、逐实体降级和选择状态哈希已经形成构建
  候选。合并后 Host v2/Contracts/Bridge/停止协调回归已汇总为 `235/235`；v1/v2 固定
  向量不变。
- 之后已将 v2 捕获器接入 `UnifiedReadOnlyContextRuntime`，并让统一 Palette 显示
  schema/version、解析数、占位数和 `complete`；`MvpAgentClient` 已显式要求 v2 能力并
  调用 `agent.turn.start.v2`。这些是源码编译证据，不是 AutoCAD live 证据。
- 新增真实 Bridge → AgentHost v2 turn 规格后，Bridge 为 `38/38`，完整 Phase 2 本机门禁
  动态汇总为 `235/235`；Host 禁用 API、AgentHost doctor、diff 和秘密扫描均通过。
- 两份独立临时输出使用目标机原版 R20.1 程序集完成 locked Release 重建，Host DLL
  逐字节一致，SHA-256 为 `4D3386D9A825B2842290ACB51376FBA6BE6603F49295E606F8C9F3F92B538C08`，
  Autodesk DLL copy count 为 `0`。P1 候选和 manifest 已冻结；自动化证据见
  `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-4d3386d9-751b97c7-7216527a.json`。
- 上述自动化检查点把 `HostV2CaptureImplemented`、`R201HostCompileVerified`、
  `RuntimeIntegrationImplemented`、`CandidateFrozen` 和 `BridgeV2Negotiated` 提升为 `true`；
  在该检查点当时，`NetLoadVerified`、`AutoCadLiveEvidence` 和实机混合选区仍为 `false`。
  后续 2026-07-22 用户实机已把这些 live 基线提升为通过，范围和限制见顶部活动快照及
  `evidence/cad-context-v2-live-observation-20260722.json`。
- 对象扩展候选冻结前要求先修复 AgentHost 停止残留；P0 与 P1 后续已分别验证、分别提交。
- P1 已受控吸收 P0 停止修复，候选冻结提交为 `c174166`，随后独立引入采集器收口提交
  `5325e35` 和证据提交 `3ea4961`；P0 与 P1 仍保持独立提交和独立 evidence。P1
  候选随后已完成用户人工实机测试；这不扩大为尚未执行的退出、DPI、断线、超时、取消或
  19 类对象逐类字段验证。

### 已验证但尚待集成的独立阶段

- 环境采集器的注册表 `Location` 工作已经受控引入当前 P1 线，提交为 `5325e35`：支持
  `AcadLocation`、`InstallLocation`、`Location`，覆盖 Location-only 非标准安装、指向
  `acad.exe` 的规范化，以及 probe、根键、子键枚举和属性读取四类失败分支。
- 当前机器 PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 自测均为 `24/24`；
  两个 Shell 的真实只读采集均发现 `1` 个可构建 R20.1 安装，`Location` 提示数为 `1`，
  总失败计数为 `0`。脱敏证据见
  `evidence/environment-collector-location-verification-20260722.json`。
- 主工作树仍含用户所有的未提交 Host.2025 UI、选择和写入原型，以及其他未跟踪文件。
  这些变化没有进入 `codex/bridge-client-net45`，不能作为本 AutoCAD 2016 候选的构建或
  验证证据，也不得由本阶段清理、覆盖或提交。

## 不可弱化的产品约束

- AutoCAD 2016 优先：进程内 `net45/x64` 薄宿主，Agent/Sandbox 保持进程外 .NET 8。
- CAD 写入固定为“计划 -> 预览 -> 一次审批 -> `DocumentLock` 内重校验 -> 单事务”。
- 审批只有“拒绝”和“一次允许”，不得提供会话级永久允许。
- HMAC、严格递增 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得降级。
- 图纸、revision、选择、图层和空间必须在事务锁内重新校验。
- Agent 中断、超时或结果不确定时不得自动重试 CAD 写入。
- 插件不得自动保存 DWG，也不得关闭用户的 AutoCAD 自动保存设置。
- 不启动、唤醒、关闭或重启用户的 AutoCAD；需要实机时只给出冻结候选和人工步骤。
- 每个阶段必须先验证，再单独提交 Git。
- 正式侧边栏 UI 已通过公共契约决策门；任何 wire 不兼容变化必须升级 v2，不能由 UI
  原型隐式扩展 v1。

## 待实机验证队列

P0 与 P1 happy path 已通过，不再重复请求相同测试。M1 仍使用 `0.3.3.0` 精确候选和
`M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md`；M2 使用 `0.4.0.0` 精确候选和
`M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`：

1. 新建对话、只清 CAD 上下文、清除全部和活动回合 `busy`。
2. 图 A/图 B 上下文、可见回答和对话隔离。
3. 取消、重复取消和终态不回退。
4. 已发布 v2 上下文时 Palette Reset 后仍保留上下文。
5. 不先 STOP，正常退出 AutoCAD 后 AgentHost/Codex 残留为 0。
6. 125%/150% DPI。
7. 启动失败、Bridge 断线、超时和迟到事件。
8. M2 五种范围、本地分页、大选择集/未知占位、取消、失效和正常退出。
9. 无选择上下文、仅有效 DrawingIndex 时 ASK，并明确触发 `cad.query_drawing` 分页查询。
10. 修改/撤销/切图后的 stale 拒绝，以及查询、回合取消和断线 fail-closed。
11. 使用已冻结 fixture 和 Host 遥测完成 M2 1k/10k/50k 扫描响应性、总时间、工作集、
    DBMOD 和 Agent 查询真实性能；自动化资产已完成，实机数值仍待采集。
12. M3 中文对象目录、块详情自动化纵切和 R20.1 API Probe 已完成；19 类对象的逐类实机
    字段核对、脱敏示例图资产、复杂对象语义和高价值受限读取仍待精确 M3 候选后执行。

## 下一步顺序

1. 核心 Agent MVP、P0 停止生命周期已分别提交：`7f10d60`、`8a4ee57`。
2. P1 已完成自动化、冻结和 AutoCAD 2016 live 基线。
3. M0 自动化、候选冻结和本地 `main` 收拢均已完成。
4. M1 代码、自动化和 `0.3.3.0` 候选冻结已完成；精确候选实机矩阵仍待 evidence。
5. M2-A 图纸索引、M2-B `cad.query_drawing`、三档 fixture、性能遥测和脱敏记录器已完成并
   冻结 `0.4.0.0` 候选；等待实机/性能 evidence 后冻结验收预算。
6. M2 实机/性能 evidence 仍是 M2 完成前提；M3 已开始只读对象语义纵切，但不替代 M2
   验收，也不启用 CAD 写入。随后关闭 M4 沙箱与审计；M4 完成前不启用 M5 CAD 写入。
7. M4.14 的 Contracts、Bridge 公开异常、AppServer RPC/data/通用异常及显式分类、AppServer
   服务端请求失败响应出站边界、AgentHost
   未知命令、诊断变体/异常图、AgentLauncher bootstrap 失败边界、`doctor`/`run` 成功状态
   最小化、通用 CLI 失败、协议故障 stderr、bootstrap CLI 错误，以及 Host.2016
   Palette/Bridge 断线/CadQuery 命令行公共错误、AgentHost 审计 CLI 最外层失败、AppServer
   stderr 观察者隔离、AgentRuntime 公共诊断/失败 turn/observer 快照、动态工具校验出站、
   Bridge terminal 安全快照、远端异常错误码归一与数值脱敏证据、Host.2016
   DrawingIndex/CadQuery 通用命令 catch，以及
   AppServer/AgentRuntime/Bridge 敏感 record 字符串和审批 payload 投影纵切已完成；
   剩余公共出口静态审计亦已收口，M4.14 完成。M4.15.1 已完成企业策略阻止进程启动的稳定
   分类、脱敏 Host 提示和正式调用链接线；M4.15.2a 已增加嵌套 Job 分配拒绝的独立稳定错误、
   无 Job 回退禁令和 Host 提示；M4.15.3a 已增加 AgentHost 意外退出的独立稳定终态、资源优先
   竞态和 Host fail-closed 路径；M4.15.3b 已让 STOP/退出主动取消进行中的 AgentHost 启动并
   保持唯一停止终态；M4.15.5a 已让未知/恶意 retention control artifact 显式转人工复核并
   阻止清理；M4.15.5b 已证明受控持久化 I/O 故障的 fail-closed、可恢复和单次收敛边界；
   M4.15.6 已把双 Shell、R20.1 候选哈希、认证/bootstrap、PATH 指纹、秘密/API 扫描和进程残留
   绑定为仅自动化就绪 evidence；真实
   AppLocker/WDAC/EDR、不可嵌套父 Job、真实进程强杀、启动中断、磁盘满、
   系统断电和企业归档环境尚未验证；
   M4.10 磁盘硬配额、M4.11 生产凭据 Broker、M4.13 企业保留策略，以及 M4.15 真实
   Codex/AutoCAD 与企业故障矩阵仍按权威目标推进；M4 完成前不进入 M5 写入调用链。

## 更新纪律

- “已验证”必须同时写明候选身份、验证范围和证据边界。
- 本地 Specs、静态扫描和构建哈希不能替代 AutoCAD 内 NETLOAD 或端到端证据。
- 未验证、失败或因条件缺失跳过的项目必须保留为明确的 `false`/待办。
- 不记录 `TRUSTEDPATHS` 内容、用户名、真实图纸路径、网络路径、许可证或凭据。
