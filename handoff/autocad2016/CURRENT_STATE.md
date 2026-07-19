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
- 该检查点只证明内存/Stream 协议原语。它没有启动或操作 AutoCAD，也没有证明真实
  AgentHost 密钥交付、传输机密性、PID/启动身份绑定、硬超时、进程生命周期或 live
  Bridge；这些项目在 evidence 中保持 `false`。

## 已构建但尚未取得实机结论

### 只读选择候选

- 分支提交：`71c2204`（`feat(host2016): add reproducible read-only selection candidate`）。
- 当前只表示源码/构建候选；尚未冻结最终 NETLOAD 清单，也未在 AutoCAD 2016
  中验证 Line、Circle、Polyline、Text/MText、BlockReference 或全过程 DBMOD。
- 在修正、重新构建、冻结哈希并给出明确测试清单之前，不安排用户加载。

## 当前活动阶段

### 只读选择候选修正与真实 AgentHost 引导准备

- 只读选择分支提交 `71c2204` 仍只是旧构建候选；在修正、重新构建、冻结哈希并给出
  明确命令清单前，不通知用户 NETLOAD。
- 下一条 live 链路必须使用本检查点的固定 bootstrap frame/KDF/HMAC 语义，但不得把
  明文 frame 放入命令行、环境变量、日志或普通可旁观 IPC。
- 真实启动设计仍需证明专用、独占、受限继承句柄、写端关闭、原子领取、PID/启动
  身份绑定、硬超时和未确认子进程终止；在这些验证完成前 AgentHost live 状态为未通过。

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

## 待实机验证队列

用户已于 2026-07-19 开放实机测试窗口。只有候选完成真实编译、冻结 SHA-256 并准备好
完整命令清单后才请求测试；仍不得由 Codex 启动、唤醒、关闭或重启 AutoCAD。当前队列：

1. Palette 125% DPI。
2. Palette 150% DPI。
3. Palette 随 AutoCAD 正常退出的生命周期和残留检查。
4. 修正并重新冻结后的只读选择候选 NETLOAD 与五类图元读取。
5. 后续真实 AgentHost 启动、live handshake、离线/断线/超时 fail-closed。
6. 最后才进入预览、拒绝、一次允许、锁内重校验和单事务写入实测。

## 下一步顺序

1. 修正只读选择候选，重新通过 Host.2016 禁止 API、隔离构建和 DBMOD 测试设计门禁。
2. 冻结选择候选 DLL SHA-256 和明确 NETLOAD/命令清单后，请用户做五类图元实机测试。
3. 实现专用继承句柄上的真实 AgentHost bootstrap、原子领取、进程身份和硬超时门禁。
4. 接通 Host.2016 live handshake、离线/断线/超时 fail-closed，再请求对应实机验证。
5. 最后才进入预览、拒绝、一次允许、锁内重校验和单事务写入闭环。

## 更新纪律

- “已验证”必须同时写明候选身份、验证范围和证据边界。
- 本地 Specs、静态扫描和构建哈希不能替代 AutoCAD 内 NETLOAD 或端到端证据。
- 未验证、失败或因条件缺失跳过的项目必须保留为明确的 `false`/待办。
- 不记录 `TRUSTEDPATHS` 内容、用户名、真实图纸路径、网络路径、许可证或凭据。
