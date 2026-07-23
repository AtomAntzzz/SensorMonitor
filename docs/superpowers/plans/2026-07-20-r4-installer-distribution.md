# R4 Inno 安装器分发 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 产出一个签名的 Inno 安装器（.exe），一键把「自包含 Host + 计划任务 + 完整扩展 MSIX」装到干净 Windows 机，装时一次 UAC、运行期零 UAC、一处卸载全清。

**Architecture:** 扩展仍是完整签名 MSIX（CmdPal 已验证发现路径，且 Release 裁剪=自包含，带自己的 .NET 运行时）。Host 改自包含发布、由安装器装到 `%ProgramFiles%\SysPulse\Host\`。Inno 安装器（提权）装 Host + 跑 `Host.exe --install-task` 注册 `/RL HIGHEST` 任务 + `Add-AppxProvisionedPackage` 全机预置扩展；卸载反向清干净。扩展仅改 `ResolveHostPath` 一处回退路径。

**Tech Stack:** C#/.NET (net8 Host / net9 扩展) · MSIX（A2 signtool/makeappx 链路）· **Inno Setup 6.3+**（ISCC.exe）· PowerShell（Appx cmdlet）· schtasks。

> **测试策略（重要）**：本期几乎无可单测代码——扩展仅一行路径改动（无扩展测试工程，同 R2 现状），其余是 Inno 脚本 / MSBuild 发布 / MSIX 注册 / 证书，无自动化测试框架。故各任务以 **构建/编译通过 + 产出物存在** 为闸，功能正确性靠 **Task 6 干净机手动验收**（这类打包/提权/发现行为只有实机能验，正如 A2）。Host 12 单测作回归（Host 近乎零改）。这偏离 writing-plans 的 TDD 默认，是本任务性质决定，非疏漏。

> **前置条件（实现者机器需具备）**：
> - **Inno Setup 6.3+**（含 `ISCC.exe`）。`winget install JRSoftware.InnoSetup` 装到**每用户**路径 `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`；传统安装器装到 `C:\Program Files (x86)\Inno Setup 6\`。`build.ps1` 两处都探测。ARM64 支持需 6.3+。
> - A2 的 dev 签名证书 `CN=SysPulse Dev` 在 `Cert:\CurrentUser\My`（缺则按 `docs/references/msix-packaging.md`「一次性准备」生成）。
> - signtool：`C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\<arch>\signtool.exe`（A2 已用）。
> - .NET SDK（构建/发布）。

---

### Task 1: 扩展 ResolveHostPath 回退指向 ProgramFiles

**Files:**
- Modify: `src/SysPulseExtension/SysPulseExtension/Commands/LaunchHostCommand.cs:19-25`

- [ ] **Step 1: 改回退路径 + 更新注释**

把第 19-25 行：

```csharp
    /// <summary>
    /// 开发期用环境变量指向仓库构建产物；打包后回退到包内 Host 目录（后续路线 R4 落地）。
    /// 例：SYSPULSE_HOST_EXE=D:\Workspace\SysPulse\src\SysPulse.Host\bin\Debug\net8.0\SysPulse.Host.exe
    /// </summary>
    internal static string ResolveHostPath() =>
        Environment.GetEnvironmentVariable("SYSPULSE_HOST_EXE")
        ?? Path.Combine(AppContext.BaseDirectory, "Host", "SysPulse.Host.exe");
```

改为：

```csharp
    /// <summary>
    /// 开发期用环境变量指向仓库构建产物；分发安装（R4 Inno 安装器）后回退到
    /// %ProgramFiles%\SysPulse\Host\ —— 安装器装 Host 到此稳定路径、计划任务 /TR 亦指向它。
    /// 例：SYSPULSE_HOST_EXE=D:\Workspace\SysPulse\src\SysPulse.Host\bin\Debug\net8.0\SysPulse.Host.exe
    /// </summary>
    internal static string ResolveHostPath() =>
        Environment.GetEnvironmentVariable("SYSPULSE_HOST_EXE")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SysPulse", "Host", "SysPulse.Host.exe");
