# installer/build.ps1 — end-to-end: produce a signed installer: publish self-contained Host ->
# build + sign the extension MSIX -> export cert -> compile with ISCC -> sign the installer.
# Usage: pwsh installer/build.ps1 -Arch x64
param(
    [ValidateSet('x64','arm64')] [string]$Arch = 'x64',
    [string]$Thumbprint,          # if omitted, auto-detect CN=SysPulse Dev
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

# 1) Publish self-contained Host
$hostOut = Join-Path $stage 'Host'
& dotnet publish "$root\src\SysPulse.Host\SysPulse.Host.csproj" -c Release -r $rid --self-contained true -o $hostOut
if ($LASTEXITCODE) { throw "Host publish failed" }

# 2) Build + sign the extension MSIX (reuses the A2 pipeline)
$appxDir = Join-Path $stage 'appx'
& dotnet build "$root\src\SysPulseExtension\SysPulseExtension\SysPulseExtension.csproj" `
    -c Release -p:Platform=$plat -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:AppxPackageDir="$appxDir\\"
if ($LASTEXITCODE) { throw "MSIX build failed" }
$msix = Get-ChildItem $appxDir -Recurse -Filter "SysPulseExtension_*_$Arch.msix" | Select-Object -First 1
if (-not $msix) { throw "No .msix found (check AppxPackageDir)" }

if (-not $Thumbprint) {
    $Thumbprint = (Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=SysPulse Dev' } | Select-Object -First 1).Thumbprint
    if (-not $Thumbprint) { throw "CN=SysPulse Dev certificate not found; see docs/references/msix-packaging.md to create one" }
}
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\10.*\$Arch\signtool.exe" -ErrorAction Ignore |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool not found (Windows Kits 10 bin\<ver>\$Arch)" }
& $signtool sign /fd SHA256 /sha1 $Thumbprint $msix.FullName
if ($LASTEXITCODE) { throw "MSIX signing failed" }

# 3) Export the certificate public key (for the installer's certutil trust step)
$cer = Join-Path $stage 'SysPulseDev.cer'
Export-Certificate -Cert "Cert:\CurrentUser\My\$Thumbprint" -FilePath $cer | Out-Null

# 4) Compile the installer with ISCC (probe the winget per-user path + the classic machine-wide paths)
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found; winget install JRSoftware.InnoSetup" }
& $iscc "/DMyArch=$Arch" "/DMyVersion=$Version" "/DMyHostDir=$hostOut" `
    "/DMyMsix=$($msix.FullName)" "/DMyMsixName=$($msix.Name)" `
    "/DMyCer=$cer" "/DMyCerName=SysPulseDev.cer" "$here\SysPulse.iss"
if ($LASTEXITCODE) { throw "ISCC compilation failed" }

# 5) Sign the installer
$setup = Join-Path $here "Output\SysPulseSetup_$Arch.exe"
& $signtool sign /fd SHA256 /sha1 $Thumbprint $setup
if ($LASTEXITCODE) { throw "Installer signing failed" }
Write-Host "OK -> $setup"
