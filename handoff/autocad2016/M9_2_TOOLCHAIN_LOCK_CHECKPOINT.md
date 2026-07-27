# M9.2 工具链与 R20.1 Probe 输入锁检查点

最后更新：2026-07-27（北京时间）

## 目标

在 M9.1 提交 `9afaaafcdf24028d984bd1b3ca81a5ea013e59ba` 之后，建立一个可由
PowerShell 7、Windows PowerShell 5.1 和 GitHub Windows Runner 共同消费的版本化工具链锁。
本阶段只读取 R20.1 程序集，不启动 AutoCAD、不执行 NETLOAD、不启用 CAD 写入。

## 审计发现

1. `global.json` 虽指定 `8.0.319`，但 `rollForward=latestPatch` 仍允许解析到其他补丁。
2. NuGet CLI 和 MSBuild 实际版本没有进入受控门禁。
3. V2ApiProbe 的旧 `packages.lock.json` 含 `win/win-*` RID，而项目当前未声明这些 RID；
   真正启用 `RestoreLockedMode=true` 会报 `NU1004`。
4. 旧验证器使用 `RestoreLockedMode=false`，掩盖了上述锁文件不一致。
5. 旧验证器优先选择机器上的最新 Visual Studio MSBuild；当前 Visual Studio 18 MSBuild
   会隐式加入 `win-*` RID，与由 `global.json` 固定的 SDK MSBuild 产生不同 restore 图。
6. Probe 源输入和实际 `acad.exe`/Autodesk managed assemblies 只有版本/存在性检查，没有
   精确文件哈希与 Authenticode 身份绑定。

## 实现

- `global.json`
  - SDK 固定 `8.0.319`。
  - `rollForward` 改为 `disable`。
  - `allowPrerelease=false`。
- `eng/toolchain-lock.json`
  - NuGet `6.10.2.8`。
  - MSBuild `17.10.46.46604`。
  - Microsoft net45 reference package 的 ID、版本、字节数、SHA-256、Author 和 Repository
    证书 SHA-256。
  - 仓库全部 4 个 `NuGet.Config` 和 5 个 `packages.lock.json`。
  - V2ApiProbe 的 csproj、Probe 源码和 AssemblyInfo。
  - 当前批准的 `acad.exe`、`accoremgd.dll`、`acdbmgd.dll`、`acmgd.dll` 的字节数、
    SHA-256、managed assembly identity、Authenticode 状态和 signer thumbprint。
- `scripts/verify-m9-toolchain-lock.ps1`
  - 严格 JSON property allowlist、真实 JSON 类型、规范相对路径、普通文件、大小和哈希验证。
  - 校验实际 SDK、NuGet、MSBuild 和离线包双签名。
  - 无 Autodesk 的 CI 模式只验证可携带输入，不冒充 R20.1 二进制或构建已验证。
  - 本机模式验证 4 个 R20.1 二进制，并在两个独立全新 cache/obj/bin 中执行 locked restore
    和 Release/x64 build；两个 AMD64 DLL 必须逐字节一致。
- `scripts/verify-autocad2016-v2-api-surface.ps1`
  - restore 前强制调用工具链输入锁。
  - 只允许由 `global.json` 解析的 `dotnet msbuild`；显式传入其他 MSBuild fail-closed。
  - 改为 `RestoreLockedMode=true`，显式传入离线 NuGet 配置、package cache 和 lock file。
- `.github/workflows/windows-core.yml`
  - 双 Shell 矩阵新增无 Autodesk 的工具链锁步骤。
  - 统一设置 `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`，纯构建不触碰用户证书存储。
  - 仍不访问用户 NuGet 配置、Secrets、AutoCAD 或本机 Codex。

## 自动化结果

- 工具链锁 18 类危险变异：
  - PowerShell 7：`18/18` 拒绝。
  - Windows PowerShell 5.1：`18/18` 拒绝。
- 无 Autodesk CI 模式：
  - 双 Shell 通过。
  - `R201BinaryInputsVerified=false`、`CleanCacheReproducible=false`，边界没有误报。
- 本机完整模式：
  - 双 Shell 均验证 4 个 R20.1 二进制哈希、assembly identity 和 Authenticode。
  - 每个 Shell 各执行两个全新缓存构建，Probe DLL 均为 AMD64、`14848` 字节，A/B SHA-256
    均为 `BE31312FC5C8AB530BE430C88903FB831BC1B6E1AB87F45127D02B5FDBFE62CE`。
  - Autodesk DLL copy count 为 `0`。
- 锁定后的 R20.1 API Probe：
  - PowerShell 7：Build 0 warning / 0 error，`29 passed / 8 expected failed`。
  - Windows PowerShell 5.1：Build 0 warning / 0 error，`29 passed / 8 expected failed`。
- 提交后换行回归：
  - 独立 CRLF checkout 暴露 V2ApiProbe `packages.lock.json` 仍锁定开发工作树混合换行。
  - 文件已按 `.gitattributes` 规范化为 CRLF，工具链锁更新为 `375` 字节和对应 SHA-256。
  - 正常提交后形态重新运行完整工具链门禁通过：
    `R201BinaryInputsVerified=true`、`CleanCacheReproducible=true`。

## 证据边界

- 当前只是独立 Worktree 实现与本地验证，尚未提交或推送。
- GitHub Actions 远端 run 尚不存在。
- 精确 R20.1 二进制锁只批准当前已审查输入；其他 AutoCAD 2016 service pack/hotfix 必须
  生成独立证据并经代码审查更新锁，不能自动放宽。
- Probe A/B 可复现不等于完整 Host/candidate 跨目录可复现；后者仍属于 M9.9。
- 没有启动 AutoCAD、没有 NETLOAD、没有 CAD 命令、没有保存图纸，也没有启用 M5。

## 验证命令

```powershell
.\scripts\verify-m9-toolchain-lock.ps1 -SelfTestOnly

.\scripts\verify-m9-toolchain-lock.ps1 `
  -SkipR201BinaryProbe

.\scripts\verify-m9-toolchain-lock.ps1 `
  -AutoCad2016Dir 'D:\AutoCAD 2016'

.\scripts\verify-autocad2016-v2-api-surface.ps1 `
  -AutoCad2016Dir 'D:\AutoCAD 2016' `
  -ArtifactRoot 'E:\cfa\m9-toolchain\v2-api'
```

## 下一步

1. 重跑更新后的完整双 Shell M9.1 工作流命令集合。
2. 运行 build-safety、diff、秘密扫描和证据 JSON 脱敏检查。
3. 经用户授权后形成独立 M9.2 Git 检查点。
4. 推送后必须取得两个远端 Windows job 的真实绿色结果，才能分别提升 M9.1/M9.2 状态。
