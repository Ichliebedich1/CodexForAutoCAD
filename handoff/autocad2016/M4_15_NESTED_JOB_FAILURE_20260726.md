# M4.15.2a 嵌套 Job 分配拒绝的结构化失败纵切

最后验证：2026-07-26（北京时间）

## 本轮结论

现有正式启动链已经在恢复挂起的 AgentHost 主线程前创建受控 Job、执行
`AssignProcessToJobObject` 并反查成员身份；分配失败会终止挂起子进程，绝不回退为无 Job
启动。本轮补齐了“目标进程已在父 Job 中且嵌套分配被拒绝”的独立诊断：

- 新增 `AgentBootstrapLaunchFailure.NestedJobAssignmentFailed`。
- 公共稳定错误码为 `agenthost_nested_job_assignment_failed`。
- 错误不可自动重试，分类为 `Environment`。
- Host.2016 显示脱敏、可操作的中文提示：
  “AgentHost 无法加入受控嵌套 Job；请让管理员检查父进程 Job 和进程隔离策略。”
- 原始 Win32 错误正文、AgentHost 路径和 inner exception graph 不进入 Palette 或命令行。
- 普通未处于任何父 Job 的分配失败继续使用
  `agenthost_process_isolation_failed`，没有把不同故障合并成同一企业策略结论。

这完成的是 M4.15.2 的自动化准备纵切，不是企业嵌套 Job 矩阵实测。真实不可嵌套父 Job、
breakaway 限制、企业启动器、EDR 和受限账户仍需在对应环境中验证。

## 正式调用链

```text
CreateProcess(CREATE_SUSPENDED)
  -> 校验 PID、创建时间、映像路径、文件身份和 SHA-256
  -> WindowsProcessTreeJob.CreateKillOnClose
  -> IsProcessInJob(process, NULL)
  -> AssignProcessToJobObject
     -> success: 反查目标 Job 成员身份，再继续 bootstrap
     -> failure while already in a Job:
        NestedJobAssignmentFailed
        -> 启动失败清理终止并等待挂起 AgentHost
        -> Host 显示稳定脱敏错误
```

不存在“嵌套 Job 失败后无 Job 继续启动”的分支。

## RED → GREEN 证据

- RED：Host.2016 MVP 因缺少 `NestedJobAssignmentFailed` 和
  `AgentHostNestedJobAssignmentFailed` 而编译失败。
- GREEN：Launcher 规格验证普通分配失败与嵌套分配失败保持不同错误码，公开异常不保留原始
  Win32 诊断。
- 既有 `NESTED_JOB_ASSIGNMENT_COMPATIBLE` 继续在当前 Windows 运行时验证正向嵌套分配。
- 新增 `NESTED_JOB_ASSIGNMENT_FAILURE_CLASSIFIED` 验证失败分类、稳定错误、脱敏和无回退语义。
- Host 规格验证中文提示、`Retryable=false` 和敏感诊断不可见。

## 最终自动化结果

- AgentLauncher bootstrap net8：`65/65`，包含连续 `500` 次启停回收。
- AgentLauncher bootstrap net45：`65/65`，包含连续 `500` 次启停回收。
- PowerShell 7 Phase 2：`416/416`。
- Windows PowerShell 5.1 Phase 2：`416/416`。
- Host.2016 MVP：`59/59`。
- Release：`0 warning / 0 error`。
- Host 禁用 API、敏感信息扫描和 AgentHost doctor：通过。
- R20.1/.NET Framework 4.5/x64 Host A/B 逐字节一致，SHA-256：
  `83D3FECC133F62A075D512AB368F3A024945A3D215DB72315956D961B2B34C87`。
- R20.1 产物中的 Autodesk DLL 复制数：`0`。
- R20.1 验证产物：
  `artifacts/m4-15-nested-job-r201-host-680ace0de6114ab8989d177921f214e7/`。
- 条件多目标 net45 还原产生的 `packages.lock.json` 临时变化已恢复，最终无实际差异。
- User PATH 没有被构建或还原命令修改。
- AgentHost、FakeAgentHost、Bridge Client TestServer 和强杀恢复工作器残留：`0`。

Phase 2 的动态汇总仍为 `416/416`，因为 AgentLauncher bootstrap 是独立专项；Launcher 专项
由上一纵切的 `64/64` 增至 `65/65`。

## 真实企业矩阵仍未完成

后续必须在受控测试机分别验证：

1. 父进程不在 Job。
2. 父进程已在允许嵌套的 Job。
3. 父进程已在拒绝嵌套或带冲突限制的 Job。
4. 分配前、分配中和分配后的 owner/AgentHost 异常退出。
5. 普通受限账户、企业启动器、AppLocker/WDAC 与 EDR 子进程策略组合。
6. 每种失败的稳定错误码、事件日志、无 AgentHost/Codex 残留和日志不泄密。

只有当前 Windows 正向嵌套分配和纯分类夹具通过，不能把
`EnterpriseNestedJobMatrixVerified` 改为 `true`。M4.15、M4 和 M4.16 均未完成，M5 CAD
写入继续硬禁用。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具，也没有提交、合并、cherry-pick、push、reset 或清理 Git 工作树。
