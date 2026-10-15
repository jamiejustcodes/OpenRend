param([switch]$SkipTests, [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$deps = Join-Path $repo '.deps'
$sdk = Join-Path $deps 'ffmpeg'
New-Item -ItemType Directory -Force -Path $deps | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $sdk 'include/libavformat/avformat.h'))) {
    $archive = Join-Path $deps 'ffmpeg.zip'
    $extract = Join-Path $deps 'ffmpeg-extract'
    Write-Host 'Downloading shared FFmpeg SDK from BtbN/FFmpeg-Builds...'
    & curl.exe -L --fail --retry 3 -o $archive 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed' }
    Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
    $folder = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
    Move-Item -LiteralPath $folder.FullName -Destination $sdk
}
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
