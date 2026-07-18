# 新机器一键引导脚本 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一个自提权、幂等的 `scripts/setup.ps1`（+ `setup.cmd` 包装），把 SensorMonitor 在全新 Windows 机器上从零拉起到"能构建、11 单测绿、扩展已尝试部署、Host 计划任务已注册"。

**Architecture:** 单文件 PowerShell 脚本，结构为「预检 → 6 个幂等阶段函数 → 总结报告表」。每阶段是独立函数，先检测状态（`winget list` / 注册表 / `schtasks /Query` / `Get-AppxPackage`）已满足就跳过。脚本开头自提权（非管理员则 `Start-Process -Verb RunAs` 重启自身）。无自动化单测——脚本副作用是系统级安装，验证靠 `-CheckOnly` 本机核对 + 真机全流程 + 二次重跑验幂等。

**Tech Stack:** Windows PowerShell 5.1+（系统自带）、winget v1.29（已确认本机存在）、既有 .NET CLI。无新增代码依赖。

**Spec:** `docs/superpowers/specs/2026-07-18-fresh-machine-setup-script-design.md`

**已确认的四个决策：** 全套安装；不装 VS 纯 CLI 部署；阶段 4 尽力而为+报错不阻断；PawnIO 默认静默装。

**已核实事实（勿重新查证）：**
- winget 包 ID：`Microsoft.DotNet.Runtime.8`、`Microsoft.DotNet.SDK.9`、`Microsoft.PowerToys`、`namazso.PawnIO`（均已 `winget search` 确认存在）。.NET 8 只需**运行时**不需 SDK：构建统一由 SDK 9 完成（net8.0 targeting pack 走 NuGet restore），但 Host/Tests 运行需 `Microsoft.NETCore.App 8.0`（已核对 Host runtimeconfig.json；major 版本默认不 roll-forward，装 SDK 9 不覆盖）。
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
.PARAMETER Relaunched
  内部参数：提权重启的实例自带，用于结束前暂停以便阅读总结表。勿手动传。
#>
[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$SkipInstall,
    [switch]$Relaunched
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

# 原生命令安全执行。PS 5.1 实测：EAP=Stop 下 native 命令一旦写 stderr 且经 2> 重定向,
# 就抛 NativeCommandError(taskkill 杀不存在的进程、schtasks 查不存在的任务都会命中)。
# 统一经此包装:临时放宽 EAP、吞 stderr,以退出码判成败。
function Invoke-Native([scriptblock]$cmd) {
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $cmd 2>$null
        return [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Output = $out }
    }
    finally { $ErrorActionPreference = $eap }
}

