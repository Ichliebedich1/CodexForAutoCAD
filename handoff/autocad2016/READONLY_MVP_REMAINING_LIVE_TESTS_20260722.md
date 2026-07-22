# AutoCAD 2016 只读 MVP 剩余实机测试

状态：P1 v2 happy path 已通过；本手册只列尚未取得证据的稳定性项目，不要求重复 100% DPI、
50 对象混合选择或正常两轮对话。

当前已验证候选：

```text
Module version: 0.3.2.0
Host SHA-256:
0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7
AgentHost SHA-256:
10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442
```

M0 重新构建统一候选后，应使用新候选身份替换本段，旧候选不自动继承新的实机结论。

## 1. 文档切换后的实际发送拒绝

1. 在图 A 捕获一个安全测试对象并确认 `Published=true`。
2. 正常切换到图 B。
3. 确认 `CODEX16CTXINFO` 为 `cleared-document-activated`。
4. 不重新捕获，执行 `CODEX16ASK` 并实际提交一条无敏感内容的问题。

预期：请求在 Host 侧 fail-closed，提示重新执行 `CODEX16CTX`；不得向 AgentHost 发送旧
上下文，不得出现旧图回答。

## 2. 已发布 v2 上下文的 Palette Reset

1. 捕获一个安全测试对象。
2. 记录 `CODEX16CTXINFO` 的 generation、selected 和 schema，不记录哈希。
3. 执行 `CODEX16PALRESET`。
4. 执行 `CODEX16PALINFO` 和 `CODEX16CTXINFO`。

预期：Palette 实例重建，已发布 v2 上下文仍保留；DBMOD 不变。

## 3. AutoCAD 正常退出清理

使用一个单独的干净 AutoCAD 进程：

1. 加载同一候选，打开 Palette，捕获一个上下文。
2. 启动 AgentHost 并确认在线。
3. 不执行 STOP，通过 AutoCAD 正常界面退出。
4. AutoCAD 完全关闭后只读检查 `Codex.AutoCAD.AgentHost.exe` 和由其启动的 Codex
   app-server 残留数量。

预期：约 30 秒内正常退出，无崩溃或卡死，相关残留进程为 0。

## 4. 125% 和 150% DPI

分别在 Windows 显示缩放 125% 与 150% 下使用干净 AutoCAD 进程验证：

- Palette 打开、停靠、浮动、隐藏重开和 Reset。
- 中文输入、换行、按钮和文本不重叠、不裁切。
- DBMOD 不因 Palette 操作改变。

测试后由用户恢复原缩放设置。不要通过插件修改注册表或显示设置。

## 5. 断线、超时和取消

仅在自然出现或已有受控测试注入时执行，不通过删除文件或强杀生产会话制造故障。

需要分别验证：

- AgentHost 启动失败。
- Bridge 断线。
- 请求超时。
- 回合取消和重复取消。
- 回合终态后的迟到事件。

预期：明确错误码和最终状态、后续 ASK fail-closed、AutoCAD 保持可操作、无重复发送、
无新 CAD 写入、无残留进程。

当前 `MvpAgentClient.OnBridgeFaulted` 尚未把客户端状态原子切换为 offline，因此该修复
进入 M1 后必须先完成自动化，再冻结新候选进行本节实机测试。

## 6. 反馈格式

只反馈：

- 候选版本和 Host/AgentHost 哈希。
- 分段通过、失败或跳过。
- DBMOD 是否不变。
- 错误码或 Palette 状态。
- 退出后的相关进程数量。

不要反馈完整 JSON、图纸路径、图名、Handle、选择/上下文哈希、TRUSTEDPATHS、用户名、
许可证、API Key、token 或完整环境变量。
