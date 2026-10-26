param(
    [string] $OutputDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$staging = Join-Path $root "artifacts/.windows-single-exe-build"
$serverOutput = Join-Path $staging "server"
$trayOutput = Join-Path $staging "tray"
$target = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Force $serverOutput, $trayOutput, $target | Out-Null

dotnet publish (Join-Path $root "src/Wimf.Server/Wimf.Server.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw "Server publication failed." }
$server = Join-Path $serverOutput "Wimf.Server.exe"
if (-not (Test-Path $server)) { throw "Missing self-contained server executable." }

dotnet publish (Join-Path $root "src/Wimf.Tray/Wimf.Tray.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:EmbeddedServerExecutable=$server" -o $trayOutput
if ($LASTEXITCODE -ne 0) { throw "Tray publication failed." }
$tray = Join-Path $trayOutput "Wimf.Tray.exe"
if (-not (Test-Path $tray)) { throw "Missing tray executable." }
Copy-Item $tray (Join-Path $target "Wimf.exe") -Force
Write-Host "Single distributable executable: $(Join-Path $target 'Wimf.exe')"
