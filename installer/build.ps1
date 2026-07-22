# installer/build.ps1 —— 端到端出「签名安装器」：发布自包含 Host → 构建+签名扩展 MSIX
# → 导出证书 → ISCC 编译 → 签名安装器。用法：pwsh installer/build.ps1 -Arch x64
param(
    [ValidateSet('x64','arm64')] [string]$Arch = 'x64',
    [string]$Thumbprint,          # 不传则自动找 CN=SensorMonitor Dev
    [string]$Version = '0.0.2.0'
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
& dotnet publish "$root\src\SensorMonitor.Host\SensorMonitor.Host.csproj" -c Release -r $rid --self-contained true -o $hostOut
if ($LASTEXITCODE) { throw "Host publish 失败" }

# 2) 构建 + 签名扩展 MSIX（复用 A2 链路）
$appxDir = Join-Path $stage 'appx'
& dotnet build "$root\src\SensorMonitorExtension\SensorMonitorExtension\SensorMonitorExtension.csproj" `
    -c Release -p:Platform=$plat -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="$appxDir\\"
if ($LASTEXITCODE) { throw "MSIX build 失败" }
$msix = Get-ChildItem $appxDir -Recurse -Filter "SensorMonitorExtension_*_$Arch.msix" | Select-Object -First 1
if (-not $msix) { throw "未找到 .msix（检查 AppxPackageDir）" }

if (-not $Thumbprint) {
    $Thumbprint = (Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=SensorMonitor Dev' } | Select-Object -First 1).Thumbprint
    if (-not $Thumbprint) { throw "未找到 CN=SensorMonitor Dev 证书；见 docs/references/msix-packaging.md 生成" }
}
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\10.*\$Arch\signtool.exe" -ErrorAction Ignore |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "未找到 signtool（Windows Kits 10 bin\<ver>\$Arch）" }
& $signtool sign /fd SHA256 /sha1 $Thumbprint $msix.FullName
if ($LASTEXITCODE) { throw "MSIX 签名失败" }

# 3) 导出证书公钥（供安装器 certutil 信任）
$cer = Join-Path $stage 'SensorMonitorDev.cer'
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
    "/DMyCer=$cer" "/DMyCerName=SensorMonitorDev.cer" "$here\SensorMonitor.iss"
if ($LASTEXITCODE) { throw "ISCC 编译失败" }

# 5) 签名安装器
$setup = Join-Path $here "Output\SensorMonitorSetup_$Arch.exe"
& $signtool sign /fd SHA256 /sha1 $Thumbprint $setup
if ($LASTEXITCODE) { throw "安装器签名失败" }
Write-Host "OK → $setup"
