# M4.15.5a 审计保留控制区人工复核状态

最后验证：2026-07-26（北京时间）

## 本轮结论

审计保留计划以前会保留未知 `retention-control` artifact，但产品计划输出没有明确告诉运维人员
“该控制区需要人工复核”，执行器也可能在未知文件仍存在时继续处理其他已批准会话。

本轮增加一个只读、有界、无路径输出的控制区检查结果：

- `ready`：只有合法 lock、receipt 和 receipt checkpoint，没有待恢复或人工复核项。
- `recovery_required`：存在合法命名的 journal、journal/receipt 临时文件或 checkpoint 临时文件；
  只输出必要的 plan hash，不输出文件名或路径。
- `manual_review_required`：存在未知文件、目录、reparse、超限/不可读 artifact，或合法命名但内容
  无法通过严格 schema 校验的 journal、receipt、checkpoint。

`audit-retention-plan` 继续使用 schema
`codex.autocad.agenthost.audit-retention-plan/1`，新增 `controlStatus` 字段；控制区状态不参与原
plan ID 计算，以免 journal/receipt 自身使已人工确认的计划 hash 无法恢复。执行
`audit-retention-apply` 时会在持有清理锁后重新检查控制区；未知、危险或不完整 inventory 使用稳定
`manual_review_required` 原因码失败关闭，不删除、移动、改写或猜测修复原 artifact。合法命名但
内容损坏的 artifact 仍保留既有更具体的 `journal_invalid` 等拒绝语义。

## 安全边界

- 检查只枚举 `retention-control` 顶层，不跟随目录或 reparse point。
- 最多检查 `4096` 个 artifact，单文件元数据上限 `4 MiB`；超过上限转人工复核。
- 输出只有 schema、状态、布尔值、计数、闭集 reason code 和必要 plan hash。
- 未知文件内容从不读取或序列化；测试中的 Bearer、用户名和路径标记不会进入状态 JSON。
- 检查不接受任意产品路径；生产 `audit-retention-plan/apply` 仍固定使用当前用户受保护审计根。
- 执行器不会自动归档未知 artifact；企业归档目的地、ACL、审批和审计流程仍待定义。

## 自动化证据

- 新增 Bridge 规格：
  `AgentHost审计保留控制区未知或恶意artifact明确转人工复核并拒绝清理`。
- 覆盖未知文件、未知目录、敏感内容不外泄、清理拒绝、证据保留、合法中断临时文件、伪造
  receipt 和 plan ID 稳定性。
- Bridge Specs：`81/81`。
- Host.2016 MVP：`61/61`。
- PowerShell 7 Phase 2：`419/419`。
- Windows PowerShell 5.1 Phase 2：`419/419`。
- AgentLauncher net8/net45：各 `65/65`，均包含连续 `500` 次启停。
- AgentLauncher evidence：
  `artifacts/autocad2016-agent-bootstrap-932e9e8394934374be95ba6b6e42881d/verification.json`。
- 本轮 AgentHost DLL SHA-256：
  `B10A3957A067750B2CF2AD20A1CB159439611C14DF60529A1D15594E5D1684D1`。
- Release：`0 warning / 0 error`；禁用 API、秘密扫描、AgentHost doctor 通过。

## 尚未验证

1. 真实磁盘满、卷离线、断电和突然复位；自动化 I/O 故障不能替代这些证据。
2. 企业默认保留期、容量、归档目标、归档审批、归档 ACL 和归档后校验。
3. AppLocker、WDAC、EDR/杀毒对控制区及归档工具的真实阻止行为。
4. 受限账户和企业服务身份下的人工复核/归档流程。
5. 真实 AutoCAD/AgentHost 异常退出留下 journal 后的端到端恢复。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具。M4.15.5、M4.15、M4 和 M4.16 仍未完成，M5 CAD 写入继续硬禁用。
