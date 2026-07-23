# 阶段 2 安全边界与交付状态

> 历史快照：本文件主体冻结在 2026-07-18，只用于解释早期阶段 2 边界，不再代表当前产品状态。
> 当前调用链、测试计数、M0-M4 进度和发布阻断项以
> `handoff/autocad2016/CURRENT_STATE.md` 与 `handoff/autocad2016/LONG_TERM_MEMORY_TODO.md`
> 为准；不得引用本文的 `121/121` 或“Host 尚未连接 AgentHost”作为当前结论。

本文记录阶段 2 实际已经实现的安全边界，以及仍然属于后续阶段的发布阻断项。它用于避免把“进程内 CAD 预演”误称为完整操作系统沙箱，也避免在真实 AgentHost 尚未接通时宣称自然语言绘图已经端到端完成。

## 已实现

### 本地 IPC

- 仅当前 Windows 用户可访问的命名管道。
- 每会话随机密钥由宿主注入，不通过命令行、磁盘或日志传递。
- 长度前缀帧上限 8 MiB，JSON 深度上限 32。
- 每个方向独立序号、nonce 与 HMAC；坏 MAC、重放、乱序、重复 nonce、部分帧和异常 EOF 均关闭连接。
- 会话结束后清零认证密钥副本。

### Agent 运行时边界

- 默认 `read-only`；`workspace-write` 必须由宿主提供已存在的绝对 `ManagedWorkspaceRoot`。
- 显式工作目录不能扩大受信根，UNC、设备路径、路径越界、符号链接和目录联接点默认拒绝。
- 本地图片和 mention 输入默认关闭；启用后仍必须位于受信根内。
- CAD 动态工具仅允许 `cad.propose_operations`，当前白名单只有强类型 `create_line`。
- 模型不能提供图纸指纹、修订号、选择哈希、审批结果或能力令牌。
- CAD 调用绑定活动的 `(threadId, turnId, callId)`，具有有界幂等缓存和并发、输入、路径、提示词配额。
- 只有受信 `IAgentCadProposalBroker` 返回事务终态 `Applied` 时工具调用才成功；未连接、拒绝、失败、取消和超时全部返回失败。

### CAD 审批核心

- CAD 计划只能通过 `CadApprovalGate.Propose(CadOperationBatch)` 进入审批门。
- 审批门内部深拷贝计划，重新执行 Schema 校验、规范化哈希、操作计数和风险推导；旧的 Binding/Descriptor API 不能承载 CAD 写计划。
- 计划哈希覆盖文档、修订、选择策略、具体空间 Handle、图层 Handle、图层名及全部操作参数。
- R3 只签发 60 秒、单次使用、与计划和图纸状态绑定的能力令牌；令牌秘密不转换为字符串并在消费或失败后清零。
- R4 在进入用户决策前必须记录检查点证据；检查点、计划摘要和风险事实由门内 HMAC 密封，缺失或篡改均使批准失效。

## 2026-07-18 托管核心验证证据

- 当前证据对应 Git HEAD `2d2ad3738095794c8374e916559c0c5d13702ba1` 上的**本地未提交工作树**；它不是已提交产物或发布构建证明。
- 已安装并由 `global.json` 解析到 .NET SDK 8.0.319。
- 主解决方案的托管核心 Release `-m:1` 构建通过：0 警告、0 错误。门禁会核对 AgentHost、Bridge、AgentRuntime 和 7 个 Specs 均属于所选配置的默认构建，同时核对 AutoCAD 2016/2025 进程内 Host 不属于主解决方案默认构建，避免缺少目标版本安装时产生假失败。
- 当前 7 个规格项目动态汇总为 121/121：Contracts 15/15、IPC 11/11、Security 19/19、AppServer 7/7、Bridge 29/29、AgentRuntime 31/31、Chat 9/9。`verify-phase2.ps1` 从各规格进程的唯一真实摘要动态解析并求和，不把 121 或任一子项目计数硬编码为放行条件。本文旧版记录的 107/107 已被本次当前工作树证据取代，不再代表现状。
- Bridge 当前用户命名管道、HMAC、seq/nonce、防重放、帧/深度限制、容量和断线行为在沙箱外受控测试中通过；29 项完整规格连续运行 20 轮，累计 580/580。生命周期专项同时覆盖关闭超时边界、同步阻塞 handler、忽略取消 handler 与并发 Dispose、迟到 fault 的 `TerminalError`/Unobserved 处理，以及非协作 stream/sendGate 静止前不清密钥、静止后延迟清零。
- AgentHost 使用本机原生 `codex.exe` 的 app-server doctor 握手通过并自然退出；生产路径现使用
  `-c mcp_servers={}` 覆盖默认 MCP server 表，但当前 doctor 仍使用全局 `%USERPROFILE%\.codex`，
  不能替代后续每会话独立 `CODEX_HOME`、插件配置和凭据隔离实现。
