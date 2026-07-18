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

# ---- 阶段函数 ----

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

# 单个 winget 包：已装则跳过，否则静默安装。返回 $true=已就绪/已装成功。
# 检测也走 Invoke-Native 并带 --accept-source-agreements：新机 winget 首跑会因
# 源协议交互确认挂住；stderr 输出在 EAP=Stop 下会炸（见 Invoke-Native 注释）。
function Install-WingetPackage([string]$id, [string]$display) {
    $probe = Invoke-Native { winget list --id $id -e --accept-source-agreements }
    if ($probe.Output | Select-String -SimpleMatch $id) {
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
    $recheck = Invoke-Native { winget list --id $id -e --accept-source-agreements }
    return [bool]($recheck.Output | Select-String -SimpleMatch $id)
}

# 阶段 1：四个包。PawnIO 装后单独提示可能需重启。
function Invoke-Stage1Toolchain {
    if ($SkipInstall) { Add-Result '1 工具链' '已就绪' '按 -SkipInstall 跳过'; return }
    Write-Step '阶段 1：工具链'
    $pkgs = @(
        # .NET 8 只装运行时：构建全由 SDK 9 承担（targeting pack 走 NuGet），
        # 但 net8.0 的 Host/Tests 运行需 8.0 运行时（major 不 roll-forward）。
        @{ id = 'Microsoft.DotNet.Runtime.8'; name = '.NET 8 运行时' },
        @{ id = 'Microsoft.DotNet.SDK.9';     name = '.NET 9 SDK' },
        @{ id = 'Microsoft.PowerToys';        name = 'PowerToys' },
        @{ id = 'namazso.PawnIO';             name = 'PawnIO 驱动' }
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
    # SDK 刚装进系统 PATH，但本进程还是旧快照——立刻刷新，阶段 3 才找得到 dotnet。
    Update-SessionPath
    Add-FollowUp 'PawnIO 内核驱动装后可能需重启才生效（CPU/主板温度依赖它）'
    if ($missing.Count) { Add-Result '1 工具链' '⚠' "未确认: $($missing -join ', ')" }
    else { Add-Result '1 工具链' '已完成' '四个包就绪' }
}

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
    $reloadReady = $true
    if (Test-Path $settings) {
        try {
            $json = Get-Content $settings -Raw | ConvertFrom-Json
            if ($json.AllowExternalReload -ne $true) {
                # 键不存在时 `$json.X = ...` 直接抛"property cannot be found"（PS 5.1 实测），
                # 须 Add-Member -Force（属性名为根级 AllowExternalReload，本机已核对）。
                $json | Add-Member -NotePropertyName AllowExternalReload -NotePropertyValue $true -Force
                ($json | ConvertTo-Json -Depth 100) | Set-Content $settings -Encoding UTF8
                # 运行中的 CmdPal 仍持旧值（退出时还可能用内存副本覆写设置文件）——
                # 结束它，让下面的协议激活带新设置重启（进程名本机已核对）。
                Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue |
                    Stop-Process -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Add-FollowUp "改 CmdPal AllowExternalReload 失败：$($_.Exception.Message)"
            $reloadReady = $false
        }
    } else {
        # 尚无设置文件 = CmdPal 从未启动过。此时无需重载：首次启动本来就会全新枚举扩展。
        Add-FollowUp 'CmdPal 尚未启动过（无设置文件）；首次打开面板会自动发现新扩展，无需 Reload'
        $reloadReady = $false
    }
    if ($reloadReady) {
        # 触发重载（CmdPal 未运行时协议激活会拉起它，同样加载新扩展）。
        Start-Process 'x-cmdpal://reload' -ErrorAction SilentlyContinue
    }
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

function Main {
    Write-Step "SensorMonitor 引导开始（RepoRoot=$script:RepoRoot；CheckOnly=$CheckOnly；SkipInstall=$SkipInstall）"
    Invoke-Stage0Preflight
    Ensure-Elevated
    Invoke-Stage1Toolchain
    Invoke-Stage2DevMode
    Invoke-Stage3BuildTest
    Invoke-Stage4Deploy
    Show-Summary
}

Main
