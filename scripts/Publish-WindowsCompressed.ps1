param()
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stage = Join-Path $root "artifacts/.windows-native-build"
$serverDir = Join-Path $stage "server"
$server = Join-Path $serverDir "Wimf.Server.exe"
$target = Join-Path $root "artifacts/windows-compressed"
New-Item -ItemType Directory -Force $target | Out-Null
if (-not (Test-Path $server)) { throw "Build normal native version first" }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot "Compress-NativeServer.ps1") -InputFile $server -OutputFile (Join-Path $serverDir "Wimf.Server.xpress")
if ($LASTEXITCODE -ne 0) { throw "Compression failed" }
$resource = Join-Path $root "src/Wimf.NativeTray/server.rc"
$source = Join-Path $root "src/Wimf.NativeTray/main.cpp"
$res = Join-Path $stage "compressed.res"
$exe = Join-Path $target "Wimf.exe"
Push-Location $serverDir
try {
    rc.exe /nologo /d WIMF_COMPRESSED_SERVER "/fo$res" $resource
    if ($LASTEXITCODE -ne 0) { throw "Compressed resource compilation failed" }
}
finally { Pop-Location }
cl.exe /nologo /std:c++17 /utf-8 /EHsc /O2 /D WIMF_COMPRESSED_SERVER $source $res "/Fe:$exe" /link /SUBSYSTEM:WINDOWS
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "Compressed native linker failed" }
$normalBytes = (Get-Item (Join-Path $root "artifacts/windows/Wimf.exe")).Length
$compressedBytes = (Get-Item $exe).Length
Write-Host "Original package: $normalBytes bytes; compressed package: $compressedBytes bytes; savings: $($normalBytes - $compressedBytes) bytes"
@"
originalBytes=$normalBytes
compressedBytes=$compressedBytes
savingsBytes=$($normalBytes - $compressedBytes)
"@ | Set-Content (Join-Path $target "sizes.txt")
