# Codex for AutoCAD 2016 交接说明

长期当前状态索引：先看 `CURRENT_STATE.md`；本文件保留完整交接背景与证据边界。

交接日期：2026-07-22
首次已提交诊断基线：`2d2ad3738095794c8374e916559c0c5d13702ba1`（`feat(host2016): add net45 diagnostic host and load verification`）
适配目标：AutoCAD 2016 原版 R20.1，x64，进程内 .NET Framework 4.5 薄宿主 + 进程外 .NET 8 Agent/Sandbox。

## 2026-07-22 当前活动快照

- 当前 P1 Worktree 使用 `codex.autocad.cad-context/2` 和 `agent.turn.start.v2`；v2 已接入
  Unified Runtime、Palette 状态、Bridge/AgentHost 测试路径，包含 19 类强类型对象和受限
  unknown/read-failed/limit placeholder。v1 固定向量和历史证据保持不变。
- P1 本机门禁当前为：Contracts `71/71`、Host v2 `12/12`、Host v2 capability policy
  `6/6`、完整 Phase 2 `259/259`、R20.1 net45/x64 Host Release `0` warning / `0` error；
  v2 API Probe 在 PowerShell 7/5.1 均通过，编译期检查全部通过，运行时反射探测为 `19` 通过、
  `8` 失败。
- P1 已增加发送前旧上下文 fail-closed 重校验，但尚无 AutoCAD 实机竞态证据。
- P1 自动化候选已冻结：`artifacts/autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30/`；
  Host SHA-256 `0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7`，
  AgentHost EXE SHA-256 `10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442`。
  该候选现在可供用户人工 NETLOAD，但 `NetLoadVerified=false`，不得当作已通过。
- 该候选的真实 AgentHost/Codex v2 live 规格已通过 `2/2`：认证 capability、
  `agent.turn.start.v2`、同一 thread 两轮合成 v2 上下文、上下文哈希事件绑定和停止后残留
  `0`。这不是 AutoCAD live，不能替代真实选区、Palette 或 `NETLOAD` 验证。
- P0 停止生命周期已由用户完成人工 NETLOAD、三次启停请求、在线/停止状态确认、DBMOD
  `20 -> 20` 和残留进程 `0` 验证，独立提交为 `8a4ee57`。
- P0 完整候选为 `autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd`；P1 已在该提交
  基础上完成受控集成，候选冻结提交为 `c174166`。当前线又包含采集器收口提交
  `5325e35` 和证据提交 `3ea4961`。集成规则见 `P0_TO_P1_CONTROLLED_INTEGRATION.md`。
- 环境采集器 `Location` 支持已在提交 `5325e35` 受控引入；当前机器 PS7/PS5.1 自测均为
  `24/24`，真实只读采集均发现 `1` 个可构建 R20.1 安装。该项不涉及 AutoCAD 启动或修改。

## 先看结论

AutoCAD 2016 工作已完成目标机环境采集、独立诊断宿主建立、真实编译和首次实机加载：

