# Codex for AutoCAD 2016 交接说明

长期当前状态索引：先看 `CURRENT_STATE.md`；本文件保留完整交接背景与证据边界。

交接日期：2026-07-18
首次已提交诊断基线：`2d2ad3738095794c8374e916559c0c5d13702ba1`（`feat(host2016): add net45 diagnostic host and load verification`）
适配目标：AutoCAD 2016 原版 R20.1，x64，进程内 .NET Framework 4.5 薄宿主 + 进程外 .NET 8 Agent/Sandbox。

## 先看结论

AutoCAD 2016 工作已完成目标机环境采集、独立诊断宿主建立、真实编译和首次实机加载：

- 已在目标机采集到原版 AutoCAD 2016 R20.1 x64 及其 Autodesk 签名托管程序集，`AcMgd`/`AcDbMgd` 程序集版本均为 `20.1.0.0`。
- 已用目标机原版程序集完成 `Host.2016` 的 `net45/x64` 真实 Release 编译；Autodesk 引用为 `Private=false`，未复制进插件输出。
- 用户已在**原本打开的 AutoCAD 2016 进程**中手工 `NETLOAD` 诊断薄宿主，`CODEXCADDOCTOR` 与 `CODEXCAD` 均可执行；诊断前后 `DBMOD` 为 `21 -> 21`，写入和自动保存均禁用。
- 上述首次诊断宿主及其实机记录已经单独提交为 `2d2ad37`。
- 本次证据快照又完成了 Host.2016 P0 可重复构建门禁：验证脚本 SHA-256 `CF1B23D376EE28520B4AF5C7FC15C0F02631E42DA62C90170FD8D441BC01C1B5`，Host 项目 SHA-256 `93C091263A089C84AE76C91C1E57CCC02D858EEF897762F655594234A1F0F7CE`；PowerShell 7.6.3 与 Windows PowerShell 5.1 均通过，独立重建和两个并行验证进程得到同一 Release DLL SHA-256 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`；输出只有一个 DLL，项目本地 `obj` 与源码树均未被隔离验证修改。
- Host 的签名锁定离线源现仅由 `src/Codex.AutoCAD.Host.2016/NuGet.Config` 管理（SHA-256 `BD61267F69CD5DF2F0996DA881F7BA3531AB4442DC2D6EB861536EC4AB0D0B8E`），项目通过 `RestoreConfigFile` 显式引用它，feed 值为 `..\..\third_party\nuget`。仓库根不放置 Host 专用 `NuGet.Config`；默认主解决方案普通 restore 与 Release build 均通过 `0` warning / `0` error，且没有使用 Host 配置或离线 feed。
- **新哈希 `E853...B440` 尚未执行 NETLOAD，验证 JSON 的 `NetLoadVerified=false`。** 当前 AutoCAD 会话关联的旧诊断候选副本仍保持 SHA-256 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，未被本轮隔离构建覆盖；原始命令记录仍未取得运行时路径/哈希绑定，因此该旧哈希也只能按“测试上下文候选副本”解释。
- Phase 2 的 .NET 8 本地最终门禁快照为 Release `0` warning / `0` error；Contracts `15/15`、IPC `11/11`、Security `19/19`、AppServer `7/7`、Bridge `29/29`、AgentRuntime `31/31`、Chat `9/9`，合计 `121/121`；Bridge 压力复跑 `20 x 29 = 580/580`。AgentHost doctor 通过且无残留进程，diff/秘密扫描通过。这些结果是**非 CAD live 证据**；其提交状态应以当前 Git 历史为准，不证明 Host.2016 已接入 Agent/Bridge。
- 2026-07-19 的跨运行时认证与 Bootstrap 原语门禁已在 PowerShell 7.6.3 和 Windows PowerShell 5.1 下通过：托管核心 Release 隔离构建 `0` warning / `0` error，Bridge `29/29`，net45/net8 IPC Specs 均为 `35/35`，六个隔离主产物逐字节一致，固定 frame、KDF 与双向 HMAC 字节一致，公共 API、MemberRef、关键状态机及完整实现 IL 均已冻结。该结果仍是**非 CAD live、非 AgentHost live** 证据；真实密钥交付、传输机密性、进程身份、硬超时和生命周期保持未验证。

因此当前准确结论是：**AutoCAD 2016 诊断编译/NETLOAD 兼容候选及 100% DPI Palette 运行时检查点已经成立；完整 AutoCAD 2016 支持尚未成立。** Palette 的 125%/150% DPI 和退出生命周期、真实 AgentHost、认证 Bridge、选择上下文、审批及 CAD 事务写入仍未在 AutoCAD 2016 内完成端到端验证。

本次 AutoCAD 命令记录没有回显所加载 DLL 的路径或现场 SHA-256。任何之后重编译得到的临时 DLL 哈希都只能标记为“本地构建产物”，不得反向绑定为当时已 `NETLOAD` DLL 的身份。当前可重复构建哈希 `E853...B440` 正是此类“仅构建候选”，不能继承旧会话的 NETLOAD 结论。

## 当前证据快照

| 范围 | 当前状态 | 证据边界 |
| --- | --- | --- |
| AutoCAD 2016 环境采集 | 已通过 | schema v4；目标安装 `1`、可构建安装 `1`；MSBuild `2` 个，均为有效 Microsoft 签名且受支持版本（当前 `>=17`），并记录 SHA-256 |
| Host.2016 已提交诊断薄宿主 | 已真实编译并由用户实机 NETLOAD | `2d2ad37`；只含诊断命令，不含 Palette/Agent/CAD 写入；现场未绑定 DLL 哈希 |
| Host.2016 当前可重复构建候选 | 静态/构建门禁通过，尚未 NETLOAD | Release SHA-256 `E853...B440`；PS7/PS5.1、独立双构建、两路并行一致；项目局部签名锁定 NuGet 恢复；`NetLoadVerified=false` |
| 诊断只读性 | 已通过当前命令记录 | `DBMOD 21 -> 21`；只覆盖 `CODEXCADDOCTOR`/`CODEXCAD` |
| Palette 100% DPI 运行时 | 已通过冻结候选 | 提交 `56115e4`，DLL SHA-256 `90620E...1DFE`；停靠、浮动、隐藏重开、释放重建、中文换行和干净样本 `DBMOD=4` 已验证 |
| 运行时 DLL 身份绑定 | 未取得 | 旧候选副本 `2E621...0C85` 未被覆盖，但 NETLOAD 现场未记录路径/哈希；新构建 `E853...B440` 未 NETLOAD，二者均不得替代现场绑定 |
| Phase 2 Release 构建 | `0` warning / `0` error | 本地阶段证据；不是 AutoCAD 内构建或加载证据 |
| Phase 2 七个 Specs | `121/121` | Contracts 15、IPC 11、Security 19、AppServer 7、Bridge 29、AgentRuntime 31、Chat 9；不是 Host.2016 live handshake |
| Phase 2 Bridge 压力 | `20 x 29 = 580/580` | 本地受控进程证据；不是 CAD E2E |
| Phase 2 doctor/清洁门禁 | 通过 | AgentHost doctor 无残留；diff/秘密扫描通过；提交状态以 Git 历史为准 |
| net45/.NET 8 Bootstrap 原语 | 双 PowerShell 门禁通过 | net45/net8 `35/35`、Bridge `29/29`、双隔离逐字节一致；不证明 live 传输、AgentHost 或 CAD 集成 |
| AutoCAD 2016 完整产品支持 | 未成立 | 仍缺 Palette 高 DPI/退出生命周期、只读上下文、Agent/Bridge、审批写入和发布验证 |

不要把以上不同层级的证据合并成一个模糊百分比。当前可审计的完成线是“诊断候选通过”，不是“产品完成”。

## 固定架构与安全边界

```text
AutoCAD 2016 / .NET Framework 4.5 / x64
  Codex.AutoCAD.Host.2016（进程内薄宿主）
  - PaletteSet + WPF UI（待实机验证）
  - 选择上下文采集（待实机验证）
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
  普通可旁观 IPC 交付。写端必须关闭句柄，硬超时后关闭句柄并终止未确认子进程。
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