```

- [ ] **Step 2: 构建验证**

```bash
taskkill //f //im SysPulseExtension.exe 2>/dev/null || true
dotnet build src/SysPulseExtension/SysPulseExtension/SysPulseExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。

- [ ] **Step 3: 提交**

```bash
git add src/SysPulseExtension/SysPulseExtension/Commands/LaunchHostCommand.cs
git commit -m "feat: Host 回退路径改指 %ProgramFiles%\\SysPulse\\Host（R4 安装器就位）"
```

---

### Task 2: Inno 安装器脚本 SysPulse.iss

**Files:**
- Create: `installer/SysPulse.iss`

- [ ] **Step 1: 写 Inno 脚本**

变量由 Task 3 的 `build.ps1` 经 `/D` 传入（`MyArch`/`MyVersion`/`MyHostDir`/`MyMsix`/`MyMsixName`/`MyCer`/`MyCerName`）。写入：

```pascal
; SysPulse.iss —— 由 installer/build.ps1 用 /D 传入外部路径/变量后编译。
; 不手工双击编译（缺 /D 变量会失败）。

#ifndef MyVersion
  #define MyVersion "0.0.1.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#if MyArch == "x64"
  #define ArchAllowed "x64compatible"
#else
  #define ArchAllowed "arm64"
#endif

[Setup]
AppId={{7A2E6B84-3C1D-4E5F-9A70-5E4C0B9D2F11}}
AppName=SysPulse
AppVersion={#MyVersion}
AppPublisher=A Lone Developer
DefaultDirName={autopf}\SysPulse
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode=x64compatible arm64
OutputDir=Output
OutputBaseFilename=SysPulseSetup_{#MyArch}
UninstallDisplayName=SysPulse
UninstallDisplayIcon={app}\Host\SysPulse.Host.exe
WizardStyle=modern

[Files]
; 自包含 Host → {app}\Host
Source: "{#MyHostDir}\*"; DestDir: "{app}\Host"; Flags: recursesubdirs createallsubdirs ignoreversion
; 扩展 msix + 签名证书公钥 → 临时目录，装完删
Source: "{#MyMsix}"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#MyCer}";  DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1) 信任 dev 证书（换真实链到受信根的证书时删掉本行）
Filename: "{sys}\certutil.exe"; Parameters: "-addstore -f TrustedPeople ""{tmp}\{#MyCerName}"""; Flags: runhidden waituntilterminated; StatusMsg: "信任签名证书…"
; 2) 注册计划任务：跑装好的 Host，TaskInstaller 以 Environment.ProcessPath 作 /TR，即本 {app}\Host 路径
Filename: "{app}\Host\SysPulse.Host.exe"; Parameters: "--install-task"; Flags: runhidden waituntilterminated; StatusMsg: "注册后台服务…"
; 3) 全机预置扩展 MSIX + 当前用户即时注册
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxProvisionedPackage -Online -PackagePath '{tmp}\{#MyMsixName}' -SkipLicense; Add-AppxPackage -Path '{tmp}\{#MyMsixName}'"""; Flags: runhidden waituntilterminated; StatusMsg: "注册命令面板扩展…"
; 4) 立即启动 Host（免等下次登录）
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN SysPulse.Host"; Flags: runhidden

[UninstallRun]
; 停 Host → 删任务 → 移除扩展（当前用户 + 预置）
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN SysPulse.Host"; Flags: runhidden; RunOnceId: "EndHost"
Filename: "{app}\Host\SysPulse.Host.exe"; Parameters: "--uninstall-task"; Flags: runhidden waituntilterminated; RunOnceId: "DelTask"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-AppxPackage *SysPulseExtension* | Remove-AppxPackage; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*SysPulseExtension*' | ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName }"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePkg"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{commonappdata}\SysPulse"
```

> 注：`[Run]` 里 PowerShell 的引号嵌套是最易出错处；Task 4 端到端编译+试装时若报引号/转义错，在此微调（`""` 表示脚本内一个 `"`）。

- [ ] **Step 2: 语法自查（不需完整编译）**

