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

function Main {
    Write-Step "SensorMonitor 引导开始（RepoRoot=$script:RepoRoot；CheckOnly=$CheckOnly；SkipInstall=$SkipInstall）"
    Invoke-Stage0Preflight
    Ensure-Elevated
    Show-Summary
}

Main
