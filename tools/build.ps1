param([switch]$SkipNative, [switch]$SkipTests, [switch]$Package)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repo '.local/dotnet'
if ((Test-Path $localDotnet) -and ($env:PATH -notlike "*$localDotnet*")) {
    $env:PATH = "$localDotnet;$env:PATH"
}
$vs18Cmake = 'D:\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin'
$cmakeBin = if (Test-Path $vs18Cmake) { $vs18Cmake } else { 'D:\VS\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin' }
if ((Test-Path $cmakeBin) -and ($env:PATH -notlike "*$cmakeBin*")) {
    $env:PATH = "$cmakeBin;$env:PATH"
}
Push-Location -LiteralPath $repo
try {
    if (!$SkipNative) {
        & (Join-Path $PSScriptRoot 'setup-native.ps1') -SkipTests:$SkipTests
    }
    & dotnet restore OpenRend.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Managed dependency restore failed' }
    & dotnet build OpenRend.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    if (!$SkipTests) {
        & dotnet test OpenRend.slnx -c Release --no-build --no-restore --filter 'Category!=AudioDevice'
        if ($LASTEXITCODE -ne 0) { throw 'Managed tests failed' }
    }
    if ($Package) { & (Join-Path $PSScriptRoot 'package.ps1') }
} finally { Pop-Location }
