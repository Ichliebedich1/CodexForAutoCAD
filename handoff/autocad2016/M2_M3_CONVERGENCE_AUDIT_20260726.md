# M2 / M3 与 M1 / M4 线的汇合审计

审计日期：2026-07-26（北京时间）。全部只读：只用了 `git rev-list`、`git cherry` 和
`git merge-tree --write-tree`，没有 merge、rebase、cherry-pick、reset、checkout 或建分支。

目标文件「实际执行顺序」第 4 条要求 M2 与 M3 **必须在同一 Host 线程 / DTO / 限制契约上
汇合**。本文件回答两个问题：现在汇合了没有，以及要汇合还差什么。

## 1. 结论

M2 与 M3 之间**已经汇合**——它们是一条线性链。真正没有汇合的是 **M2/M3 线与 M1/M4 线**。

`codex/m3-highvalue-limited` 是 M2+M3 的真正全集：它按 patch-id 完整包含
`codex/m2-drawing-index`、`codex/m2-benchmark-fixtures` 和 `codex/m3-read-semantics`
（各缺 0 个提交）。

**名字有陷阱，不要靠名字判断：**

- `codex/m3-integration` 并**不是** M2+M3 的集成分支，它缺 `m3-highvalue-limited` 的 12 个
  提交、`m3-read-semantics` 的 7 个。
- Worktree 目录 `C:\tmp\CodexForAutoCAD-m2-integration` 检出的分支是 `codex/m3-integration`，
  不是任何 m2 分支。

## 2. 吸收矩阵

行 = 容器分支，列 = 候选分支，值 = 列分支中按 patch-id 不在行分支里的提交数。

| 容器 \ 候选 | m1-int | m2-idx | m2-bench | m3-read | m3-hv | m3-int | m4-int | m4-cred |
|---|---|---|---|---|---|---|---|---|
| m1-integration | – | 2 | 3 | 7 | 12 | 14 | 31 | 41 |
| m2-drawing-index | 5 | – | 1 | 5 | 10 | 16 | 33 | 43 |
| m2-benchmark-fixtures | 5 | 0 | – | 4 | 9 | 16 | 33 | 43 |
| m3-read-semantics | 5 | 0 | 0 | – | 5 | 16 | 33 | 43 |
| **m3-highvalue-limited** | 5 | **0** | **0** | **0** | – | 16 | 30 | 40 |
| m3-integration | 3 | 2 | 3 | 7 | 12 | – | 17 | 27 |
| m4-integration | 3 | 2 | 3 | 7 | 9 | **0** | – | 10 |
| **m4-credential-broker** | **3** | 2 | 3 | 7 | **9** | **0** | **0** | – |

读法：`m4-credential-broker` 已完整包含 `m3-integration` 和 `m4-integration`，但缺
`m3-highvalue-limited` 的 9 个提交和 `m1-integration` 的 3 个。由于 `m3-highvalue-limited`
是 M2/M3 全集，这 9 个提交已经覆盖 m2-idx 的 2 个和 m3-read 的 7 个，不需要另外单独吸收。

**没有任何一个分支包含全部工作。** 全集 = `m4-credential-broker` + 9（m3-highvalue-limited）
+ 3（m1-integration）。

## 3. 冲突测量

`git merge-tree --write-tree --name-only`，按目录分类：

| 组合 | 冲突文件总数 | `src/` | `tests/` | 其他（文档等） |
|---|---|---|---|---|
| m4-credential-broker × main | **0** | 0 | 0 | 0 |
| m4-credential-broker × m1-integration | **0** | 0 | 0 | 0 |
| m1-integration × m3-highvalue-limited | 43 | **5** | 2 | 36 |
| m4-credential-broker × m3-highvalue-limited | 164 | **21** | 15 | 128 |

M1 与 M4 之间零冲突——这两条线本来就一致。分歧全部集中在 M2/M3 线上。

**m1-integration × m3-highvalue-limited 的 5 个源码冲突**（全部在生产宿主 Host.2016）：

