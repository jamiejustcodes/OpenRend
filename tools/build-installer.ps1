param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$appDir = Join-Path $dist 'OpenRend'

# 1. Ensure the application is published if dist/OpenRend is missing or requested
if (!$SkipPublish -or !(Test-Path (Join-Path $appDir 'OpenRend.exe'))) {
    Write-Host "Publishing OpenRend application bundle..."
    & (Join-Path $PSScriptRoot 'package.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Application packaging failed' }
}

# 2. Locate Inno Setup 6 compiler (iscc.exe)
$isccPaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\iscc.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
    "$env:ProgramFiles\Inno Setup 6\iscc.exe"
)
$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) { $iscc = $path; break }
}
if (!$iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (!$iscc) {
    Write-Host "Inno Setup compiler not found. Attempting to install via winget..."
    & winget install --id JRSoftware.InnoSetup --silent --accept-package-agreements --accept-source-agreements
    foreach ($path in $isccPaths) {
        if (Test-Path $path) { $iscc = $path; break }
    }
    if (!$iscc) {
        throw 'Inno Setup compiler (iscc.exe) could not be located. Install Inno Setup 6 to compile the installer.'
    }
}

Write-Host "Using Inno Setup compiler: $iscc"

# 3. Compile installer script
$issScript = Join-Path $repo 'installer/OpenRend.iss'
Write-Host "Compiling $issScript with ultra LZMA2 solid compression..."
& $iscc $issScript
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }

# 4. Locate generated installer
$installer = Join-Path $dist 'OpenRend-Setup-v0.2.0.exe'
if (!(Test-Path $installer)) {
    $found = Get-ChildItem -Path $dist -Filter "OpenRend-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) { $installer = $found.FullName }
}

# Also create generic OpenRend-Setup.exe copy for stable direct download links
$genericSetup = Join-Path $dist 'OpenRend-Setup.exe'
Copy-Item -LiteralPath $installer -Destination $genericSetup -Force

# 5. Compute SHA256 checksums
$installerName = Split-Path -Leaf $installer
$hash = (Get-FileHash -Path $installer -Algorithm SHA256).Hash
$genericHash = (Get-FileHash -Path $genericSetup -Algorithm SHA256).Hash

$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
$existing = if (Test-Path $sumsPath) { Get-Content $sumsPath } else { @() }
$lines = $existing | Where-Object { $_ -notmatch 'OpenRend-Setup' }
$lines += "$hash *$installerName"
$lines += "$genericHash *OpenRend-Setup.exe"
Set-Content -Path $sumsPath -Value $lines

$sizeMB = [math]::Round((Get-Item $installer).Length / 1MB, 2)
Write-Host "=========================================="
Write-Host "OpenRend Installer successfully created!"
Write-Host "Output: $installer ($sizeMB MB)"
Write-Host "Generic link: $genericSetup"
Write-Host "SHA256: $hash"
Write-Host "=========================================="

