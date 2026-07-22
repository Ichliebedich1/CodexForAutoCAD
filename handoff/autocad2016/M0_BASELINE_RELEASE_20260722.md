# M0 统一只读 v2 基线冻结记录

日期：2026-07-22

## 结论

P0 停止生命周期、P1 CadContextJson v2 产品调用链和主分支文档已在独立
`codex/m0-baseline` Worktree 中受控收拢。合并提交为 `e66ef1e`；候选构建脚本的并行
发布锁和 lock 文件改写问题以独立提交 `c96e9a3` 修复。最终 M0 自动化候选从
`c96e9a3f2f3edc5e1407cb3438a3ec8d2313dcf2` 构建。

2026-07-22，本地 `main` 已在不覆盖或暂存用户 Host.2025 原型的前提下安全快进并吸收
冻结提交 `4833e76`。本次动作未自动推送远端，也未改变下述候选身份、哈希或实机证据边界。

## 候选身份

```text
Candidate ID:
autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54

Module version:
0.3.2.0

CadContext schema:
codex.autocad.cad-context/2

Host SHA-256:
37C1953D9AD996F9892486300295E69043F8E020D506E0683FC1301F8FC4C532

AgentHost EXE SHA-256:
AB1CE675EF48947F670E0A4FC013E09108AF9A91D5D14F49874039F42018CD3A

Manifest SHA-256:
FF11069F766A055D3F2DEA7D9D320CB1B4A5D874260FB4E47EE083D42E12F8BD
```

候选目录位于 M0 Worktree 的：

`artifacts/autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54/`

## 从精确源码提交重跑的门禁

- 托管核心 Release：`0` warning / `0` error。
- Phase 2：9 个规格项目，动态汇总 `259/259`。
- Host.2016 MVP：`24/24`。
- R20.1 Host net45/x64 A/B：逐字节一致。
- PowerShell 7.6.3 与 Windows PowerShell 5.1 v2 API Probe：均为 0 warning / 0 error，
  运行时成员集合一致，`19` 个可用、`8` 个按冻结集合不可用。
- 候选内 AgentHost -> 本机 Codex v2 两轮：`2/2`。
- 候选 manifest：26 个受管文件全部匹配，额外 Autodesk DLL 为 `0`。
- 候选内 AgentHost doctor：通过；测试前后 AgentHost PID 集合不变。
- Host 禁用 API、敏感信息和 Git 差异门禁：通过。

聚合脱敏证据：

`evidence/m0-baseline-verification-20260722.json`

候选构建证据：

`evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54.json`

## AutoCAD 实机证据边界

P1 候选已经取得 AutoCAD 2016 的真实 v2 基线：Doctor、100% DPI Palette、50 对象混合
选区、placeholder、DBMOD 不变、真实 Codex 两轮对话、上下文清除和文档激活清除。权威
范围为 `evidence/cad-context-v2-live-observation-20260722.json`。

最终 M0 候选的 Host/AgentHost 哈希与 P1 实机候选不同，因此不能把旧 NETLOAD 记录自动
绑定到本候选。当前准确表述是：

- P1 产品调用链已实机通过；
- M0 已把该调用链合入并通过完整自动化和真实 Codex 非 CAD live 门禁；
- M0 精确候选仍为 `NetLoadVerified=false`、`AutoCadLiveEvidence=false`。

不需要为了重复 happy path 立即打断开发；下一次 M1 实机矩阵应使用当时冻结的最新候选，
并重新建立精确哈希绑定。

## 下一阶段

M1 只读稳定化优先处理 Bridge 断线 offline、request/turn 终态、取消和超时、迟到事件、
清除 CAD 上下文/新建对话/全部清除三种语义、按图纸隔离、正常退出清理和高 DPI。
M2 再进入整图 DrawingIndex、分页和 CadQuery；不得简单放大 64 实体上限。
