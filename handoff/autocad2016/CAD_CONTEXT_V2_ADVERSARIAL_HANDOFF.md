# CadContextJson v2 独立对抗性测试交接文档

## 概述

本文档记录 `Codex.AutoCAD.Contracts.Adversarial.Specs` 项目的实现细节和验收结果。

## 项目位置

- 测试项目: `tests/Codex.AutoCAD.Contracts.Adversarial.Specs/`
- 分支: `codex/mimo-v2-contract-adversarial`
- 基线: `e7e2a70 test(contracts): harden cad context v2 boundaries`

## 实现的测试

### ADV-V2-001: xorshift32固定种子打乱实体顺序保持canonical确定性

- 固定种子: `0xC0D3CA16`
- 测试轮数: 128轮
- 测试内容: 打乱19强类型+unsupported实体顺序及Table cells
- 验证: canonical bytes/hash不变，几何数组保持原始顺序

### ADV-V2-002: 重复Handle精确包含context_v2_handle_duplicate

- 测试相同类型重复Handle
- 测试不同类型重复Handle
- 测试大小写不同的Handle
- 测试三个实体中两个重复

### ADV-V2-003: 中文emoji换行组合Unicode保持确定性

- 测试中文文字
- 测试合法emoji
- 测试换行符和制表符
- 测试组合Unicode
- 测试CJK扩展字符

### ADV-V2-004: 控制字符注入精确结构化失败码

- U+0000 (NUL) 注入 text/layer/name
- U+0007 (BEL) 注入 text/layer/name
- U+001B (ESC) 注入 text/layer/name

### ADV-V2-005: 双向格式零宽字符代理项稳定拒绝

- U+202E (Right-to-Left Override)
- U+200B (Zero Width Space)
- U+200C (Zero Width Non-Joiner)
- U+200D (Zero Width Joiner)
- U+2028/U+2029 (Line/Paragraph Separator)
- 孤立高/低代理项
- 代理项对中的低代理项在前

### ADV-V2-006: 规范JSON超256KiB精确包含context_v2_json_bytes_limit

- 创建64个实体，每个实体文本2000个中文字符
- 验证超过256 KiB时返回 `context_v2_json_bytes_limit`
- 验证在限制内时不触发错误

### ADV-V2-007: null/零payload精确shape/entity错误码

- null entity
- null payload (Line entity with null Line)
- zero payload (no payload set)
- unsupported payload=null
- multiple payloads

### ADV-V2-008: 不一致状态精确拒绝

- EntityCount != Entities.Length
- ParsedEntityCount 不匹配
- UnsupportedEntityCount 不匹配
- ParsedEntityCount + UnsupportedEntityCount != EntityCount
- Complete 不一致
- Selection 为 null
- Entities 为 null

### ADV-V2-009: 反射DTO确认无隐私字段

- 反射检查所有v2 DTO公共属性
- 确认不存在 documentPath/path/exception/stackTrace/trustedPaths/apiKey/token/credential/externalReferencePath 等隐私字段
- 验证规范化JSON不含敏感信息
- 验证普通CAD文本值被允许

### ADV-V2-010: 256轮压力测试aggregate hash

- 固定种子: `0xC0D3CA16`
- 测试轮数: 256轮
- 每轮合法且连续序列化3次一致
- 拼接每轮hash计算aggregate

## 测试结果

```
PASS ADV-V2-001 xorshift32固定种子打乱实体顺序保持canonical确定性
PASS ADV-V2-002 重复Handle精确包含context_v2_handle_duplicate
PASS ADV-V2-003 中文emoji换行组合Unicode保持确定性
PASS ADV-V2-004 控制字符注入精确结构化失败码
PASS ADV-V2-005 双向格式零宽字符代理项稳定拒绝
PASS ADV-V2-006 规范JSON超256KiB精确包含context_v2_json_bytes_limit
PASS ADV-V2-007 null/零payload精确shape/entity错误码
PASS ADV-V2-008 不一致状态精确拒绝
PASS ADV-V2-009 反射DTO确认无隐私字段
PASS ADV-V2-010 256轮压力测试aggregate hash
10/10 adversarial specs passed
```

## Aggregate Hash

```
CAD_CONTEXT_JSON_V2_ADVERSARIAL seed=C0D3CA16 rounds=256 sha256=9fffb4e5541aa99f6b1f0ad68d50aece048d50b58c2e100dc0f9fbf18c9bde2e
```

## 文件清单

```
tests/Codex.AutoCAD.Contracts.Adversarial.Specs/
├── Codex.AutoCAD.Contracts.Adversarial.Specs.csproj
├── Program.cs
├── AdvV2001_XorShift32ShufflePreservesCanonical.cs
├── AdvV2002_DuplicateHandleRejected.cs
├── AdvV2003_UnicodeDeterminism.cs
├── AdvV2004_ControlCharInjectionRejected.cs
├── AdvV2005_BidiAndSurrogateRejected.cs
├── AdvV2006_JsonBytesLimitEnforced.cs
├── AdvV2007_NullPayloadRejected.cs
├── AdvV2008_InconsistentStateRejected.cs
├── AdvV2009_NoPrivacyFieldsInDto.cs
└── AdvV2010_256RoundStressTest.cs
```

## 构建要求

- 默认 net8.0
- `EnableAutoCad2016=true` 时为 net45/net8.0
- 只引用 Contracts
- Release 0 warning/error

## 验收条件

1. ✅ 新增并通过 ADV-V2-001 至 ADV-V2-010 的独立多目标 Specs
2. ✅ net45 与 net8 完整 stdout 逐字节一致并冻结 aggregate hash
3. ✅ 不修改生产代码、现有 Specs 或构建基础设施

## 注意事项

- 不修改现有 `CadContextJsonV2Specs.cs`/`Program.cs`
- 不修改 `src/**`、scripts、solution、Directory.Build
- 不使用第三方测试框架或新 NuGet 依赖
- 不执行 AutoCAD 操作
