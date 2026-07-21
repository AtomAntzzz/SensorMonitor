# A2 MSIX 打包验证 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 验证正式 MSIX 打包产物（Release 裁剪构建 → 自签名 → 安装）在本机可安装、可加载、Dock 正常，产出可复现打包文档，为 R4/商店铺路。

**Architecture:** 一次打包链路 spike：永久改自签名 dev 身份 → 造证书并信任 → x64/ARM64 各出 Release 已签名 `.msix` → 合签 `.msixbundle` → x64 实装验证 → 卸载复原松散开发注册 → 文档化。无业务代码改动（除非 Task 6 暴露裁剪问题需最小修复）。

**Tech Stack:** dotnet MSIX 打包（`GenerateAppxPackageOnBuild`）、`New-SelfSignedCertificate`、Windows Kits `signtool`/`makeappx`。无新依赖。

**Spec:** `docs/superpowers/specs/2026-07-19-a2-msix-packaging-design.md`（含 8 阶段成功判据与验收清单，勿重开设计）

**测试说明（无自动化测试，有意）：** 这是打包/系统状态 spike，产物是 `.msix`/系统安装状态，验证靠命令输出核对 + 用户目视 Dock（Task 1/6 两个用户检查点）。扩展/Host 业务代码零改动，A1 的 11 Host 单测与行为不受影响。

**已核实事实（勿重新查证）：**
- signtool/makeappx 实路径：`C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\{signtool,makeappx}.exe`（本机 PATH 无 signtool，用全路径）。
- 命令均在仓库根 `D:/Workspace/SensorMonitor` 执行；项目目录 = `src/SensorMonitorExtension/SensorMonitorExtension/`。
- manifest = `src/SensorMonitorExtension/SensorMonitorExtension/Package.appxmanifest`：当前 `Name="SensorMonitorExtension"`、`Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"`、`Version="0.0.1.0"`。
- csproj = `src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj`：已有 `EnableMsixTooling=true`、Release 段 `PublishTrimmed=true`、`ILLinkTreatWarningsAsErrors=false`。
- 松散注册 AppxManifest：`src/SensorMonitorExtension/SensorMonitorExtension/bin/x64/Debug/net9.0-windows10.0.26100.0/win-x64/AppxManifest.xml`（Debug 构建时生成，随身份变更同步更新）。
- 当前装的包 `SensorMonitorExtension_0.0.1.0_x64__8wekyb3d8bbwe`（PublisherId 由 CN 派生，改身份后必变 → 包家族名变 → `slots.json` 重置一次，已接受）。

**⚠ 头号风险闸（Task 6）：** Release 裁剪从未跑过。若实装后扩展崩/Dock 空/轮换失效，**这是 A2 的关键发现**，不是失败——按 Task 6 分支如实记录，最小修复或记 R4，不硬钻。

**权限说明：** Task 2 的证书信任导入需管理员（`Start-Process -Verb RunAs`，弹一次 UAC）；其余非提权可完成（签名用 CurrentUser\My 私钥、装包走已启用的开发者模式）。

---

## 文件结构

| 文件 | 动作 | 职责 |
|------|------|------|
| `.../Package.appxmanifest` | 修改 | Publisher 改 dev 身份 |
| `.../SensorMonitorExtension.csproj` | 修改 | 补 AppxPackage 身份三属性 |
| `.gitignore` | 修改 | 忽略 `AppPackages/`、`*.cer`、`*.pfx` |
| `docs/references/msix-packaging.md` | 新增 | 可复现打包命令序列 + 裁剪发现 |
| `CLAUDE.md` | 修改 | 文档地图挂指针 + 状态行 |

> `AppPackages/`（`.msix`/`.msixbundle`）、证书文件均为 gitignore 产物，不入库。下文路径省略前缀 `src/SensorMonitorExtension/SensorMonitorExtension/`（记作 `<PROJ>/`）。

---

## Task 1: 身份改造 + gitignore + 松散 Debug 复验

**Files:**
- Modify: `<PROJ>/Package.appxmanifest`
- Modify: `<PROJ>/SensorMonitorExtension.csproj`
- Modify: `.gitignore`

- [ ] **Step 1: 改 manifest Publisher**

`Package.appxmanifest` 的 `<Identity>` 整体改为（删除紧随其后的占位提示注释亦可）：