用 ISCC 只做预处理语法检查（缺 `/D` 外部文件仍会在 `[Files]` 校验失败，属正常——此步只看**语法**错误，不看文件存在性）：

```bash
ISCC=$(ls "$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe" "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" 2>/dev/null | head -1)
"$ISCC" "/DMyHostDir=." "/DMyMsix=SysPulse.iss" "/DMyMsixName=x" "/DMyCer=SysPulse.iss" "/DMyCerName=x" installer/SysPulse.iss 2>&1 | head -20 || true
```

Expected: 无 `Unknown` / 语法类报错（可以有 "files … does not exist" 类校验错，那是缺真实 staging，Task 4 会补齐）。

- [ ] **Step 3: 提交**

```bash
git add installer/SysPulse.iss
git commit -m "feat: Inno 安装器脚本（装 Host + 注册任务 + 预置扩展 MSIX + 卸载清理）"
```

---

### Task 3: 端到端构建脚本 build.ps1

**Files:**
- Create: `installer/build.ps1`

- [ ] **Step 1: 写编排脚本**

```powershell
# installer/build.ps1 —— 端到端出「签名安装器」：发布自包含 Host → 构建+签名扩展 MSIX
# → 导出证书 → ISCC 编译 → 签名安装器。用法：pwsh installer/build.ps1 -Arch x64
param(
    [ValidateSet('x64','arm64')] [string]$Arch = 'x64',
    [string]$Thumbprint,          # 不传则自动找 CN=SysPulse Dev
    [string]$Version = '0.0.1.0'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$rid  = "win-$Arch"
$plat = if ($Arch -eq 'x64') { 'x64' } else { 'ARM64' }
$stage = Join-Path $here "stage\$Arch"
Remove-Item $stage -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Force $stage | Out-Null

# 1) 自包含发布 Host
$hostOut = Join-Path $stage 'Host'
& dotnet publish "$root\src\SysPulse.Host\SysPulse.Host.csproj" -c Release -r $rid --self-contained true -o $hostOut
if ($LASTEXITCODE) { throw "Host publish 失败" }

# 2) 构建 + 签名扩展 MSIX（复用 A2 链路）
$appxDir = Join-Path $stage 'appx'
& dotnet build "$root\src\SysPulseExtension\SysPulseExtension\SysPulseExtension.csproj" `
    -c Release -p:Platform=$plat -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="$appxDir\"
if ($LASTEXITCODE) { throw "MSIX build 失败" }
$msix = Get-ChildItem $appxDir -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw "未找到 .msix（检查 AppxPackageDir）" }

if (-not $Thumbprint) {
    $Thumbprint = (Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=SysPulse Dev' } | Select-Object -First 1).Thumbprint
    if (-not $Thumbprint) { throw "未找到 CN=SysPulse Dev 证书；见 docs/references/msix-packaging.md 生成" }
}
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\$Arch\signtool.exe"
& $signtool sign /fd SHA256 /sha1 $Thumbprint $msix.FullName
if ($LASTEXITCODE) { throw "MSIX 签名失败" }

# 3) 导出证书公钥（供安装器 certutil 信任）
$cer = Join-Path $stage 'SysPulseDev.cer'
Export-Certificate -Cert "Cert:\CurrentUser\My\$Thumbprint" -FilePath $cer | Out-Null

# 4) ISCC 编译安装器（探测 winget 每用户路径 + 传统机器级路径）
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "未找到 Inno Setup 6（ISCC.exe）；winget install JRSoftware.InnoSetup" }
& $iscc "/DMyArch=$Arch" "/DMyVersion=$Version" "/DMyHostDir=$hostOut" `
    "/DMyMsix=$($msix.FullName)" "/DMyMsixName=$($msix.Name)" `
    "/DMyCer=$cer" "/DMyCerName=SysPulseDev.cer" "$here\SysPulse.iss"
if ($LASTEXITCODE) { throw "ISCC 编译失败" }

