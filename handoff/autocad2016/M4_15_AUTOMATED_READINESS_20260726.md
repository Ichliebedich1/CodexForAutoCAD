# M4.15.6 自动化就绪证据绑定

日期：2026-07-26

## 结论

M4.15.6 的自动化收口切口已经完成。当前状态只能称为
`automated_readiness_only`，不能称为 M4 完成、M4.16 冻结或可进入 M5。

本切口没有启动、重启或控制 AutoCAD，没有执行 `NETLOAD`，没有启用 CAD 写入或插件保存，
没有修改用户或机器 PATH，也没有读取、记录或输出凭据、访问令牌、完整环境变量或
`TRUSTEDPATHS`。

## 实现

1. `scripts/verify-phase2.ps1`
   - 新增可选 `-EvidencePath`。
   - 输出 PowerShell edition/version、Release 配置、九个规格项目的动态计数、总计数、构建、
     Host 禁用 API、AgentHost doctor、Git diff 和基础秘密扫描状态。
   - 明确记录 AutoCAD 未启动/未控制、CAD 写入和插件保存禁用、企业/真实机器矩阵未验证。
2. `scripts/verify-m4-r201-host-build.ps1`
   - 只读取目标机 AutoCAD 2016 的 `20.1.0.0` 托管 API 身份与哈希。
   - 对当前 Host.2016 执行两次隔离依赖恢复和 R20.1/.NET Framework 4.5/x64 Release 构建。
   - warning 视为 error；比较五文件输出快照和 Host DLL SHA-256。
   - 验证不复制 Autodesk DLL、跟踪锁文件恢复、AutoCAD 进程集合不变、相关 Agent 进程残留为
     `0`。
3. `scripts/verify-m4-automated-readiness.ps1`
   - 严格读取并验证 PowerShell 7/5.1 Phase 2、Agent bootstrap、认证原语和 R20.1 Host evidence。
   - 双 Shell Phase 2 必须具有相同的九项目动态规格集合和总数。
   - 绑定当前 Git HEAD/分支/dirty 状态、源码 manifest 摘要、Bridge.Client 锁文件、Host 与
     AgentHost 候选哈希、用户 PATH 长度/哈希和相关进程残留计数。
   - 输出不持久化原始路径、原始环境或输入 evidence 的内部路径，只持久化输入 evidence
     SHA-256。
   - `-SelfTestOnly` 在 PowerShell 7 和 5.1 中验证：未验证企业项的 `true`、无效候选哈希和
     部分通过规格摘要必须失败关闭。
4. `scripts/verify-autocad2016-auth-compat.ps1`
   - 删除过期 Bridge `49/49` 硬编码，改为解析唯一且全部通过的动态规格摘要；当前为
     `83/83`。

## 当前自动化证据

- PowerShell 7 Phase 2：`421/421`。
- Windows PowerShell 5.1 Phase 2：`421/421`。
- Bridge：`83/83`。
- Host MVP：`61/61`。
- AgentLauncher net45/net8：各 `65/65`，包含每目标框架 500 次启停。
- R20.1 Host：`.NET Framework 4.5`、`x64`、A/B 五文件逐字节一致、`0 warning / 0 error`、
  Autodesk DLL 复制数 `0`。
- Host DLL SHA-256：
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`。
- AgentHost DLL SHA-256：
  `780D3CD57786CC624D8A033B2069E41095F7119EE4E695110D7E94E8CCB399D2`。
- 用户 PATH：长度 `661`，只记录 SHA-256 指纹，不记录内容。
- AgentHost/FakeAgentHost/Bridge TestServer 残留：`0`。
- readiness 汇总器 PowerShell 7/5.1 自检：通过。
- readiness 汇总器 PowerShell 7/5.1 正式输出：通过，除记录时间与 JSON 格式缩进外语义等价。

当前临时输入和汇总 evidence 位于 `artifacts/m4-readiness-inputs/`；该目录不作为已提交正式
M4.16 候选，重新构建或源码变化后必须重新生成。

## 明确未验证

以下字段在 readiness evidence 中必须保持 `false`：

- 真实 Credential Manager 凭据读取；
- 真实 Codex 登录、keyring 持久化和生产 `auth.json` 缺失；
- 生产 RestrictedToken 或其他受限身份全链；
- 固定容量卷、真实磁盘满和卷离线；
- 系统断电；
- 真实 Codex/AgentHost/AutoCAD 正常退出、异常退出、强杀和分阶段启动中断矩阵；
- AppLocker、WDAC、杀毒/EDR 和企业父 Job 矩阵；
- 企业默认保留、人工归档、ACL、审批和恢复矩阵；
- AutoCAD `NETLOAD`、实机运行时绑定；
- M4 完成和 M4.16 冻结。

## 复现入口

所有 dotnet 命令前设置：

```powershell
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
```

分别使用 PowerShell 7 和 Windows PowerShell 5.1 运行 `verify-phase2.ps1` 并传入不同的
`-EvidencePath`；再运行 `verify-m4-r201-host-build.ps1`、当前 Agent bootstrap 门禁和
`verify-autocad2016-auth-compat.ps1`。最后把四类 evidence 传给
`verify-m4-automated-readiness.ps1`。汇总器可先用 `-SelfTestOnly` 做双 Shell 自检。

任何输入缺失、schema/status 不符、规格未全通过、候选哈希非法、锁文件不一致、相关进程残留，
或把未验证矩阵错误设为 `true`，都必须失败关闭且不生成 readiness 输出。
