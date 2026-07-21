# P0 AgentHost 停止阶段证据交接

## 概述

本文档描述 P0 AgentHost 停止阶段验证器的证据交接流程。停止阶段验证器执行构建验证、规格测试和候选包创建，但不进行 AutoCAD 运行时集成验证。

## 验证器脚本

- `scripts/verify-autocad2016-agent-stop-stage.ps1` - 主要验证器脚本
- `scripts/test-autocad2016-agent-stop-stage.ps1` - 自测试脚本（PS7 和 Windows PowerShell 5.1）

## 验证流程

### 1. 环境检查

- 验证 `CODEX_AGENTHOST_PATH` 和 `CODEX_AGENTHOST_SHA256` 环境变量未设置
- 验证必需文件存在

### 2. Git 绑定

- 获取 Git HEAD 提交哈希
- 计算 dirty diff SHA-256（覆盖 tracked、staged 和 untracked 内容）
- 生成源输入清单 SHA-256（覆盖所有实际输入源码/项目/脚本）

### 3. 隔离构建

- 执行两次独立的 Release 构建
- Host 和 AgentHost 输出到不同目录
- 验证两次构建输出一致

### 4. R20.1 合规性验证

- 验证 Host DLL 存在且为 net45/x64
- 验证原版 Autodesk 程序集未复制到输出目录
- 验证 Autodesk 引用的 Private=false

### 5. 规格测试

- 运行 Host.2016.Mvp Specs
- 运行 AgentLauncher Specs
- 运行 Phase 2 门禁（调用仓库真实验证脚本）

### 6. 候选包创建

候选包包含以下文件（net45 依赖精确为 AgentLauncher、Bridge.Client、Contracts、Ipc）：

- `Codex.AutoCAD.Host.2016.dll` (Host DLL)
- `Codex.AutoCAD.AgentLauncher.dll` (net45 依赖)
- `Codex.AutoCAD.Bridge.Client.dll` (net45 依赖)
- `Codex.AutoCAD.Contracts.dll` (net45 依赖)
- `Codex.AutoCAD.Ipc.dll` (net45 依赖)
- `Codex.AutoCAD.AgentHost.exe` (AgentHost EXE)
- `Codex.AutoCAD.AgentHost.exe.sha256` (SHA-256 sidecar)

### 7. 证据生成

证据文件包含以下关键信息：

- `schemaVersion`: 1
- `scope`: autocad2016-agent-stop-stage
- `autoCadLiveEvidence`: false
- `autoCadProcessStarted`: false
- `autoCadProcessControlled`: false
- `cadCommandsSent`: false

## 验证标志

证据文件中的 `verificationFlags` 字段：

- `paletteSourceWiringInspected: true` - 调色板源布线已检查
- `paletteBehaviorAutomatedVerified: false` - 调色板行为未自动化验证
- `paletteRuntimeVerified: false` - 调色板运行时未验证
- `netLoadVerified: false` - NETLOAD 未验证
- `runtimeToArtifactBindingVerified: false` - 运行时到工件绑定未验证

## 证据文件位置

证据文件生成在 `handoff/autocad2016/evidence/` 目录下，文件名格式：

```
agent-stop-build-verification-{date}.json
```

## 候选包位置

候选包创建在 `artifacts/agent-stop-stage/candidate/` 目录下。

## 自测试

运行自测试脚本：

```powershell
# PowerShell 7
pwsh -File scripts/test-autocad2016-agent-stop-stage.ps1

# Windows PowerShell 5.1
powershell -File scripts/test-autocad2016-agent-stop-stage.ps1
```

自测试使用合成夹具验证：

- 构建相等性检查
- 候选包创建
- 证据结构完整性
- 环境变量覆盖检测
- SHA-256 一致性
- Sidecar 文件验证
- Dirty diff 检测（覆盖 tracked、staged 和 untracked）

## 限制

停止阶段验证器：

- 不启动 AutoCAD 进程
- 不控制 AutoCAD 进程
- 不发送 CAD 命令
- 不创建实时证据
- 不验证 NETLOAD 行为
- 不验证运行时到工件绑定

## 证据冻结时间

证据文件中的 `frozenAtUtc` 字段在证据生成前冻结，`recordedAtUtc` 必须大于或等于 `frozenAtUtc`。

## 候选 ID 格式

候选 ID 格式：`agent-stop-{git-head-short}-{timestamp}`

其中：
- `{git-head-short}`: Git HEAD 提交哈希的前 8 位
- `{timestamp}`: UTC 时间戳，格式为 `yyyyMMdd-HHmmss`

## Git 提交

验证器运行成功后，应创建一个单独的 Git 提交，包含：

- 验证器脚本
- 自测试脚本
- 规格测试项目
- 证据文件
- 交接文档

提交后不应合并到主分支。