# 5) 签名安装器
$setup = Join-Path $here "Output\SysPulseSetup_$Arch.exe"
& $signtool sign /fd SHA256 /sha1 $Thumbprint $setup
if ($LASTEXITCODE) { throw "安装器签名失败" }
Write-Host "OK → $setup"
```

- [ ] **Step 2: 加 .gitignore（stage / Output 产物不入库）**

在仓库根 `.gitignore` 追加（若无则创建）：

```
installer/stage/
installer/Output/
```

- [ ] **Step 3: 提交**

```bash
git add installer/build.ps1 .gitignore
git commit -m "feat: 安装器端到端构建脚本（发布 Host + 签名 MSIX + ISCC + 签名）"
```

---

### Task 4: 端到端构建产出签名安装器

**Files:** 无（运行 Task 3 脚本，验证产出）

- [ ] **Step 1: 跑 x64 构建**

```bash
taskkill //f //im SysPulse.Host.exe 2>/dev/null || true
powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer/build.ps1 -Arch x64 2>&1 | tail -20
```

Expected: 结尾 `OK → ...\installer\Output\SysPulseSetup_x64.exe`，无 throw。若 ISCC 报 `[Run]` 引号/转义错 → 回 Task 2 微调 PowerShell 引号后重跑。

- [ ] **Step 2: 验证产出物 + 签名**

```bash
ls -la installer/Output/SysPulseSetup_x64.exe
"/c/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe" verify /pa installer/Output/SysPulseSetup_x64.exe 2>&1 | tail -3
```

Expected: 文件存在；`Successfully verified`。

- [ ] **Step 3: 提交（仅记录，无产物入库）**

无源码改动则跳过提交；若 Step 1 回改了 `SysPulse.iss`：

```bash
git add installer/SysPulse.iss
git commit -m "fix: 安装器脚本引号/转义修正，端到端编译通过"
```

---

### Task 5: 复现文档 installer.md

**Files:**
- Create: `docs/references/installer.md`

- [ ] **Step 1: 写复现文档**

```markdown
# R4 分发安装器（本机复现）

> 产出：签名 Inno 安装器 `SysPulseSetup_<arch>.exe`——一键装「自包含 Host + 计划任务 + 完整扩展 MSIX」。
> 渠道 = GitHub Release + WinGet（installer type: inno）。**不走 MS Store**（驱动+提权约束，见 R4 spec）。

## 前置
- Inno Setup 6.3+（`ISCC.exe`）。
- dev 签名证书 `CN=SysPulse Dev`（`Cert:\CurrentUser\My`）；缺则见 `msix-packaging.md`。
- signtool（Windows Kits 10.0.26100.0）。

## 构建
​```powershell
pwsh installer/build.ps1 -Arch x64      # ARM64 同理换 -Arch arm64
# 产出：installer/Output/SysPulseSetup_x64.exe（已签名）
​```

## 安装做了什么（提权一次 UAC）
1. `certutil -addstore TrustedPeople`：信任 dev 证书（真实证书时安装器删此步）。
2. `Host.exe --install-task`：注册 `SysPulse.Host`（`/RL HIGHEST /SC ONLOGON`，`/TR`=`%ProgramFiles%\SysPulse\Host\SysPulse.Host.exe`）。
3. `Add-AppxProvisionedPackage`（全机）+ `Add-AppxPackage`（当前用户）注册扩展 MSIX。
4. `schtasks /Run` 立即起 Host。

## 卸载（走「程序和功能 → SysPulse」主入口，勿单独卸 MSIX 组件）
删任务 + `Remove-AppxPackage`/`Remove-AppxProvisionedPackage` + 删 `%ProgramFiles%\SysPulse\` + `%ProgramData%\SysPulse\`。

## 已知限制
- 双卸载入口：MSIX 在「设置>应用」另列一条；单独卸它只清扩展、残留 Host+任务。以主入口卸载为准。
- dev 证书需信任步；换 Partner Center 真证书（R4b）后去掉。
- R4b 待办：WinGet 清单 + GitHub Release 上传 + 真实证书/身份。
```

- [ ] **Step 2: 提交**

```bash
git add docs/references/installer.md
git commit -m "docs: R4 安装器复现文档"
```

