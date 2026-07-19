# 公司电脑 AutoCAD 2016 操作手册

开始操作前先核对 `CURRENT_STATE.md` 的冻结候选、待实机队列和当前禁止事项。

本手册用于复验已成立的 AutoCAD 2016 诊断候选，并继续完成 Palette、只读上下文、Agent/Bridge 和审批写入阶段。

## 当前已知状态

- 首次已提交诊断基线：`2d2ad3738095794c8374e916559c0c5d13702ba1`。
- 目标机：原版 AutoCAD 2016 简体中文，R20.1，x64；托管 API 程序集版本 `20.1.0.0`。
- Host.2016：`net45/x64` 诊断薄宿主已用目标机原版程序集真实编译。
- 实机：用户已在原本打开的 AutoCAD 2016 进程中手工 `NETLOAD`，`CODEXCADDOCTOR`/`CODEXCAD` 可运行，`DBMOD 21 -> 21`。
- 当前可重复构建候选：PowerShell 7.6.3 与 Windows PowerShell 5.1 验证均通过，独立双构建及两路并行验证均产生 SHA-256 `E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`。该候选**尚未 NETLOAD**，验证结果必须保持 `NetLoadVerified=false`。
- 旧诊断候选副本仍保持 SHA-256 `2E621C5D7AAF7F3F59C5CBD65C8E899712FA93F1E3ED5758F7E7A0ECDBFB0C85`，没有被隔离构建覆盖。由于原始 AutoCAD 命令记录没有采集运行时路径/哈希，该值仍只是测试上下文候选副本身份，不是已加载程序集的密码学绑定。
- Palette 已建立独立运行时检查点：提交 `56115e4`，冻结 DLL SHA-256 `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`；用户已在原有 AutoCAD 2016 进程验证 100% DPI 下停靠、浮动、隐藏重开、释放重建、中文换行和干净样本 `DBMOD=4`。125%/150% DPI 与退出生命周期仍待验证。
- 能力边界：诊断记录中的 Palette、Agent、认证 IPC、选择上下文和 CAD 写入仍为禁用/未测；独立 Palette 检查点提升 Palette 100% DPI 范围，独立 ReadOnlyContext 检查点提升六类选择读取与缓存清除范围。两者尚未合并为正式侧边栏，也不证明 Agent、审批或 CAD 写入。
- 证据缺口：当时命令记录没有回显 DLL 路径或现场 SHA-256，因此不得把任何后续重编译产物的哈希写成“已 NETLOAD DLL 哈希”。
- 当前公共契约阶段 Phase 2 回归：Release 构建 `0` warning / `0` error；Contracts `27/27`、IPC `35/35`、Security `19/19`、AppServer `7/7`、Bridge `29/29`、AgentRuntime `31/31`、Chat `9/9`，合计 `157/157`。AgentHost 安全引导提交中的历史快照仍为 `145/145`，不得反向改写。此前 Bridge 压力复跑为 `20 x 29 = 580/580`；以上仍是**非 CAD live** 证据，不证明 Host.2016 已接入 Agent/Bridge。
- 跨运行时认证与 Bootstrap 原语门禁已在 PowerShell 7.6.3 和 Windows PowerShell 5.1.19041.6456 下通过：SDK `8.0.319`，托管核心 Release `0` warning / `0` error，Bridge `29/29`，net45/net8 均为 `35/35`，双隔离主产物逐字节一致。该门禁没有启动或操作 AutoCAD，也没有验证真实 AgentHost 句柄交付、传输机密性、进程身份或硬超时。
- 真实进程外 AgentHost 的有界 bootstrap-doctor 门禁已在 PowerShell 7.6.3 和 Windows PowerShell 5.1.19041.6456 下通过：net45/net8 Launcher 精确强制 ID 集均为 `15/15`，每套门禁两次隔离 Release 构建，`106` 个可运行输出文件逐字节一致并在 Specs 后复核未变化。受限继承句柄密钥交付、批准 EXE SHA-256、确认 PID/创建时间、启动截止触发 fail-closed 中止并随后进行最多 `5` 秒有界终止清理、取消、确认后挂起、句柄 canary、继承位清除、stderr 限界/脱敏和 `0 -> 0` 残留进程均通过。该检查点不声称终止严格发生在配置的启动截止内；没有启动或操作 AutoCAD。长运行 `IAgentBridgeClient`、外部句柄复制抵抗、刻意 suspended-launch TOCTOU 攻击、Host.2016 live Bridge 和 CAD 集成仍未验证。
- 独立只读选择上下文已建立运行时检查点：阶段基线 `2036fd6`，分支 `codex/selection2016-readonly-v2`，冻结 DLL SHA-256 `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`，大小 `31744` 字节且冻结副本为只读。用户在原本打开的 AutoCAD 2016 中手工 NETLOAD 该候选，成功读取 Line、Circle、Polyline、DBText、MText、BlockReference 各 `1` 个，`selected=6`、canonical bytes `738`，捕获和用户清除均为 `DBMOD 4 -> 4`；文档激活事件清除了旧缓存。
- CadContextJson v1 与 Host/Agent/UI 公共契约已经冻结：规范见 `MVP_PUBLIC_CONTRACT_V1.md`，固定向量为 `2225` 字节、SHA-256 `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423`。PowerShell 7/5.1 下 net45/net8 均 `27/27`，双隔离构建逐字节一致，Phase 2 `157/157`；统一 Host.2016、Palette JSON、live Bridge 和 AutoCAD 对话仍未验证。
- 首次 `validation-no-implied-selection` 是前置 `DBMOD` 取消预选后触发的预期 fail-closed，不是加载或捕获失败。选择哈希按脱敏策略不进入仓库；实体总数和插件自动保存未单独取得运行时证据。

