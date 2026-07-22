# Codex for AutoCAD 2016：先读这里

最后更新：2026-07-22（北京时间）

长期目标与完整 M0-M12 队列见 `LONG_TERM_MEMORY_TODO.md`；当前证据边界见
`CURRENT_STATE.md`。本文件只提供当前基线、候选身份、操作入口和下一步验证顺序。

## 1. 当前准确结论

AutoCAD 2016 R20.1 已建立一个真实运行的 CadContextJson v2 只读 AI 基线：

- `net45/x64` 统一 Host 可人工 `NETLOAD`。
- Doctor 显示 `codex.autocad.cad-context/2`。
- 100% DPI Palette 的打开、停靠、浮动、隐藏重开、重建、中文输入和布局通过。
- 一个 50 对象混合选区成功发布，其中 6 个对象以受限 placeholder 表示；
  `jsonBytes=23142`，`DBMOD 21 -> 21`。
- 本机 Codex 使用真实 v2 CAD 上下文完成两轮连续对话。
- 显式 CAD 上下文清除和文档激活清除旧缓存通过。
- P0 停止生命周期已有独立实机证据：重复 STOP、DBMOD 不变、AgentHost 残留为 0。
- CAD 写入和插件发起的保存始终禁用。

脱敏实机范围证据：
`evidence/cad-context-v2-live-observation-20260722.json`。

这仍不是完整产品：

- 当前选择快照最多 64 个实体、canonical JSON 最多 256 KiB。
- 19 类对象尚未逐类完成字段实机核对。
- Bridge 断线后客户端离线化、超时、取消和迟到事件尚未收口。
- 文档切换后真正提交问题的 fail-closed 尚未实测。
- AutoCAD 正常退出、125%/150% DPI 和故障矩阵尚未完成。
- CAD 写入、完整 OS 沙箱、长期记忆、签名安装和企业部署尚未完成。

## 2. 当前候选身份

M0 当前统一自动化候选：

```text
Module version: 0.3.2.0
CadContext schema: codex.autocad.cad-context/2
Candidate directory:
C:\tmp\CodexForAutoCAD-m0-baseline\artifacts\autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54

Host:
Codex.AutoCAD.Host.2016.dll
SHA-256:
37C1953D9AD996F9892486300295E69043F8E020D506E0683FC1301F8FC4C532

AgentHost:
AgentHost\Codex.AutoCAD.AgentHost.exe
SHA-256:
AB1CE675EF48947F670E0A4FC013E09108AF9A91D5D14F49874039F42018CD3A

Manifest SHA-256:
FF11069F766A055D3F2DEA7D9D320CB1B4A5D874260FB4E47EE083D42E12F8BD
```

该身份从源码提交 `c96e9a3` 构建，完整自动化、真实本机 Codex v2 两轮、manifest 和
候选 doctor 已通过。它尚未按精确哈希在 AutoCAD 内人工 NETLOAD，因此保持
`NetLoadVerified=false`。已完成实机绑定的 P1 候选仍是 Host `0D72EDC3...`、AgentHost
`10BEA363...`；两份证据不能互相替代。详见 `M0_BASELINE_RELEASE_20260722.md`。

## 3. 当前架构

```text
AutoCAD 2016 R20.1 / x64
  Codex.AutoCAD.Host.2016 / .NET Framework 4.5
  - Palette
  - 只读选择捕获
  - CadContextJson v2
  - 认证 Bridge Client
                 |
                 | 当前用户命名管道
                 | HMAC + sequence + nonce + 防重放
                 v
  AgentHost / .NET 8
  - CodexAgentRuntime
  - codex app-server --stdio
  - 结构化事件返回 Palette
```

AutoCAD UI 不直接启动或解析 Codex 控制台文本。当前没有 Provider-neutral 抽象，也不开发
Direct API Provider 或第二套 Agent Loop。

## 4. 常用命令

```text
CODEXCADDOCTOR
CODEXCAD
CODEX16PAL
CODEX16PALINFO
CODEX16CTX
CODEX16CTXINFO
CODEX16CTXCLEAR
CODEX16AGENTSTART
CODEX16ASK
CODEX16AGENTSTOP
CODEX16PALRESET
```

语义注意：

- `CODEX16CTXCLEAR` 只清除内存中的 CAD 上下文，不创建新 Codex thread。
- 因此清除 CAD 上下文后，当前会话仍可能记得先前聊天内容。
- M1 将增加“新建对话”和“全部清除”，并按图纸隔离会话。
- `CODEX16ASK` 能弹出输入提示不代表旧上下文可发送；必须实际提交后才算 fail-closed
  验证。