# winget 刚装完 .NET SDK 时,本进程 PATH 还是启动时的快照,dotnet 找不到。
# 从注册表重读 Machine+User PATH 合并进当前会话。
function Update-SessionPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Show-Summary {
    Write-Host ''
    Write-Host '===== 总结 =====' -ForegroundColor Yellow
    $script:Results | Format-Table -AutoSize Stage, Status, Detail | Out-String | Write-Host
    if ($script:FollowUps.Count -gt 0) {
        Write-Host '需人工收尾：' -ForegroundColor Yellow
        foreach ($f in $script:FollowUps) { Write-Host "  - $f" }
    }
    # 提权重启的实例跑在临时新窗口里,脚本一结束窗口即关,总结表会一闪而过——暂停等确认。
    if ($Relaunched) { Read-Host '按 Enter 关闭窗口' | Out-Null }
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
    # -Relaunched：新窗口结束前暂停，否则总结表随窗口关闭一闪而过。
    $argList = @('-ExecutionPolicy', 'Bypass', '-NoProfile', '-File', "`"$PSCommandPath`"", '-Relaunched')
    if ($SkipInstall) { $argList += '-SkipInstall' }
    try {
        Start-Process powershell -Verb RunAs -ArgumentList $argList
    }
    catch {
        # 用户在 UAC 点了"否"：Start-Process 抛异常，友好退出而非裸报错。
        Write-Warning '已取消提权，未做任何更改。无管理员权限体检可用 -CheckOnly。'
        exit 1
    }
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
# 单个工具链组件：**功能性探测**（能力就位=已装，与安装渠道无关），缺失才 winget 装。
# 为什么不用 winget-ID 检测：非 winget 渠道装的组件（VS 带的 SDK、随其他软件装的
# 运行时）在 ARP 里的 ID 对不上 winget 包 ID，会被漏报成"缺失"而白白重装
# （本机实测：dotnet 功能齐全但 Runtime.8/SDK.9 两个 winget-ID 均探不到）。
# 返回 'ready'（本就就绪）/ 'installed'（本次装上）/ 'missing'（CheckOnly 缺）/ 'failed'。
function Install-ToolchainItem([hashtable]$item) {
    if ([bool](& $item.Probe)) {
        Write-Host "  $($item.Name) 已就绪（功能探测），跳过"
        return 'ready'
    }
    if ($CheckOnly) {
        Write-Host "  [CheckOnly] 缺 $($item.Name) → winget install -e --id $($item.Id)"
        return 'missing'
    }
    Write-Step "  安装 $($item.Name) …"
    winget install -e --id $item.Id --silent `
        --accept-package-agreements --accept-source-agreements
    # winget 退出码非 0 也可能是"已装/需重启"——以装后功能复探为准。
    # dotnet 类组件刚装完 PATH 还是旧快照，先刷新否则复探必假阴。
    Update-SessionPath
    if ([bool](& $item.Probe)) { return 'installed' } else { return 'failed' }
}

# 阶段 1：四个组件。PawnIO 装后单独提示可能需重启。
function Invoke-Stage1Toolchain {
    if ($SkipInstall) { Add-Result '1 工具链' '已就绪' '按 -SkipInstall 跳过'; return }
    Write-Step '阶段 1：工具链'
    $pkgs = @(
        # .NET 8 只装运行时：构建全由 SDK 9 承担（targeting pack 走 NuGet），
        # 但 net8.0 的 Host/Tests 运行需 8.0 运行时（major 不 roll-forward）。
        @{ Id = 'Microsoft.DotNet.Runtime.8'; Name = '.NET 8 运行时'; Probe = {
            (Get-Command dotnet -ErrorAction SilentlyContinue) -and
            ((Invoke-Native { dotnet --list-runtimes }).Output -match '^Microsoft\.NETCore\.App 8\.') } },
        @{ Id = 'Microsoft.DotNet.SDK.9'; Name = '.NET 9 SDK'; Probe = {
            (Get-Command dotnet -ErrorAction SilentlyContinue) -and
            ((Invoke-Native { dotnet --list-sdks }).Output -match '^9\.') } },
        @{ Id = 'Microsoft.PowerToys'; Name = 'PowerToys'; Probe = {
            (Test-Path "$env:ProgramFiles\PowerToys\PowerToys.exe") -or
            (Test-Path "$env:LOCALAPPDATA\PowerToys\PowerToys.exe") } },
        # PawnIO 装完的落点未实测过，三路信号任一命中即视为就位；若装后全部未命中
        # 会被复探判 ⚠ 未确认——届时按实际落点补探测项。
        @{ Id = 'namazso.PawnIO'; Name = 'PawnIO 驱动'; Probe = {
            (Get-Service PawnIO -ErrorAction SilentlyContinue) -or
            (Test-Path "$env:ProgramFiles\PawnIO") -or
            (Test-Path 'HKLM:\SOFTWARE\PawnIO') } }
    )
    $missing = @()
    $installedNow = @()
    foreach ($p in $pkgs) {
        switch (Install-ToolchainItem $p) {
            'installed' { $installedNow += $p.Name }
            'missing'   { $missing += $p.Name }
            'failed'    { $missing += $p.Name }
        }
    }
    if ($CheckOnly) {
        if ($missing.Count) { Add-Result '1 工具链' '⚠' "缺: $($missing -join ', ')" }
        else { Add-Result '1 工具链' '已就绪' '全部已装' }
        return
    }
    # SDK 刚装进系统 PATH，但本进程还是旧快照——立刻刷新，阶段 3 才找得到 dotnet。
    Update-SessionPath
    # 重启提示只在本次真装了 PawnIO 时给：已就绪跳过的机器不骚扰；且本类驱动
    # 多数免重启（本机实测装完即读到 CPU/主板温度），先验证再决定是否重启。
    if ($installedNow -contains 'PawnIO 驱动') {
        Add-FollowUp 'PawnIO 本次新装：若 Dock/浏览页缺 CPU/主板温度再重启（多数机型免重启，可先直接验证）'
    }
    if ($missing.Count) { Add-Result '1 工具链' '⚠' "未确认: $($missing -join ', ')" }
    else { Add-Result '1 工具链' '已完成' '四个组件就绪（功能探测）' }
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
Expected: 逐个打印四个组件"已就绪（功能探测），跳过"或"[CheckOnly] 缺 …"；总结表 `1 工具链` 行为 `已就绪` 或 `⚠ 缺: …`。**[仅新机器]** 无 `-CheckOnly` 全流程真正安装留待新机器验证。

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
    # -SkipInstall 时未经阶段 1 的 PATH 刷新；dotnet 找不到就再刷一次，仍没有则硬失败。
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Update-SessionPath
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            Add-Result '3 构建测试' '❌' 'dotnet 不在 PATH（SDK 刚装完可能需重开会话再跑）'
            Show-Summary
            exit 1
        }
    }
    # 停运行中的 Host，避免锁 bin（坑 #6）。新机上进程必然不存在，taskkill 会写 stderr——
    # 直接 2>$null 在 EAP=Stop 下必炸，须走 Invoke-Native。
    Invoke-Native { taskkill /f /im SensorMonitor.Host.exe } | Out-Null
    Push-Location $script:RepoRoot
    try {
        # 逐步校验退出码：restore 失败（如断网）若放任级联，最终会被误报成"test 失败"。
        # 注：sln 只含 Host + Tests（扩展在独立 sln，阶段 4 单独带 -p:Platform=x64 构建），
        # 整 sln 构建无需平台参数，已实测通过。
        foreach ($step in @(
            @{ Name = 'dotnet restore'; Cmd = { dotnet restore SensorMonitor.sln } },
            @{ Name = 'dotnet build';   Cmd = { dotnet build SensorMonitor.sln -c Debug } },
            @{ Name = 'dotnet test';    Cmd = { dotnet test tests/SensorMonitor.Host.Tests -c Debug } }
        )) {
            & $step.Cmd
            if ($LASTEXITCODE -ne 0) {
                Add-Result '3 构建测试' '❌' "$($step.Name) 退出码 $LASTEXITCODE"
                Show-Summary
                exit 1   # 构建/测试红是硬失败
            }
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
# CmdPal 外部重载：确保 AllowExternalReload=true 后触发 x-cmdpal://reload。返回 $true=已触发。
# 设置文件**不做 JSON 全量解析/回写**：PowerToys 会写入仅大小写不同的重复键
# （colorpicker/colorPicker.savedColors），PS 5.1 ConvertFrom-Json 遇到直接抛（真机实测）；
# 且 ConvertTo-Json 全量回写会丢重复键之一、破坏 PowerToys 设置——只做正则级微创读改。
function Invoke-CmdPalReload {
    $pkg = Get-AppxPackage Microsoft.CommandPalette -ErrorAction SilentlyContinue
    if (-not $pkg) {
        Add-FollowUp 'CmdPal(Microsoft.CommandPalette) 未安装，无法自动重载；装 PowerToys/CmdPal 后手动 Reload'
        return $false
    }
    $settings = Join-Path $env:LOCALAPPDATA `
        "Packages\$($pkg.PackageFamilyName)\LocalState\settings.json"
    if (-not (Test-Path $settings)) {
        # 尚无设置文件 = CmdPal 从未启动过。此时无需重载：首次启动本来就会全新枚举扩展。
        Add-FollowUp 'CmdPal 尚未启动过（无设置文件）；首次打开面板会自动发现新扩展，无需 Reload'
        return $false
    }
    $raw = Get-Content $settings -Raw
    if ($raw -notmatch '"AllowExternalReload"\s*:\s*true') {
        try {
            if ($raw -match '"AllowExternalReload"\s*:\s*false') {
                $patched = $raw -replace '"AllowExternalReload"(\s*:\s*)false', '"AllowExternalReload"$1true'
            } else {
                # 键不存在：插入最外层对象开头（根级键，本机核对过位置）。
                $patched = $raw -replace '^(\s*\{)', ('$1' + "`r`n" + '  "AllowExternalReload": true,')
            }
            Set-Content $settings -Value $patched -Encoding UTF8 -NoNewline
            # 运行中的 CmdPal 仍持旧值（退出时还可能用内存副本覆写设置文件）——
            # 结束它，让下面的协议激活带新设置重启（进程名本机已核对）。
            Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
        } catch {
            Add-FollowUp "改 CmdPal AllowExternalReload 失败：$($_.Exception.Message)"
            return $false
        }
    }
    # 触发重载（CmdPal 未运行时协议激活会拉起它，同样加载新扩展）。
    Start-Process 'x-cmdpal://reload' -ErrorAction SilentlyContinue
    return $true
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
        # 已注册的松散包被 CmdPal 激活后，扩展进程常驻并锁死自己的 bin（坑 #6 的扩展版，
        # 真机实测：MSB3027 文件被 SensorMonitorExtension 锁定）——构建前先杀，
        # CmdPal 会按需重拉，末尾 reload 也会重启它。
        Invoke-Native { taskkill /f /im SensorMonitorExtension.exe } | Out-Null
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
        $reloaded = Invoke-CmdPalReload
        $detail = if ($reloaded) { '已注册并触发重载' } else { '已注册（重载未触发，见收尾提示）' }
        Add-Result '4 扩展部署' '已完成' "${detail}：$($manifest.FullName)"
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
# 存在性检测走 Invoke-Native：`( cmd; expr )` 圆括号内放分号语句是语法错误（须 $()），
# 且 schtasks 查不到任务时写 stderr，在 EAP=Stop 下经 2> 重定向必炸（均 PS 5.1 实测）。
function Invoke-Stage5ScheduledTask {
    Write-Step '阶段 5：计划任务'
    if ((Invoke-Native { schtasks /Query /TN SensorMonitor.Host }).Ok) {
        Add-Result '5 计划任务' '已就绪' '任务 SensorMonitor.Host 已存在'; return
    }
    if ($CheckOnly) { Add-Result '5 计划任务' '⚠' '未注册（需 --install-task）'; return }
    $hostExe = Join-Path $script:RepoRoot `
        'src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe'
    if (-not (Test-Path $hostExe)) {
        Add-Result '5 计划任务' '⚠' "Host exe 不存在（阶段 3 未成功？）: $hostExe"
        return
    }
    # Host 是 WinExe（GUI 子系统）：`& $hostExe` 不等它退出就返回，立即复查必竞态——
    # 用 Start-Process -Wait 拿真实退出码。
    $p = Start-Process -FilePath $hostExe -ArgumentList '--install-task' -Wait -PassThru
    if ($p.ExitCode -eq 0 -and (Invoke-Native { schtasks /Query /TN SensorMonitor.Host }).Ok) {
        Add-Result '5 计划任务' '已完成' '已注册静默提权任务'
    }
    else { Add-Result '5 计划任务' '⚠' "--install-task 退出码 $($p.ExitCode)，或任务仍未注册" }
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
    Show-Summary
}
```

- [ ] **Step 3: 验证（本机 -CheckOnly 全流程）**

Run:
```bash
cd "D:/Workspace/SensorMonitor" && powershell -ExecutionPolicy Bypass -NoProfile -File scripts/setup.ps1 -CheckOnly 2>&1
```
Expected: 六阶段全跑完（0–5），总结表 6 行，各行状态合理（本机多为 `已就绪`，`4 扩展部署` 为 `已就绪 | CLI 部署已实测可行；CheckOnly 不执行`）；"需人工收尾"列出 PawnIO 提示；退出码 0，**未**触发 UAC、**未**出现"按 Enter 关闭窗口"暂停（仅 `-Relaunched` 实例暂停）。

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

- **Spec 覆盖**：交付物三项（`setup.ps1`/`setup.cmd`/CLAUDE.md）→ Task 1–8 全覆盖；六阶段 → Task 2（0）/3（1）/4（2）/5（3）/6（4）/7（5+6）；参数 `-CheckOnly`/`-SkipInstall`（及内部 `-Relaunched`）→ Task 1 定义、各阶段函数分支实现；四决策（全套/纯 CLI/尽力而为/PawnIO 默认装）→ 分别落在 Task 3、Task 6、Task 6 catch、Task 3。无缺口。
- **占位符**：无 TBD/TODO；每个改代码的 Step 均含完整可粘贴代码。
- **类型/命名一致性**：`Add-Result`/`Add-FollowUp`/`Write-Step`/`Show-Summary`/`Invoke-Native`/`Update-SessionPath`/`$script:Results`/`$script:FollowUps`/`$script:RepoRoot` 全脚本一致；阶段函数名 `Invoke-StageN*` 与 `Main` 调用一一对应；`Install-WingetPackage` 定义（Task 3）与调用（Task 3）签名一致。

---

## 复查修订记录（2026-07-18，本机 PS 5.1 实测后修订）

初版计划复查发现 8 处问题，已全部修入上方各 Task 代码。修复模式（`Invoke-Native`、`Add-Member -Force`、`Start-Process -Wait`）均已在本机 Windows PowerShell 5.1 验证可行。执行本计划时**勿回退**到旧写法：

| # | 问题 | 修复位置 | 证据 |
|---|------|---------|------|
| P1 | `( cmd; expr )` 圆括号内放分号语句是 PowerShell **语法错误**，整脚本无法解析 | Task 7 阶段 5 改用 `Invoke-Native` | 本机 ParserError 实测 |
| P2 | EAP=Stop 下 native 命令写 stderr 且经 `2>$null` 重定向 → 抛 NativeCommandError；`taskkill`（新机 Host 必不存在）与 `schtasks /Query`（任务不存在是新机正常路径）必命中 | Task 1 增 `Invoke-Native` 辅助；Task 5/7 的 taskkill/schtasks 改走它（Task 6 结束 CmdPal 用纯 PowerShell `Stop-Process`，不涉 stderr 问题） | 本机 `THREW: RemoteException` 实测 |
| P3 | winget 装完 SDK 后本进程 PATH 是旧快照，阶段 3 找不到 `dotnet`（新机核心场景必失败） | Task 1 增 `Update-SessionPath`；Task 3 装后调用；Task 5 兜底重查+硬失败 | PATH 快照机制 |
| P4 | Host 是 `WinExe`，`& $hostExe --install-task` 不等 GUI 进程退出，立即复查计划任务必竞态 | Task 7 改 `Start-Process -Wait -PassThru` 取真实退出码 | csproj `OutputType` 实查 |
| P5 | `$json.AllowExternalReload = $true` 在键不存在时抛异常被 catch 吞掉 → 重载被静默跳过 | Task 6 改 `Add-Member -NotePropertyName -Force`（属性名/根级位置已在本机 settings.json 核对） | 本机 THREW(assign) 实测 |
| P6 | CmdPal 运行中时改 settings.json 不生效（运行实例持旧值，退出时可能覆写回去） | Task 6：值确实变更时先 `Stop-Process Microsoft.CmdPal.UI` 再协议激活（进程名已实查） | 进程列表实查 |
| P7 | 新机 winget 首跑因源协议交互确认挂住（检测用的 `winget list` 未带同意参数） | Task 3 检测/复查均加 `--accept-source-agreements` 并走 `Invoke-Native` | winget 已知行为 |
| P8 | 提权重启的新窗口在脚本结束即关，总结表看不到；UAC 被拒时 `Start-Process -Verb RunAs` 抛未处理异常 | Task 1 增 `-Relaunched` 参数 + `Show-Summary` 末尾暂停；Task 2 透传参数 + try/catch 友好退出 | 行为推演 |

| P9 | 阶段 3 只查 `dotnet test` 退出码：restore 失败（断网/代理）级联后会被误报成"test 失败"，排障方向带偏 | Task 5 改为 restore/build/test 逐步校验退出码，失败即报对应步骤名 | 逻辑复查 |
| P14 | PawnIO"可能需重启"提示无条件输出：组件已就绪跳过时也骚扰；且本机实测装完**免重启**即读全 135 传感器 | Task 3 `Install-ToolchainItem` 改返回 ready/installed/missing/failed 四态，仅本次真装 PawnIO 时给条件化提示（文案改为"先验证再决定重启"）；Task 7 Main 的无条件 FollowUp 删除 | 本机装后免重启实测 |
| P13 | CmdPal settings.json 含**仅大小写不同的重复键**（PowerToys 写入的 colorpicker/colorPicker.savedColors），PS 5.1 `ConvertFrom-Json` 直接抛 → 重载被静默跳过且总结文案误报"已触发"；更深隐患：`ConvertTo-Json` 全量回写会丢重复键之一、破坏 PowerToys 设置 | Task 6 弃用 JSON 解析/回写，改**正则微创**三分支（已 true→免改直接 reload；false→只替换该键；缺失→根级插入）；`Invoke-CmdPalReload` 返回是否触发，阶段 4 文案如实反映 | 真机报错复现 + 三分支合成样例（含重复键）实测，产物经序列化器验证仍为合法 JSON |
| P12 | 阶段 4 扩展构建在真机实跑失败（MSB3027/MSB3021：`SensorMonitorExtension.exe` 被自身进程锁定）：已注册的松散包被 CmdPal 激活后扩展进程常驻，锁死自己的 bin 输出——**坑 #6 的扩展版**。spike 当时能过是因为扩展进程尚未被拉起 | Task 6 构建前先 `taskkill SensorMonitorExtension.exe`（CmdPal 按需重拉，末尾 reload 亦会重启）；修复序列已真机验证（杀→构建 0 错→注册 Ok→reload） | 真机阶段 4 失败复现 + 修复后实跑 |
| P11 | 阶段 1 用 winget-ID 检测已装状态会**漏报非 winget 渠道的安装**：本机实测 dotnet 功能齐全（runtime 8 + SDK 9 都在），但 `winget list --id Microsoft.DotNet.Runtime.8 / SDK.9` 均探不到 → 会白白重装。检测与安装解耦：**检测走功能性探测**（dotnet 用 `--list-runtimes/--list-sdks`、PowerToys 查 exe 落点、PawnIO 查服务/目录/注册表三路信号），winget 仅作安装通道；装后以功能复探判定成败（复探前刷 PATH 防假阴） | Task 3 `Install-WingetPackage` 重构为 `Install-ToolchainItem`（组件表带 Probe scriptblock） | 本机 `-CheckOnly` 实跑对照：功能探测 4 项全判对，winget-ID 误判 2 项 |
| P10 | 装 `Microsoft.DotNet.SDK.8` 过重：构建可全由 SDK 9 承担（net8.0 targeting pack 走 NuGet），真正缺的只是 8.0 **运行时**（Host runtimeconfig 要求 `Microsoft.NETCore.App 8.0`，major 不 roll-forward）。注意"本机无 SDK 8 也能跑测试"是因为本机恰好装有 8.0.22/8.0.29 运行时，**不是** roll-forward——全新机器只装 SDK 9 会在运行环节失败 | Task 3 包列表 `Microsoft.DotNet.SDK.8` → `Microsoft.DotNet.Runtime.8`（winget ID 已确认，约省 170MB） | 本机 `--list-sdks/--list-runtimes` + runtimeconfig.json 实查 |

另两点核查结论：
- CmdPal 从未启动过（无设置文件）时无需重载——首次启动本会全新枚举扩展，Task 6 已按此调整 FollowUp 文案并跳过协议激活。
- 阶段 3 整 sln 构建**无平台映射问题**：`SensorMonitor.sln` 只含 Host + Tests（均 AnyCPU；扩展项目在独立 sln，阶段 4 单独带 `-p:Platform=x64` 构建），且 `dotnet build SensorMonitor.sln -c Debug` 已在本机实测通过（0 错误）。
