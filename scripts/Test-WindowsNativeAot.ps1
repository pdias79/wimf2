param(
    [string] $OutputDirectory = "artifacts/windows-aot-experiment"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$target = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$serverOutput = Join-Path $target "server"
$logPath = Join-Path $target "publish.log"
New-Item -ItemType Directory -Force $serverOutput | Out-Null

# Probe only: AOT is not enabled in the production project.
# Preserve warnings and exit status for evaluation without affecting the stable JIT bundle.
$arguments = @(
    "publish", (Join-Path $root "src/Wimf.Server/Wimf.Server.csproj"),
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishAot=true", "-p:StripSymbols=true",
    "-p:DebugType=None", "-p:DebugSymbols=false",
    "-o", $serverOutput
)
& dotnet @arguments 2>&1 | Tee-Object -FilePath $logPath
$exitCode = $LASTEXITCODE
$binary = Join-Path $serverOutput "Wimf.Server.exe"
if ($exitCode -eq 0 -and (Test-Path $binary)) {
    $size = (Get-Item $binary).Length
    "result=success`nserverBytes=$size" | Set-Content (Join-Path $target "result.txt")
    Write-Host "AOT server compilation succeeded, size=$size bytes"

    # Keep stable build unchanged: package the native server inside a separate tray EXE.
    $trayOutput = Join-Path $target "tray"
    New-Item -ItemType Directory -Force $trayOutput | Out-Null
    & dotnet publish (Join-Path $root "src/Wimf.Tray/Wimf.Tray.csproj") -c Release -r win-x64 --self-contained true "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" "-p:DebugType=None" "-p:DebugSymbols=false" "-p:EmbeddedServerExecutable=$binary" -o $trayOutput 2>&1 | Tee-Object -FilePath $logPath -Append
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $trayOutput "Wimf.Tray.exe"))) {
        throw "Native AOT trial launcher publication failed."
    }
    $trial = Join-Path $target "Wimf-AOT.exe"
    Copy-Item (Join-Path $trayOutput "Wimf.Tray.exe") $trial -Force
    $trialBytes = (Get-Item $trial).Length
    Add-Content (Join-Path $target "result.txt") "packageBytes=$trialBytes"
    Write-Host "Native AOT trial package: $trial ($trialBytes bytes)"
} else {
    "result=failed`nexitCode=$exitCode" | Set-Content (Join-Path $target "result.txt")
    Write-Host "AOT server compilation failed: inspect publish.log (exit=$exitCode)"
}
# Deliberately do not fail the regular Windows CI: report AOT feasibility separately.