```xml
  <Identity
    Name="SensorMonitorExtension"
    Publisher="CN=SensorMonitor Dev"
    Version="0.0.1.0" />
```

- [ ] **Step 2: csproj 补身份三属性**

`SensorMonitorExtension.csproj` 首个 `<PropertyGroup>`（含 `<OutputType>WinExe</OutputType>`）里、`</PropertyGroup>` 之前加：

```xml
    <AppxPackageIdentityName>SensorMonitorExtension</AppxPackageIdentityName>
    <AppxPackagePublisher>CN=SensorMonitor Dev</AppxPackagePublisher>
    <AppxPackageVersion>0.0.1.0</AppxPackageVersion>
```

- [ ] **Step 3: .gitignore 补打包产物**

`.gitignore` 中 `# NuGet` 行之前插入：

```
# MSIX 打包产物 & 本机测试证书（A2）
AppPackages/
*.cer
*.pfx
```

- [ ] **Step 4: 松散 Debug 复验（改身份未破坏现有链路）**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

```bash
powershell -NoProfile -Command "Get-AppxPackage *SensorMonitor* | Remove-AppxPackage; Add-AppxPackage -Register 'D:\Workspace\SensorMonitor\src\SensorMonitorExtension\SensorMonitorExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml'; (Get-AppxPackage *SensorMonitor*).PackageFullName"
```
Expected: 打印新 `PackageFullName`，PublisherId 段**不再是** `8wekyb3d8bbwe`。

- [ ] **Step 5: 用户检查点 —— Dock 仍正常**

```bash
powershell -NoProfile -Command "Start-Process 'x-cmdpal://reload'"
```
请用户确认 Dock 4 控件仍显示读数。异常 → 停下查 manifest 语法/身份。`slots.json` 因家族名变化已重置、控件回默认项，属预期不算异常。

