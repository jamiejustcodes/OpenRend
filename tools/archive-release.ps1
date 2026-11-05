$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$zipPath = Join-Path $dist 'OpenRend-win-x64.zip'
$sumsPath = Join-Path $dist 'SHA256SUMS.txt'

Write-Host "Compressing $dist/OpenRend to $zipPath..."
Compress-Archive -Path "$dist/OpenRend/*" -DestinationPath $zipPath -Force

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
Set-Content -Path $sumsPath -Value "$hash *OpenRend-win-x64.zip"
Write-Host "Archive created successfully."
Write-Host "SHA256: $hash"
