# AutoCAD 2016 实机测试记录

## 证据来源与适用边界

本报告严格分开四类证据：

1. **用户实机命令记录**：用户在目标机已打开的原版 AutoCAD 2016 命令行中手工执行 `NETLOAD`、`CODEXCADDOCTOR`、`CODEXCAD` 和 `DBMOD`。
2. **环境采集器证据**：schema v4 只读采集；不启动 AutoCAD、不读取 `TRUSTEDPATHS` 内容。
3. **Host.2016 静态/构建门禁**：隔离 Release 重编译和签名、版本、引用、禁止 API 等验证；脚本不执行 NETLOAD。
4. **Phase 2 本地规格证据**：七个 Specs、Bridge 压力、AgentHost doctor、diff 与秘密扫描的本地阶段快照；未进入 AutoCAD，提交状态以 Git 历史为准。

本次实机命令记录没有回显所加载 DLL 的路径或现场 SHA-256。因此：

- `candidateDllSha256FromTestingContext` 记录为 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，仅表示测试上下文中的候选 DLL。
- `loadedDllSha256` 必须记为未知。
- `runtimeToCandidateBindingVerified` 必须记为 `false`。
- 运行时记录没有与任何后续本地重编译产物建立密码学身份绑定。
- 隔离验证脚本输出的临时 DLL 哈希只能证明当次构建产物，不能替代或追认已 NETLOAD DLL 的身份。
- 当前 P0 可重复构建候选 SHA-256 为 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`。该候选尚未 NETLOAD，`NetLoadVerified=false`，不得继承旧会话的运行时结论。
- 旧诊断候选副本仍保持 SHA-256 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，没有被本轮隔离验证覆盖；这仍不改变 `runtimeToCandidateBindingVerified=false` 的证据边界。

当前证据只证明 `net45/x64` 诊断薄宿主可以编译，并可在该目标机 AutoCAD 2016 中 NETLOAD。Palette、真实 AgentHost、认证 IPC、选择上下文和 CAD 写入均未获得实机通过证据，不能据此宣称完整支持 AutoCAD 2016。

## 用户实机命令记录摘要

- NETLOAD 前：`DBMOD = 21`。
- NETLOAD：成功加载“Codex for AutoCAD 2016 诊断薄宿主”，`CODEXCADDOCTOR` 命令可用。
- `CODEXCADDOCTOR`：
  - Host target：`.NET Framework 4.5`
  - Process architecture：`x64`
  - CLR：`4.0.30319.42000`
  - AcMgd assembly：`20.1.0.0`
  - AcDbMgd assembly：`20.1.0.0`
  - Write capability：`disabled in diagnostic stage`
  - Automatic save：`disabled`
  - ACADVER：`20.1s (LMS Tech)`
  - VERNUM：`unavailable (InvalidInput)`
  - SECURELOAD：`1`
  - APPAUTOLOAD：`14`
  - 命令内 DBMOD：`21`
- `CODEXCAD`：明确报告当前仅为诊断薄宿主，Palette、Agent 与 CAD 写入保持禁用。
- 命令结束后：`DBMOD = 21`。

本次没有重新启动或自动操作 AutoCAD，也没有屏幕录像。运行时证据来源是用户提供的 AutoCAD 命令行文本。

## 基本信息

- 测试日期：2026-07-18
- 证据采集时的首次诊断基线：`2d2ad3738095794c8374e916559c0c5d13702ba1`
- 证据采集时工作树是否干净：否；后续阶段代码、证据和文档仍在生成
- AutoCAD 产品：AutoCAD 2016 简体中文，原版目标机安装
- ACADVER：`20.1s (LMS Tech)`
- VERNUM：`unavailable (InvalidInput)`
- acad.exe 文件版本：`R20.1.49.0.0`
- AutoCAD 进程：x64
- CLR：`4.0.30319.42000`
- Host 目标框架：`.NET Framework 4.5`
- AutoCAD 托管 API：AcMgd `20.1.0.0`；AcDbMgd `20.1.0.0`
- 测试上下文候选 DLL SHA-256：`2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`
- Host.2016 已 NETLOAD DLL SHA-256：**未知；未在现场采集，不得用候选或后续构建哈希替代**
- `runtimeToCandidateBindingVerified`：`false`
- 运行时与后续本地构建产物身份绑定：未验证
- 当前可重复构建候选 DLL SHA-256：`E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`
- 当前可重复构建候选 `NetLoadVerified`：`false`
- 旧诊断候选副本是否被当前构建覆盖：否；副本哈希仍为 `2E621...0C85`
- AgentHost 版本/SHA-256：未测
- 显示缩放：未记录

不要填写真实姓名、许可证序列号、API Key、内部服务器地址、`TRUSTEDPATHS` 内容或真实图纸路径。

## 环境采集器结果

2026-07-18 的 schema v4 采集器结果：

- PowerShell 7 无参数自动发现：通过；采集时间 `2026-07-18T14:57:00+08:00`。
- Windows PowerShell 5.1 无参数自动发现：通过；最新采集时间 `2026-07-18T15:01:50+08:00`。
- AutoCAD 2016 R20.1 installation：`1`。
- `BuildReady` installation：`1`。
- MSBuild candidates：`2`；二者均为有效 Microsoft Authenticode 签名、主版本 `>=17`，并由采集器记录 SHA-256。
- `TRUSTEDPATHS`：未采集。
- AutoCAD：采集器未启动。

该结果证明环境发现和编译工具链门禁可用，不等于 Host 编译或 NETLOAD 通过。

## 测试图纸

- 脱敏图纸代号：未记录；用户当前已打开图纸
- 图纸路径哈希：未记录
- 初始 DBMOD：`21`
- 结束 DBMOD：`21`
- 初始实体数：未读取
- 单位：未读取
- 典型图元：未读取
- 外部参照：未读取

## 实机用例结果

| ID | 用例 | 结果 | 证据/日志 | 备注 |
| --- | --- | --- | --- | --- |
| L01 | 手工 NETLOAD 无绑定错误 | 通过 | 用户命令记录显示诊断薄宿主已加载且命令已注册 | NETLOAD 路径/哈希未回显；不能绑定到后续本地 DLL |
| L02 | CODEXCADDOCTOR | 通过 | net45、x64、CLR 4.0.30319.42000、AcMgd/AcDbMgd 20.1.0.0、ACADVER 20.1s | VERNUM 返回 `unavailable (InvalidInput)` |
| D01 | 诊断命令前后 DBMOD 不变 | 通过 | `21 -> 21`，命令内 DBMOD 也为 21 | 只覆盖诊断命令，不代表选择上下文已验证 |
| U01 | 打开/停靠/浮动/隐藏/重开 | 未测 | | 诊断提交未接入 Palette |
| U02 | 中文输入与 DPI | 未测 | | 100%/125%/150% 均未测 |
| R01 | AgentHost 离线安全降级 | 未测 | | Agent 尚未接入 Host.2016 |
| R02 | 两轮只读对话与上下文记忆 | 未测 | | |
| C01 | 选择 Line/Circle/Polyline/Text/Block | 未测 | | 选择上下文尚未获得 2016 实机证据 |
| C02 | 只读选择操作 DBMOD 不变 | 未测 | | D01 不能替代本项 |
| A01 | CAD 拒绝后零修改 | 未测 | | CAD 审批与写入尚未接入 2016 |
| A02 | 一次允许仅执行展示计划 | 未测 | | |
| A03 | 审批令牌重放失败 | 未测 | | |
| A04 | 选择/图纸/图层/空间变化使旧计划失效 | 未测 | | |
| A05 | Agent 中断不自动重试写入 | 未测 | | |
| A06 | 成功写入后不自动保存 | 未测 | | 诊断命令未写入，不能证明写入后的行为 |
| X01 | 切换/关闭图纸无事件泄漏 | 未测 | | |
| X02 | 退出 AutoCAD 无残留进程/线程 | 未测 | | |

结果只允许：通过、失败、阻塞、未测。

## Host.2016 静态与构建复核

在不启动或操作 AutoCAD 的前提下运行 `scripts/verify-autocad2016-host.ps1`：

- PowerShell 7.6.3 单次完整验证：通过。
- Windows PowerShell 5.1.19041.6456 单次完整验证：通过；同一进程内六个 dotnet/NuGet 临时环境变量均精确恢复原值或原先不存在状态。
- 仓库从根目录精确解析 `.NET SDK 8.0.319`：通过。
- 验证脚本 SHA-256：`CF1B23D376EE28520B4AF5C7FC15C0F02631E42DA62C90170FD8D441BC01C1B5`。
- Host 项目 SHA-256：`93C091263A089C84AE76C91C1E57CCC02D858EEF897762F655594234A1F0F7CE`。
- Host 项目局部 `src/Codex.AutoCAD.Host.2016/NuGet.Config` SHA-256：`BD61267F69CD5DF2F0996DA881F7BA3531AB4442DC2D6EB861536EC4AB0D0B8E`；项目通过 `RestoreConfigFile` 显式引用，唯一离线 feed 相对值为 `..\..\third_party\nuget`。
- 仓库根 Host 专用 `NuGet.Config`：不存在。默认主解决方案普通 restore 与 Release build 均通过 `0` warning / `0` error，restore 没有引用 Host 配置或 Host 离线 feed，Host 源码树未变化。
- MSBuild、dotnet host、ildasm 的 Microsoft Authenticode 签名：全部 `Valid`。
- Release、`net45`、`x64` 隔离首次构建与独立重建逐字节一致：通过。
- PowerShell 7、Windows PowerShell 5.1、两次独立重建及两个并行验证进程的 DLL SHA-256 均为 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`。
- 两个并行验证进程使用不同隔离目录；Host 源码树和项目本地 `obj` 前后 manifest 不变：通过。
- acad.exe 与 accoremgd/acdbmgd/acmgd 的 Autodesk 签名、R20.1 文件版本和 `20.1.0.0` 程序集版本门禁：通过。
- 项目程序集引用、PackageReference、Compile、Import、evaluated MSBuild graph 和诊断命令允许清单：通过。
- 输出只包含一个 `Codex.AutoCAD.Host.2016.dll`；无 PDB、配置脚本、原生载荷或复制的 Autodesk DLL：通过。
- 输出 IL 精确门禁：`31` 个 MemberRef、`7` 个 MethodDef；`CODEXCADDOCTOR`/`CODEXCAD` 注册属性和 flags `0`：通过。
- Save、DxfOut、Quit、Invoke 高风险成员负向样本：全部拒绝。
- 把真实 `CommandMethod` 改成相同文字的注释后，程序集注册属性门禁明确拒绝：通过。
- 恶意 Directory.Build props/targets、额外源码与恶意仓库根 NuGet 配置均未进入 Host evaluated/restore graph：通过。
- 验证 JSON 与实际 DLL、源码、项目、包、隔离目录和工具签名逐字段交叉核对：`AuditPassed=true`、`Errors=[]`。
- 验证状态：`Status=compiled-candidate-not-runtime-verified-by-this-script`，`NetLoadVerified=false`。
- 当前 AutoCAD 会话关联的旧诊断候选副本仍为 `2E621...0C85`，未被隔离输出覆盖；本轮构建验证没有启动、重启或操作 AutoCAD。

