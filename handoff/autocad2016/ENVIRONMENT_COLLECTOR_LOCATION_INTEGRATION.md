# Environment Collector Location Integration

## 概述

本文档记录 AutoCAD 2016 环境采集器 Location 支持的集成工作。

## 变更内容

### 1. 注册表值名称扩展

- 新增 `Location` 注册表值名称支持（原有 `AcadLocation` 和 `InstallLocation`）
- `Location` 值通常指向 `acad.exe` 文件路径，自动规范化为安装目录

### 2. 注册表访问器抽象

- 新增 `New-DefaultAutoCadRegistryAccessor` 函数，提供默认只读访问器
- 新增 `Get-RequiredRegistryOperation` 函数，验证访问器操作
- `Get-AutoCadRegistryHints` 支持注入自定义访问器，便于测试

### 3. 诊断计数

- 新增 `DiscoveryDiagnostics` 输出，包含：
  - `RegistryRootsConfigured`: 配置的注册表根数量
  - `ReleaseRootsPresent`: 存在的发布根数量
  - `ReleaseRootProbeFailureCount`: 发布根探测失败次数
  - `ReleaseRootReadFailureCount`: 发布根读取失败次数
  - `ChildEnumerationFailureCount`: 子键枚举失败次数
  - `KeysInspected`: 检查的键数量
  - `KeysRead`: 成功读取的键数量
  - `PropertyReadFailureCount`: 属性读取失败次数
  - `AcadLocationHintCount`: AcadLocation 提示数量
  - `InstallLocationHintCount`: InstallLocation 提示数量
  - `LocationHintCount`: Location 提示数量

### 4. 自测试功能

- 新增 `-RunDiscoverySelfTest` 开关参数
- 包含 24 个断言，覆盖：
  - 三种注册表值名称识别
  - Location 指向 acad.exe 的规范化
  - 目录和可执行文件提示去重
  - 空注册表值拒绝
  - 注册表探测失败处理
  - 注册表读取失败处理
  - 属性读取失败处理

### 5. 报告架构升级

- SchemaVersion 从 4 升级到 5
- 新增 `DiscoveryDiagnostics` 字段
- SUMMARY.txt 新增注册表提示统计

## 测试结果

### 采集器信息

- 路径: `scripts/collect-autocad2016-environment.ps1`
- SHA-256: `BCFA796058FB24FAA313F0703D152BBCBD99F1CD08FB304254BC37883FB5651C`
- Git blob hash: `e7104430a3f25979a46d555cd814e67bb0b59310`
- 报告架构版本: 5
- Baseline commit: `ecaff6be8ad30918813da2b587623b712e14ab3e`

### 合成测试（自测试）

| 阶段 | 运行时 | 状态 | 说明 |
|------|--------|------|------|
| 当前阶段 | Windows PowerShell 5.1 | 24/24 通过 | 版本 5.1.19041.6456 |
| 当前阶段 | PowerShell 7 | 未运行 | pwsh 未安装在此机器上 |
| 历史参考 | PowerShell 7 | 24/24 通过 | commit 083f5f1，PS7 7.6.3，相同 collector blob |

**当前阶段测试命令**: `powershell -File scripts/collect-autocad2016-environment.ps1 -RunDiscoverySelfTest`

**历史参考说明**: commit 083f5f1 曾对完全相同的 collector Git blob `e7104430a3f25979a46d555cd814e67bb0b59310` (SHA-256 BCFA7960...) 运行 PS7 7.6.3 self-test 24/24。该历史证据因代码身份相同而可引用，但本轮集成阶段未重跑 PS7。

### 真实机器采集

| 阶段 | 运行时 | 状态 | 说明 |
|------|--------|------|------|
| 当前阶段 | Windows PowerShell 5.1 | 成功 | 1 个安装，1 个就绪 |
| 当前阶段 | PowerShell 7 | 未运行 | pwsh 未安装在此机器上 |
| 历史参考 | PowerShell 7 | 成功 | commit 083f5f1，相同 collector blob |

**当前阶段采集命令**: `powershell -File scripts/collect-autocad2016-environment.ps1`

**原始输出 SHA-256**: `1EFC151614519D81597B1F4FBBF183B59569D957249700D2076BF4651D1A9883`

**采集结果**:
- SchemaVersion: 5
- CollectionSucceeded: true
- AutoCAD 2016 安装数量: 1
- 就绪数量: 1
- ReleaseRootsPresent: 1
- KeysInspected: 4
- KeysRead: 4
- AcadLocationHintCount: 1
- InstallLocationHintCount: 0
- LocationHintCount: 1
- 所有失败计数: 0

## 证据文件

- `evidence/environment-collector-hardening-20260719.json`: 硬化证据
- `evidence/environment-collector-failure-regression-20260720.json`: 失败回归证据

## 安全边界

- 所有证据已脱敏，不包含用户名、绝对路径、信任路径或许可证数据
- 采集器保持只读，不启动 AutoCAD 或修改系统设置
- TRUSTEDPATHS 未被查询或请求

## 验收状态

| 条件 | 状态 | 说明 |
|------|------|------|
| AcadLocation, InstallLocation, Location 支持 | ✓ | |
| 目录/acad.exe 规范化 | ✓ | |
| 探测、根枚举、子键枚举、属性读取失败覆盖 | ✓ | |
| PS5.1 回归测试通过 | ✓ | 当前阶段 24/24 |
| PS7 回归测试 | 历史参考 | 当前阶段未运行；历史 commit 083f5f1 对相同 blob 运行 24/24 |
| 真实采集保持只读 | ✓ | |
| 文档文件未被覆盖 | ✓ | |
| 证据不包含敏感信息 | ✓ | |
| 证据与文档一致 | ✓ | |
