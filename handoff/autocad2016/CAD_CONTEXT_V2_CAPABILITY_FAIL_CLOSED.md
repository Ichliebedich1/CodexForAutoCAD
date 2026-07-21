# CadContext v2 Capability Fail-Closed Decision

## 缺陷描述

`BridgeClientJsonCodec.DeserializeCapabilitiesResponse()` 存在两个 fail-open 缺陷：

1. 使用 `wire.SupportedCadContextSchemas is { Length: > 0 }` 判断是否包含 v2 schema 列表。当 AgentHost 发送显式空数组 `supportedCadContextSchemas: []` 时，`Length` 为 0，空数组被静默替换为 v1 默认条目。
2. 形状校验器 `SchemaArray` 类型同时接受 `array` 和 `null`。当 AgentHost 发送 `supportedCadContextSchemas: null` 时，`null` 被视为字段缺失并恢复 v1 默认——但 `null` 是显式赋值，不是 absent。

## 修复

### 修复 1：空数组 fail-closed

```csharp
// 修复前（fail-open）：
hasV2Schemas = wire.SupportedCadContextSchemas is { Length: > 0 };
// 修复后（fail-closed）：
hasV2Schemas = wire.SupportedCadContextSchemas is not null;
```

### 修复 2：null 不是 absent

新增 `SchemaArrayNotNull` 字段类型，仅允许 `array`，拒绝 `null`：

```csharp
// CapabilitiesResponseV2ExtendedShape 使用 SchemaArrayNotNull
new JsonFieldSpec("supportedCadContextSchemas", JsonFieldKind.SchemaArrayNotNull)
```

当形状校验遇到 `"supportedCadContextSchemas":null` 时，抛出 `request_invalid`。
字段真正不存在时，v2 形状校验失败（缺少字段），回退到 v1 形状（不含此字段）。

### 修复 3：schemaVersion 严格整数校验

嵌套 `schemaVersion` 字段增加 `IsStrictInteger()` 校验，拒绝 `1.0`、`1e0` 等非整数格式。

## 兼容性决策

### 关键区分：null ≠ absent

- **absent**：JSON 中不包含 `supportedCadContextSchemas` 字段。v2 形状校验失败（缺少字段），回退到 v1 形状。`wire.SupportedCadContextSchemas` 为 null。保留 v1 默认条目（向后兼容）。
- **explicit null**：JSON 中包含 `"supportedCadContextSchemas":null`。v2 形状校验失败（`SchemaArrayNotNull` 拒绝 null 类型）。抛出 `request_invalid`。

### 场景矩阵

| 场景 | `supportedCadContextSchemas` 字段 | 解码行为 | 验证结果 |
|------|-----------------------------------|----------|----------|
| 1. 字段不存在（旧 v1 Host） | JSON 中无此字段 | v2 形状缺少字段 → 回退 v1 形状 → 保留 v1 默认 | 通过（向后兼容） |
| 2. 显式 null | `null` | v2 形状拒绝 null 类型 → `request_invalid` | 拒绝（fail-closed） |
| 3. 显式空数组 | `[]` | 形状通过 → `wire` 不为 null → 保留空数组 | 拒绝（fail-closed） |
| 4. 仅 v1 | `[{schema,1}]` | 正常解码 | 通过 |
| 5. 仅 v2（无 v1） | `[{schema,2}]` | 正常解码 | 拒绝（v1 必需） |
| 6. v1 + v2 | `[{schema,1},{schema,2}]` | 正常解码 | 通过 |
| 7. 重复条目 | `[{schema,1},{schema,1}]` | 正常解码 | 拒绝（重复） |
| 8. null 条目 | `[null]` | 解码失败 | 拒绝 |
| 9. 小数版本 | `[{schema,1.0}]` | `IsStrictInteger` 拒绝 | 拒绝（非整数） |
| 10. 科学记数法版本 | `[{schema,1e0}]` | `IsStrictInteger` 拒绝 | 拒绝（非整数） |

### 决策原则

1. **Fail-closed**：显式空数组和显式 null 都必须被拒绝，不得恢复为 v1 默认值。
2. **null ≠ absent**：`null` 是显式赋值，absent 是字段不存在。仅 absent 允许 v1 默认。
3. **向后兼容**：仅当 `supportedCadContextSchemas` 字段真正不存在时（旧 v1 Host），才保留 v1 默认条目。
4. **v1 始终必需**：验证器要求 `supportedCadContextSchemas` 必须包含 v1 条目。
5. **严格整数**：schemaVersion 必须是严格整数词法（禁止 `1.0`、`1e0`）。
6. **不改变 CAD 运行时或 UI**：此修复仅影响 Bridge Client 的 JSON 解码层。

## 测试覆盖

### Contracts.Specs（协议级验证）

- `BRIDGE-V2-006` 显式空 supportedCadContextSchemas 被拒绝（fail-closed）
- `BRIDGE-V2-007` 缺失 legacy supportedCadContextSchemas 保留 v1 默认（向后兼容）
- `BRIDGE-V2-008` 仅 v2 schema 无 v1 被拒绝
- `BRIDGE-V2-009` 畸形 schema 条目（null）被拒绝
- `BRIDGE-V2-010` v1-only schema 列表通过验证
- `BRIDGE-V2-011` 缺少 schemaVersion 的条目被拒绝

### Bridge.Client.Specs（跨运行时编解码）

- `bridge-client-explicit-empty-schemas-fail-closed` 显式空数组 → codec 拒绝
- `bridge-client-absent-schemas-v1-default` 缺失字段 → v1 默认 → 通过
- `bridge-client-v1v2-schemas-pass` v1+v2 → 解码通过
- `bridge-client-duplicate-schemas-rejected` 重复 → codec 拒绝
- `bridge-client-null-entry-schemas-rejected` null 条目 → codec 拒绝
- `bridge-client-explicit-null-schemas-rejected` 显式 null 字段 → codec 拒绝（≠ absent）
- `bridge-client-schema-version-decimal-rejected` 版本 1.0 → codec 拒绝（非整数）
- `bridge-client-schema-version-scientific-rejected` 版本 1e0 → codec 拒绝（非整数）
- `bridge-client-v1-only-schemas-pass` v1-only → 解码通过
- `bridge-client-v2-only-schemas-rejected` v2-only → codec 拒绝（v1 必需）