以上新增结论属于本次静态/构建候选 `E853...B440`；证据采集时尚未绑定到新的阶段提交，且至今没有 NETLOAD 证据。首次诊断基线 `2d2ad37` 和用户旧命令记录仍只证明首次诊断候选可加载；后续提交状态应以 Git 历史为准，任何 Host.2016 修改都必须重新执行门禁并对冻结候选重新人工 NETLOAD，不能继承旧运行时结论。

## Phase 2 本地规格证据

| 组件 | 配置 | 结果 | 适用边界 |
| --- | --- | --- | --- |
| 解决方案构建 | Release | `0` warning / `0` error | 本地阶段快照；不是 AutoCAD 内构建证据 |
| Contracts Specs | Release | `15/15` | 本地契约规格 |
| IPC Specs | Release | `11/11` | 包含 sequence overflow；不是 Host.2016 live handshake |
| Security Specs | Release | `19/19` | 本地审批/安全规格；不是 CAD 实机审批 |
| AppServer Specs | Release | `7/7` | 本地进程协议规格 |
| Bridge Specs | Release | `29/29` | 本地命名管道/生命周期规格；尚未接入 Host.2016 |
| AgentRuntime Specs | Release | `31/31` | 本地假进程/代理边界；不是 CAD live |
| Chat Specs | Release | `9/9` | 本地 UI/会话逻辑规格 |
| 七个 Specs 合计 | Release | `121/121` | 本地阶段快照；提交状态以 Git 历史为准 |
| Bridge 压力复跑 | Release | `20 x 29 = 580/580` | 当前本地稳定性证据；不是 CAD E2E |
| AgentHost doctor | Release | 通过且无残留进程 | 不等于 Host.2016 已连接 AgentHost |
| diff/秘密扫描 | 工作树 | 通过 | 仅为本地清洁与泄密门禁 |

