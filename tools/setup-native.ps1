param([switch]$SkipTests, [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$deps = Join-Path $repo '.deps'
$sdk = Join-Path $deps 'ffmpeg'
$lockPath = Join-Path $repo 'src/OpenRend.Native/ffmpeg.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$archive = Join-Path $deps $lock.archive
function Assert-ChildPath([string]$Path, [string]$Parent) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$absolute.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing path outside $Parent`: $absolute" }
    return $absolute
}
New-Item -ItemType Directory -Force -Path $deps | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Write-Host "Downloading pinned FFmpeg SDK $($lock.version)..."
    & curl.exe -L --fail --retry 3 -o $archive $lock.url
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed' }
}
$actualSha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $lock.sha256) { throw "FFmpeg archive checksum mismatch: expected $($lock.sha256), received $actualSha. The SDK was not installed." }
if (!(Test-Path -LiteralPath (Join-Path $sdk 'include/libavformat/avformat.h'))) {
    $extract = Join-Path $deps "ffmpeg-extract-$PID"
    Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
    $folders = @(Get-ChildItem -LiteralPath $extract -Directory)
    if ($folders.Count -ne 1) { throw 'FFmpeg archive did not contain one SDK directory' }
    $sourcePath = Assert-ChildPath (Resolve-Path -LiteralPath $folders[0].FullName).ProviderPath $extract
    $destinationPath = Assert-ChildPath $sdk $deps
    $null = Assert-ChildPath $deps $repo
    if (!(Test-Path -LiteralPath (Join-Path $sourcePath 'include/libavformat/avformat.h'))) { throw 'FFmpeg archive has no SDK headers' }
    if (Test-Path -LiteralPath $destinationPath) { throw "Incomplete SDK already exists at $destinationPath. Preserve or move it before installing the pinned SDK." }
    Move-Item -LiteralPath $sourcePath -Destination $destinationPath
}
$versionLine = & (Join-Path $sdk 'bin/ffmpeg.exe') -version | Select-Object -First 1
if ($versionLine -notlike "ffmpeg version $($lock.version) *") { throw "Installed FFmpeg differs from the pinned SDK: $versionLine" }
Copy-Item -LiteralPath $lockPath -Destination (Join-Path $sdk 'provenance.json') -Force
Copy-Item -LiteralPath $lockPath -Destination (Join-Path $deps 'ffmpeg-provenance.json') -Force
$fixture = Join-Path $repo 'tests/native/fixture.mp4'
$audio = Join-Path $repo 'tests/native/fixture.wav'
$image = Join-Path $repo 'tests/native/fixture.png'
$ffmpeg = Join-Path $sdk 'bin/ffmpeg.exe'
if (!(Test-Path -LiteralPath $fixture)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=320x180:rate=30:duration=3' -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=3' -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Native video fixture generation failed' }
}
if (!(Test-Path -LiteralPath $audio)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=880:sample_rate=44100:duration=2' -c:a pcm_s16le $audio
    if ($LASTEXITCODE -ne 0) { throw 'Native audio fixture generation failed' }
}
if (!(Test-Path -LiteralPath $image)) {
    & $ffmpeg -hide_banner -loglevel error -y -i $fixture -frames:v 1 $image
    if ($LASTEXITCODE -ne 0) { throw 'Native image fixture generation failed' }
}
$testFlag = if ($SkipTests) { 'OFF' } else { 'ON' }
& cmake -S $repo -B (Join-Path $repo 'build/native') -A x64 "-DOPENREND_BUILD_TESTS=$testFlag"
if ($LASTEXITCODE -ne 0) { throw 'Native CMake configuration failed' }
& cmake --build (Join-Path $repo 'build/native') --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native engine build failed' }
if (!$SkipTests) {
    & ctest --test-dir (Join-Path $repo 'build/native') -C $Configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native integration tests failed' }
}
Write-Host "Native runtime: $(Join-Path $repo "build/native/$Configuration")"