- 已在目标机采集到原版 AutoCAD 2016 R20.1 x64 及其 Autodesk 签名托管程序集，`AcMgd`/`AcDbMgd` 程序集版本均为 `20.1.0.0`。
- 已用目标机原版程序集完成 `Host.2016` 的 `net45/x64` 真实 Release 编译；Autodesk 引用为 `Private=false`，未复制进插件输出。
- 用户已在**原本打开的 AutoCAD 2016 进程**中手工 `NETLOAD` 诊断薄宿主，`CODEXCADDOCTOR` 与 `CODEXCAD` 均可执行；诊断前后 `DBMOD` 为 `21 -> 21`，写入和自动保存均禁用。
- 上述首次诊断宿主及其实机记录已经单独提交为 `2d2ad37`。
- 本次证据快照又完成了 Host.2016 P0 可重复构建门禁：验证脚本 SHA-256 `CF1B23D376EE28520B4AF5C7FC15C0F02631E42DA62C90170FD8D441BC01C1B5`，Host 项目 SHA-256 `93C091263A089C84AE76C91C1E57CCC02D858EEF897762F655594234A1F0F7CE`；PowerShell 7.6.3 与 Windows PowerShell 5.1 均通过，独立重建和两个并行验证进程得到同一 Release DLL SHA-256 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`；输出只有一个 DLL，项目本地 `obj` 与源码树均未被隔离验证修改。
- Host 的签名锁定离线源现仅由 `src/Codex.AutoCAD.Host.2016/NuGet.Config` 管理（SHA-256 `BD61267F69CD5DF2F0996DA881F7BA3531AB4442DC2D6EB861536EC4AB0D0B8E`），项目通过 `RestoreConfigFile` 显式引用它，feed 值为 `..\..\third_party\nuget`。仓库根不放置 Host 专用 `NuGet.Config`；默认主解决方案普通 restore 与 Release build 均通过 `0` warning / `0` error，且没有使用 Host 配置或离线 feed。
- **新哈希 `E853...B440` 尚未执行 NETLOAD，验证 JSON 的 `NetLoadVerified=false`。** 当前 AutoCAD 会话关联的旧诊断候选副本仍保持 SHA-256 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，未被本轮隔离构建覆盖；原始命令记录仍未取得运行时路径/哈希绑定，因此该旧哈希也只能按“测试上下文候选副本”解释。
- 当前 Bridge Client 阶段的 Phase 2 回归为 Release `0` warning / `0` error；Contracts `27/27`、IPC `35/35`、Security `19/19`、AppServer `7/7`、Bridge `34/34`、Bridge Client `22/22`、AgentRuntime `31/31`、Chat `9/9`，合计 `184/184`。公共契约检查点的历史快照仍为 `157/157`，AgentHost 安全引导检查点仍为 `145/145`，不得反向改写；此前 Bridge 压力复跑 `20 x 29 = 580/580` 也只保留为历史证据。以上仍是**非 CAD live 证据**，不证明 Host.2016 已接入 Agent/Bridge。
- 2026-07-19 的跨运行时认证与 Bootstrap 原语门禁已在 PowerShell 7.6.3 和 Windows PowerShell 5.1 下通过：托管核心 Release 隔离构建 `0` warning / `0` error，Bridge `29/29`，net45/net8 IPC Specs 均为 `35/35`，六个隔离主产物逐字节一致，固定 frame、KDF 与双向 HMAC 字节一致，公共 API、MemberRef、关键状态机及完整实现 IL 均已冻结。该结果仍是**非 CAD live、非 AgentHost live** 证据；真实密钥交付、传输机密性、进程身份、硬超时和生命周期保持未验证。
- 真实进程外 AgentHost 的有界 bootstrap-doctor 检查点已在 PowerShell 7.6.3 和 Windows PowerShell 5.1.19041.6456 下通过：net45/net8 Launcher 均为精确强制 ID 集 `15/15`，每套门禁两次隔离 Release 构建，`106` 个可运行输出文件逐字节一致且在 Specs 后复核未变化。Bootstrap 密钥和 frame 只通过受限继承的标准句柄交付，不进入命令行、环境变量、日志、命名管道或内存映射；批准 EXE SHA-256、确认 PID/创建时间、启动截止触发 fail-closed 中止并随后进行最多 `5` 秒有界终止清理、取消、确认后挂起、句柄 allowlist canary、继承位清除、stderr 限界/脱敏和 `0 -> 0` 残留进程均取得动态证据。该检查点不声称进程终止本身严格完成于配置的启动截止内；没有启动或操作 AutoCAD。长运行 `IAgentBridgeClient`、完整传输机密性、外部句柄复制抵抗、刻意 suspended-launch TOCTOU 攻击、Host.2016 live Bridge 和 CAD 集成仍未验证。
- 独立只读选择上下文已建立冻结候选和用户实机检查点：基线 `2036fd6`、分支 `codex/selection2016-readonly-v2`，DLL SHA-256 `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`。用户在原本打开的 AutoCAD 2016 中手工 NETLOAD 后，预选 Line、Circle、Polyline、DBText、MText、BlockReference 各 `1` 个，得到 `status=published-read-only`、`selected=6`、canonical bytes `738` 和 `DBMOD 4 -> 4`；用户清除后 `published=false`、`selected=0`、`DBMOD 4 -> 4`。另一个受控文档切换样本中，文档激活事件清除了旧缓存，切换前原图和切换后目标图命令行 `DBMOD` 均为 `21`。Selection/context hash 按脱敏策略不入库。
- CadContextJson v1 与 Host/Agent/UI 公共契约已完成冻结候选：schema `codex.autocad.cad-context`、schemaVersion `1`，六类图元使用显式强类型字段，canonical UTF-8 固定向量为 `2225` 字节、SHA-256 `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423`。PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 下，net45/net8 Contracts Specs 均为 `27/27`，两次隔离 Release 构建逐字节一致，Phase 2 回归 `157/157`。契约同时冻结能力协商、thread/turn、assistant 事件、错误闭集、上下文身份回显与仅一次审批；尚未接入统一 Host.2016、Palette 或 live Bridge。
- 具体 `IAgentBridgeClient` 已完成跨运行时检查点：PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过两次隔离确定性构建；net45/net8 Client 各 `22/22` 且输出一致，Bridge `34/34`，Phase 2 `184/184`。已验证 thread/turn/context 身份绑定、assistant 事件、合法 turn 终态消费及迟到事件拒绝、HMAC/sequence/nonce/防重放、严格 JSON/帧拒绝以及离线、断线、超时、取消和释放 fail-closed。当前又已形成 Bridge -> AgentHost -> 真实 Codex 的 v2 两轮 live 证据，但仍未形成 AutoCAD Host.2016 -> Bridge -> AgentHost -> Codex 的 P1 v2 实机链路。

因此当前准确结论是：**P0 停止生命周期已完成并实机通过；P1 CadContextJson v2 已完成代码、非 CAD 门禁、R20.1 构建和候选冻结，但尚未完成 AutoCAD 2016 实机 NETLOAD。** P1 仍待真实混合选区、Palette v2 字段、v2 Agent 两轮对话、文档切换、DBMOD/保存边界、高 DPI 与退出生命周期验证；CAD 写入、审批、沙箱和安装发布仍未进入。

本次 AutoCAD 命令记录没有回显所加载 DLL 的路径或现场 SHA-256。任何之后重编译得到的临时 DLL 哈希都只能标记为“本地构建产物”，不得反向绑定为当时已 `NETLOAD` DLL 的身份。当前可重复构建哈希 `E853...B440` 正是此类“仅构建候选”，不能继承旧会话的 NETLOAD 结论。

## 当前证据快照

| 范围 | 当前状态 | 证据边界 |
| --- | --- | --- |
| AutoCAD 2016 环境采集 | 已通过 | schema v4；目标安装 `1`、可构建安装 `1`；MSBuild `2` 个，均为有效 Microsoft 签名且受支持版本（当前 `>=17`），并记录 SHA-256 |
| Host.2016 已提交诊断薄宿主 | 已真实编译并由用户实机 NETLOAD | `2d2ad37`；只含诊断命令，不含 Palette/Agent/CAD 写入；现场未绑定 DLL 哈希 |
| Host.2016 当前可重复构建候选 | 静态/构建门禁通过，尚未 NETLOAD | Release SHA-256 `E853...B440`；PS7/PS5.1、独立双构建、两路并行一致；项目局部签名锁定 NuGet 恢复；`NetLoadVerified=false` |
| 诊断只读性 | 已通过当前命令记录 | `DBMOD 21 -> 21`；只覆盖 `CODEXCADDOCTOR`/`CODEXCAD` |
| Palette 100% DPI 运行时 | 已通过冻结候选 | 提交 `56115e4`，DLL SHA-256 `90620E...1DFE`；停靠、浮动、隐藏重开、释放重建、中文换行和干净样本 `DBMOD=4` 已验证 |
| 独立只读选择上下文运行时 | 已通过冻结候选 | 基线 `2036fd6`，DLL SHA-256 `AB3132...E6B8`；六类对象各 `1`、`selected=6`、canonical bytes `738`、捕获/清除 `DBMOD 4 -> 4`、文档激活清缓存已验证；实体总数与插件自动保存未单独运行时验证 |
| 旧诊断薄宿主历史运行时 DLL 身份绑定 | 未取得 | 旧候选副本 `2E621...0C85` 未被覆盖，但 NETLOAD 现场未记录路径/哈希；诊断重建 `E853...B440` 未 NETLOAD。该缺口不适用于已绑定的 Palette 与 Selection 冻结候选 |
| Phase 2 Release 构建 | `0` warning / `0` error | 本地阶段证据；不是 AutoCAD 内构建或加载证据 |
| 当前 Phase 2 八个 Specs | `184/184` | Contracts 27、IPC 35、Security 19、AppServer 7、Bridge 34、Bridge Client 22、AgentRuntime 31、Chat 9；公共契约和 AgentHost 旧检查点分别保留历史 `157/157`、`145/145`；不是 Host.2016 live handshake |
| Phase 2 Bridge 压力 | `20 x 29 = 580/580` | 本地受控进程证据；不是 CAD E2E |
| Phase 2 doctor/清洁门禁 | 通过 | AgentHost doctor 无残留；diff/秘密扫描通过；提交状态以 Git 历史为准 |
| net45/.NET 8 Bootstrap 原语 | 双 PowerShell 门禁通过 | net45/net8 `35/35`、Bridge `29/29`、双隔离逐字节一致；不证明 live 传输、AgentHost 或 CAD 集成 |
| 真实进程外 AgentHost 安全引导 | 双 PowerShell 有界门禁通过 | net45/net8 Launcher `15/15`；受限继承句柄、批准 EXE 哈希、确认身份、启动截止 fail-closed 中止及最多 `5` 秒有界清理、取消、stderr 限界和 `0 -> 0` 残留通过；长运行 Bridge、外部句柄复制抵抗、刻意 TOCTOU 攻击和 CAD 集成未验证 |
| CadContextJson v1 / 公共契约 | 双 PowerShell、net45/net8 门禁通过 | net45/net8 `27/27`；canonical `2225` 字节、SHA-256 `c5a03d...4423`；Phase 2 `157/157`；未构建或 NETLOAD 统一 Host.2016 |
| 具体 IAgentBridgeClient | 自动化门禁与真实 Codex v2 live 通过 | net45/net8 `22/22`、Bridge `34/34`、历史 Phase 2 `184/184`；当前 AgentHost/Codex v2 两轮 `2/2`、哈希绑定和残留 `0`；仍未连接 P1 AutoCAD Host |
| P0 AgentHost 停止生命周期 | 已提交并实机通过 | `8a4ee57`；Host `1CC9...478C`、AgentHost `8E6B...4FA2`；用户确认多次启停、DBMOD 不变、残留 `0` |
| P1 CadContextJson v2 候选 | 自动化冻结，等待 AutoCAD 实机 | Host `0D72ED...1D0AD7`；AgentHost `10BEA3...A65442`；Phase 2 `259/259`；真实 AgentHost/Codex v2 两轮 `2/2`；退出重试 `24/24`；`NetLoadVerified=false` |
| AutoCAD 2016 完整产品支持 | 未成立 | 仍缺 P1 统一 Host 实机、Palette 高 DPI/退出生命周期、AutoCAD Host-Agent-Codex v2 live 链路、审批写入和发布验证 |

不要把以上不同层级的证据合并成一个模糊百分比。历史 v1 AutoCAD happy path 和 P0 停止生命周期已有实机证据；本次新增的是非 AutoCAD 的真实 AgentHost/Codex v2 live。它仍不能替代 P1 统一 Host NETLOAD、真实混合选区、Palette v2 和端到端 AutoCAD v2 对话。

## 固定架构与安全边界

```text
AutoCAD 2016 / .NET Framework 4.5 / x64
  Codex.AutoCAD.Host.2016（进程内薄宿主）
  - PaletteSet + WPF UI（待实机验证）
  - 选择上下文采集（独立 sidecar 检查点已实机验证；待正式宿主/UI 集成）
  - DocumentLock / Transaction / Preview（待接入 2016）
  - 审批后的最小 CAD 执行器（待接入 2016）
                |
                | 当前用户命名管道 + HMAC + seq + nonce
                v
