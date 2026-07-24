# M4.2 Codex 自动发现与健康预检交接

最后更新：2026-07-24（北京时间）

## 状态

M4.2 的源代码切口和非 AutoCAD 自动化门禁已通过并独立提交；AutoCAD 2016 x64 实机
启动链尚未绑定到正式 M4 候选，因此本子目标仍记为“实机验收待完成”，不能标记为最终完成。

本切口不启用 CAD 写入，不引入 Provider-neutral 抽象、Direct API Provider 或自研 Agent
Loop，也不修改主工作区中的 Host.2025/Kimi UI 原型。

## 身份

- 冻结目标 SHA-256：
  `164333019801590C57C21A74AE40F7E5AC8A677B1E1D60FE6D4D5E0C884B8DB5`
- 分支：`codex/m4-integration`
- 源代码提交：`3c373d555a4eb5ba988c2ffff7590045da3a2ee9`
- 提交说明：`feat(appserver): bind Codex health preflight to executable identity`

## 已实现

- 延续正式配置解析顺序：显式参数、受控环境配置、已知本机安装布局、绝对 PATH
  候选；无效显式配置 fail-closed，不静默回退。
- 启动前以 `UseShellExecute=false` 和参数数组执行 `codex --version`，禁止 shell 字符串拼接。
- 产品兼容窗口固定为 `>= 0.144.4` 且 `< 0.145.0`；兼容范围不是环境变量或用户日志
  可以扩大的一般配置。
- 版本进程 stdout/stderr 均有字节上限，使用严格 UTF-8；非零退出、输出超限、编码错误、
  格式错误、超时和不兼容版本均返回稳定结构化错误。
- 版本预检超时或取消会有界终止完整进程树；终止失败单独映射为稳定错误，不把原始
  stderr、真实路径或环境内容带入产品错误。
- 对已解析的 `codex.exe` 和父目录链持有 Windows 文件身份租约。租约覆盖版本预检、
  app-server 启动和运输生命周期，阻止或检测预检后替换、目录重命名和身份变化。
- `doctor`、`run` 和认证后的 `bootstrap-serve` 在报告可用前均执行版本预检和真实
  app-server initialize 握手。
- 握手超时通过链接取消令牌取消底层 `StartAsync`；调用方主动取消仍传播
  `OperationCanceledException`，不会误报为握手超时。
- AgentHost JSON 与审计只输出稳定错误码；版本、身份租约、握手超时和握手失败相互区分。

## 自动化证据

最终双 Shell 门禁：

- PowerShell 7.6.4：通过。
- Windows PowerShell 5.1.19041.6456：通过。
- Phase 2：`353/353`。
- Bridge：`46/46`。
- AppServer net45/net8：各 `30/30`，输出一致。
- Release：`0 warnings / 0 errors`。
- 本机 Codex：`0.144.4`，真实 app-server initialize 握手通过。
- 敏感信息扫描、Host 禁用 API 扫描、确定性双构建和 Git diff check：通过。
- 测试结束后无 AgentHost、TestServer、FakeAgentHost 或测试 `ping` 进程残留。

原始验证文件（未纳入 Git）：

```text
artifacts/autocad2016-bridge-client-stage-2cbbcfff98ca46b083790c7a075af62f/verification.json
SHA-256: 5612D1DD572953E2F048B641CA96651F696A516F5196FBB942F24A578696DB72
```

复现入口：

```powershell
pwsh -NoProfile -File .\scripts\verify-autocad2016-bridge-client-stage.ps1 -Configuration Release
```

该脚本会分别驱动 PowerShell 7 和 Windows PowerShell 5.1 的隔离 worker，并生成新的、
路径不同且哈希也可能不同的证据目录；判定应以新证据内容和该文件自身 SHA-256 为准。

## 证据边界

本次验证没有启动或控制 AutoCAD，没有执行 `NETLOAD`，没有发送 CAD 命令，也没有证明：

- AutoCAD 2016 Host 从 Palette 启动 AgentHost 后能够完成这套版本预检和握手；
- 实机上的启动失败、超时、断线、重复启动/停止和 AutoCAD 正常退出清理；
- 正式 M4 候选的精确 DLL/EXE/manifest 哈希；
- M4.3 之后的会话 `CODEX_HOME`、凭据、资源配额、ACL、审计链和企业故障矩阵；
- M4.16 安全候选。

`autoCadLiveEvidence=false` 是预期且必须保留的边界，不得用本自动化证据替代 AutoCAD
实机记录。

## 下一验收入口

M4 集成候选冻结后，在干净 AutoCAD 2016 x64 进程中使用候选 manifest 指定的 Host DLL，
至少验证 `NETLOAD`、`CODEXCADDOCTOR`、`CODEX16AGENTSTART`、一次只读上下文问答、重复
`CODEX16AGENTSTOP` 和不手动 STOP 的正常退出清理。命令记录必须绑定候选 manifest 和
精确哈希，且确认 DBMOD 不因插件命令变化、CAD 写入与插件保存仍为禁用。
