# CadContextJson v2 人工实机测试包交接文档

## 1. 概述

本文档是 CadContextJson v2 人工实机测试包的交接说明。测试包用于验证 AutoCAD 2016 中
CadContextJson v2 的运行时行为，不包含任何自动化操作或真实 runtime evidence。

## 2. 组件清单

| 文件 | 用途 |
|------|------|
| `CAD_CONTEXT_V2_RUNTIME_TEST_KIT.md` | 人工测试说明和流程 |
| `CAD_CONTEXT_V2_RUNTIME_TEST_WORKSHEET.md` | 测试工作表模板 |
| `CAD_CONTEXT_V2_RUNTIME_TEST_KIT_HANDOFF.md` | 本文档 |
| `evidence/cad-context-v2-runtime-candidate-manifest-template.json` | 候选 manifest 模板 |
| `evidence/cad-context-v2-runtime-evidence-template.json` | evidence 模板 |
| `scripts/verify-autocad2016-v2-runtime-report.ps1` | 报告验证器 |
| `scripts/test-autocad2016-v2-runtime-report-validator.ps1` | 验证器自测 |
| `tests/Codex.AutoCAD.Host.2016.V2RuntimeReport.Specs/` | 验证器测试规格 |

## 3. 测试矩阵

### 3.1 强类型覆盖（19种）

| # | entityType | R20.1 类型 |
|---|------------|-----------|
| 1 | line | Line |
| 2 | circle | Circle |
| 3 | polyline | Polyline |
| 4 | dbText | DBText |
| 5 | mText | MText |
| 6 | blockReference | BlockReference |
| 7 | arc | Arc |
| 8 | ellipse | Ellipse |
| 9 | spline | Spline |
| 10 | point | DBPoint |
| 11 | ray | Ray |
| 12 | xline | Xline |
| 13 | polyline2d | Polyline2d |
| 14 | polyline3d | Polyline3d |
| 15 | dimension | Dimension |
| 16 | hatch | Hatch |
| 17 | leader | Leader |
| 18 | mLeader | MLeader |
| 19 | table | Table |

### 3.2 占位原因覆盖（3种）

| # | reason | 触发条件 |
|---|--------|---------|
| 1 | unknown-entity-type | 图元类型不在 v2 白名单中 |
| 2 | entity-read-failed | 已知类型读取失败 |
| 3 | entity-data-limit | 实体数据超过 v2 限额 |

## 4. 验证器说明

`verify-autocad2016-v2-runtime-report.ps1` 是只读验证器，用于验证用户准备的脱敏
manifest 和 evidence 文件。

### 4.1 验证范围

- 严格 UTF-8 无 BOM
- 拒绝未知/重复/缺失字段
- 拒绝错误类型
- 拒绝候选身份不一致
- 拒绝 schema 非 v2
- 拒绝计数/complete/DBMOD 关系错误
- 拒绝 save/write/tool CAD command 为 true
- 拒绝敏感字段或路径值

### 4.2 错误码

| 错误码 | 说明 |
|--------|------|
| manifest_invalid | manifest 结构无效 |
| evidence_invalid | evidence 结构无效 |
| unknown_field | 存在未知字段 |
| duplicate_field | 存在重复字段 |
| candidate_identity_mismatch | 候选身份不一致 |
| commit_invalid | commit SHA 无效 |
| artifact_sha_invalid | artifact SHA 无效 |
| schema_mismatch | schema 版本不匹配 |
| coverage_incomplete | 覆盖不完整 |
| count_mismatch | 计数不匹配 |
| complete_flag_mismatch | complete 标志不匹配 |
| dbmod_changed | DBMOD 已改变 |
| plugin_save_observed | 检测到插件保存 |
| cad_write_observed | 检测到 CAD 写入 |
| runtime_binding_invalid | 运行时绑定无效 |
| sensitive_field_rejected | 拒绝敏感字段 |
| sensitive_value_rejected | 拒绝敏感值 |
| prohibited_tool_behavior | 禁止的工具行为 |

### 4.3 安全约束

验证器**不会**：

- 读取 DLL
- 查询 Git
- 查询注册表
- 查询进程
- 启动进程
- 访问网络

验证器**只会**：

- 读取用户提供的 manifest 和 evidence 文件
- 验证 JSON 结构和字段
- 验证计数和标志位关系
- 检测敏感信息

## 5. 自测说明

`test-autocad2016-v2-runtime-report-validator.ps1` 包含完整的正负向测试用例：

### 5.1 正向测试

- 合法 manifest 和 evidence 样本
- 所有 19 强类型覆盖
- 所有 3 占位原因覆盖
- 混合选区测试
- DBMOD 不变验证

### 5.2 负向测试

- 错误 commit SHA
- 错误 artifact SHA
- 候选身份不一致
- schema 版本错误
- 覆盖不完整
- 占位缺失
- 计数/complete/DBMOD 错误
- save/write/CAD command 为 true
- 敏感信息检测：
  - 路径
  - 用户名
  - TRUSTEDPATHS
  - JSON 原文
  - Handle
  - 哈希
  - 坐标
  - 文字
  - API Key
  - 堆栈跟踪
- 未知字段
- 重复字段
- null 值
- 尾随 JSON

### 5.3 运行环境

- PowerShell 7
- PowerShell 5.1

### 5.4 安全约束

自测脚本**不会**：

- 操作 AutoCAD
- 使用 COM
- 使用 SendKeys
- 使用 Start-Process
- 访问网络

## 6. 使用流程

1. 阅读 `CAD_CONTEXT_V2_RUNTIME_TEST_KIT.md` 了解测试流程
2. 按照测试矩阵在 AutoCAD 2016 中执行人工测试
3. 填写 `CAD_CONTEXT_V2_RUNTIME_TEST_WORKSHEET.md`
4. 使用模板生成脱敏的 manifest 和 evidence
5. 运行 `verify-autocad2016-v2-runtime-report.ps1` 验证报告
6. 运行 `test-autocad2016-v2-runtime-report-validator.ps1` 验证自测

## 7. 注意事项

- 不得修改任何生产代码
- 不得自动操作 AutoCAD
- 不得产生真实 runtime evidence
- 不得将敏感信息写入聊天或 evidence
- 不得用后续构建回填历史
- status 必须为空
- 不得留下 `.mimocode`、artifacts、临时 JSON 或 raw log
- 不得自行合并

## 8. 提交信息

```text
test(host2016): add cad context v2 runtime test kit
```
