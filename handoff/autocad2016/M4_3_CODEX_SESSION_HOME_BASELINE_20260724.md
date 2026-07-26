# M4.3 每会话 Codex Home 基础交接

最后更新：2026-07-24（北京时间）

## 状态

M4.3 的可选调用链、会话 home 租约和非 AutoCAD 自动化基础已完成并独立提交；生产
`bootstrap-serve` 仍使用现有已登录的全局 Codex profile，因此本子目标不能标记为最终完成。

保持生产默认不变是有意的 fail-closed 决策：本机全局 Codex `0.144.4` 已登录 ChatGPT，空的
`CODEX_HOME` 则未登录。当前代码不复制、链接、解析或记录全局 `auth.json`、令牌、API key、
完整环境变量或用户 MCP/插件配置。M4.11 凭据 Broker 形成受支持的授权路径前，不允许把空
session home 直接切成生产默认。

本切口不启用 CAD 写入，不引入 Provider-neutral 抽象、Direct API Provider 或自研 Agent
Loop，也不修改主工作区中的 Host.2025/Kimi UI 原型。

## 身份

- 冻结目标 SHA-256：
  `164333019801590C57C21A74AE40F7E5AC8A677B1E1D60FE6D4D5E0C884B8DB5`
- 分支：`codex/m4-integration`
- 源代码提交：`6d99bb96d0c5c5df20d68e58959236d0209939d1`
- 提交说明：`feat(agenthost): prepare isolated Codex session homes`

## 已实现

- `CodexLocalAppServerConfigurationRequest` 增加可选 `CodexHomeDirectory`；仅接受存在于固定
  本地磁盘、完全限定且路径链不含重解析点的目录。
- Codex 子进程使用显式环境白名单并设置 `InheritParentEnvironment=false`。未显式提供
  session home 时，不继承父进程 `CODEX_HOME`；提供后，以验证过的 session 路径覆盖父值并
  进入真实 `CodexProcessTransport` 子进程。
- `CodexSessionHomeLease` 只接受 32 字节小写十六进制系统 session ID，在该 session 的受管
  root 下创建唯一 `codex-home`。
- home 固定写入空 `mcp_servers`、`features.plugins=false`、空插件目录、缓存目录、活动租约
  文件和版本化 session marker。
- 已存在 home、非法 session ID、非法 root、初始化失败和清理失败均 fail-closed，并映射为
  不含路径或原始异常正文的稳定审计错误码。
- 正常释放会删除受管 home；根目录及任意子项为 junction/symlink 等重解析点时只删除链接
  本身，不递归跟随目标。
- 双 Shell evidence 的源码 manifest 扩大到全部非生成 `src`、`tests`、`scripts`、主 solution、
  `global.json` 和 `Directory.Build.props`，并使用显式 Ordinal 排序，避免 PowerShell 7 与 5.1
  的文化排序差异制造伪失败。

## 认证边界

本机 `codex login --help` 公开的受支持入口包括：

- `--with-api-key`：从 stdin 读取 API key；
- `--with-access-token`：从 stdin 读取 access token；
- `--device-auth`：交互设备授权。

这些入口只证明存在官方协议，不等于项目已经安全实现凭据 Broker。后续 M4.11 必须选择明确
授权方式、限制凭据生命周期、禁止命令行/日志/普通文件暴露，并在隔离 home 中完成登录恢复、
取消、过期和撤销测试。不得反向解析全局 profile 的私有文件格式。

## 自动化证据

最终双 Shell 门禁：

- PowerShell 7.6.4：通过。
- Windows PowerShell 5.1.19041.6456：通过。
- AppServer：`32/32`。
- Bridge：`49/49`。
- Phase 2：`358/358`。
- Bridge Client net45/net8：各 `30/30`，输出一致。
- Release：`0 warnings / 0 errors`。
- 本机 Codex：`0.144.4`，真实 app-server initialize 握手通过。
- 确定性双构建、Host 禁用 API、敏感信息和 Git diff 检查：通过。
- 测试结束后 AgentHost、TestServer、FakeAgentHost 和测试 `ping` 残留均为 `0`。

原始验证文件（未纳入 Git）：

```text
artifacts/autocad2016-bridge-client-stage-20812ff0e1bf411d8bda01ee888df605/verification.json
SHA-256: 035A51F324352520FD129302C8B606DE7F0D38BDA2735D92E96A898F997FEF67
sourceManifestFileCount: 212
sourceManifestSha256: 0F8B3EDAC2EA1408AE6E93E69507EAAD0732F4F88F0BB909F42944F2D3F10533
```

复现入口：

```powershell
pwsh -NoProfile -File .\scripts\verify-autocad2016-bridge-client-stage.ps1 -Configuration Release
```

## 未完成与证据边界

`autoCadLiveEvidence=false`。本次没有启动或控制 AutoCAD，没有执行 `NETLOAD`，也没有证明：

- 隔离 home 已进入生产 `bootstrap-serve`；
- 隔离 home 能通过批准的凭据 Broker 建立真实 Codex 会话；
- AgentHost/Codex 异常退出后的 stale-home 自动识别、恢复和有界清理；
- M4.8 的最小 ACL、跨进程 lease 强化及同用户 TOCTOU 对抗；
- AutoCAD 2016 中启动、问答、停止、断线、重复启动/停止和正常退出矩阵；
- 正式 M4 候选的精确 DLL/EXE/manifest 哈希；
- M4.16 安全候选。

生产激活顺序必须是：M4.8 收紧 ACL/lease，M4.11 提供受支持凭据 Broker，在独立进程中验证
隔离 Codex 登录和 app-server，再进入 AutoCAD 2016 实机矩阵。任一步失败都保持现有全局只读
模式且 CAD 写入继续禁用。
