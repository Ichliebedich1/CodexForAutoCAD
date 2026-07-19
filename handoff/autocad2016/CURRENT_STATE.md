# AutoCAD 2016 当前状态索引

最后更新：2026-07-19（北京时间）

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
- 冻结构建哈希：AgentHost EXE `002BBA9D...49706`，AgentHost DLL
  `852BD92C...86033`，net45 Launcher `597D99E8...F849`，net8 Launcher
  `84E0E2A7...1FE9`；完整值保存在阶段 evidence。
- Phase 2 回归为 Release `0` warning / `0` error、七个既有 Specs `145/145`、
  AgentHost doctor、Host 禁止 API、秘密扫描和 diff 通过；认证兼容回归在两个 PowerShell
  下均保持 Bridge `29/29`、net45/net8 `35/35` 和固定向量一致。
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

## 当前活动阶段

### Host.2016 认证 Bridge 与正式侧边栏决策门

- 只读选择上下文已建立独立运行时检查点；它尚未与 Palette、AgentHost 或正式侧边栏
  合并，不能扩大为完整只读产品或 AutoCAD 2016 完整支持。
- 真实进程外 bootstrap-doctor 已建立安全引导检查点；下一条 live 链路必须复用已验证的
  固定 frame/KDF/HMAC 和受限继承句柄语义，不得回退到命令行、环境变量、日志或普通
  可旁观 IPC 交付密钥。
- 当前尚未完成的是长运行 AgentHost 与具体 `IAgentBridgeClient`、Host.2016 认证 Bridge、
  断线/离线/超时 fail-closed 和结果身份绑定；这些项目通过前不得把 bootstrap-doctor
  扩大表述为 Agent/Bridge 已接入 CAD。
- 在开始实现或合并正式侧边栏 UI 前，必须先通知用户，并冻结 Codex 与 Kimi 共同遵守的
  版本化契约，包括事件模型、请求/响应、只读上下文字段、审批状态、错误语义和兼容规则。
  决策门通过后，两端才可按同一契约并行开发；不得先各自实现再用 UI 隐式决定协议。

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
- 正式侧边栏 UI 的开发必须经过“通知用户 + 冻结共同契约”的显式决策门。

## 待实机验证队列

用户已于 2026-07-19 开放实机测试窗口。只有候选完成真实编译、冻结 SHA-256 并准备好
完整命令清单后才请求测试；仍不得由 Codex 启动、唤醒、关闭或重启 AutoCAD。当前队列：

1. Palette 125% DPI。
2. Palette 150% DPI。
3. Palette 随 AutoCAD 正常退出的生命周期和残留检查。
4. Host.2016 与长运行 AgentHost 的 live handshake、离线/断线/超时 fail-closed。
5. 最后才进入预览、拒绝、一次允许、锁内重校验和单事务写入实测。

## 下一步顺序

1. 将已通过的真实 AgentHost 安全引导检查点按验证后单独提交的纪律收口。
2. 接通 Host.2016 与长运行 AgentHost 的认证 Bridge、结果身份绑定及离线/断线/超时
   fail-closed，再请求对应实机验证。
3. 在正式侧边栏 UI 开工前通知用户并冻结 Codex/Kimi 共同契约，再按契约并行开发。
4. 将已验证的只读选择上下文按冻结契约接入正式宿主/UI。
5. 最后才进入预览、拒绝、一次允许、锁内重校验和单事务写入闭环。

## 更新纪律

- “已验证”必须同时写明候选身份、验证范围和证据边界。
- 本地 Specs、静态扫描和构建哈希不能替代 AutoCAD 内 NETLOAD 或端到端证据。
- 未验证、失败或因条件缺失跳过的项目必须保留为明确的 `false`/待办。
- 不记录 `TRUSTEDPATHS` 内容、用户名、真实图纸路径、网络路径、许可证或凭据。
