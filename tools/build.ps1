param([switch]$SkipNative, [switch]$SkipTests, [switch]$Package)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
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