```text
src/Codex.AutoCAD.Host.2016/CodexAutoCad2016Extension.cs
src/Codex.AutoCAD.Host.2016/CodexCad2016Commands.cs
src/Codex.AutoCAD.Host.2016/MvpAgentClient.cs
src/Codex.AutoCAD.Host.2016/MvpAgentRuntime.cs
src/Codex.AutoCAD.Host.2016/Properties/AssemblyInfo.cs
```

**m4-credential-broker × m3-highvalue-limited 的 21 个源码冲突**跨越六个程序集：
AgentHost（`AgentHostBridgeSession`、`Program`）、AgentLauncher
（`WindowsInheritedBootstrapProcess`）、AgentRuntime（`AgentModels`、`CadDynamicTools`、
`CodexAgentRuntime`）、AppServer（5 个文件）、Bridge.Client（`BridgeClientJsonCodec`）、
Contracts（`DrawingIndexContracts`）以及 Host.2016（8 个文件，含
`DrawingIndexCore` / `DrawingIndexRuntime` / `DrawingIndexEntityReader`）。

## 4. 分歧是怎么来的

四条线都从 M0 基线 `9edc83e` 分出。M4 的线在某个时点吸收了 `codex/m3-integration`
并继续前进；但 M3 的工作此后继续留在 `codex/m3-highvalue-limited` 上，没有回流。
M1 的线同期改动了同一批 Host.2016 文件。于是 M2/M3 线与 M1/M4 线各自演化了同一个
生产宿主。

未回流的 9 个提交：

```text
9aa78f0  feat(host2016): add readonly drawing index and query
f8a659d  feat(host2016): connect Codex to readonly drawing index
e70a534  feat(host2016): add drawing index benchmark fixtures
4c33574  feat(host2016): add cad read issue diagnostics
4f8163f  feat(host2016): add bounded block read semantics
83b5f56  chore(host2016): freeze M3 read semantics candidate
a270e6d  chore(host2016): freeze M3 core read fixture candidate
28d2187  docs(m4): record credential isolation boundary
7a0e20b  feat(host2016): classify high-value index entities
```

这 9 个提交就是 M2 的 DrawingIndex 主体和 M3 的读取语义主体。它们目前**不在**任何
被当作主线的分支上。

## 5. 建议的顺序（需要用户决策）

建议先让 M3 线与 **M1** 汇合，再把结果与 M4 汇合，而不是直接拿 M3 撞 M4。

理由：M1 × M3 只有 5 个源码冲突且全在 Host.2016；M4 × M3 有 21 个、跨六个程序集。M4 在
Host.2016 之上又叠了进程沙箱、审计和诊断脱敏，直接三方合并等于同时解决两代改动。

**这是判断，不是测量结论。** 冲突数只说明文本重叠规模，不说明语义难度；也存在先做
M1×M3 之后 M4 侧冲突并未按比例减少的可能。真正的验证只能是实际做一次并跑门禁。

无论选哪种顺序，都必须满足：

1. 汇合后 `verify-phase2.ps1` 双 Shell 全绿，且 Host.2016 CAD 写入硬禁用门禁仍通过；
2. R20.1 Host A/B 逐字节一致，重新产出候选哈希——旧的 M2/M3 候选哈希
   （`E85D97EC…` / `FB18D959…`）在汇合后一定失效，不得继续引用；
3. M2.13 性能门禁重新跑，因为 M4 的进程与资源改动可能影响分片预算。

## 6. 本审计没有做的事

没有执行任何 merge、rebase、cherry-pick、reset、checkout 或分支创建；没有改动任何
Worktree 的工作树；没有启动 AutoCAD；没有启用 CAD 写入或插件保存。

两条测量上的保留：`git cherry` 用 patch-id 判等，被 rebase 且内容改过的提交会被算成
「缺失」；`git merge-tree` 的冲突数是文本层面的，文档冲突（本审计里占大多数）通常
可机械解决，源码冲突则需要逐个判断。