---

### Task 6: 干净机手动验收 + 回归 + 收口

**Files:** 无（验收）→ 末尾改状态文档

> 需一台**干净 Windows**（VM 快照或第二台机，最好未装 .NET）。安装器 + 内含 MSIX/Host 用的是 dev 自签证书，安装器会自动 `certutil` 信任它。

- [ ] **Step 1: Host 单测回归**

```bash
taskkill //f //im SysPulse.Host.exe 2>/dev/null || true
dotnet test tests/SysPulse.Host.Tests
```

Expected: 12 通过（Host 近乎零改，确认无回归）。

- [ ] **Step 2: 干净机安装验收**

拷 `installer/Output/SysPulseSetup_x64.exe` 到干净机，双击安装（**一次 UAC**）。逐条验：
1. 安装无报错完成。
2. 打开 CmdPal → 出现 SysPulse 扩展 + Dock 有实时读数（**未设 `SYSPULSE_HOST_EXE`、未预装 .NET**）。
3. 任务计划程序里有 `SysPulse.Host`（`/TR` = `%ProgramFiles%\SysPulse\Host\SysPulse.Host.exe`）。
4. 重启机器 → 登录后读数自动恢复，全程无 UAC。

- [ ] **Step 3: 卸载验收**

「程序和功能 → SysPulse」卸载 → 验：计划任务消失、`%ProgramFiles%\SysPulse\` 与 `%ProgramData%\SysPulse\` 删除、CmdPal 里扩展消失（`Get-AppxPackage *SysPulse*` 空）。

- [ ] **Step 4: 收口——更新状态与路线**

在 `CLAUDE.md`「当前状态」段追加 R4 完成条目（仿 R7/R2：日期、Inno 安装器、自包含 Host、装时一次 UAC、渠道 GitHub Release+WinGet 非 Store），在 `docs/plans/2026-07-18-verification-and-next-phase.md` 的 R4 行标 ✅ + 指向本 plan 与 `docs/references/installer.md`；注明 R4b（WinGet/Release 提交 + 真证书）为后续。

```bash
git add CLAUDE.md docs/plans/2026-07-18-verification-and-next-phase.md
git commit -m "docs: R4 安装器分发完成收口，更新状态与路线"
```

---

## 自检（plan vs spec）

- **Spec 覆盖**：构建管线（Task 3：Host 自包含发布 + MSIX 复用 A2 + ISCC）✓；安装流程 4 步（Task 2 `[Run]`：certutil/‑‑install-task/预置+注册/schtasks Run）✓；运行时零 UAC + ResolveHostPath 改动（Task 1 + Task 6 Step 2.4）✓；卸载一处清（Task 2 `[UninstallRun]`/`[UninstallDelete]` + Task 6 Step 3）✓；全机预置默认（Task 2 `Add-AppxProvisionedPackage`）✓；双入口文档（Task 5 已知限制）✓；证书信任步（Task 2 `[Run]` 1 + Task 3 导出）✓；Host 零改 + 12 测回归（Task 6 Step 1）✓；YAGNI（R4b/真证书/sparse/`--setup` 均不含）✓。
- **类型/签名一致**：`ResolveHostPath` 回退 = `%ProgramFiles%\SysPulse\Host\SysPulse.Host.exe`（Task 1 定义）与安装器 `[Files]` DestDir `{app}\Host`（`{app}`=`{autopf}\SysPulse`）、任务 `/TR`、Task 6 验收路径三处一致 ✓；`build.ps1` 传入的 `/D` 变量名（MyArch/MyVersion/MyHostDir/MyMsix/MyMsixName/MyCer/MyCerName）与 `.iss` 引用一致 ✓；证书名 `SysPulseDev.cer` 三处一致 ✓；任务名 `SysPulse.Host` 全篇一致 ✓。
- **占位符扫描**：无 TBD/TODO；`.iss`、`build.ps1`、`installer.md` 均给完整内容；命令带预期输出。`[Run]` PowerShell 引号标注为 Task 4 微调点（非占位符，是已知易错项 + 收敛步骤）。
