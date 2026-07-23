# A2 — MSIX 打包验证 Design

> 状态：已获用户批准（2026-07-19）。对应诉求：`docs/plans/2026-07-18-verification-and-next-phase.md` A2 节（R4 的第一步）。

## 目标

当前部署走松散布局注册（`Add-AppxPackage -Register AppxManifest.xml`，Debug 未裁剪）。A2 验证**正式 MSIX 打包产物**
（Release 构建 → 自签名 → 安装）在本机可安装、可加载、Dock 正常，并产出可复现的打包命令文档，为 R4
（Host 随包分发）和商店提交铺路。**本质是一次打包链路 spike + 文档化，不是功能开发。**

## 需求定型（澄清结论，勿重新讨论）

1. **范围 = x64 + ARM64 + bundle**：
   - x64：构建→签名→**实装**→Dock 正常→卸载干净→恢复松散开发注册（"能装能用"硬验证）。
   - ARM64：仅验证**能构建出 .msix**（本机 x64 无法实装 ARM64，只证跨架构编译通过）。
   - bundle：makeappx 合出 `.msixbundle` 并签名成功（商店提交形态，只验证产物生成，不提交）。
2. **签名 = 自签名 dev 身份，永久采用**：manifest/csproj 身份永久改为 `CN=SysPulse Dev`（真实项目不该留 `CN=Microsoft` 占位）；商店提交时再换 Partner Center 身份。
3. **构建配置 = Release（带裁剪）**：贴近商店真实产物；A2 的核心价值之一即提前暴露裁剪问题（至今验证的松散部署都是 Debug 未裁剪）。
4. **Host 不打进包**：Host 随包分发是 R4。A2 测试期依赖**已注册的计划任务**（指向仓库构建的 Host exe）拉起 Host，故"Dock 正常"可达成而无需捆绑 Host。
5. **接受的副作用**：包家族名随身份变更 → A1 的 `slots.json` 持久化重置一次（用户轮换重选即可）。

## 前提事实（已核实，勿重查）

- csproj 已具 MSIX 工具链：`EnableMsixTooling=true`、`Microsoft.Windows.SDK.BuildTools.MSIX` 包、`Properties/PublishProfiles/win-x64.pubxml`+`win-arm64.pubxml`。
- 当前 manifest：`Name="SysPulseExtension"`、`Publisher="CN=Microsoft Corporation, ..."`（占位）、`Version="0.0.1.0"`、含 `runFullTrust`+`internetClient` capability、COM server CLSID `7d829c17-1969-490c-bf62-141c9b61cfd3`。
- csproj Release 段：`PublishTrimmed=true`、`IsAotCompatible=true`、`ILLinkTreatWarningsAsErrors=false`（Release）。Debug 段 `PublishTrimmed=false`。
- 本机 **PATH 无 signtool**；signtool/makeappx 需从 SDK BuildTools 包或 Windows Kits 定位。
- 打包命令模板见 `.github/skills/publish-extension/references/store-publishing.md`（Step 3/4）。
- 计划任务 `SysPulse.Host` 已注册（指向仓库 `bin/Debug/net8.0/SysPulse.Host.exe`），静默通道可用。

## 执行阶段

