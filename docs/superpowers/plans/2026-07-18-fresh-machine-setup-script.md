# 新机器一键引导脚本 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一个自提权、幂等的 `scripts/setup.ps1`（+ `setup.cmd` 包装），把 SensorMonitor 在全新 Windows 机器上从零拉起到"能构建、11 单测绿、扩展已尝试部署、Host 计划任务已注册"。

**Architecture:** 单文件 PowerShell 脚本，结构为「预检 → 6 个幂等阶段函数 → 总结报告表」。每阶段是独立函数，先检测状态（`winget list` / 注册表 / `schtasks /Query` / `Get-AppxPackage`）已满足就跳过。脚本开头自提权（非管理员则 `Start-Process -Verb RunAs` 重启自身）。无自动化单测——脚本副作用是系统级安装，验证靠 `-CheckOnly` 本机核对 + 真机全流程 + 二次重跑验幂等。

**Tech Stack:** Windows PowerShell 5.1+（系统自带）、winget v1.29（已确认本机存在）、既有 .NET CLI。无新增代码依赖。

**Spec:** `docs/superpowers/specs/2026-07-18-fresh-machine-setup-script-design.md`

**已确认的四个决策：** 全套安装；不装 VS 纯 CLI 部署；阶段 4 尽力而为+报错不阻断；PawnIO 默认静默装。

**已核实事实（勿重新查证）：**
- winget 包 ID：`Microsoft.DotNet.SDK.8`、`Microsoft.DotNet.SDK.9`、`Microsoft.PowerToys`、`namazso.PawnIO`（均已 `winget search` 确认存在）。
- 仓库根 = `SensorMonitor.sln` 所在目录；`scripts/` 将建在其下，脚本用 `$PSScriptRoot\..` 上溯定位根。
- Host exe 构建输出：`src/SensorMonitor.Host/bin/Debug/net8.0/SensorMonitor.Host.exe`（`dotnet build` Debug 默认路径）。
- Host 已有 `--install-task`（注册计划任务 `SensorMonitor.Host`）与 `--dump` 参数（见 `src/SensorMonitor.Host/Program.cs`）。
- Host 单测项目：`tests/SensorMonitor.Host.Tests`，当前 11 测全绿。
- 扩展项目：`src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj`，`TargetFramework=net9.0-windows10.0.26100.0`，`RuntimeIdentifiers=win-x64;win-arm64`，`EnableMsixTooling=true`。

**开发说明：** 本机（写脚本的机器）已装 winget/.NET，可用 `-CheckOnly` 与逐函数 dot-source 验证检测逻辑；但真正的"安装"副作用只应在目标新机器上跑。每个 Task 的验证步骤里，凡标注 **[仅新机器]** 的手动整机验证不在本会话执行，留待桌面会话——本会话只做 `-CheckOnly` 与语法/逻辑验证。

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/setup.ps1` | 主脚本：参数、自提权、日志辅助、6 个阶段函数、总结表 |
| `scripts/setup.cmd` | 薄包装：以 `-ExecutionPolicy Bypass` 调 `setup.ps1`，透传参数 |
| `CLAUDE.md`（改） | "常用命令"补 `scripts\setup.cmd` 一行 |

脚本内部按职责分节（同一文件内的函数），非拆多文件——环境编排脚本内聚性强、共享日志/状态收集器，单文件更易读易分发。

---

## Task 1: 脚本骨架 —— 参数、日志、状态收集器、总结表

**Files:**
- Create: `scripts/setup.ps1`

本 Task 先搭"能跑但只打印总结"的骨架：参数解析、`$script:Results` 收集器、`Write-Step`/`Add-Result` 辅助、末尾总结表。后续 Task 往里填阶段函数。

- [ ] **Step 1: 写骨架**

`scripts/setup.ps1`:

```powershell
<#
.SYNOPSIS
  SensorMonitor 新机器一键开发环境引导（自提权、幂等）。
.DESCRIPTION
  预检 → 工具链 → 开发者模式 → Host 构建测试 → 扩展部署 → 计划任务 → 总结。
  详见 docs/superpowers/specs/2026-07-18-fresh-machine-setup-script-design.md。
.PARAMETER CheckOnly
  只检测缺什么并打印报告，不改系统、不提权。
.PARAMETER SkipInstall
  跳过阶段 1（工具链已装时，只跑构建/部署/计划任务）。
