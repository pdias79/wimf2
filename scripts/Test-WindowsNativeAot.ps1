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

    # Second alternative: tiny Win32 C++ tray, linking the same AOT server as RCDATA.
    # Runs from the server directory so rc.exe can locate Wimf.Server.exe.
    $nativeSource = Join-Path $root "src/Wimf.NativeTray/main.cpp"
    $nativeResource = Join-Path $root "src/Wimf.NativeTray/server.rc"
    $resourceOutput = Join-Path $target "server.res"
    $nativeOutput = Join-Path $target "Wimf-Native.exe"
    if ((Get-Command rc.exe -ErrorAction SilentlyContinue) -and (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        Push-Location $serverOutput
        try {
            & rc.exe /nologo "/fo$resourceOutput" $nativeResource 2>&1 | Tee-Object -FilePath $logPath -Append
            if ($LASTEXITCODE -ne 0) { throw "Native launcher resource compilation failed" }
        }
        finally { Pop-Location }
        & cl.exe /nologo /std:c++17 /EHsc /O2 /DUNICODE /D_UNICODE $nativeSource $resourceOutput "/Fe:$nativeOutput" /link /SUBSYSTEM:WINDOWS 2>&1 | Tee-Object -FilePath $logPath -Append
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $nativeOutput)) { throw "Native launcher C++ compilation failed" }
        $nativeBytes = (Get-Item $nativeOutput).Length
        Add-Content (Join-Path $target "result.txt") "nativePackageBytes=$nativeBytes"
        Write-Host "Native C++ launcher package: $nativeOutput ($nativeBytes bytes)"
    } else {
        Add-Content (Join-Path $target "result.txt") "nativeLauncher=compiler-unavailable"
        Write-Host "MSVC compiler not available: native launcher skipped."
    }
} else {
    "result=failed`nexitCode=$exitCode" | Set-Content (Join-Path $target "result.txt")
    Write-Host "AOT server compilation failed: inspect publish.log (exit=$exitCode)"
}
# Deliberately do not fail the regular Windows CI: report AOT feasibility separately.
