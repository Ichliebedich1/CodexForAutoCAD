# AutoCAD 2016 实机测试记录

## 证据来源与适用边界

本报告严格分开六类证据：

1. **用户实机命令记录**：用户在目标机已打开的原版 AutoCAD 2016 命令行中手工执行 `NETLOAD`、`CODEXCADDOCTOR`、`CODEXCAD` 和 `DBMOD`。
2. **环境采集器证据**：历史 schema v4 与当前 schema v5 只读采集；不启动 AutoCAD、不读取 `TRUSTEDPATHS` 内容。schema v5 增加 `Location` 注册表来源和脱敏发现失败计数。
3. **Host.2016 静态/构建门禁**：隔离 Release 重编译和签名、版本、引用、禁止 API 等验证；脚本不执行 NETLOAD。
4. **Phase 2 本地规格证据**：七个 Specs、Bridge 压力、AgentHost doctor、diff 与秘密扫描的本地阶段快照；未进入 AutoCAD，提交状态以 Git 历史为准。
5. **Host.2016 Palette 静态/构建与实机证据**：独立 Palette solution/project 的隔离 Release 门禁，以及冻结候选哈希绑定后的人工 NETLOAD、只读 UI 和生命周期记录；不得继承诊断 Host 的运行时结论。
6. **Host.2016 ReadOnlyContext 静态/构建与实机证据**：独立 Selection sidecar 的双 PowerShell 可重复构建门禁，以及冻结候选哈希绑定后的人工 NETLOAD、六类只读选择、清除和文档激活缓存失效记录；不得继承 Palette 或诊断 Host 的运行时结论。

以下身份缺口只适用于首次诊断薄宿主的历史实机命令记录；Palette 与 ReadOnlyContext 均有各自独立的冻结候选身份绑定，三者不得互相继承：