#>
[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'

# 仓库根：脚本固定在 <root>\scripts\ 下，上溯一级。
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# 每阶段结果收集，末尾统一成表。Status ∈ 已就绪/已完成/⚠/❌
$script:Results = [System.Collections.Generic.List[object]]::new()
# 需人工收尾的提示，末尾统一打印。
$script:FollowUps = [System.Collections.Generic.List[string]]::new()

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Add-Result([string]$stage, [string]$status, [string]$detail = '') {
    $script:Results.Add([pscustomobject]@{ Stage = $stage; Status = $status; Detail = $detail })
}

function Add-FollowUp([string]$msg) { $script:FollowUps.Add($msg) }

function Show-Summary {
    Write-Host ''
    Write-Host '===== 总结 =====' -ForegroundColor Yellow
    $script:Results | Format-Table -AutoSize Stage, Status, Detail | Out-String | Write-Host
    if ($script:FollowUps.Count -gt 0) {
        Write-Host '需人工收尾：' -ForegroundColor Yellow
        foreach ($f in $script:FollowUps) { Write-Host "  - $f" }
    }
}

# ---- 阶段函数占位（后续 Task 填充） ----

function Main {
    Write-Step "SensorMonitor 引导开始（RepoRoot=$script:RepoRoot；CheckOnly=$CheckOnly；SkipInstall=$SkipInstall）"
    # 阶段调用在后续 Task 接入。
    Show-Summary
}

Main
```

- [ ] **Step 2: 语法与骨架运行验证**

Run（Git Bash）：
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | head -20
```
Expected: 打印 `==> SensorMonitor 引导开始（RepoRoot=...SensorMonitor...）` 和 `===== 总结 =====` 空表，无报错、退出码 0。

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): script skeleton with summary collector"
```

---

## Task 2: 阶段 0 预检 + 自提权

**Files:**
- Modify: `scripts/setup.ps1`

- [ ] **Step 1: 加预检与自提权函数**

在 Task 1 的「阶段函数占位」处插入：

```powershell
function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 非管理员且需要改系统 → 提权重启自身；-CheckOnly 不提权。
function Ensure-Elevated {
    if ($CheckOnly) { return }
    if (Test-IsAdmin) { return }
    Write-Step '需要管理员权限，正在提权重启（会弹一次 UAC）…'
    $argList = @('-ExecutionPolicy', 'Bypass', '-NoProfile', '-File', "`"$PSCommandPath`"")
    if ($SkipInstall) { $argList += '-SkipInstall' }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    exit 0
}

# 阶段 0：winget 是否可用。缺失 → 硬失败（工具链全靠它）。
function Invoke-Stage0Preflight {
    Write-Step '阶段 0：预检'
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Add-Result '0 预检' '❌' 'winget 不存在，请先从 Microsoft Store 安装 App Installer'
        Show-Summary
        exit 1
    }
    Add-Result '0 预检' '已就绪' "winget $(winget --version 2>$null)"
}
```

- [ ] **Step 2: 接入 Main**

把 `Main` 函数体改为：

```powershell
function Main {
    Write-Step "SensorMonitor 引导开始（RepoRoot=$script:RepoRoot；CheckOnly=$CheckOnly；SkipInstall=$SkipInstall）"
    Invoke-Stage0Preflight
    Ensure-Elevated
    Show-Summary
}
```

- [ ] **Step 3: 验证（本机 -CheckOnly，不提权）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | head -20
```
Expected: 总结表出现 `0 预检 | 已就绪 | winget v1.29...`，退出码 0，**未**触发 UAC（因 `-CheckOnly`）。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 0 preflight and self-elevation"
```

---

## Task 3: 阶段 1 工具链安装（winget，幂等）

**Files:**
- Modify: `scripts/setup.ps1`

- [ ] **Step 1: 加工具链函数**

在阶段 0 函数后插入：

```powershell
# 单个 winget 包：已装则跳过，否则静默安装。返回 $true=已就绪/已装成功。
function Install-WingetPackage([string]$id, [string]$display) {
    $installed = (winget list --id $id -e 2>$null | Select-String -SimpleMatch $id)
    if ($installed) {
        Write-Host "  $display 已装，跳过"
        return $true
    }
    if ($CheckOnly) {
        Write-Host "  [CheckOnly] 缺 $display → winget install -e --id $id"
        return $false
    }
    Write-Step "  安装 $display …"
    winget install -e --id $id --silent `
        --accept-package-agreements --accept-source-agreements
    # winget 退出码非 0 也可能是"已装/需重启"，用装后复查判定真实状态。
    $ok = [bool](winget list --id $id -e 2>$null | Select-String -SimpleMatch $id)
    return $ok
}

# 阶段 1：四个包。PawnIO 装后单独提示可能需重启。
function Invoke-Stage1Toolchain {
    if ($SkipInstall) { Add-Result '1 工具链' '已就绪' '按 -SkipInstall 跳过'; return }
    Write-Step '阶段 1：工具链'
    $pkgs = @(
        @{ id = 'Microsoft.DotNet.SDK.8'; name = '.NET 8 SDK' },
        @{ id = 'Microsoft.DotNet.SDK.9'; name = '.NET 9 SDK' },
        @{ id = 'Microsoft.PowerToys';    name = 'PowerToys' },
        @{ id = 'namazso.PawnIO';         name = 'PawnIO 驱动' }
    )
    $missing = @()
    foreach ($p in $pkgs) {
        if (-not (Install-WingetPackage $p.id $p.name)) { $missing += $p.name }
    }
    if ($CheckOnly) {
        if ($missing.Count) { Add-Result '1 工具链' '⚠' "缺: $($missing -join ', ')" }
        else { Add-Result '1 工具链' '已就绪' '全部已装' }
        return
    }
    Add-FollowUp 'PawnIO 内核驱动装后可能需重启才生效（CPU/主板温度依赖它）'
    if ($missing.Count) { Add-Result '1 工具链' '⚠' "未确认: $($missing -join ', ')" }
    else { Add-Result '1 工具链' '已完成' '四个包就绪' }
}
```

- [ ] **Step 2: 接入 Main（阶段 0 之后）**

`Main` 中 `Ensure-Elevated` 之后加：
```powershell
    Invoke-Stage1Toolchain
```

- [ ] **Step 3: 验证（本机 -CheckOnly）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | tail -25
```
Expected: 逐个打印四个包"已装，跳过"或"[CheckOnly] 缺 …"；总结表 `1 工具链` 行为 `已就绪` 或 `⚠ 缺: …`。**[仅新机器]** 无 `-CheckOnly` 全流程真正安装留待新机器验证。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 1 idempotent toolchain install via winget"
```

---

## Task 4: 阶段 2 开发者模式（注册表，幂等）

**Files:**
- Modify: `scripts/setup.ps1`

- [ ] **Step 1: 加开发者模式函数**

在阶段 1 函数后插入：

```powershell
# 阶段 2：松散 MSIX 注册前提。检测/置注册表 AllowDevelopmentWithoutDevLicense=1。
function Invoke-Stage2DevMode {
    Write-Step '阶段 2：开发者模式'
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    $val = (Get-ItemProperty -Path $key -Name AllowDevelopmentWithoutDevLicense `
            -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($val -eq 1) { Add-Result '2 开发者模式' '已就绪' '已开启'; return }
    if ($CheckOnly) { Add-Result '2 开发者模式' '⚠' '未开启（需 admin 写注册表）'; return }
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name AllowDevelopmentWithoutDevLicense `
        -PropertyType DWord -Value 1 -Force | Out-Null
    Add-Result '2 开发者模式' '已完成' '已开启'
}
```

- [ ] **Step 2: 接入 Main（阶段 1 之后）**

```powershell
    Invoke-Stage2DevMode
```

- [ ] **Step 3: 验证（本机 -CheckOnly）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | tail -15
```
Expected: 总结表 `2 开发者模式` 行为 `已就绪`（本机已开）或 `⚠ 未开启`。CheckOnly 下不写注册表。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 2 enable developer mode"
```

---

## Task 5: 阶段 3 Host 构建 + 测试

**Files:**
- Modify: `scripts/setup.ps1`

- [ ] **Step 1: 加构建测试函数**

在阶段 2 函数后插入：

```powershell
# 阶段 3：停占用 bin 的 Host（坑 #6）→ restore → build → test。测试红 = 硬失败。
function Invoke-Stage3BuildTest {
    Write-Step '阶段 3：Host 构建 + 测试'
    if ($CheckOnly) {
        $hasDotnet = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
        Add-Result '3 构建测试' ($(if ($hasDotnet) {'已就绪'} else {'⚠'})) `
            ($(if ($hasDotnet) {'dotnet 可用（未实跑）'} else {'dotnet 不在 PATH'}))
        return
    }
    # 停运行中的 Host，避免锁 bin。
    taskkill /f /im SensorMonitor.Host.exe 2>$null | Out-Null
    Push-Location $script:RepoRoot
    try {
        dotnet restore SensorMonitor.sln
        dotnet build SensorMonitor.sln -c Debug
        dotnet test tests/SensorMonitor.Host.Tests -c Debug
        if ($LASTEXITCODE -ne 0) {
            Add-Result '3 构建测试' '❌' "dotnet test 退出码 $LASTEXITCODE"
            Show-Summary
            exit 1   # 构建/测试红是硬失败
        }
        Add-Result '3 构建测试' '已完成' '11 单测通过'
    }
    finally { Pop-Location }
}
```

- [ ] **Step 2: 接入 Main（阶段 2 之后）**

```powershell
    Invoke-Stage3BuildTest
```

- [ ] **Step 3: 验证（本机 -CheckOnly）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | tail -12
```
Expected: 总结表 `3 构建测试` 行为 `已就绪 | dotnet 可用（未实跑）`。CheckOnly 不真正构建（避免副作用/耗时）。

- [ ] **Step 4: [本机可选] 全流程构建测试冒烟**

> 本机已装 dotnet，可安全验证阶段 3 的实跑逻辑（无系统级安装副作用，仅构建）。若本机当前无运行的 Host，可执行：
```bash
cd "D:/Workspace/SensorMonitor" && dotnet test tests/SensorMonitor.Host.Tests -c Debug 2>&1 | tail -3
```
Expected: `已通过! ... 通过: 11`。这验证阶段 3 内部命令序列正确。

- [ ] **Step 5: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 3 host build and test"
```

---

## Task 6: 阶段 4 扩展 CLI 部署（尽力而为 + 报错不阻断）

**Files:**
- Modify: `scripts/setup.ps1`

阶段 4 已实测通过（2026-07-18 spike）：`dotnet build -c Debug -p:Platform=x64` → 松散 `AppxManifest.xml`（在 `bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\` 下）→ `Add-AppxPackage -Register`（`Status: Ok`）。再 patch CmdPal `AllowExternalReload=true` 并 `x-cmdpal://reload` 触发**无 GUI 重载**。异常**不改退出码**，仅标 ⚠ 并打印原始错误 + 兜底。

- [ ] **Step 1: 加部署 + 自动重载辅助函数**

在阶段 3 函数后插入：

```powershell
# CmdPal 外部重载：置 AllowExternalReload=true 并触发 x-cmdpal://reload（spike 已验证）。
# 尽力而为：找不到 CmdPal / 设置文件缺失都只提示，不抛。
function Invoke-CmdPalReload {
    $pkg = Get-AppxPackage Microsoft.CommandPalette -ErrorAction SilentlyContinue
    if (-not $pkg) {
        Add-FollowUp 'CmdPal(Microsoft.CommandPalette) 未安装，无法自动重载；装 PowerToys/CmdPal 后手动 Reload'
        return
    }
    $settings = Join-Path $env:LOCALAPPDATA `
        "Packages\$($pkg.PackageFamilyName)\LocalState\settings.json"
    if (Test-Path $settings) {
        try {
            $json = Get-Content $settings -Raw | ConvertFrom-Json
            if ($json.AllowExternalReload -ne $true) {
                $json.AllowExternalReload = $true
                ($json | ConvertTo-Json -Depth 100) | Set-Content $settings -Encoding UTF8
            }
        } catch { Add-FollowUp "改 CmdPal AllowExternalReload 失败：$($_.Exception.Message)" }
    } else {
        Add-FollowUp 'CmdPal 尚未生成设置文件；先启动一次 CmdPal，或在设置里手动开"启用外部重新加载"'
    }
    # 触发重载（CmdPal 未运行时协议激活会拉起它，同样加载新扩展）。
    Start-Process 'x-cmdpal://reload' -ErrorAction SilentlyContinue
}

# 阶段 4：CLI 复刻 VS Deploy —— 构建扩展松散布局、注册、自动重载。
function Invoke-Stage4Deploy {
    Write-Step '阶段 4：扩展部署（CLI，已实测）'
    $extProj = Join-Path $script:RepoRoot `
        'src\SensorMonitorExtension\SensorMonitorExtension\SensorMonitorExtension.csproj'
    if ($CheckOnly) {
        Add-Result '4 扩展部署' '已就绪' 'CLI 部署已实测可行；CheckOnly 不执行'
        return
    }
    try {
        Push-Location $script:RepoRoot
        try {
            dotnet build $extProj -c Debug -p:Platform=x64
            if ($LASTEXITCODE -ne 0) { throw "dotnet build 扩展退出码 $LASTEXITCODE" }
        }
        finally { Pop-Location }

        # 松散 AppxManifest.xml 在 RID 子目录 win-x64 下；优先取 x64。
        $outRoot = Join-Path (Split-Path $extProj) 'bin\x64\Debug'
        $manifest = Get-ChildItem -Path $outRoot -Recurse -Filter 'AppxManifest.xml' `
            -ErrorAction SilentlyContinue |
            Sort-Object { $_.FullName -notmatch 'win-x64' } | Select-Object -First 1
        if (-not $manifest) {
            throw "未找到 AppxManifest.xml（$outRoot 下）——CLI 可能未生成松散布局"
        }
        Add-AppxPackage -Register $manifest.FullName -ErrorAction Stop
        Invoke-CmdPalReload
        Add-Result '4 扩展部署' '已完成' "已注册并触发重载：$($manifest.FullName)"
    }
    catch {
        # 尽力而为：不改退出码，如实报原始错误 + 兜底。
        Add-Result '4 扩展部署' '⚠' "CLI 部署失败：$($_.Exception.Message)"
        Add-FollowUp "扩展 CLI 部署失败，兜底：装 Visual Studio(WinUI 工作负载)走 Deploy，或按上方 Add-AppxPackage 报错补 WinAppSDK runtime 包。原始错误已在日志。"
        Write-Warning $_.Exception.Message
    }
}
```

- [ ] **Step 2: 接入 Main（阶段 3 之后）**

```powershell
    Invoke-Stage4Deploy
```

- [ ] **Step 3: 验证（本机 -CheckOnly）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1 | tail -12
```
Expected: 总结表 `4 扩展部署` 行为 `已就绪 | CLI 部署已实测可行；CheckOnly 不执行`。

- [ ] **Step 4: [本机可选] 实跑一次部署冒烟**

> 本机已装 dotnet + 开发者模式已开，可安全实测阶段 4（build + 注册是 per-user 可逆 dev 部署，`Remove-AppxPackage` 可撤）。
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -Command "& { . { $null } ; Add-AppxPackage -Register 'src/SensorMonitorExtension/SensorMonitorExtension/bin/x64/Debug/net9.0-windows10.0.26100.0/win-x64/AppxManifest.xml' ; Get-AppxPackage *SensorMonitor* | Select-Object Name,Status }" 2>&1 | tail -3
```
Expected: `SensorMonitorExtension ... Ok`（spike 已确认）。

- [ ] **Step 5: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 4 CLI extension deploy with auto-reload"
```

---

## Task 7: 阶段 5 计划任务注册 + 阶段 6 总结接入

**Files:**
- Modify: `scripts/setup.ps1`

- [ ] **Step 1: 加计划任务函数**

在阶段 4 函数后插入：

```powershell
# 阶段 5：注册 Host 静默提权计划任务（D7）。已注册则跳过。
function Invoke-Stage5ScheduledTask {
    Write-Step '阶段 5：计划任务'
    $exists = (schtasks /Query /TN SensorMonitor.Host 2>$null; $LASTEXITCODE -eq 0)
    if ($exists) { Add-Result '5 计划任务' '已就绪' '任务 SensorMonitor.Host 已存在'; return }
    if ($CheckOnly) { Add-Result '5 计划任务' '⚠' '未注册（需 --install-task）'; return }
    $hostExe = Join-Path $script:RepoRoot `
        'src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe'
    if (-not (Test-Path $hostExe)) {
        Add-Result '5 计划任务' '⚠' "Host exe 不存在（阶段 3 未成功？）: $hostExe"
        return
    }
    & $hostExe --install-task
    $ok = (schtasks /Query /TN SensorMonitor.Host 2>$null; $LASTEXITCODE -eq 0)
    if ($ok) { Add-Result '5 计划任务' '已完成' '已注册静默提权任务' }
    else { Add-Result '5 计划任务' '⚠' '--install-task 后仍未见任务' }
}
```

- [ ] **Step 2: 接入 Main（阶段 4 之后，Show-Summary 之前）**

`Main` 最终形态：
```powershell
function Main {
    Write-Step "SensorMonitor 引导开始（RepoRoot=$script:RepoRoot；CheckOnly=$CheckOnly；SkipInstall=$SkipInstall）"
    Invoke-Stage0Preflight
    Ensure-Elevated
    Invoke-Stage1Toolchain
    Invoke-Stage2DevMode
    Invoke-Stage3BuildTest
    Invoke-Stage4Deploy
    Invoke-Stage5ScheduledTask
    Add-FollowUp '装 PawnIO 并重启后，管理员运行 Host 或触发计划任务，CPU/主板传感器才齐全'
    Show-Summary
}
```

- [ ] **Step 3: 验证（本机 -CheckOnly 全流程）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1
```
Expected: 六阶段全跑完（0–5），总结表 6 行，各行状态合理（本机多为 `已就绪`，扩展部署为 `⚠ 未实测`）；"需人工收尾"列出 PawnIO 重启、CmdPal Reload、CPU 传感器提示；退出码 0，**未**触发 UAC。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.ps1 && git commit -m "feat(setup): stage 5 scheduled task and full pipeline wiring"
```

---

## Task 8: cmd 包装 + CLAUDE.md 收口

**Files:**
- Create: `scripts/setup.cmd`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 写 cmd 包装**

`scripts/setup.cmd`:

```bat
@echo off
rem 新机器双击/命令行即用：以 Bypass 执行 setup.ps1，透传全部参数。
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0setup.ps1" %*
```

- [ ] **Step 2: 验证包装透传**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && cmd //c "scripts\\setup.cmd -CheckOnly" 2>&1 | tail -12
```
Expected: 与直接跑 `setup.ps1 -CheckOnly` 同样的总结表（证明 `-CheckOnly` 透传成功）。

- [ ] **Step 3: CLAUDE.md 补常用命令**

`CLAUDE.md` 的「常用命令」代码块内，在 `dotnet test ...` 行之前加一行：

```bash
scripts\setup.cmd                 # 新机器一键引导（自提权，装工具链+构建+部署+计划任务）；-CheckOnly 只体检
```

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add scripts/setup.cmd CLAUDE.md && git commit -m "feat(setup): cmd wrapper and CLAUDE.md command reference"
```

---

## 真机验收（本会话之外，桌面会话执行）

CLI-only 部署的最终确认必须在**全新机器**上完成——本会话只验证了 `-CheckOnly` 与本机构建逻辑：

1. 全新机器 clone 仓库 → 双击 `scripts\setup.cmd` → 一次 UAC。
2. 核对总结表：阶段 1–3、5 应 `已完成/已就绪`；阶段 4（扩展部署）观察是走通还是 `⚠`——**这是 spec 里标注的未实测项**。
3. 阶段 4 若走通：记录确切的 `dotnet build` + `Add-AppxPackage -Register` 命令，回写 `CLAUDE.md` 坑 #1（把"必须 VS Deploy"更新为"CLI 亦可，命令如下"）。
4. 阶段 4 若失败：按总结的兜底提示补 WinAppSDK runtime 包或装 VS，并把实际报错补进 spec 的风险点 1。
5. 二次重跑 `scripts\setup.cmd`：所有阶段应 `已就绪`（幂等验证），无重复安装。

---

## Self-Review 结论

- **Spec 覆盖**：交付物三项（`setup.ps1`/`setup.cmd`/CLAUDE.md）→ Task 1–8 全覆盖；六阶段 → Task 2（0）/3（1）/4（2）/5（3）/6（4）/7（5+6）；两参数 `-CheckOnly`/`-SkipInstall` → Task 1 定义、各阶段函数分支实现；四决策（全套/纯 CLI/尽力而为/PawnIO 默认装）→ 分别落在 Task 3、Task 6、Task 6 catch、Task 3。无缺口。
- **占位符**：无 TBD/TODO；每个改代码的 Step 均含完整可粘贴代码。
- **类型/命名一致性**：`Add-Result`/`Add-FollowUp`/`Write-Step`/`Show-Summary`/`$script:Results`/`$script:FollowUps`/`$script:RepoRoot` 全脚本一致；阶段函数名 `Invoke-StageN*` 与 `Main` 调用一一对应；`Install-WingetPackage` 定义（Task 3）与调用（Task 3）签名一致。