| # | 阶段 | 动作 | 成功判据 |
|---|------|------|----------|
| 1 | 身份改造 | manifest Publisher→`CN=SysPulse Dev`；csproj 补 `AppxPackageIdentityName=SysPulseExtension`/`AppxPackagePublisher=CN=SysPulse Dev`/`AppxPackageVersion=0.0.1.0` | 松散注册（Debug）仍能装、Dock 正常（改身份未破坏现有链路） |
| 2 | 证书 | `New-SelfSignedCertificate -Type CodeSigning -Subject "CN=SysPulse Dev"`（CurrentUser\My）→ 导出 `.cer`/记录 thumbprint → 导入 `LocalMachine\TrustedPeople` | `Get-ChildItem Cert:\CurrentUser\My` 见证书；thumbprint 记录备用 |
| 3 | 构建 x64 MSIX | `dotnet build -c Release -p:GenerateAppxPackageOnBuild=true -p:Platform=x64 -p:AppxPackageDir="AppPackages\x64\" -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=<thumb>` | `AppPackages\x64\` 下生成**已签名** x64 `.msix`；Release 裁剪构建 0 错误 |
| 4 | 构建 ARM64 MSIX | 同上 `-p:Platform=ARM64 -p:AppxPackageDir="AppPackages\ARM64\"` | `AppPackages\ARM64\` 下生成 ARM64 `.msix`（仅证能构建，不实装） |
| 5 | 合 bundle | `bundle_mapping.txt` 映射两架构 → `makeappx bundle` → signtool 签名 bundle | 生成已签名 `.msixbundle` |
| 6 | 实装验证 x64 | 移除松散注册 dev 扩展 → `Add-AppxPackage <签名 x64.msix>`（非 -Register）→ `x-cmdpal://reload` → 目视 Dock | 4 控件正常显示读数、右键轮换/图标/菜单沉底全在（同 A1 验收）；**若裁剪破坏功能→记录为 A2 关键发现** |
| 7 | 清理与复原 | `Remove-AppxPackage` 卸载打包版 → 恢复松散注册（`Add-AppxPackage -Register AppxManifest.xml`）供日常开发 | 卸载无残留；松散 dev 版恢复、Dock 正常 |
| 8 | 文档化 | 成功命令序列写入 `docs/references/msix-packaging.md`；CLAUDE.md 挂指针；记录裁剪发现与 R4 待办 | 文档可被他人/未来照做复现 |

## 关键风险与处理（诚实项）

1. **Release 裁剪破坏运行时**（头号）：`PublishTrimmed=true` 下 `SlotStore` 的 `System.Text.Json`（IL2026/IL3050 已知警告）、CsWinRT 投影、CmdPal 反射激活可能被裁坏——装后扩展可能崩/Dock 空/轮换失效。**处理**：这是 A2 最有价值的发现，如实记录；对策按代价排序：① `System.Text.Json` 换 source-generated `JsonSerializerContext`（trim-safe）；② 对受影响程序集加 `TrimmerRootDescriptor`；③ 局部 `<PublishTrimmed>false</PublishTrimmed>` 兜底。本 spec 不预判具体修法——先跑出现象再定，若修复超出打包范畴则记为 R4 前置项。
2. **ARM64 交叉裁剪构建失败**：x64 机器交叉编译 ARM64 + 裁剪可能报错。**处理**：记录错误；ARM64 仅"能构建"目标，失败不阻断 x64 主线，降级为 R4 待办。
3. **signtool/makeappx 定位**：PATH 无 signtool。**处理**：从 `%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools.msix\*\bin\` 或 `C:\Program Files (x86)\Windows Kits\10\bin\*\x64\` 定位；构建时签名（`AppxPackageSigningEnabled`）可避开手动 signtool，bundle 签名仍需 signtool。
4. **打包版与松散版身份冲突**：改身份后两者同 Name+Publisher。**处理**：实装前先移除松散注册（阶段 6 首步），避免 already-installed 冲突。
5. **slots.json 重置**：包家族名变更导致（已接受）。**处理**：文档提示用户重选轮换项一次。

## 验收清单（阶段完成后逐项核对）

1. 改身份后松散 Debug 部署仍正常（阶段 1）。
2. 自签证书生成且导入 TrustedPeople（阶段 2）。
3. x64 已签名 `.msix` 生成、Release 裁剪构建 0 错误（阶段 3）。
4. ARM64 `.msix` 生成（阶段 4）——失败则记录为 R4 待办，不阻断。
5. 已签名 `.msixbundle` 生成（阶段 5）。
6. x64 打包版实装成功、Dock 4 控件与 A1 全部交互正常（阶段 6）——**或**记录确切的裁剪破坏现象。
7. 卸载干净、松散 dev 版复原（阶段 7）。
8. `docs/references/msix-packaging.md` 产出、CLAUDE.md 挂指针（阶段 8）。

## 明确不做（YAGNI / 留给后续）

- Host 打进包 → R4。
- Partner Center 真实身份、商店提交、WinGet/Inno Setup → 商店发布阶段。
- CI/CD 自动打包（GitHub Actions）→ 发布阶段。
- 裁剪问题的**根治**若超出打包配置范畴（需改业务代码架构）→ 记录为 R4 前置，不在 A2 内强修。