## 下一阶段清单

按顺序推进；每一项都必须先验证，再单独提交 Git：

1. 补做可审计的 NETLOAD 产物身份绑定：加载前记录 DLL SHA-256，命令记录标明同一候选产物；不得用后续重编译哈希补写历史。
2. 补齐 Palette 125%/150% DPI 与 AutoCAD 正常退出生命周期、残留线程/进程验证；100% DPI 下打开、停靠、浮动、隐藏、重开、中文输入和文档切换已建立检查点。
3. 接入只读选择上下文，覆盖 Line、Circle、Polyline、Text/MText、BlockReference，并以 `DBMOD`、实体数及图纸修改状态证明零写入。
4. 启动真实进程外 AgentHost，验证秘密通过 stdin/继承句柄交付、硬超时、停止/退出竞态和无残留进程。
5. 接入 Host.2016 认证 Bridge，完成 live handshake、HMAC、seq、nonce、防重放、配额、结果身份绑定和断线 fail-closed。
6. 接入 CAD 计划与预览；拒绝必须零修改。
7. 接入一次性审批、锁内重校验和单事务直线写入；验证令牌重放、图纸/选择/图层/空间变化全部拒绝。
8. 验证 Agent 中断不自动重试、成功写入后不自动保存、关闭图纸仍由 AutoCAD 正常询问保存。
9. 完成 R4 可恢复检查点、OS/企业沙箱策略、签名与 AppLocker/WDAC/EDR 验证。
10. 生成限制到实际 R20.1 的 AutoCAD 2016 `.bundle`，完成普通用户安装、升级、回滚和干净机验收。

## 最小产品验收门槛

- 普通用户启动 AutoCAD 2016，已绑定哈希的发布候选可加载且无程序集绑定错误。
- 面板可打开、停靠、隐藏、重开；关闭图纸/退出 AutoCAD 无残留线程或进程。
- 只读选择摘要覆盖要求图元，且 `DBMOD`、实体数和图纸修改状态不变。
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
- `handoff/autocad2016/evidence/phase2-local-specs-20260718.json`：Phase 2 Specs 与压力复跑的本地阶段快照；明确不是 AutoCAD 运行证据，提交状态以 Git 历史为准。
- `handoff/autocad2016/evidence/palette-build-verification-20260718.json` 与 `handoff/autocad2016/TEST_REPORT_TEMPLATE.md`：Palette 冻结构建和用户实机检查点；高 DPI 与退出生命周期仍待补证。
- `handoff/autocad2016/evidence/auth-bootstrap-verification-20260719.json`：net45/.NET 8 Bootstrap 原语的双 PowerShell、双隔离构建、固定向量、Specs 与编译边界脱敏证据；明确所有 live AgentHost、传输机密性和 CAD 运行项均未验证。
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
