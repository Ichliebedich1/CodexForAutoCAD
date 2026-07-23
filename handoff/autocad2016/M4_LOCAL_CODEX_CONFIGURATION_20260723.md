# M4：本机 Codex 启动配置

最后更新：2026-07-23（北京时间）

## 目标和边界

本切口把 AgentHost 对本机 Codex 的启动输入收敛为一份经过校验的配置，并把 Codex CLI 版本和
App Server 初始化纳入启动前门槛；不引入 Provider 抽象、自研 Agent Loop、每会话 `CODEX_HOME`
或独立凭据。本切口没有启动、关闭或控制 AutoCAD，也没有加载 DLL、保存或修改图纸。

## 已实现

- `CodexLocalAppServerConfigurationResolver` 按以下优先级解析本机可执行文件：
  1. `--codex`；
  2. `CODEX_EXECUTABLE`；
  3. 已知 npm `@openai` 安装布局；
  4. 仅由绝对 PATH 条目组成的 `codex.exe` 候选。
- 任何显式 `--codex` 或 `CODEX_EXECUTABLE` 无效都会 fail-closed，绝不静默回退到另一个
  PATH 可执行文件。
- 解析结果必须是已存在、非 reparse point、固定本地磁盘上的绝对 `.exe`；工作目录也必须是
  已存在、非 reparse point、固定本地磁盘的绝对目录。网络、相对、裸命令和 shell 包装均被拒绝。
- 启动和关闭超时成为正式配置（当前默认分别为 15 秒和 5 秒，最大均为 60 秒）。
- `CodexVersionCompatibility` 是产品拥有的配置，默认只接受 `>=0.144.4 <0.145.0`；它不从用户
  环境变量读取，避免未经审查的本地覆盖扩大可接受协议面。
- `doctor`、`run` 和认证 `bootstrap-serve` 均先在同一清空父环境后的 child allowlist 中运行
  `codex --version`。输出严格限制为单行 UTF-8 三段版本、最多 `4 KiB`；不兼容、异常、超时、
  非文本或超限输出均 fail-closed。
- `doctor`、`run` 和受认证的 `bootstrap-serve` 使用同一配置对象；`doctor` 只在既有非敏感
  健康字段外增加 `codexExecutableSource`、规范化版本和范围，不报告真实路径、版本原文或 stderr。
  版本通过后仍须完成 App Server `initialize`；在 `bootstrap-serve` 中，Bridge 会话不可用前还会
  完成 `CodexAgentRuntime.StartAsync()`。
- 无效配置返回稳定 JSON：`error=codex_configuration` 与 `errorCode`；错误文本和日志不包含
  可执行文件、工作目录或环境变量内容。

## 已验证

```text
dotnet run --project tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj --configuration Release
Result: 27/27 specs passed

dotnet build src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj --configuration Release
Result: 0 warnings, 0 errors

scripts\verify-phase2.ps1 -Configuration Release
Result: Release 0 warnings / 0 errors; dynamic specs 341/341; Host disabled-API and basic
sensitive-information scans passed; local AgentHost doctor handshake passed.

AgentHost doctor
Result: local npm-discovered Codex 0.144.4 passed the frozen version preflight and completed app-server initialize;
doctor emitted only the source label and normalized version/range.

AgentHost doctor --codex relative-codex.exe
Result: stable codex_configuration / InvalidConfiguredExecutable; no path escaped.
```

## 尚未完成

- 当前冻结范围只覆盖已验证的 `0.144.x`；升级新的 Codex 次版本前必须重新审查 App Server 协议、
  更新范围、补齐 real doctor/live 和双 Shell 门禁，不能靠环境变量绕过。
- 每会话独立 `CODEX_HOME`、插件配置隔离和独立凭据仍未完成；默认空 MCP 已由生产
  `-c mcp_servers={}` 覆盖完成，现有父环境白名单不等于其余边界。
  边界。
- 工作目录磁盘硬配额、受限令牌/AppContainer、受保护审计锚点和真实 Codex/AutoCAD 异常退出、僵尸
  进程矩阵也仍不属于本切口。现有 Job、ACL、保留和只读审计/本地哈希链的已完成范围见
  `M4_PROCESS_ISOLATION_BASELINE_20260723.md`。
