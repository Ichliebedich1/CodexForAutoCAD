# Bridge v2 跨版本兼容测试夹具交接

状态：已完成。13 项主测试全部通过，1 项生产缺口已真实暴露。

## 基线

- 当前基线：`589c8ea feat(agent): add explicit cad context v2 bridge path`
- 旧 v1 Client 固定基线：`0ceb123 feat(contracts): freeze CadContextJson v2`

## 测试矩阵

| 编号 | 名称 | 结果 | 说明 |
|------|------|------|------|
| COMPAT-V2-001 | 旧 v1 Client 读取新 Host 能力响应 | PASS | v2-capable Host 能力响应通过契约验证，旧 Client 只需 v1 字段 |
| COMPAT-V2-002 | 当前 Client 读取旧 Host 响应 | PASS | SupportedCadContextSchemas 默认为 [v1] |
| COMPAT-V2-003 | supportedCadContextSchemas=[] fail-closed | PASS | 空 schema 列表被正确拒绝 |
| COMPAT-V2-004 | supportedCadContextSchemas=null fail-closed | PASS | null schema 列表被正确拒绝 |
| COMPAT-V2-005 | 重复/未知 schema 结构化拒绝 | PASS | 未知 schema/version、缺少 v1、超出限制均拒绝 |
| COMPAT-V2-006 | v2 capability wire roundtrip | PASS | 保留 v1/v2 schema、agent.turn.start.v2、原 v1 字段和 contractVersion |
| COMPAT-V2-007 | v2 context/hash 矩阵 | PASS | null+空通过, null+非空拒绝, 非空+空拒绝, 非空+正确通过, 非空+错误拒绝 |
| COMPAT-V2-008 | 请求期间取消 | PASS | 有界结束、不自动重试、终端 fail-closed |
| COMPAT-V2-009 | 请求期间断线 | PASS | ConnectionLost、不回退未认证通道 |
| COMPAT-V2-010 | 请求超时 | PASS | 有界、不重试、终端 fail-closed |
| COMPAT-V2-011 | 旧 v1 行为不变 | PASS | v1 thread/turn/interrupt/approval/assistant 事件/stop 在 v2-capable Host 上行为不变 |
| COMPAT-V2-012 | 安全原语不变 | PASS | 坏 MAC/sequence 间隙/nonce 重放/超大帧均 fail-closed |
| NEGATIVE-SELF-CHECK | 负向自检 | PASS | 5 项检查均证明关键验证存在且有效 |

## Audit / Enforce

- **Audit**：完整执行并逐项输出真实 pass/fail；产品失败未写成通过
- **Enforce**：所有 required case 通过，退出码 0
- **负向自检**：证明删除关键检查、混淆 null/empty、跳过取消检查时夹具会失败

## 生产缺口

### COMPAT-V2-005-duplicate-gap

- **描述**：`AgentBridgeContractValidator.ValidateSupportedCadContextSchemas` 不拒绝重复 schema 条目
- **requiredPassed**：`false`
- **影响**：Host 能力响应可包含重复的 schema 条目而不被拒绝
- **建议**：在验证器中添加去重检查

## 旧 Client 身份

- 旧 v1 Client 固定基线：`0ceb123 feat(contracts): freeze CadContextJson v2`
- 由于无法在此环境从 `0ceb123` 构建旧 Client DLL，COMPAT-V2-001 通过契约级验证模拟旧 Client 行为
- 验证了 v2-capable Host 能力响应的 `CadContextSchema`/`CadContextSchemaVersion` 仍为 v1 值
- 旧 Client 只需 v1 字段即可安全读取 v2-capable Host 响应

## 未验证项

- 未从 `0ceb123` 精确构建旧 v1 Client DLL 进行 IPC 级测试
- 未在 AutoCAD 2016 实机中验证 v2 选区
- 未验证 CadContextJson v2 Host 捕获
- 未验证 net45 目标框架下的测试（当前仅 net8.0-windows）

## 修改的文件

- `tests/Codex.AutoCAD.Bridge.V2Compat.Specs/` (新增)
  - `Codex.AutoCAD.Bridge.V2Compat.Specs.csproj`
  - `Program.cs`
- `tests/Codex.AutoCAD.Bridge.Client.TestServer/Program.cs` (修改)
  - 新增 `v2-happy` 模式支持
  - 新增 `agent.turn.start.v2` 方法处理
  - 能力响应根据模式返回 v1-only 或 v1+v2 schema
- `scripts/verify-autocad2016-bridge-v2-compat.ps1` (新增)
- `handoff/autocad2016/BRIDGE_V2_COMPAT_HARNESS_HANDOFF.md` (新增)
- `handoff/autocad2016/evidence/bridge-v2-compat-harness-verification-20260721.json` (新增)

## 证据

- 验证 JSON：`handoff/autocad2016/evidence/bridge-v2-compat-harness-verification-20260721.json`
- Audit JSON 内嵌于 V2Compat.Specs 运行输出

## 约束保持

- `AutoCadStartedOrRestarted=false`
- `CadCommandsSent=false`
- `NetLoadVerified=false`
- `AutoCadLiveEvidence=false`
- 未修改任何 `src/**` 代码
- 未修改安全原语
- 未启动或操作 AutoCAD