Release 构建、七个 Specs、Bridge 压力、AgentHost doctor、diff 与秘密扫描均已通过，但仍是**非 CAD live** 的本地阶段快照。提交状态以 Git 历史为准；不得将这些能力归入旧诊断提交 `2d2ad37`，也不得据此宣称 Host.2016 的 Agent/Bridge 集成通过。

## 问题与缺口

本轮没有在诊断命令中观察到程序集绑定错误或 DBMOD 变化。尚未完成：

- 运行时 DLL 路径/哈希的现场身份绑定。
- 当前可重复构建候选 `E853...B440` 的冻结产物人工 NETLOAD 与命令复验。
- Palette/DPI/文档生命周期实机验证。
- 只读选择上下文及零修改证明。
- 真实 AgentHost 启动、秘密交付、停止/超时和退出清理。
- Host.2016 认证 Bridge live handshake、HMAC、防重放和 fail-closed。
- 一次审批、锁内重校验、单事务写入及不自动保存的实机闭环。
- `.bundle`、签名、企业策略、普通用户安装/回滚和干净机验证。

## 测试结论

- 是否达到“2016 诊断编译/NETLOAD 兼容候选”：**是**；仅限提交 `2d2ad37` 的诊断薄宿主和当前用户命令记录。
- 是否达到“2016 只读候选”：**否**；选择上下文、文档切换和 Palette 生命周期未验证。
- 是否达到“2016 CAD 写入候选”：**否**；审批、锁内重校验和事务写入未获得 2016 实机证据。
- 是否达到“完整支持 AutoCAD 2016”：**否**。
- 是否允许进入下一阶段：是；只允许按阶段进入 Palette 与只读上下文，验证后单独提交。
- 是否允许发布：否。

最终表述：目标机 AutoCAD 2016 已证明可加载 `net45/x64` 诊断薄宿主，且所记录诊断命令未改变 DBMOD。因为运行时 DLL 身份未绑定，且 Palette、Agent、CAD 读取/写入及完整安全闭环没有实机证据，AutoCAD 2016 完整支持仍未成立。

## 证据文件

- `handoff/autocad2016/evidence/autocad2016-diagnostic-netload-20260718.json`
- `handoff/autocad2016/evidence/environment-collector-20260718.json`
- `handoff/autocad2016/evidence/host-build-verification-20260718.json`
- `handoff/autocad2016/evidence/phase2-local-specs-20260718.json`
