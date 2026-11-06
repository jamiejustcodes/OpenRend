param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repo '.local/dotnet'
if ((Test-Path $localDotnet) -and ($env:PATH -notlike "*$localDotnet*")) {
    $env:PATH = "$localDotnet;$env:PATH"
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist/OpenRend' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$workspace = [IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
if (!$destination.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) { throw 'The development bundle must stay inside the workspace' }

# Publish application bundle
& dotnet publish (Join-Path $repo 'src/OpenRend.App/OpenRend.App.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }

# Prune non-English satellite resource directories (reduces file count and size)
Get-ChildItem -Path $destination -Directory | Where-Object { $_.Name -match '^(cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hans|zh-Hant)$' } | Remove-Item -Recurse -Force

# Prune diagnostic/debuggers and unused WindowsForms / VisualBasic / Data assemblies
$prunePatterns = @(
    'createdump.exe',
    'mscordbi.dll',
    'mscordaccore*.dll',
    'Microsoft.DiaSymReader.Native*.dll',
    'System.Windows.Forms*.dll',
    'Microsoft.VisualBasic*.dll',
    'System.Data.*.dll',
    'System.DirectoryServices*.dll',
    '*.pdb'
)
foreach ($pattern in $prunePatterns) {
    Get-ChildItem -Path $destination -Filter $pattern -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

$native = Join-Path $repo 'build/native/Release/OpenRend.Native.dll'
Copy-Item -LiteralPath $native -Destination $destination -Force
Get-ChildItem -LiteralPath (Join-Path $repo '.deps/ffmpeg/bin') -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -in @('ffmpeg.exe', 'ffprobe.exe') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }
foreach ($name in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $destination -Force }
$notices = Join-Path $destination 'licenses/ffmpeg'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo '.deps/ffmpeg/LICENSE.txt') -Destination $notices -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/OpenRend.Native/ffmpeg.lock.json') -Destination $notices -Force
Write-Host "Local development bundle: $(Join-Path $destination 'OpenRend.exe')"
