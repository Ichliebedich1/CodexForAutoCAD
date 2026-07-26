# M4：本机 Codex 启动配置

最后更新：2026-07-24（北京时间）

## 目标和边界

本切口把 AgentHost 对本机 Codex 的启动输入收敛为一份经过校验的配置，而不引入 Provider
抽象、自研 Agent Loop、每会话 `CODEX_HOME` 或 Windows Job Object。本切口没有启动、关闭或
控制 AutoCAD，也没有加载 DLL、保存或修改图纸。

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
- `doctor`、`run` 和受认证的 `bootstrap-serve` 使用同一配置对象；`doctor` 仅报告
  `codexExecutableSource`，不报告真实路径。
- 无效配置返回稳定 JSON：`error=codex_configuration` 与 `errorCode`；错误文本和日志不包含
  可执行文件、工作目录或环境变量内容。

## 已验证

```text
dotnet run --project tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj --configuration Release
Result: 15/15 specs passed

dotnet build src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj --configuration Release
Result: 0 warnings, 0 errors

scripts\verify-phase2.ps1 -Configuration Release
Result at integration commit 25c373d: Release 0 warnings / 0 errors; dynamic specs 331/331
in both PowerShell 7 and Windows PowerShell 5.1; Host disabled-API and sensitive-information
scans passed; local AgentHost doctor handshake passed.

AgentHost doctor
Result: local npm-discovered Codex completed app-server initialize; doctor emitted the source label only.

AgentHost doctor --codex relative-codex.exe
Result: stable codex_configuration / InvalidConfiguredExecutable; no path escaped.
```

## 尚未完成

- 目前配置验证的是启动身份、固定本地路径、工作目录和时间边界，不是 Codex CLI 版本兼容
  策略。必须先冻结官方支持的版本范围和稳定的非交互 `--version` 协议，才能把版本设为硬门槛。
- 当前 doctor 的 app-server initialize 是健康检查；正常 `bootstrap-serve` 仍会在实际会话启动时
  建立它自己的 app-server 连接。
- 每会话独立 `CODEX_HOME`、环境白名单、空 MCP/插件配置、凭据隔离、资源配额、ACL、审计及
  故障/僵尸进程实测不属于本配置切口。后续 M4 集成已经加入基础 Job Object 回收边界，但没有
  加入资源限制，也没有完成真实 Codex/AutoCAD/企业嵌套 Job 矩阵。
