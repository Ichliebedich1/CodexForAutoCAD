# M4：AgentHost 私有存储与保留清理

最后更新：2026-07-23（北京时间）

## 结论

AgentHost 的真实 `bootstrap-serve` 会话现在使用独立 session workspace，并将工作区与审计目录
收敛到受保护的 Windows ACL。正常 STOP 会先给 Bridge/AgentHost 最多 `1` 秒自然退出时间，使
AgentHost 完成审计终态和 workspace 清理；只有仍未退出时才进入既有 `5` 秒强制进程树回收。

这关闭了 M4 的“工作目录最小 ACL 与有界保留清理”切片，但不是磁盘硬配额、凭据隔离、
AppContainer 或受保护审计锚点/签名方案。CAD 写入与插件保存继续禁用；本地 SHA-256 链的范围
见 `M4_AUDIT_HASH_CHAIN_20260723.md`。

## 私有 ACL

工作区 session 根、`inputs`、`work`、`outputs`、`temp`、lease 文件、审计目录和审计文件都采用：

- 关闭父 ACL 继承并拒绝任何继承规则；
- owner 固定为当前 Windows 用户；
- 仅当前用户、`LOCAL SYSTEM` 和内置 Administrators 拥有显式 FullControl；
- 应用后重新读取 owner 与完整规则集，任何额外、缺失、拒绝或继承规则均 fail-closed。

存储根必须是固定本地磁盘的绝对路径。UNC、设备路径、ADS 和路径中任一重解析目录均拒绝。
清理器不跟随文件或目录重解析点，并将单次树扫描限制为 `50,000` 项。

## Workspace 生命周期

每个认证 bootstrap session 使用受协议约束的 `32` 位小写十六进制 ID 创建目录，并创建
`.codex-session.lock` 独占 lease。活动 lease 不允许保留清理器取得写独占句柄，因此不会被
另一个 AgentHost 会话误删。

默认策略：

| 项目 | 默认值 |
| --- | ---: |
| session 过期年龄 | 24 小时 |
| 最多保留 session | 64 |
| 单次发现上限 | 4,096 |
| 单次目录树清理上限 | 50,000 项 |

正常退出直接删除当前 session workspace。崩溃残留只有在 lease 已释放后才可被后续启动清理；
过期 session 优先删除，达到容量时再从最旧的非活动 session 开始删除。全部候选仍活动或清理
失败时，新的 AgentHost 会话 fail-closed，不会无界增长。

## 审计保留

每个 session 仍以 `CreateNew` 创建一个内容脱敏 JSONL。新增默认策略为保留 `30` 天、最多
`512` 个受管理文件，绝对发现上限为 `4,096`。活动日志以不允许删除共享的方式保持打开，
保留清理不会删除活动文件。未知文件不作为受管理日志删除，但仍计入发现上限以避免无界扫描。

审计 schema、字段白名单、容量和 fail-closed 行为仍见
`M4_RUNTIME_AUDIT_BASELINE_20260723.md`；本切片没有伪造 `approval_resolved` 或 CAD 写入终态。

## 验证

```text
Bridge/AgentHost Specs: 49/49
AgentLauncher bootstrap gate: net45 36/36, net8 36/36
Windows PowerShell 5.1 Phase 2: 334/334, 0 warnings, 0 errors
PowerShell 7 Phase 2: 334/334, 0 warnings, 0 errors
真实 Codex v2 live: 2/2
live 前后 managed session 目录: 2 -> 2
live 后 AgentHost: 0
```

规格覆盖 ACL owner/规则集、正常删除、过期残留、活动 lease、目录链接不跟随、审计活动文件、
审计保留和重解析根拒绝。真实 live 同时启动 Bridge STOP 与 AgentHost STOP，并直接断言当前
session workspace 在 `2` 秒内消失。测试前已有的 Codex 桌面进程集合未变化。

脱敏证据：`evidence/m4-agenthost-private-storage-retention-20260723.json`，当前工作区字节
SHA-256 为 `41CA5E7EF36565600C8A7137E856C8308B7A5A0052A275953A5A3F1D18614E5C`。

本轮没有启动、关闭或控制 AutoCAD，没有 `NETLOAD`，也没有发送 CAD 命令、修改或保存图纸。

## 未完成边界

- 工作目录可靠磁盘硬配额；不得用轮询目录大小冒充硬配额。
- 每会话 `CODEX_HOME`、独立凭据、插件配置隔离；默认空 MCP 已由
  `M4_EMPTY_MCP_BOUNDARY_20260723.md` 的结构化配置覆盖完成。
- 受限令牌或 AppContainer，以及企业嵌套 Job/受限桌面兼容矩阵。
- 真实 Codex 的进程槽、总提交内存和 CPU 节流耗尽测试。
- AutoCAD/AgentHost 异常退出、断电式残留和僵尸进程完整矩阵。
- 审计哈希链或等价防篡改、脱敏导出、审批解决与 M5 CAD 写入终态。

以上完成前不得把 M4 标记为完成，也不得启用 AutoCAD 2016 CAD 写入。