- [ ] **Step 6: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitorExtension/SensorMonitorExtension/Package.appxmanifest src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj .gitignore && git commit -m "chore(ext): adopt CN=SensorMonitor Dev identity, ignore MSIX artifacts"
```

---

## Task 2: 自签名证书 + 信任导入

**Files:** 无（生成系统证书 + `$TEMP` 下 `.cer`，均 gitignore 外）

- [ ] **Step 1: 造自签名代码签名证书（非提权，CurrentUser\My）**

```bash
cat > /tmp/mkcert.ps1 <<'EOF'
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SensorMonitor Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
"THUMBPRINT=$($cert.Thumbprint)"
Export-Certificate -Cert $cert -FilePath "$env:TEMP\SensorMonitorDev.cer" | Out-Null
"CER=$env:TEMP\SensorMonitorDev.cer"
EOF
powershell -NoProfile -ExecutionPolicy Bypass -File /tmp/mkcert.ps1
```
Expected: 打印 `THUMBPRINT=<40位十六进制>` 与 `CER=...SensorMonitorDev.cer`。**记下 THUMBPRINT**，Task 3/5 签名要用。

- [ ] **Step 2: 信任导入（提权，一次 UAC）**

```bash
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-Command','Import-Certificate -FilePath \"$env:TEMP\SensorMonitorDev.cer\" -CertStoreLocation Cert:\LocalMachine\TrustedPeople'"
```
Expected: 弹一次 UAC，同意后证书进入 `LocalMachine\TrustedPeople`（签名的 `.msix` 才会被系统信任）。

- [ ] **Step 3: 验证证书就位**

```bash
powershell -NoProfile -Command "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=SensorMonitor Dev' | Select-Object Subject, Thumbprint | Format-List; Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=SensorMonitor Dev' | Select-Object HasPrivateKey | Format-List"
```
Expected: TrustedPeople 里见 `CN=SensorMonitor Dev` + thumbprint；CurrentUser\My 里 `HasPrivateKey : True`（签名需私钥）。两处 thumbprint 应与 Step 1 一致。

无代码改动，无 commit。

---

## Task 3: 构建 + 签名 x64 Release MSIX

**Files:** 无（产出 gitignore 的 `AppPackages/x64/*.msix`）

- [ ] **Step 1: 构建 x64 Release 未签名 msix**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="AppPackages\x64\\" 2>&1 | tail -5
```
Expected: `0 个错误`（Release 裁剪构建首次跑；若报 IL2026/IL3050 **error**——因 Release 段 `ILLinkTreatWarningsAsErrors=false` 通常降为 warning——记录后继续）。

- [ ] **Step 2: 定位生成的 .msix**

```bash
ls -R src/SensorMonitorExtension/SensorMonitorExtension/AppPackages/x64/ 2>/dev/null | grep -i "\.msix$"
```
Expected: 见形如 `SensorMonitorExtension_0.0.1.0_x64.msix` 的文件。记其完整路径为 `<X64MSIX>`。

- [ ] **Step 3: 用证书签名（signtool 从 CurrentUser\My 按 thumbprint 取私钥，非提权）**

将 `<THUMB>` 替换为 Task 2 的 THUMBPRINT、`<X64MSIX>` 为 Step 2 路径：

```bash
"/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe" sign //fd SHA256 //sha1 <THUMB> "<X64MSIX>"
```
Expected: `Successfully signed: <X64MSIX>`（自签名无时间戳，不加 `//t`）。

- [ ] **Step 4: 验证签名**

```bash
"/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe" verify //pa "<X64MSIX>"
```
Expected: `Successfully verified`（证书已在 TrustedPeople，链可验）。若报未信任 → 回 Task 2 Step 2 确认导入。

无 commit（产物 gitignore）。

---

## Task 4: 构建 ARM64 Release MSIX（仅证能构建）

**Files:** 无（产出 gitignore 的 `AppPackages/ARM64/*.msix`）

- [ ] **Step 1: 构建 ARM64 Release msix**

```bash
cd "D:/Workspace/SensorMonitor" && dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Release -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="AppPackages\ARM64\\" 2>&1 | tail -5
```
Expected: `0 个错误` + 生成 ARM64 `.msix`。**若交叉裁剪构建失败**（spec 风险 2）：记录确切错误到 Task 8 文档的"裁剪/交叉发现"节，ARM64 降级为 R4 待办，**不阻断**——直接进 Task 5 只用 x64（bundle 退化为单架构或跳过，见 Task 5 Step 1 分支）。

- [ ] **Step 2: 签名 ARM64 msix（成功时）**

将 `<THUMB>`、`<ARM64MSIX>`（`ls AppPackages/ARM64/*.msix`）替换：

```bash
"/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe" sign //fd SHA256 //sha1 <THUMB> "<ARM64MSIX>"
```
Expected: `Successfully signed`。ARM64 构建失败则跳过本步。

无 commit。

---

## Task 5: 合成并签名 msixbundle

**Files:** 无（产出 gitignore 的 `AppPackages/*.msixbundle`）

- [ ] **Step 1: 备齐两架构 msix 到一个暂存目录**

若 Task 4 ARM64 成功：

```bash
cd "D:/Workspace/SensorMonitor/src/SensorMonitorExtension/SensorMonitorExtension" && mkdir -p AppPackages/bundle_stage && cp AppPackages/x64/*.msix AppPackages/ARM64/*.msix AppPackages/bundle_stage/ && ls AppPackages/bundle_stage/
```
Expected: 暂存目录含 x64 与 ARM64 两个 `.msix`。

> ARM64 构建失败分支：本 Task 记为"因 ARM64 缺失跳过 bundle，记 R4 待办"，直接进 Task 6（bundle 是商店提交形态，缺一架构无法合规 bundle）。

- [ ] **Step 2: makeappx 合 bundle**

```bash
cd "D:/Workspace/SensorMonitor/src/SensorMonitorExtension/SensorMonitorExtension" && "/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/makeappx.exe" bundle //d AppPackages/bundle_stage //p AppPackages/SensorMonitorExtension_0.0.1.0.msixbundle
```
Expected: `Package creation succeeded`，生成 `AppPackages/SensorMonitorExtension_0.0.1.0.msixbundle`。

- [ ] **Step 3: 签名 bundle**

```bash
cd "D:/Workspace/SensorMonitor/src/SensorMonitorExtension/SensorMonitorExtension" && "/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe" sign //fd SHA256 //sha1 <THUMB> "AppPackages/SensorMonitorExtension_0.0.1.0.msixbundle"
```
Expected: `Successfully signed`。

无 commit。

---

## Task 6: 实装 x64 + Dock 验证（用户检查点 + 裁剪风险闸）

**Files:** 无（系统安装状态；如需最小裁剪修复则动 csproj/业务代码，见 Step 4 分支）

- [ ] **Step 1: 移除松散注册，安装打包版 x64 msix**

将 `<X64MSIX>` 替换为 Task 3 路径：

```bash
powershell -NoProfile -Command "Get-AppxPackage *SensorMonitor* | Remove-AppxPackage; Add-AppxPackage -Path '<X64MSIX>'; (Get-AppxPackage *SensorMonitor*) | Select-Object PackageFullName, SignatureKind | Format-List"
```
Expected: 安装成功；`SignatureKind` 为 `Developer` 或 `Store`（非 `None`），`PackageFullName` 为新身份。**若报证书不信任/签名无效** → 回 Task 2 确认信任导入。

- [ ] **Step 2: 确保 Host 在跑（打包版不含 Host，靠计划任务）**

```bash
powershell -NoProfile -Command "schtasks /Run /TN SensorMonitor.Host 2>&1 | Out-Null; Start-Sleep 3; (Get-Process SensorMonitor.Host -ErrorAction SilentlyContinue) | Select-Object Id"
```
Expected: Host 进程 Id 出现（静默通道拉起，无 UAC）。

- [ ] **Step 3: 触发 reload + 用户检查点**

```bash
powershell -NoProfile -Command "Start-Process 'x-cmdpal://reload'"
```
请用户在 Dock 目视（对照 A1 验收）：4 控件显示读数、右键轮换生效、图标在、菜单"启动 Host"沉底。逐项让用户报结果。

- [ ] **Step 4: 裁剪风险闸判定**

- **正常** → 记"Release 裁剪构建实装无碍"到 Task 8 文档，进 Task 7。
- **异常**（崩/Dock 空/轮换失效/图标丢）→ **这是 A2 关键发现，不是失败**：
  1. 记录确切现象 + `Get-AppxLog` 或事件查看器相关报错到 Task 8 文档"裁剪发现"节。
  2. 判定修复代价：若疑似 `System.Text.Json` 反射被裁（`SlotStore` 最可能）→ 最小修复用 source-generated `JsonSerializerContext`（改 `Dock/SlotStore.cs`，加 trim-safe 上下文），重打包重装复验一次。
  3. 若根因超出打包配置（需改架构）→ 记为 **R4 前置待办**，Task 8 文档写清，A2 以"打包链路通、裁剪问题已定位并归档"结项，不硬修。
  - 任何修复动作单独 commit：`git commit -m "fix(ext): <裁剪修复描述> (A2 trimming finding)"`。

---

## Task 7: 卸载复原松散开发注册

**Files:** 无（系统状态复原）

- [ ] **Step 1: 卸载打包版，恢复松散 Debug 注册**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -2
```
Expected: Debug 构建 `0 个错误`（为松散注册准备 AppxManifest.xml）。

```bash
powershell -NoProfile -Command "Get-AppxPackage *SensorMonitor* | Remove-AppxPackage; Add-AppxPackage -Register 'D:\Workspace\SensorMonitor\src\SensorMonitorExtension\SensorMonitorExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml'; (Get-AppxPackage *SensorMonitor*).PackageFullName"
```
Expected: 打包版卸载、松散 dev 版（新身份）注册回来，打印 `PackageFullName`。

- [ ] **Step 2: 用户检查点 —— 日常开发版复原**

```bash
powershell -NoProfile -Command "Start-Process 'x-cmdpal://reload'"
```
请用户确认 Dock 恢复正常（松散 dev 版工作如常，A2 未污染日常开发环境）。

无 commit（纯系统状态）。

---

## Task 8: 文档化 + 收口推送

**Files:**
- Create: `docs/references/msix-packaging.md`
- Modify: `CLAUDE.md`
- Modify: `docs/plans/2026-07-18-verification-and-next-phase.md`

- [ ] **Step 1: 写打包参考文档**

`docs/references/msix-packaging.md` 内容（把 `<THUMB>` 换成本次实际 thumbprint、路径按实际；"裁剪发现"节据 Task 6 结果填实际现象）：

```markdown
# MSIX 打包（本机复现步骤）

> A2 验证产出（2026-07-19）。身份 = `CN=SensorMonitor Dev`（自签名 dev，商店提交时换 Partner Center）。
> 工具：Windows Kits 10.0.26100.0 的 signtool/makeappx（`C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\`）。

## 一次性准备
- 证书：`New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SensorMonitor Dev" -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable`
- 信任（管理员）：导出 .cer → `Import-Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople`

## 构建 + 签名（每架构）
    dotnet build <csproj> -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="AppPackages\x64\"
    signtool sign /fd SHA256 /sha1 <THUMB> AppPackages\x64\*.msix
（ARM64 同理换 Platform/目录。）

## 合 bundle
    makeappx bundle /d <暂存目录含两架构 msix> /p AppPackages\<name>.msixbundle
    signtool sign /fd SHA256 /sha1 <THUMB> AppPackages\<name>.msixbundle

## 本机实装
    Get-AppxPackage *SensorMonitor* | Remove-AppxPackage
    Add-AppxPackage -Path AppPackages\x64\*.msix
（Host 不在包内 —— 靠计划任务 SensorMonitor.Host 拉起，R4 才随包分发。）

## 裁剪发现（Release PublishTrimmed=true）
<据 Task 6 实际结果填：无碍 / 具体破坏现象 + 已采对策 / 记为 R4 前置>

## R4 待办
- Host 随包分发（打进 MSIX，替换计划任务依赖）
- <ARM64 若失败：交叉裁剪构建修复>
- Partner Center 真实身份 + 商店/WinGet 提交
```

- [ ] **Step 2: CLAUDE.md 挂指针 + 状态**

`CLAUDE.md` 文档地图表格（"碰传感器 / 权限 / 驱动问题"行下）加一行：

```markdown
| 打 MSIX 包 / 签名 / 分发 | `docs/references/msix-packaging.md` |
```

当前状态段的 A1 行之后加：

```markdown
- ✅ A2（2026-07-19）：MSIX 打包链路验证——自签名 dev 身份（`CN=SensorMonitor Dev`）、x64 Release
  已签名 .msix 实装 + Dock 正常、bundle 生成；步骤见 `docs/references/msix-packaging.md`。
  <据 Task 6：裁剪无碍 / 或裁剪发现摘要>
```

- [ ] **Step 3: 路线计划标记 A2 完成**

`docs/plans/2026-07-18-verification-and-next-phase.md` 的 "A2 — 测试 MSIX 打包" 段（在"Phase 2"或"产品诉求清单"里）标题下加：

```markdown
> ✅ 已完成（2026-07-19）：实现见 `docs/superpowers/plans/2026-07-19-a2-msix-packaging.md`、
> 参考 `docs/references/msix-packaging.md`。<裁剪结论一句话>
```

- [ ] **Step 4: Commit + push**

```bash
cd "D:/Workspace/SensorMonitor" && git add docs/references/msix-packaging.md CLAUDE.md docs/plans/2026-07-18-verification-and-next-phase.md && git commit -m "docs: A2 MSIX packaging validated, add repro reference" && git push
```

---

## Self-Review 结论

- **Spec 覆盖**：8 执行阶段 → Task 1（身份）/2（证书）/3（x64 构建签名）/4（ARM64）/5（bundle）/6（实装+裁剪闸）/7（清理复原）/8（文档）一一对应；spec 5 风险 → Task 3 裁剪 error 处置、Task 4 ARM64 失败降级、Task 2+3 signtool 定位、Task 1/6 身份冲突先移除、Task 1 slots.json 重置提示，全覆盖；spec 验收清单 8 项 = 各 Task 的 Expected/检查点。无缺口。
- **占位符**：无 TBD/TODO。`<THUMB>`/`<X64MSIX>`/`<ARM64MSIX>` 是运行期产生的真实值占位（首次生成后回填），非计划缺口，每处均注明来源 Task/命令；文档"裁剪发现"待 Task 6 实测填实，属 spike 应有的 discover-then-record，非含糊要求。
- **一致性**：身份串 `CN=SensorMonitor Dev` 跨 manifest/csproj/证书/签名统一；signtool/makeappx 全路径统一 `10.0.26100.0/x64`；`AppPackages/` 目录结构（x64//ARM64//bundle_stage）跨 Task 3/4/5/6 一致；松散注册 AppxManifest 路径跨 Task 1/7 一致。
- **无测试合理性**：打包 spike，spec 明确以命令输出 + 用户目视验收；已在 Task 1/6/7 设三处用户/系统检查点。