- Host.2025 词法禁用 API 规则自检与源码扫描通过，明确覆盖 `Database.Save/DwgOut/DxfOut`、`Application.Quit/Invoke`、命令字符串执行、`Process.Start`、`FileStream`/文件写入、动态加载、直接 IPC/网络和注册表；未暂存及已暂存 `git diff --check` 与基础秘密扫描也通过。
- 门禁及 Bridge 压力运行结束后未发现残留 `dotnet.exe` 或 `Codex.AutoCAD.AgentHost.exe` 进程；测试进程均自然退出，未通过强制终止获得绿色结果。
- 上述证据全部是本机托管核心验证，**不是 AutoCAD live 证据**：没有在本次门禁中执行 Host.2016 NETLOAD、Palette、选择读取、认证 Bridge 或 CAD 写入 E2E，也不能据此扩大为 AutoCAD 2016 完整支持声明。

Host 禁用 API 扫描是保守的源码词法拒绝列表，会连同注释和字符串一起扫描，并通过危险/安全样例自检防止已知规则退化；它不是 Roslyn 语义分析、IL 审计或运行时拦截，不能单独证明别名、反射、源码生成器或未来 Autodesk API 变体绝对不存在。因此该结果只作为托管核心提交门禁中的负面信号，不能替代目标 Host 的代码审查、真实编译和 AutoCAD 实机验证。

## 尚未完成，禁止据此发布

- AutoCAD 宿主尚未启动真实 AgentHost，也未实现 stdin 会话密钥引导和具体命名管道 `IAgentBridgeClient`。
- `IAgentCadProposalBroker` 尚未连接到 AutoCAD 主线程的预览、审批和事务执行器，因此自然语言到 DWG 的完整 E2E 尚未成立。
- 当前工作树中的 AutoCAD 2025 UI/选择/直线写入纵向原型不属于本阶段托管核心提交，也未完成目标版本真机验收；不得用该原型证明 Side Database 预演、Palette、`DocumentLock`、锁内重验、单事务或不自动保存已经交付。即使后续使用 Side Database，它也只是 CAD 一致性预演，不是进程隔离。
- 独立受限令牌或 AppContainer、工作目录硬配额、受保护审计锚点及真实 Codex/AutoCAD 故障矩阵
  尚未实现。后续 M4 已实现 Windows Job Object、进程树终止、CPU/内存/时间配额；其范围不等同于
  完整 OS 沙箱。
- 认证 `bootstrap-serve` 已有可选每会话 `CODEX_HOME`/`CODEX_SQLITE_HOME` 与 Windows Generic
  Credential 路径：无引用时保持默认 profile 兼容；有引用时不复制、链接、读取、记录或修改全局
  `.codex` profile，版本预检不接收 token。该结论目前仅来自 synthetic 凭据/目录规格；真实
  Credential Manager、真实隔离登录及插件/技能配置读取面仍未验收。详见
  `handoff/autocad2016/M4_CODEX_SESSION_ISOLATION_20260723.md`。
- R4 审批门已经强制检查点证明，但真实恢复 DWG 的创建、摘要和恢复演练尚未接入。
- SQLite 上下文记忆、审计哈希链、保留/清除策略尚未实现。
- AutoCAD 2016 已完成 net45/x64 诊断薄宿主的目标机原版程序集编译与手工 NETLOAD；Palette、选择上下文、认证 Bridge、审批和 CAD 写入仍未接入，不能宣称完整支持。

## 阶段 2 托管核心提交门槛

提交托管核心阶段前必须同时满足；AutoCAD 目标 Host 的功能阶段仍需各自真机验证和独立提交：

1. 固定 SDK 的托管核心 Release 构建通过。
2. Contracts、IPC、Security、AppServer、Bridge、AgentRuntime、Chat 全部规格通过。
3. Host.2025 词法拒绝列表及规则自检通过，未发现命令字符串执行、保存/导出/退出、动态加载、文件/注册表/网络/IPC 或任意进程启动入口；该扫描的非语义边界按上文限定，不得扩大解释。
4. 本机 `codex app-server` doctor 握手通过。
5. `git diff --check` 和基础秘密扫描通过。
6. AutoCAD 2016/2025 Host 必须分别使用目标机原版程序集构建，不把 Autodesk DLL 纳入核心提交。
7. 完成以上验证后才允许创建托管核心阶段提交。
