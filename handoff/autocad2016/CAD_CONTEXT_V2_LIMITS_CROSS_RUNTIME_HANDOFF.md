# CadContext v2 限额与跨运行时测试补缺交接

日期：2026-07-21
基线：`e7e2a70 test(contracts): harden cad context v2 boundaries`

## 新增 Spec

本次新增 4 个连续 Spec ID（CTX-V2-124 至 CTX-V2-127），从 62/62 提升至 66/66。

| Spec ID | 描述 | 验证内容 |
| --- | --- | --- |
| CTX-V2-124 | 样条曲线总点数上限 | 128+128=256 通过；129+128=257 精确失败为 `context_v2_spline_point_limit` |
| CTX-V2-125 | 引线顶点上限 | 256 通过；257 精确失败为 `context_v2_leader_vertices_limit` |
| CTX-V2-126 | 多重引线顶点总数上限 | 多条线总计 256 通过；总计 257 精确失败为 `context_v2_mleader_vertex_limit`；单条 257 同时覆盖 `context_v2_mleader_vertices_limit` |
| CTX-V2-127 | 冻结合法边界 fixture | 固定时间/Handle，含 Spline(128+128)、Leader(256)、MLeader(128+128)；连续序列化 3 次 SHA-256 与字节数完全一致；纯 ASCII 输出 |

## 固定向量

所有原有固定向量保持不变：

| 向量 | 字节 | SHA-256 |
| --- | --- | --- |
| v1 | 2225 | `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423` |
| v2 | 6678 | `21cc9378a618022c5bc21cb35c58db7818272c33d0adc5b5bd8618b4a638c3b4` |

新增边界 fixture 固定向量：

| 向量 | 字节 | SHA-256 |
| --- | --- | --- |
| v2-limits | 17721 | `fb532a9c3932f400d6fa093cab4d5b2f9abef3a65bb0b2eb890fbe2d1bbf629e` |

输出格式：

```text
CAD_CONTEXT_JSON_V2_LIMITS sha256=fb532a9c3932f400d6fa093cab4d5b2f9abef3a65bb0b2eb890fbe2d1bbf629e bytes=17721
```

## 跨运行时结果

| 运行时 | 结果 |
| --- | --- |
| net45 | 66/66 specs passed |
| net8 | 66/66 specs passed |

两个运行时的 `CAD_CONTEXT_JSON_V1`、`CAD_CONTEXT_JSON_V2` 和 `CAD_CONTEXT_JSON_V2_LIMITS` 行逐字节一致。

## Phase 2 回归

| Specs 项目 | 结果 |
| --- | --- |
| Contracts | 66/66 |
| IPC | 35/35 |
| Security | 19/19 |
| AppServer | 7/7 |
| Bridge | 37/37 |
| Bridge Client | 22/22 |
| AgentRuntime | 31/31 |
| Chat | 9/9 |
| **合计** | **226/226** |

Host 禁用 API、AgentHost doctor、`git diff --check` 和敏感信息扫描均通过。

## 修改文件

- `tests/Codex.AutoCAD.Contracts.Specs/CadContextJsonV2Specs.cs`
- `tests/Codex.AutoCAD.Contracts.Specs/Program.cs`

未修改任何生产代码（`src/**`、`scripts/**`）。

## 未验证边界

以下项仍为未验证状态，与本次测试补缺无关：

- Spline 控制点/拟合点单数组分别 256+1 的独立边界（本次只验证总数）
- Leader 顶点上限 256 的单数组 `context_v2_leader_vertices_limit` 独立验证
- MLeader 单条线 256+1 的 `context_v2_mleader_vertices_limit` 独立验证（本次通过单条 257 同时覆盖两个错误码）
- 其他已知限额（MaximumEntities=64、MaximumPolylineVertices=256 等）已在先前 Spec 中覆盖
