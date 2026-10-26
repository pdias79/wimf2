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
} else {
    "result=failed`nexitCode=$exitCode" | Set-Content (Join-Path $target "result.txt")
    Write-Host "AOT server compilation failed: inspect publish.log (exit=$exitCode)"
}
# Deliberately do not fail the regular Windows CI: report AOT feasibility separately.