进程外 / .NET 8
  Bridge + AgentHost + Agent Runtime + AppServer
  - 模型会话与上下文记忆
  - MCP / 动态工具
  - 文件与进程沙箱
  - 审批策略、审计与配额
```

以下边界在后续适配中不得弱化：

- Bootstrap frame 明文携带 session secret，HMAC 只证明完整性、不提供机密性。真实启动
  只能使用专用、独占、受限继承且具机密性的句柄；禁止通过命令行、环境变量、日志或
  普通可旁观 IPC 交付。写端必须关闭句柄；启动截止触发 fail-closed 中止后，必须在
  最多 `5` 秒有界清理窗口内关闭句柄并证明未确认子进程已终止。
- CAD 写入必须保持“计划 -> 预览 -> 一次审批 -> 锁内重校验 -> 单事务执行”。
- 审批只有“一次允许”和“拒绝”，不得增加会话级永久允许。
- HMAC、严格递增序号、nonce、防重放、结果身份绑定和 fail-closed 语义必须保持。
- 写入前必须在 `DocumentLock` 内重新校验图纸、revision、选择摘要、图层和空间。
- Agent 断开、超时或结果不确定时不得自动重试 CAD 写入。
- 插件不得自动保存 DWG；是否保存始终由用户通过 AutoCAD 决定。
- 不得关闭或自动降低 `SECURELOAD`，不得脚本化修改企业受信路径或注册表策略。
- 不得把 AutoCAD 2025 宿主或 2025 Autodesk 托管程序集加载/复制到 2016。

## 已完成的 AutoCAD 2016 阶段

1. 已采集并锁定目标机 AutoCAD 2016 R20.1、x64、`acad.exe` 文件版本 `R20.1.49.0.0`、Autodesk 有效签名和托管 API 版本。
2. 已建立独立 `src/Codex.AutoCAD.Host.2016`，目标 `net45/x64`。
3. 已使用原版 2016 托管程序集真实编译最小 `IExtensionApplication` 诊断宿主。
4. 已在用户当前打开的 AutoCAD 2016 中手工 `NETLOAD`，没有观察到程序集绑定错误。
5. 已运行 `CODEXCADDOCTOR`/`CODEXCAD`，确认进程 x64、CLR `4.0.30319.42000`、API `20.1.0.0`、写入禁用、自动保存禁用以及 `DBMOD 21 -> 21`。
6. 已通过 Host.2016 隔离 Release 重编译、签名/版本、引用、命令面、禁止 API、输出引用和 Autodesk DLL 不复制等静态门禁。
7. 已通过当前 Host.2016 P0 可重复构建复核：SDK `8.0.319`，PS7/PS5.1 结果一致，独立与并行构建均产生 SHA-256 `E853...B440`；IL 精确门禁为 `31` 个 MemberRef、`7` 个 MethodDef、两个命令注册属性，且 Save/DxfOut/Quit/Invoke、注释伪装、Directory.Build 注入和恶意根 NuGet 配置注入负向测试通过。
8. 已将 Host 离线 feed 限定在项目局部 `NuGet.Config`，并验证默认 `Codex.AutoCAD.sln` 普通 restore/build 不读取该配置、不使用该 feed、不构建或修改 Host.2016。
9. 已确认本轮验证没有覆盖旧诊断候选副本 `2E621...0C85`，也没有启动、重启或操作 AutoCAD；新候选保持 `NetLoadVerified=false`。
10. 已将首次诊断宿主阶段单独提交为 `2d2ad37`；本次可重复构建候选不绑定到该旧提交，应随对应阶段验证结果单独提交，并以包含本证据的 Git 历史为准。
11. 已建立 Palette 运行时检查点：提交 `56115e4`、冻结 DLL SHA-256 `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`；用户已在原有 AutoCAD 2016 进程验证 100% DPI 下停靠、浮动、隐藏重开、释放重建、中文换行和干净样本 `DBMOD=4`。125%/150% DPI 与退出生命周期仍待验证。
12. 已完成跨运行时认证与 Bootstrap 协议原语门禁：SDK `8.0.319`，PS7/PS5.1 均通过，net45/net8 `35/35`、Bridge `29/29`，固定向量和编译后 API/MemberRef/IL 边界已冻结；全过程未启动、重启或操作 AutoCAD，live AgentHost 与传输机密性仍为未验证。
13. 已建立独立只读选择上下文运行时检查点：冻结 DLL SHA-256 `AB3132...E6B8`，用户手工 NETLOAD 后验证六类白名单图元各 `1`、`selected=6`、canonical bytes `738`、捕获和清除全过程 `DBMOD 4 -> 4`，并在另一个受控样本中验证文档激活事件清除旧缓存。首次无预选的 `validation-no-implied-selection` 是前置 `DBMOD` 取消预选后触发的预期 fail-closed；实体总数和插件自动保存未单独取得运行时证据。
14. 已完成真实进程外 AgentHost 的有界安全引导门禁：PowerShell 7/5.1、net45/net8 Launcher `15/15`、两次隔离构建及 `106` 文件输出树逐字节一致；受限继承句柄交付、批准 EXE SHA-256、确认 PID/创建时间、启动截止 fail-closed 中止及随后最多 `5` 秒有界终止清理、取消、确认后挂起、句柄 canary、stderr 限界和无残留进程均通过。该门禁不声称终止严格发生在配置的启动截止内，不操作 AutoCAD，也不证明长运行 Bridge、外部句柄复制抵抗、刻意 suspended-launch TOCTOU 攻击或 CAD 集成。

## 下一阶段清单

按顺序推进；每一项都必须先验证，再单独提交 Git：

1. 用户人工 NETLOAD P1 v2 候选，完成混合选区、unknown placeholder、Palette JSON、v2 Agent 两轮、文档切换和 DBMOD 不变验证。
2. 补齐 Palette 125%/150% DPI、AutoCAD 正常退出生命周期和残留线程/进程验证。
3. 补测 AgentHost 离线、断线、超时以及 v2 上下文过期 fail-closed。
4. 只读 MVP 运行门槛通过后，接入 CAD 计划/预览、一次性审批、锁内重校验和单事务写入。
5. 再进行 R4、企业沙箱、签名、`.bundle` 安装和发布验收。

## 最小产品验收门槛

- 普通用户启动 AutoCAD 2016，已绑定哈希的发布候选可加载且无程序集绑定错误。
- 面板可打开、停靠、隐藏、重开；关闭图纸/退出 AutoCAD 无残留线程或进程。
- 只读选择摘要覆盖要求图元且 `DBMOD` 不变；独立 sidecar 的六类图元和缓存清除已通过，实体总数独立计量及正式宿主/UI 集成仍待完成。
- AgentHost 离线时 UI 可解释地失败，不崩溃、不自动执行 CAD、不回退到未认证通道。
- “拒绝”后图纸、实体数、revision 和 `DBMOD` 均不变化。
- “一次允许”只执行展示过且锁内重校验仍一致的计划；令牌重放及上下文变化必须拒绝。
- AgentHost 在执行中断开时禁止自动重试 CAD 写入。
- 成功写入后不自动保存，关闭图纸仍由 AutoCAD 正常询问是否保存。
- 全部安全 Specs、Host 禁止 API 扫描、阶段验证脚本和实机测试矩阵通过。

完成以上门槛之前，只能使用“AutoCAD 2016 诊断编译/NETLOAD 兼容候选”这一表述，不得宣称正式或完整支持 AutoCAD 2016。

## 证据索引

- `handoff/autocad2016/evidence/autocad2016-diagnostic-netload-20260718.json`：脱敏的用户实机诊断记录；明确未取得运行时 DLL 哈希绑定。
- `handoff/autocad2016/evidence/environment-collector-20260718.json`：schema v4 环境采集器的脱敏计数与门禁结果。
- `handoff/autocad2016/evidence/host-build-verification-20260718.json`：当前 Host.2016 P0 可重复构建、项目局部 NuGet 作用域、默认主解决方案恢复隔离、工具签名、IL 白名单、并行隔离及负向测试的脱敏证据；明确 `E853...B440` 尚未 NETLOAD，且旧候选副本未被覆盖。
- `handoff/autocad2016/evidence/phase2-local-specs-20260718.json`：早期 `121/121` Phase 2 本地历史快照；明确不是当前增强门禁或 AutoCAD 运行证据。
- `handoff/autocad2016/evidence/phase2-guardrail-verification-20260718.json`：已提交的增强门禁正向证据，记录 IPC `17/17`、七个 Specs `127/127`，以及不安全 Host.2025 原型被双 PowerShell 负向门禁拒绝。
- `handoff/autocad2016/evidence/palette-build-verification-20260718.json` 与 `handoff/autocad2016/TEST_REPORT_TEMPLATE.md`：Palette 冻结构建和用户实机检查点；高 DPI 与退出生命周期仍待补证。
- `handoff/autocad2016/evidence/auth-bootstrap-verification-20260719.json`：net45/.NET 8 Bootstrap 原语的双 PowerShell、双隔离构建、固定向量、Specs 与编译边界脱敏证据；明确所有 live AgentHost、传输机密性和 CAD 运行项均未验证。
- `handoff/autocad2016/evidence/agent-bootstrap-verification-20260719.json`：由 `scripts/verify-autocad2016-agent-bootstrap-stage.ps1` 自动生成并绑定双 PowerShell bootstrap/auth raw evidence、Phase2 raw log 与四个验证器 SHA-256；记录精确 Spec ID、受限继承句柄、批准 EXE 哈希、确认身份、启动截止 fail-closed 中止及随后最多 `5` 秒有界终止清理、输出树可重复性和无残留进程证据；明确长运行 Bridge、外部句柄复制抵抗、刻意 TOCTOU 动态攻击和 CAD 集成未验证。
- `handoff/autocad2016/evidence/readonly-context-build-verification-20260718.json`：只读选择上下文的静态/双 PowerShell 可重复构建证据，以及 2026-07-19 对冻结 `AB3132...E6B8` 候选的脱敏用户实机检查点；Selection/context hash 不落库，冻结 DLL SHA-256 正常入库，实体总数与插件自动保存 runtime 布尔值保持 `false`。
- `handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md` 与 `handoff/autocad2016/evidence/cad-context-contract-v1-verification-20260719.json`：CadContextJson v1、Host/Agent/UI 方法/事件/错误/审批契约及双 PowerShell、net45/net8 `27/27`、Phase 2 `157/157` 的冻结证据；明确统一 Host.2016、Palette、live Bridge 和 AutoCAD 对话尚未验证。
- `handoff/autocad2016/evidence/bridge-client-stage-verification-20260720.json`：具体 net45/net8 `IAgentBridgeClient` 的双 PowerShell、双隔离确定性构建、各 `22/22`、Bridge `34/34`、Phase 2 `184/184`、turn 终态消费/迟到事件拒绝、doctor、diff、秘密扫描和无残留 TestServer 脱敏证据；明确 `AutoCadLiveEvidence=false`，不证明统一 Host、长运行 AgentHost 或真实 Codex CAD 对话。
- `handoff/autocad2016/evidence/agent-stop-live-observation-20260722.json`：P0 停止生命周期用户实机证据；不继承给 P1。
- `handoff/autocad2016/evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-4d3386d9-751b97c7-7216527a.json`：P1 候选自动化构建、双重 R20.1 Host 输出、AgentHost 发布、manifest、235/235 门禁和 v2 API Probe 边界；明确 `NetLoadVerified=false`、`AutoCadLiveEvidence=false`。
- `handoff/autocad2016/evidence/environment-collector-location-verification-20260722.json`：当前机器 PS7/PS5.1 环境采集器自测 `24/24`、只读真实采集结果和脱敏边界。
- `handoff/autocad2016/evidence/cad-context-v2-candidate-package-doctor-20260722.json`：P1 候选包内 AgentHost doctor 成功加载并完成本机 Codex app-server 初始化；不等同于 AutoCAD live 证据。
- `handoff/autocad2016/evidence/cad-context-v2-candidate-package-doctor-20260722-refresh.json`：新 P1 候选包的 AgentHost doctor 结果；不等同于 AutoCAD live 证据。
- `handoff/autocad2016/evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30.json`：最新 P1 候选的 R20.1 双构建、AgentHost 发布、259/259 门禁和哈希身份；明确 `NetLoadVerified=false`、`AutoCadLiveEvidence=false`。
- `handoff/autocad2016/evidence/host2016-terminate-exit-retry-20260722.json`：AutoCAD 正常退出清理重试的 24/24 自动化证据；不等同于 AutoCAD 退出实机证据。
- `handoff/autocad2016/evidence/phase2-final-gate-20260722-exit-retry.json`：纳入 Host.2016.Mvp.Specs 后的 9 个规格项目、259/259、Release、禁用 API、doctor、秘密扫描和 diff 门禁汇总。
- `handoff/autocad2016/evidence/agenthost-v2-live-two-turns-20260722.json`：冻结 P1 候选的
  真实 AgentHost/Codex v2 capability、同一 thread 两轮合成上下文、哈希绑定、assistant
  完成事件和残留 `0` 证据；明确 `autoCadLive=false`。
- `handoff/autocad2016/evidence/agenthost-v2-live-two-turns-20260722-refresh.json`：最新候选
  `0d72edc3-10bea363-af580c30` 的真实 AgentHost/Codex v2 两轮 live 证据；明确 `autoCadLive=false`。
- `handoff/autocad2016/evidence/phase2-final-gate-20260722.json`：采集器集成后当前线重新执行的 Release、235/235 Specs、Host 禁用 API、doctor、秘密扫描和 diff 门禁汇总。
- `handoff/autocad2016/TEST_REPORT_TEMPLATE.md`：当前实机记录与剩余测试矩阵。

证据中不得写入 `TRUSTEDPATHS` 内容、用户名、真实图纸路径或网络路径。现场需要保存受信路径时，只保留在企业内部受控记录，不提交到 Git。

## 官方兼容依据

- Autodesk 列出 AutoCAD 2015/2016 的开发目标为 Visual Studio 2012/2013 与 .NET Framework 4.5：<https://help.autodesk.com/cloudhelp/2017/CSY/AutoCAD-NET/files/GUID-450FD531-B6F6-4BAE-9A8C-8230AAC48CB4.htm>
- AutoCAD 2016 系统要求列出 .NET Framework 4.5：<https://help.autodesk.com/view/ACADWEB/ENU/?caas=caas%2Fsfdcarticles%2Fsfdcarticles%2FSystem-requirements-for-AutoCAD-2016.html>
- AutoCAD 2016 起建议签名，并需满足安全加载要求：<https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-Customization/files/GUID-5E50A846-C80B-4FFD-8DD3-C20B22098008.htm>
- `.bundle` 的 `RuntimeRequirements` 应通过 `SeriesMin`/`SeriesMax` 限制到目标系列：<https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-Customization/files/GUID-1591CA01-EF87-48CD-952B-772FE26037F1.htm>

## 交接责任边界

目标机负责提供真实 AutoCAD 2016 环境和脱敏测试 DWG；源码仍以本仓库为唯一来源。任何实机发现都应记录机器环境、候选提交、候选 DLL 身份、复现步骤、完整错误、预期/实际行为、脱敏日志和是否修改了 DWG。

不要把公司图纸、账号、API Key、许可证数据、完整用户路径、`TRUSTEDPATHS` 内容或内部服务器地址提交到 Git。