- `candidateDllSha256FromTestingContext` 记录为 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，仅表示测试上下文中的候选 DLL。
- `loadedDllSha256` 必须记为未知。
- `runtimeToCandidateBindingVerified` 必须记为 `false`。
- 运行时记录没有与任何后续本地重编译产物建立密码学身份绑定。
- 隔离验证脚本输出的临时 DLL 哈希只能证明当次构建产物，不能替代或追认已 NETLOAD DLL 的身份。
- 当前 P0 可重复构建候选 SHA-256 为 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`。该候选尚未 NETLOAD，`NetLoadVerified=false`，不得继承旧会话的运行时结论。
- 旧诊断候选副本仍保持 SHA-256 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，没有被本轮隔离验证覆盖；这仍不改变 `runtimeToCandidateBindingVerified=false` 的证据边界。
- Palette 冻结候选 SHA-256 为 `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`，已由加载前/后相同哈希及用户对 NETLOAD 完整冻结路径的确认建立独立身份绑定。
- ReadOnlyContext 冻结候选 SHA-256 为 `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`，已由双 PowerShell 可重复构建、只读冻结副本及用户对精确冻结 DLL 的人工 NETLOAD 建立独立身份绑定。
- Selection/context hash 按策略不入库且不持久化；这不等于 DLL SHA-256 不入库。证据中正常保留冻结 DLL SHA-256、规范化字节数和六类计数。

当前证据分别证明 `net45/x64` 诊断薄宿主可加载、独立 Palette 冻结候选在 96 DPI 下通过有界 UI/DBMOD 检查点，以及独立 ReadOnlyContext 冻结候选通过六类只读选择、清除和文档激活缓存失效检查点。真实 AgentHost、认证 IPC、正式侧边栏集成、审批和 CAD 写入仍未获得实机通过证据，不能据此宣称完整支持 AutoCAD 2016。

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

- 测试日期：2026-07-18 至 2026-07-19
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
- Palette 独立候选 DLL SHA-256：`90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`
- Palette 冻结候选代号：`autocad2016-palette-frozen-90620EA3`；冻结副本为只读，大小 `16384` 字节
- Palette 模块人工 NETLOAD/命令观察：已观察到预期加载消息，且 `CODEX16PAL`、`CODEX16PALINFO`、`CODEX16PALRESET` 可执行
- Palette 冻结候选 `NetLoadVerified`：`true`；用户已明确确认 NETLOAD 文件选择器选择了完整冻结候选路径
- Palette 运行时与冻结候选身份绑定：`true`；加载前记录、加载后复算 SHA-256 均为 `90620E...01DFE`，并取得用户对所选完整冻结路径的明确确认
- ReadOnlyContext 独立候选 DLL SHA-256：`AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`
- ReadOnlyContext 冻结候选代号：`autocad2016-readonly-context-frozen-20260719-main2036fd6-AB3132CF`；冻结副本为只读，大小 `31744` 字节
- ReadOnlyContext 冻结候选 `NetLoadVerified`：`true`；用户在现有 AutoCAD 2016 进程中人工 NETLOAD 精确冻结 DLL
- ReadOnlyContext 运行时与冻结候选身份绑定：`true`
- ReadOnlyContext 运行时检查点：六类各 `1`、selected `6`、generation `2`、canonical bytes `738`、捕获 `DBMOD 4 -> 4`；显式清除后 selected `0`、clearCount `14`、`DBMOD 4 -> 4`
- Selection/context hash：按策略脱敏且不持久化；冻结 DLL SHA-256 正常记录
- 旧诊断候选副本是否被当前构建覆盖：否；副本哈希仍为 `2E621...0C85`
- AgentHost 版本/SHA-256：未测
- 显示缩放：当前会话 `96 x 96 DPI`；125% 与 150% 未测

不要填写真实姓名、许可证序列号、API Key、内部服务器地址、`TRUSTEDPATHS` 内容或真实图纸路径。

## 环境采集器结果

2026-07-18 的 schema v4 历史采集器与 2026-07-19 的 schema v5 加固采集器结果：

- PowerShell 7 无参数自动发现：通过；采集时间 `2026-07-18T14:57:00+08:00`。
- Windows PowerShell 5.1 无参数自动发现：通过；最新采集时间 `2026-07-18T15:01:50+08:00`。
- AutoCAD 2016 R20.1 installation：`1`。
- `BuildReady` installation：`1`。
- MSBuild candidates：`2`；二者均为有效 Microsoft Authenticode 签名、主版本 `>=17`，并由采集器记录 SHA-256。
- `TRUSTEDPATHS`：未采集。
- AutoCAD：采集器未启动。
- schema v5 PowerShell 7/5.1 发现 fixture：均为 `10/10`。
- schema v5 PowerShell 7/5.1 真实只读采集：均为安装 `1`、BuildReady `1`；
  `AcadLocation=1`、`InstallLocation=0`、`Location=1`、注册表读取失败 `0`。
- schema v5 完整 Phase 2 非 CAD 回归：两套 PowerShell 均为 Release
  `0 warning / 0 error`、七个 Specs `145/145`、AgentHost doctor、Host 禁止 API、
  diff 和秘密扫描通过。

该结果证明环境发现和编译工具链门禁可用，不等于 Host 编译或 NETLOAD 通过。

## 测试图纸

- 脱敏图纸代号：未记录；用户当前已打开图纸
- 图纸路径哈希：未记录
- 诊断样本 DBMOD：`21 -> 21`
- ReadOnlyContext 捕获/显式清除样本 DBMOD：`4 -> 4`；这是同一选择检查点窗口
- ReadOnlyContext 文档激活清缓存样本 DBMOD：原图清除后 `21`、目标图 `21`；这是另一独立样本，不得与前一项的 `4 -> 4` 合并解释
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
| U01 | 打开/停靠/浮动/隐藏/重开 | 通过 | 用户明确确认左停靠、右停靠、浮动、点 X 隐藏及 `CODEX16PAL` 重开均正常；最终 INFO visible=true | 当前会话为 96 DPI；125%/150% 仍待测 |
| U02 | 中文输入与换行 | 通过 | 用户使用中文输入法实际输入固定两行，并明确确认显示与换行正常 | 输入仅保留在控件内，不发送、不写图、不保存 |
| U03 | 当前 96 DPI 会话的 DPI、DIP/物理尺寸记录 | 通过 | 首次 `300 x 866` physical/DIP；RESET 后 `370 x 366`；均为 `96 x 96 DPI` | 不代表 125%/150% 已通过 |
| U04 | RESET 释放并重建 | 通过 | 已连续观察 generation/reset/release `1/0/0 -> 2/1/1 -> 3/2/2 -> 4/3/3`，每次 RESET 后 INFO 均可执行 | 证明多次释放重建计数准确；完整退出生命周期仍待测 |
| U05 | RESET 后不存在重复事件订阅 | 未测 | 最新有效样本中 StateChanged `13 -> 15`、SizeChanged `17 -> 18` | 事件计数变化不能单独证明所有处理器均未重复；仍缺退出生命周期验证 |
| U06 | Palette 模块的 Agent/选择读取/CAD 写入/插件自动保存保持禁用 | 通过 | 两次 Palette INFO 均显示 Agent、Selection read、CAD write、Automatic save 为 disabled | 只描述 Palette 模块自身；不与后来独立 NETLOAD 的 ReadOnlyContext sidecar 冲突，也不代表 AutoCAD 自身 `.sv$` 自动保存被禁用 |
| U07 | Palette INFO/RESET 干净窗口内 DBMOD 精确不变 | 通过 | 有效隔离复测的四次命令行与两次 INFO 共六个 DBMOD 均为 `4` | 先前两轮受原生自动保存/选择提示污染的样本保留为无效记录，不用于通过结论 |
| U08 | 冻结 Palette DLL 与运行时身份绑定 | 通过 | 冻结 DLL 加载前/后哈希均为 `90620E...01DFE`，用户明确确认 NETLOAD 选择了完整冻结候选路径 | 提交证据不保存本机完整路径 |
| R01 | AgentHost 离线安全降级 | 未测 | | Agent 尚未接入 Host.2016 |
| R02 | 两轮只读对话与上下文记忆 | 未测 | | |
| C01 | 选择 Line/Circle/Polyline/DBText/MText/BlockReference | 通过 | `status=published-read-only`；selected `6`，六类各 `1`，generation `2`，canonical bytes `738` | Selection/context hash 按策略不入库；DLL SHA-256 已入库并完成身份绑定 |
| C02 | 只读选择操作 DBMOD 不变 | 通过 | 捕获前后 `4 -> 4`，`dbmodUnchanged=true` | 实体总数未独立计数，不能把本项扩大为实体数不变证明 |
| C03 | 用户命令显式清除缓存 | 通过 | `status=cleared-user-command`；published=false、selected `0`、clearCount `14`、`DBMOD 4 -> 4` | 只证明显式清除路径 |
| C04 | 文档激活时旧图缓存失效 | 通过 | 独立样本为 `status=cleared-document-activated`、DocumentActivated events `1`、旧缓存未跨图保留 | 原图清除后与目标图 DBMOD 均为 `21`；不得与 C02/C03 的 `4 -> 4` 混为同一样本 |
| C05 | 捕获/清除前后实体总数不变 | 未测 | 未独立读取命令前后实体总数 | `entityCountUnchangedVerifiedInAutoCad2016=false`；DBMOD 不变不能替代本项 |
| A01 | CAD 拒绝后零修改 | 未测 | | CAD 审批与写入尚未接入 2016 |
| A02 | 一次允许仅执行展示计划 | 未测 | | |
| A03 | 审批令牌重放失败 | 未测 | | |
| A04 | 选择/图纸/图层/空间变化使旧计划失效 | 未测 | | |
| A05 | Agent 中断不自动重试写入 | 未测 | | |
| A06 | 成功写入后不自动保存 | 未测 | | 诊断命令未写入，不能证明写入后的行为 |
| X01 | 切换/关闭图纸无事件泄漏 | 未测 | 文档切换的缓存失效子项已由 C04 通过 | 文档关闭清缓存及事件泄漏仍未实测，不能把整项判为通过 |
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

## Host.2016 Palette 静态、构建与实机复核

Palette 阶段必须使用独立永久模块，不覆盖当前进程已经加载的诊断程序集：

- solution：`Codex.AutoCAD.2016.Palette.sln`
- project：`src/Codex.AutoCAD.Host.2016.Palette/Codex.AutoCAD.Host.2016.Palette.csproj`
- assembly：`Codex.AutoCAD.Host.2016.Palette.dll`
- 命令面：`CODEX16PAL`、`CODEX16PALINFO`、`CODEX16PALRESET`
- 验证脚本：`scripts/verify-autocad2016-palette.ps1`
- 脱敏证据：`handoff/autocad2016/evidence/palette-build-verification-20260718.json`

静态/构建门禁已完成；这些结果仍不是 AutoCAD 运行时证据：

- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456：完整门禁均通过。
- 在安全门禁提交 `e039738` 上重新执行两套 PowerShell 门禁：均通过，冻结候选代号为 `autocad2016-palette-frozen-90620EA3`。
- MSBuild、dotnet host、ildasm 及 AutoCAD 2016 原版程序集签名/版本：通过。
- verifier SHA-256：`9E255CD47B183AD3C4DEC68D096BCDADB456EDCA570DCB4603C87E510B23AA18`。
- solution SHA-256：`29CBFCAE5ADD3256BB3D3C21446E58AC247D508336EA18646A64467A562E1C22`。
- project SHA-256：`9C990A405103F5CDCD8ED855DA68DD616FFD1CB78795BBD2CA9E41EF0154D344`；六个 Compile 源文件均已精确锁定。
- 两套 PowerShell 各自的隔离 Release 首次构建与独立重建逐字节一致；四个 DLL SHA-256 均为 `90620E...01DFE`。
- 输出仅一个 16384 字节 DLL、无 PDB、无复制 Autodesk/System DLL：通过。
- evaluated Compile/Reference/Import/Package/Target 图精确允许清单：通过。
- 精确 IL 门禁：`37` 个 MethodDef、`117` 个 MemberRef、`8` 个输出程序集引用；ExtensionApplication、CommandClass 和三个 flags `0` 的 CommandMethod 属性通过。
- CAD 数据库/选择/事务、保存、命令字符串、Process、IPC、文件、网络、注册表、运行时反射、后台线程、Agent 耦合及文档身份访问均 fail-closed；危险样本和注释伪装负测通过。
- verifier 没有启动、重启、发送命令或写入 AutoCAD；状态为 `compiled-palette-candidate-not-runtime-verified-by-this-script`，`NetLoadVerified=false`。

2026-07-18 首次人工运行取得以下**部分运行时观察**；在该次记录形成时，特定冻结候选身份尚未绑定，且 `DBMOD` 不变门禁失败，因此当时不能验收：

- 用户复用原本打开的 AutoCAD 2016 进程，人工 `NETLOAD` 后观察到预期 Palette 模块加载消息，并成功运行 `CODEX16PAL`、`CODEX16PALINFO`、`CODEX16PALRESET`；在完整冻结路径确认前，这只能作为模块级观察，不能写成特定候选 `NetLoadVerified=true`。
- 冻结 DLL 在加载前记录、加载后于 CAD 外复算均为 SHA-256 `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`，大小仍为 `16384` 字节且保持只读；所选完整路径仍等待用户明确确认后才能把运行时身份绑定置为通过。
- 首次 INFO：created/visible 为 true，generation `1`，DPI `96 x 96`，physical/DIP `300 x 866`；RESET 后 INFO：generation `2`、reset `1`、release `1`，DPI `96 x 96`，physical/DIP `370 x 366`。
- 两次 INFO 均显示 Agent、选择读取、CAD 写入和插件自动保存为 disabled；匿名文档事件只记录计数，没有输出图纸名称或路径。
- 加载前命令误输入为 `CODEX16PAINFO`，缺少字母 `L`；其“未知命令”结果仅记录为输入错误，不作为加载前命令面证据。
- `DBMOD` 实测为：NETLOAD 前命令行 `4`，第一次 INFO 内 `5`，第二次 INFO 内 `5`，最终命令行 `5`。`4 -> 5` 新增对象数据库修改位，变化发生区间包含 NETLOAD、AutoCAD 原生定时自动保存、Palette 打开和其他现场交互，原因尚未隔离，因此 `dbmodUnchanged=false`；该门禁按失败记录，但不能把原因直接归于 Palette。
- `CODEX16PAL` 后和 `CODEX16PALRESET` 后各出现一次“指定对角点或 [栏选/圈围/圈交]”窗口选择提示。命令记录没有证明其来源；两次提示均作为污染事件记录，不能解释为 Palette 发起选择，也不能忽略。
- `.sv$` 的本地路径和文件名不进入 Git。精确源码及 IL 门禁证明候选不包含保存 API；AutoCAD 原生定时自动保存消息与“插件主动保存”必须分开解释。

2026-07-19 的第一次隔离复测仍被 AutoCAD 原生定时自动保存污染：

- 新建未保存空白临时图后，首次命令行和 INFO 内 `DBMOD` 均为 `4`；随后出现原生 `.sv$` 自动保存，下一次命令行 `DBMOD` 变为 `5`。
- RESET 后 generation/reset/release 从 `2/1/1` 精确变为 `3/2/2`，但其余命令行和 INFO 内 `DBMOD` 均为 `5`。
- 因自动保存位于观察窗口内，该轮按预定规则作废；既不算 Palette 通过，也不把变化归因于 Palette。

随后同一 AutoCAD 进程完成了有效的干净隔离复测：

- 使用另一个未保存空白临时图，测试窗口内没有原生自动保存、窗口选择提示、第三方加载消息或其他交互。
- 四次命令行 `DBMOD` 与两次 INFO 内部 `DBMOD` 共六个读数全部为 `4`，对象数据库修改位始终为 `0`。
- RESET 前后 generation/reset/release 从 `3/2/2` 精确变为 `4/3/3`；created/visible 均为 true，DPI 为 `96 x 96`，physical/DIP 均为 `300 x 866`。
- 该轮满足既定判定规则，证明当前已加载 Palette 模块的 `CODEX16PALINFO`/`CODEX16PALRESET` 路径在有效观察窗口内没有修改图纸数据库。
- 该次干净复测首先建立模块级零写入观察；随后用户于 2026-07-19 明确确认 NETLOAD 文件选择器选择了完整冻结候选路径，因此特定 `90620E...01DFE` 候选现已建立运行时身份绑定并可设置 `NetLoadVerified=true`。

当前已打开 AutoCAD 2016 进程中的最小决定性 DBMOD 复测已按以下协议通过；该协议保留用于后续回归：

```text
DBMOD
CODEX16PALINFO
DBMOD
CODEX16PALRESET
DBMOD
CODEX16PALINFO
DBMOD
```

判定规则：

1. 首个 `DBMOD` 的对象数据库修改位必须为 `0`，即数值必须为偶数；若为奇数，样本在测试开始前已经无法检测新增对象修改，直接作废。
2. 四次命令行 `DBMOD` 与两次 INFO 内部 `DBMOD` 必须六值完全相同；任一变化即失败。
3. 第一次和第二次 INFO 之间，generation、reset、release 必须各精确增加 `1`；不得据此单独宣称所有事件处理器均无重复。
4. 测试窗口内若出现 `.sv$` 自动保存、窗口选择提示、第三方自动加载消息、其他命令或任何画布/停靠/文本交互，样本作废并重新选择安静窗口复测；不得修改 AutoCAD 自动保存设置来制造通过结果。本次最终有效样本未出现这些污染事件。
5. 复测只隔离已加载模块的 INFO/RESET 行为；冻结候选身份由加载前/后相同 SHA-256 与用户对完整冻结路径的明确确认单独建立，`runtimeToCandidateBindingVerified=true`。

上述 DBMOD 窗口结束后，用户又单独完成左停靠、右停靠、浮动、点 X 隐藏、`CODEX16PAL` 重开及两行中文/IME 输入，并明确确认显示与换行正常。最终 INFO 为 generation/reset/release `4/3/3`、StateChanged `25`、SizeChanged `29`、DPI `96 x 96`，INFO 内及随后命令行 `DBMOD` 均为 `4`。

当前 96 DPI/100% 的 UI 与 IME 矩阵已通过。125%/150% DPI 及 AutoCAD 退出生命周期允许在后续受控独立会话补测；不得从当前单一进程推断为全部通过。

## Host.2016 ReadOnlyContext 静态、构建与实机复核

ReadOnlyContext 阶段使用独立 Selection sidecar，不覆盖当前进程已经加载的诊断或 Palette 程序集：

- solution：`Codex.AutoCAD.2016.ReadOnlyContext.sln`
- project：`src/Codex.AutoCAD.Host.2016.ReadOnlyContext/Codex.AutoCAD.Host.2016.ReadOnlyContext.csproj`
- assembly：`Codex.AutoCAD.Host.2016.ReadOnlyContext.dll`
- 命令面：`CODEX16CTX`、`CODEX16CTXINFO`、`CODEX16CTXCLEAR`
- 验证脚本：`scripts/verify-autocad2016-readonly-context.ps1`
- 脱敏证据：`handoff/autocad2016/evidence/readonly-context-build-verification-20260718.json`

静态、规格、IL、禁写和可重复构建门禁已完成：

- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 的完整门禁、规则正负自测均通过。
- 四次隔离 Release 构建逐字节一致，DLL SHA-256 均为 `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`。
- 冻结候选代号为 `autocad2016-readonly-context-frozen-20260719-main2036fd6-AB3132CF`；DLL 大小 `31744` 字节、只读，输出仅此一个 DLL，无 PDB 或复制的 Autodesk 程序集。
- Specs `25/25` 通过，覆盖六类黄金向量、独立参考编码器、输入顺序规范化、数值句柄排序、重复句柄拒绝、全部导出字段哈希绑定、非有限数拒绝、Unicode/尺寸上限、文化无关和防御性复制。
- 只允许 Line、Circle、Polyline、DBText、MText、BlockReference，最多 `64` 个实体、每条 Polyline 最多 `256` 个顶点、文本最多 `2048` 个 UTF-16 code units、canonical bytes 最多 `65536`；任何截断均不允许。
- 选择只来自 `Editor.SelectImplied`；读取只使用 `StartOpenCloseTransaction` 和唯一一个 `GetObject(..., ForRead, false)` 调用；不取 DocumentLock、不 Commit、不发布部分结果。
- 捕获前后要求 DBMOD 精确相等，并在发布前重校验活动文档身份与 document epoch；文档切换和关闭路径按设计清缓存。
- IL 与 fail-closed 门禁拒绝 ForWrite、UpgradeOpen、Commit/Abort、Erase/Append、DocumentLock、Save、命令注入、SetSystemVariable、SetImpliedSelection、图纸/外参路径、反射、未批准实体、Process/IPC/网络/文件/注册表、native 和后台执行。
- Selection/context hash 为小写 SHA-256，但按证据策略不持久化、不入库；证据只保留 canonical bytes 与六类计数。冻结 DLL SHA-256 正常入库，二者不得混淆。
- 当前导出的只读状态哈希只覆盖显式白名单字段，不足以直接充当未来 CAD 写入审批的完整锁内重校验哈希。

2026-07-19 的人工实机检查点使用现有 AutoCAD 2016 进程，用户精确 NETLOAD 上述冻结 DLL：

- 运行时与候选身份绑定为 `true`，加载成功且三个命令均可执行；构建 verifier 本身仍不操作 CAD，其输出中的 `NetLoadVerified=false` 只描述脚本边界，不覆盖独立的用户实机证据。
- 首次出现 `validation-no-implied-selection`，原因是前置 `DBMOD` 命令取消了 implied selection；该次按预期 fail-closed，分类为 `candidateFailure=false`，不是 DLL 运行失败。
- 用户重新预选六个实体后，`CODEX16CTX` 得到 `status=published-read-only`、published=true、generation `2`、selected `6`，Line/Circle/Polyline/DBText/MText/BlockReference 各 `1`，canonical bytes `738`，DBMOD 精确 `4 -> 4`。
- `CODEX16CTXCLEAR` 得到 `status=cleared-user-command`、published=false、selected `0`、clearCount `14`，DBMOD 精确 `4 -> 4`。
- 文档切换是另一个独立样本：`status=cleared-document-activated`、DocumentActivated events `1`、旧图缓存未跨图保留；原图清除后 DBMOD 为 `21`，切换后目标图 DBMOD 为 `21`。这不能与捕获/清除样本的 `4 -> 4` 合并成同一时间线。
- 图纸实体总数没有在命令前后独立计量，因此 `entityCountUnchangedVerifiedInAutoCad2016=false`；DBMOD 不变不能替代这一缺失证据。
- 候选没有保存 API，现场也没有观察到插件调用保存；但 `automaticSaveRuntimeVerified=false`，不得把“插件未保存”扩大为 AutoCAD 自身 `.sv$` 自动保存已禁用或已验证。
- 此检查点证明有界的只读选择、显式清除和 DocumentActivated 缓存失效；动态块 effective name、xref 分类、DocumentToBeDestroyed/文档关闭、事件泄漏、正式侧边栏 UI、Agent/Bridge、审批、写入和保存仍在边界之外。

进入正式侧边栏 UI 之前必须先通知用户，并冻结 Codex 与 Kimi 共同遵守的上下文展示、命令面、错误语义、审批边界和兼容契约；未完成该共同契约决策门，不开始正式 UI 集成。

## Phase 2 本地规格证据

| 组件 | 配置 | 结果 | 适用边界 |
| --- | --- | --- | --- |
| 解决方案构建 | Release | `0` warning / `0` error | 本地阶段快照；不是 AutoCAD 内构建证据 |
| Contracts Specs | Release | `15/15` | 本地契约规格 |
| IPC Specs | Release | `17/17` | 包含固定认证向量、严格 sequence、nonce 和防重放；不是 Host.2016 live handshake |
| Security Specs | Release | `19/19` | 本地审批/安全规格；不是 CAD 实机审批 |
| AppServer Specs | Release | `7/7` | 本地进程协议规格 |
| Bridge Specs | Release | `29/29` | 本地命名管道/生命周期规格；尚未接入 Host.2016 |
| AgentRuntime Specs | Release | `31/31` | 本地假进程/代理边界；不是 CAD live |
| Chat Specs | Release | `9/9` | 本地 UI/会话逻辑规格 |
| 七个 Specs 合计 | Release | `127/127` | 本地阶段快照；提交状态以 Git 历史为准 |
| Bridge 压力复跑 | Release | `20 x 29 = 580/580` | 当前本地稳定性证据；不是 CAD E2E |
| AgentHost doctor | Release | 通过且无残留进程 | 不等于 Host.2016 已连接 AgentHost |
| diff/秘密扫描 | 隔离提交候选 | 通过 | `e039738` 的正向候选通过；当前未提交写入原型会被门禁按预期拒绝 |

Release 构建、七个 Specs、Bridge 压力、AgentHost doctor、diff 与秘密扫描均已有通过证据，但仍是**非 CAD live** 的本地阶段快照。认证兼容阶段提交为 `7358764`；Host 禁写门禁阶段提交为 `e039738`。当前工作树中的未提交 Host.2025 写入原型会在 `127/127` 后被增强门禁稳定拒绝 `8` 处，因此不能把当前 dirty 工作树称为全绿，也不得据此宣称 Host.2016 的 Agent/Bridge 集成通过。

## 问题与缺口

旧诊断薄宿主轮次没有观察到程序集绑定错误，且 `DBMOD 21 -> 21`。Palette 的早期污染样本观察到 `DBMOD 4 -> 5`，但后续有效隔离复测的六个读数全部为 `4`，因此 INFO/RESET 的模块级 DBMOD 门禁现已通过。ReadOnlyContext 冻结候选也已通过六类只读选择、显式清除、DBMOD 不变和 DocumentActivated 缓存失效的有界检查点。尚未完成：

- 旧诊断薄宿主历史轮次的运行时 DLL 路径/哈希绑定仍未取得；本项不再适用于已绑定的 Palette 与 ReadOnlyContext 冻结候选。
- 当前可重复构建候选 `E853...B440` 的冻结产物人工 NETLOAD 与命令复验。
- Palette 的 125%/150% DPI 与 AutoCAD 退出生命周期实机验证。
- ReadOnlyContext 的命令前后实体总数独立计量、动态块 effective name、xref 分类、DocumentToBeDestroyed/文档关闭清缓存及事件泄漏验证。
- ReadOnlyContext 与正式 Host.2016/侧边栏 UI 的集成；进入该阶段前必须先通知用户并冻结 Codex/Kimi 共同契约。
- 真实 AgentHost 启动、秘密交付、停止/超时和退出清理。
- Host.2016 认证 Bridge live handshake、HMAC、防重放和 fail-closed。
- 一次审批、锁内重校验、单事务写入及不自动保存的实机闭环。
- 插件自动保存行为的运行时验证；候选无保存 API，且现场未观察插件保存，不等于 `automaticSaveRuntimeVerified=true`，也不等于 AutoCAD 自身 `.sv$` 被禁用。
- `.bundle`、签名、企业策略、普通用户安装/回滚和干净机验证。
- Palette 冻结候选 `90620E...01DFE` 已取得绑定后的人工 NETLOAD、连续 RESET、当前 96 DPI UI/IME 和干净临时图 DBMOD 不变证据；仍缺 125%/150% DPI 和退出生命周期。

## 测试结论

- 是否达到“2016 诊断编译/NETLOAD 兼容候选”：**是**；仅限提交 `2d2ad37` 的诊断薄宿主和当前用户命令记录。
- 是否达到“2016 独立只读 Selection sidecar 检查点”：**是**；仅限冻结候选 `AB3132...E6B8` 的六类 implied selection、显式清除、`DBMOD 4 -> 4` 和 DocumentActivated 缓存失效。
- 是否达到“2016 完整只读产品候选”：**否**；正式侧边栏 UI、Agent/Bridge 集成、实体总数、文档关闭/泄漏及其余运行时矩阵仍未完成。
- 是否达到“2016 CAD 写入候选”：**否**；审批、锁内重校验和事务写入未获得 2016 实机证据。
- 是否达到“完整支持 AutoCAD 2016”：**否**。
- Palette 静态/构建门禁是否通过：**是**。
- Palette INFO/RESET 的干净 DBMOD 门禁是否通过：**是**；有效样本六个读数全部为 `4`。
- Palette 完整运行时阶段是否通过：**否**；冻结候选身份、当前 96 DPI UI/IME 与零写入门禁已通过，但 125%/150% DPI 和退出生命周期仍未完成。
- 是否允许提交当前 Palette 检查点：**是**；只允许以“已绑定的 96 DPI Palette 运行时候选检查点”单独提交，不得表述为完整 Palette 验收或完整 AutoCAD 2016 支持。
- ReadOnlyContext 静态/规格/IL/禁写/可重复构建门禁是否通过：**是**；双 PowerShell、Specs `25/25` 和四次相同 DLL SHA-256 均有证据。
- ReadOnlyContext 有界实机检查点是否通过：**是**；冻结候选身份、六类捕获、显式清除、DBMOD 不变及 DocumentActivated 缓存失效均已验证。
- 是否允许单独提交当前 ReadOnlyContext 检查点：**是**；只能表述为“已绑定的独立只读 Selection sidecar 检查点”，不得表述为正式 UI/Agent 集成、CAD 写入或完整 AutoCAD 2016 支持。
- 是否允许发布：否。

最终表述：目标机 AutoCAD 2016 已证明可加载 `net45/x64` 诊断薄宿主；已绑定的独立 Palette 冻结候选通过当前 96 DPI 的打开、停靠、浮动、隐藏重开、中文 IME 与干净 DBMOD 验证；已绑定的独立 ReadOnlyContext 冻结候选通过六类只读 implied selection、显式清除、`DBMOD 4 -> 4` 和 DocumentActivated 缓存失效检查点。诊断宿主历史身份、125%/150% DPI、退出/关闭生命周期、实体总数、正式 UI/Agent/Bridge、CAD 写入和完整安全闭环仍不完整，因此 AutoCAD 2016 完整支持仍未成立。

## 证据文件

- `handoff/autocad2016/evidence/autocad2016-diagnostic-netload-20260718.json`
- `handoff/autocad2016/evidence/environment-collector-20260718.json`
- `handoff/autocad2016/evidence/environment-collector-hardening-20260719.json`
- `handoff/autocad2016/evidence/host-build-verification-20260718.json`
- `handoff/autocad2016/evidence/phase2-local-specs-20260718.json`
- `handoff/autocad2016/evidence/phase2-guardrail-verification-20260718.json`（增强门禁当前口径为 IPC `17/17`、七个 Specs `127/127`；旧 `121/121` 只允许作为历史快照）
- `handoff/autocad2016/evidence/palette-build-verification-20260718.json`（静态门禁、冻结候选身份绑定、当前 96 DPI UI/IME 及最新有效样本六个 `DBMOD=4` 均通过；125%/150% DPI 与退出生命周期延期）
- `handoff/autocad2016/evidence/readonly-context-build-verification-20260718.json`（双 PowerShell 可重复构建、Specs `25/25`、冻结 DLL 身份绑定、六类只读捕获、显式清除、`DBMOD 4 -> 4` 及独立文档激活缓存失效检查点）