除非需要验证启动/卸载生命周期，不要为了重复诊断而重启当前 AutoCAD 进程。

## 1. 源码与交接包校验

如使用交接 ZIP，先校验同批次 SHA-256：

```powershell
$zip = 'D:\Transfer\CodexForAutoCAD-2016-source-handoff-时间戳.zip'
$expected = (Get-Content "$zip.sha256.txt" -Raw).Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)[0]
$actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "交接包校验失败：$actual" }
Write-Host '交接包 SHA-256 校验通过'
```

解压或克隆到本地短路径。不要从邮件临时目录、网络共享、同步目录或 ZIP 内直接构建/加载。

继续开发前核对：

```powershell
git rev-parse HEAD
git status --short
```

Git 历史中必须包含首次诊断基线 `2d2ad3738095794c8374e916559c0c5d13702ba1`。不要要求当前 `HEAD` 永远等于该旧提交；应继续核对后续阶段是否各自经过验证并单独提交，不要把多个未验证阶段混成一次提交。

## 2. 环境采集与复验

当前目标机环境已经采集。2026-07-18 的 schema v4 采集器在 PowerShell 7 无参数自动发现时通过：

- AutoCAD 2016 R20.1 安装数：`1`
- 可用于 Host.2016 编译的安装数：`1`
- 具有有效 Microsoft 签名的 MSBuild 候选数：`2`
- 采集时间：`2026-07-18T14:57:00+08:00`

Windows PowerShell 5.1 无参数自动发现也通过，最新采集时间为 `2026-07-18T15:01:50+08:00`。以上只证明采集/工具链门禁，不替代编译或 NETLOAD。