## 5. 已通过的实机项目

- [x] Host 加载和 Doctor。
- [x] v2 schema 和只读/禁保存声明。
- [x] 100% DPI Palette 全部人工交互。
- [x] 50 对象混合选区和 6 个 placeholder。
- [x] DBMOD 在混合读取样本中保持 `21 -> 21`。
- [x] 本机 Codex v2 两轮连续对话。
- [x] 显式上下文清除。
- [x] 文档激活清除旧上下文。
- [x] P0 AgentHost 停止无残留。

## 6. 尚需实机验证

当前允许延期，但不得写成已通过：

1. 在图 A 捕获后切到图 B，不重新捕获，实际提交问题并确认 fail-closed。
2. v2 上下文已发布时执行 Palette Reset，确认上下文仍保留。
3. 正常退出 AutoCAD，不先 STOP，确认 AgentHost/Codex 残留为 0。
4. 125% 和 150% DPI。
5. AgentHost 启动失败、Bridge 断线、请求超时、回合取消、重复取消和迟到事件。
6. 19 类强类型对象的逐类字段核对。
7. 超过 64 个实体和整图数量级；该项将由 DrawingIndex/CadQuery 新架构解决。

## 7. 当前开发顺序

1. M0：已完成 P0/P1 集成、evidence/文档收拢、门禁复跑和统一候选冻结。
2. M1：当前下一阶段；Bridge offline、请求状态/取消/超时、对话清除语义和剩余生命周期。
3. M2：整图扫描、索引、分页、按需查询和 1k/10k/50k 基准。
4. M3：读取对象语义与覆盖。
5. M4：进程沙箱、配置和审计基础。
6. M5：AutoCAD 2016 `create_line` 安全写入最小闭环。
7. 后续阶段见 `LONG_TERM_MEMORY_TODO.md`。

## 8. 构建与自动化边界

M0 已从精确源码提交 `c96e9a3` 重跑以下门禁：

- Host.2016 MVP：`24/24`。
- 完整 Phase 2：`259/259`。
- AgentHost -> 本机 Codex v2 两轮 live：`2/2`。
- R20.1 Host Release：0 warning / 0 error。
- PowerShell 7 与 Windows PowerShell 5.1 v2 API Probe。
- Host 禁止 API、秘密扫描、候选包 Doctor 和无残留检查。

M0 必须从集成提交重新运行这些门禁。历史绿色结果不能自动证明新的集成提交。

## 9. 安全与隐私

- 不向聊天或 Git 粘贴完整 canonical JSON。
- 不记录真实图纸路径、图名、Handle、选择哈希、上下文哈希或外参路径。
- 不记录 API Key、token、完整环境变量、`TRUSTEDPATHS`、用户名或许可证信息。
- Autodesk DLL 不提交仓库、不复制到插件包。
- 插件不自动保存 DWG，不修改 SAVETIME。
- Codex 不自动启动、关闭、重启或操作用户的 AutoCAD；实机步骤由用户执行。
- 未完成安全写入闭环前，CAD 写入保持编译期和运行时禁用。

## 10. 关键 evidence

- `evidence/cad-context-v2-live-observation-20260722.json`：本次 P1 AutoCAD live 范围。
- `evidence/agent-stop-live-observation-20260722.json`：P0 停止生命周期。
- `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30.json`：
  P1 R20.1 构建和候选身份。
- `evidence/cad-context-v2-candidate-package-doctor-20260722-refresh.json`：候选 AgentHost doctor。
- `evidence/agenthost-v2-live-two-turns-20260722-refresh.json`：非 AutoCAD 的真实 Codex v2 两轮。
- `evidence/phase2-final-gate-20260722-exit-retry.json`：P1 Phase 2 `259/259`。
- `evidence/host2016-terminate-exit-retry-20260722.json`：退出清理重试自动化 `24/24`。
- `evidence/m0-baseline-verification-20260722.json`：M0 聚合门禁、候选身份和实机边界。
- `M0_BASELINE_RELEASE_20260722.md`：M0 冻结记录与下一阶段入口。

## 11. 支持声明

当前可以准确表述为：

> AutoCAD 2016 R20.1 已实机跑通 CadContextJson v2 的只读选择、Palette、本机 Codex 和
> 两轮连续对话基线；50 对象混合选区中的未知对象不会中断发布。当前仍受 64 实体选择
> 快照上限约束，生命周期故障矩阵、整图规模、安全 CAD 写入、完整沙箱、长期记忆和
> 发布安装尚未完成。

不得表述为完整支持 AutoCAD 2016，也不得表述为已经支持安全 CAD 写入。
