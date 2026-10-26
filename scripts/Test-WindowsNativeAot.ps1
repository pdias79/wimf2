param(
    [string] $OutputDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$target = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$stage = Join-Path $root "artifacts/.windows-native-build"
$serverOutput = Join-Path $stage "server"
New-Item -ItemType Directory -Force $target, $serverOutput | Out-Null

# Mandatory native Windows package: AOT server + C++ tray in one distributable EXE.
dotnet publish (Join-Path $root "src/Wimf.Server/Wimf.Server.csproj") -c Release -r win-x64 --self-contained true "-p:PublishAot=true" "-p:StripSymbols=true" "-p:DebugType=None" "-p:DebugSymbols=false" -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw "Native AOT server publication failed" }
$server = Join-Path $serverOutput "Wimf.Server.exe"
if (-not (Test-Path $server)) { throw "Native AOT server executable missing" }

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue) -or -not (Get-Command rc.exe -ErrorAction SilentlyContinue)) {
    throw "MSVC and Windows resource compiler are required"
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "src/Wimf.Tray/GenerateIcon.ps1") -Output (Join-Path $serverOutput "wimf.ico")
if ($LASTEXITCODE -ne 0) { throw "Native icon generation failed" }

$resourceOutput = Join-Path $stage "server.res"
$source = Join-Path $root "src/Wimf.NativeTray/main.cpp"
$resource = Join-Path $root "src/Wimf.NativeTray/server.rc"
$built = Join-Path $stage "Wimf.exe"
Push-Location $serverOutput
try {
    rc.exe /nologo "/fo$resourceOutput" $resource
    if ($LASTEXITCODE -ne 0) { throw "Native resource compilation failed" }
}
finally { Pop-Location }
cl.exe /nologo /std:c++17 /utf-8 /EHsc /O2 $source $resourceOutput "/Fe:$built" /link /SUBSYSTEM:WINDOWS
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $built)) { throw "Native C++ launcher compilation failed" }

Copy-Item $built (Join-Path $target "Wimf.exe") -Force
$size = (Get-Item (Join-Path $target "Wimf.exe")).Length
Write-Host "Windows native single-file executable: $size bytes"