新目标机、AutoCAD 更新、Build Tools 更新或怀疑环境漂移时，以普通用户重新运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\collect-autocad2016-environment.ps1
```

采集脚本只读环境，不启动 AutoCAD、不修改注册表、不修改图纸。只有 schema v4 报告的 `CollectionSucceeded=true` 且至少一个安装 `ReadyForHost2016Build=true` 时，才进入编译。

在 AutoCAD 命令行读取：

```text
ACADVER
VERNUM
SECURELOAD
APPAUTOLOAD
DBMOD
```

`TRUSTEDPATHS` 只在现场由授权人员查看，不把其内容复制进仓库证据。证据只记录“是否由企业批准”，不得保存真实目录。

## 3. 必须确认的环境信息

- 产品、语言、`ACADVER`、`acad.exe` 完整文件版本、更新状态和 x64。
- `acmgd.dll`、`acdbmgd.dll`、`accoremgd.dll` 的文件版本、程序集版本、SHA-256 和 Autodesk 有效签名。
- MSBuild 为受信本机候选：有效 Microsoft 签名且受支持版本（当前主版本 `>=17`），并记录工具 SHA-256。
- .NET Framework 4.5 编译引用可用；AgentHost 所需 .NET 8 Runtime 或经批准的 self-contained 发布可用。
- AppLocker、WDAC、EDR、杀毒、代码签名、子进程和当前用户命名管道策略。
- 受信插件目录和 `.bundle` 部署位置已由企业批准。

不要导出 Autodesk 许可证注册表、序列号、用户名、内部软件白名单详情或网络路径。

## 4. Host.2016 构建门槛

Host.2016 必须满足：

- 目标 `net45`、`x64`，引用目标机原版 R20.1 托管程序集。
- Autodesk 引用全部 `Private=false`，不得复制到插件输出。
- 不把 .NET Framework 系统 DLL、2025 API DLL 或无关 NuGet 程序集放入插件目录。
- AgentHost/Runtime 保持进程外 .NET 8；不得把 .NET 8 宿主 DLL 直接 NETLOAD 到 AutoCAD 2016。
- 每次候选构建运行 `scripts/verify-autocad2016-host.ps1`，必须通过隔离 Release 编译、签名/版本、项目引用、命令面、禁止 API、程序集引用和输出清洁门禁。
- Host 专用离线 NuGet 配置必须位于 `src/Codex.AutoCAD.Host.2016/NuGet.Config`，并由项目的 `RestoreConfigFile` 显式引用；其唯一 feed 相对值为 `..\..\third_party\nuget`。
- 不得为了 Host 在仓库根放置 `NuGet.Config`。默认主解决方案的普通 restore/build 不得读取 Host 配置、不得使用 Host 离线 feed，也不得构建或修改 Host.2016。

编译使用公司电脑合法安装程序集或公司批准的 ObjectARX 2016 SDK。不得重新分发 Autodesk 二进制。

2026-07-18 当前 P0 门禁的已验证基准为：

- 仓库解析 SDK：`.NET SDK 8.0.319`。
- 验证脚本 SHA-256：`CF1B23D376EE28520B4AF5C7FC15C0F02631E42DA62C90170FD8D441BC01C1B5`。
- Host 项目 SHA-256：`93C091263A089C84AE76C91C1E57CCC02D858EEF897762F655594234A1F0F7CE`。
- Host 项目局部 `NuGet.Config` SHA-256：`BD61267F69CD5DF2F0996DA881F7BA3531AB4442DC2D6EB861536EC4AB0D0B8E`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过。
- 同一次验证的首次构建与独立重建必须逐字节一致；两个并行验证进程必须使用不同隔离目录，且不得修改 Host 源码树或项目本地 `obj`。
- Release DLL SHA-256：`E8535C11AA09F93C405EBB7DFB46199EEDC27EE046959B4CC86395A06998B440`。
- 输出只允许一个 Host DLL，不生成 PDB，不复制 Autodesk DLL。
- IL 门禁必须保持精确 `31` 个 MemberRef、`7` 个 MethodDef，以及 `CODEXCADDOCTOR`/`CODEXCAD` 两个 flags 为 `0` 的注册属性。
- Save、DxfOut、Quit、Invoke、注释伪装、Directory.Build 注入与恶意根 NuGet 配置注入负向验证必须通过。
- MSBuild、dotnet host 和 ildasm 必须具有有效 Microsoft Authenticode 签名。
- 构建脚本不得启动、重启、发送命令或写入 AutoCAD；其 JSON 必须输出 `Status=compiled-candidate-not-runtime-verified-by-this-script` 和 `NetLoadVerified=false`。
- 默认 `Codex.AutoCAD.sln` 的普通 restore 与 Release build 必须继续通过 `0` warning / `0` error；restore 日志不得引用 Host 项目局部配置或 `third_party\nuget` feed。

上述 `E853...B440` 是**静态/构建候选**，不是当前 CAD 会话已加载的 DLL。只有完成第 5 节的冻结候选身份绑定和人工 NETLOAD 后，才可产生新的运行时证据。

## 5. NETLOAD 候选身份绑定

当前已有一次真实 NETLOAD 成功记录，但**没有运行时 DLL 身份绑定**。下一次正式复验必须按以下顺序补证：

1. 构建完成后冻结候选目录，不再重编译或覆盖 DLL。
2. 在 AutoCAD 外计算候选 DLL SHA-256，并记录 Git 提交、构建配置和时间。
3. 在 NETLOAD 记录中使用同一冻结候选；现场内部记录可保留本地路径，提交到仓库的证据只保留哈希和脱敏候选 ID。
4. 执行 `NETLOAD` 后立即运行 `CODEXCADDOCTOR`、`CODEXCAD` 和 `DBMOD`。
5. 只有冻结候选哈希与 NETLOAD 操作建立同一条可审计记录时，才可设置 `runtimeToArtifactBindingVerified=true`。

禁止用后续隔离验证或临时重编译 DLL 的 SHA-256 回填历史 NETLOAD 记录。

当前两个哈希的解释必须保持如下，不得合并：

- `2E621...0C85`：旧诊断候选副本当前仍存在且未被覆盖；它与旧 AutoCAD 会话有关联背景，但现场记录没有证明加载路径/哈希，因此 `runtimeToCandidateBindingVerified=false`。
- `E853...B440`：当前可重复构建门禁产生的 Release 候选；尚未人工 NETLOAD，因此 `NetLoadVerified=false`、`runtimeToNewCandidateBindingVerified=false`。

## 6. 当前诊断复验

如当前 AutoCAD 进程仍已加载诊断宿主，可直接运行，无需重启：

```text
DBMOD
CODEXCADDOCTOR
CODEXCAD
DBMOD
```

当前已记录的期望输出：

- Host target `.NET Framework 4.5`
- Process architecture `x64`
- CLR `4.0.30319.42000`
- `AcMgd`/`AcDbMgd` `20.1.0.0`
- `ACADVER 20.1s (LMS Tech)`
- `VERNUM unavailable (InvalidInput)`
- `SECURELOAD 1`、`APPAUTOLOAD 14`
- 写入禁用、自动保存禁用
- `DBMOD 21 -> 21`

`VERNUM` 返回 `InvalidInput` 已作为实际结果记录；不要为了得到期望字符串而修改系统变量或运行写入命令。

本节命令复验只适用于当前会话中已经存在的旧诊断宿主。它不能把尚未 NETLOAD 的 `E853...B440` 候选自动升级为运行时已验证产物，也不得用隔离构建覆盖当前已加载副本。

## 7. Phase 2 与 AgentHost 本地门禁

Bootstrap 原语 Specs 只证明内存/Stream 协议语义。Frame 中的 session secret 为明文，
HMAC 不提供机密性；真实启动仍必须使用专用、独占、受限继承句柄，不得使用命令行、
环境变量、日志或普通可旁观命名管道交付 bootstrap frame 或认证键。

在进入 CAD 内集成前，重新运行并保存脱敏摘要：

```powershell
.\scripts\verify-autocad2016-auth-compat.ps1
```

该验证器必须在 PowerShell 7 和 Windows PowerShell 5.1 下分别通过，并保持：

- net45 IPC/Bootstrap Specs：`35/35`
- net8 IPC/Bootstrap Specs：`35/35`
- Bridge 回归：`29/29`
- net45/net8 六个主产物双隔离构建逐字节一致
- 固定 frame、KDF、Host→Agent 与 Agent→Host HMAC 字节一致
- 公共 API、MemberRef、关键状态机方法和完整 Bootstrap 实现 IL 冻结值一致
- `AutoCadStartedOrRestarted=false`、`CadCommandsSent=false`、`NetLoadVerified=false`

这组结果只允许称为“跨运行时认证与 Bootstrap 协议原语门禁通过”。不得据此声称
真实 AgentHost 已启动、密钥已安全交付、Bridge 已接入 CAD 或 AutoCAD 2016 已完整支持。

真实进程外 bootstrap-doctor 的阶段门禁使用最终编排器：

```powershell
.\scripts\verify-autocad2016-agent-bootstrap-stage.ps1 -Configuration Release
```

最终编排器会自行在 PowerShell 7 和 Windows PowerShell 5.1 下分别运行
`verify-autocad2016-agent-bootstrap.ps1`、`verify-autocad2016-auth-compat.ps1` 和
`verify-phase2.ps1`，解析并哈希各自的 raw evidence/log，比较跨 shell 规范化结果，
再自动生成脱敏阶段 evidence。原始日志只保存在 ignored `artifacts/`；单独运行组成脚本
只用于聚焦诊断，不能替代最终编排器的阶段证据。门禁必须保持：

- net45 Launcher Specs：精确强制 ID 集 `15/15`
- net8 Launcher Specs：精确强制 ID 集 `15/15`
- 每个 shell 两次隔离 Release 构建，`106` 个可运行输出文件逐字节一致
- Specs 前后完整输出树复核不变，源码树 `bin/obj` 不被隔离验证修改
- 真实 AgentHost bootstrap-doctor 和连续 `5` 次重复引导通过
- 批准 EXE SHA-256 不匹配、非 EXE、确认身份不匹配、畸形/重复 frame 全部 fail-closed
- 未确认超时和确认后继续挂起均由启动截止触发 fail-closed 中止，随后在最多 `5` 秒
  有界清理窗口内证明子进程终止；取消也执行有界终止清理，不声称终止严格发生在
  配置的启动截止内
- 子进程清除继承位，父进程 canary 句柄不进入子进程
- stderr 只公开受限字节数和截断标志，不公开原始文本
- 相关 Agent 进程基线/终态 `0 -> 0`
- `AutoCadStartedOrRestarted=false`、`CadCommandsSent=false`、`NetLoadVerified=false`

该检查点只允许称为“真实 AgentHost 有界安全引导通过”。完整传输机密性、外部进程复制
句柄抵抗、刻意替换 EXE 的 suspended-launch TOCTOU 动态攻击、长运行
`IAgentBridgeClient`、pending-bootstrap 原子消费、Host.2016 live Bridge 和 CAD 集成仍是
明确的未验证项。

- Release 解决方案构建：`0` warning / `0` error
- Contracts Specs：`27/27`
- IPC Specs：`35/35`
- Security Specs：`19/19`
- AppServer Specs：`7/7`
- Bridge Specs：`29/29`
- AgentRuntime Specs：`31/31`
- Chat Specs：`9/9`
- 七个 Specs 合计：`157/157`
- Bridge 压力复跑：`20 x 29 = 580/580`
- AgentHost doctor：通过，退出后无残留进程
- `git diff --check` 与秘密扫描：通过

命名管道测试在受限沙箱中可能因访问控制失败；只有在受控的普通用户环境中通过才可作为本地 IPC 规格证据。本节结果是本地阶段验证快照，提交状态以 Git 历史为准；它仍不能替代 Host.2016 live handshake、Agent 与 CAD 的实际连接或 CAD 内验证。

## 8. Palette 与只读冒烟

诊断候选不包含 Palette 或选择上下文。独立 Palette 与 ReadOnlyContext sidecar 已分别建立检查点；后续正式宿主集成或回归时按顺序执行：

1. 面板打开、停靠、浮动、隐藏、重开。
2. 中文输入、换行、发送、停止；测试 100%、125%、150% DPI。
3. AgentHost 未启动时只显示离线，不崩溃、不回退到未认证 IPC。
4. AgentHost 启动后完成两轮只读对话并确认上下文记忆。
5. 分别选择 Line、Circle、Polyline、DBText、MText、BlockReference。
6. 核对上下文只含白名单字段；选择哈希只用于现场比对，按脱敏策略不写入仓库。
7. 再次检查 `DBMOD` 和图纸修改状态均未变化；实体总数若要声明通过，必须另行读取并记录脱敏计数，不能用 `selected=6` 代替。
8. 切换/关闭图纸、隐藏面板、退出 AutoCAD，检查无残留线程、事件泵和 Agent 进程。

只读冒烟失败时，不进入 CAD 写入测试。

当前已通过的 ReadOnlyContext 冻结候选由两个独立受控样本组成，不得把
`DBMOD=4` 的捕获/清除窗口和 `DBMOD=21` 的文档切换窗口拼成同一次连续测试。

样本 A：捕获与用户清除

```text
保持未选择状态
DBMOD
预选六类对象各一个
CODEX16CTX
CODEX16CTXINFO
DBMOD
CODEX16CTXCLEAR
CODEX16CTXINFO
DBMOD
```

首个 `DBMOD` 必须在未选择状态执行；AutoCAD 会取消现有预选，因此必须在它之后重新
预选六类对象，并立即执行 `CODEX16CTX`。若在预选后再插入 `DBMOD`，候选应以
`validation-no-implied-selection` 预期 fail-closed，不能把它记成捕获失败。

样本 B：文档激活清缓存

```text
在原图清除缓存后执行 DBMOD
切换到另一张图纸
CODEX16CTXINFO
DBMOD
```

已取得的脱敏结果：发布状态 `published-read-only`、generation `2`、六类计数各 `1`、
canonical bytes `738`；用户清除状态 `cleared-user-command`、clear count `14`、
`published=false`、`selected=0`；样本 A 的捕获与清除窗口为 `DBMOD 4 -> 4`。样本 B 的
文档切换状态为 `cleared-document-activated`、事件计数 `1`，切换前原图清除后的命令行
`DBMOD=21`，切换后目标图命令行 `DBMOD=21`，旧缓存未跨图纸保留。该结果不证明文档
关闭生命周期、实体总数、插件自动保存、Agent 或 CAD 写入。

正式侧边栏公共契约决策门已经通过。Codex/Kimi 必须共同遵守
`handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md`：版本化事件、请求/响应、六类只读上下文、
一次审批、错误闭集和兼容规则均已冻结。两端可以并行开发视觉 UI，但不得让任一侧 UI
原型反向定义或扩展 v1；不兼容 wire 变化必须升级 v2。

## 9. CAD 审批与写入冒烟

只用脱敏 DWG 副本：

1. 请求一条测试直线，核对计划摘要和预览。
2. 选择“拒绝”，确认 `DBMOD`、实体数、revision 不变化。
3. 再次请求并选择“一次允许”，确认只新增展示计划中的一条直线。
4. 重放同一审批令牌，必须拒绝。
5. 审批后、执行前修改选择、图纸、图层或空间，旧计划必须拒绝。
6. 验证执行前在 `DocumentLock` 内重校验并在单个事务中完成。
7. 执行中停止 AgentHost，禁止自动重试 CAD 写入；结果不确定时停止后续写入并保留日志。
8. 成功写入后不得自动保存；关闭图纸应由 AutoCAD 正常询问是否保存。

一次审批、HMAC、防重放、锁内重校验或“不自动保存”任一项失败，均不得继续发布。

## 10. `.bundle` 候选、故障与回滚

只有手工 NETLOAD、Palette、只读、认证 Bridge 和审批写入全部通过后，才生成限制到 R20.1 的 2016 专用 `.bundle`。不要脚本化修改 `SECURELOAD`、`TRUSTEDPATHS` 或企业注册表。

故障分级：

- P0：图纸损坏、超出计划写入、自动保存、审批绕过。立即停止并保留 DWG 副本和日志。
- P1：AutoCAD 崩溃/卡死、未认证 IPC、令牌可重放、Agent 断开后自动重试写入。停止写入测试。
- P2：Palette 生命周期、DPI、选择摘要缺失、程序集绑定或加载策略问题。只允许继续只读诊断。
- P3：文案、布局和非阻断视觉问题。记录后排期。

回滚：关闭 AutoCAD，确认 AgentHost 已退出，移除本次测试 `.bundle`，不删除/覆盖用户 DWG，不修改 Autodesk 安装目录或系统注册表。重启 AutoCAD 只用于验证启动/卸载生命周期，不是重复诊断的前置条件。

## 11. 证据与提交纪律

- 每完成一个阶段，先通过对应构建、Specs 和实机验收，再单独提交 Git。
- AutoCAD 命令记录、环境采集、静态验证、本地 Specs 和端到端结果分别记录，不互相替代。
- 证据 JSON 不含 `TRUSTEDPATHS`、用户名、真实图纸路径、网络路径、许可证数据或 API Key。
- 当前诊断阶段只能称为“AutoCAD 2016 诊断编译/NETLOAD 兼容候选”，不得称为完整支持。
- 当前 Host.2016 可重复构建证据见 `handoff/autocad2016/evidence/host-build-verification-20260718.json`；该文件明确新候选未 NETLOAD，并且不包含本机路径、用户名、图纸路径或企业受信目录内容。
- 跨运行时 Bootstrap 原语证据见 `handoff/autocad2016/evidence/auth-bootstrap-verification-20260719.json`；该文件只保存脱敏计数、哈希和布尔边界，所有 live AgentHost、传输机密性及 CAD 集成项保持 `false`。
- 真实 AgentHost 有界安全引导证据由 `scripts/verify-autocad2016-agent-bootstrap-stage.ps1` 自动生成到 `handoff/autocad2016/evidence/agent-bootstrap-verification-20260719.json`；该文件绑定双 PowerShell 的 bootstrap/auth raw evidence、Phase2 raw log 和四个验证器 SHA-256，只保存哈希、计数、动态通过项和明确未验证项，不保存 artifact 临时目录、用户名、PID、原始 stderr 或 CAD/图纸路径。
- 只读选择上下文证据见 `handoff/autocad2016/evidence/readonly-context-build-verification-20260718.json`；文件同时记录静态/可重复构建门禁和 2026-07-19 的冻结候选实机检查点。Selection/context hash、图纸名称和路径不进入 Git；冻结 DLL SHA-256 正常入库，实体总数与插件自动保存 runtime 布尔值保持 `false`。
- 公共契约规范见 `handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md`；双 PowerShell、net45/net8 固定向量与 Phase 2 回归证据见 `handoff/autocad2016/evidence/cad-context-contract-v1-verification-20260719.json`。该证据的 `NetLoadVerified=false` 和 `RuntimeIntegrationVerified=false` 必须保持，直到统一 Host.2016 真实编译和人工 NETLOAD。
