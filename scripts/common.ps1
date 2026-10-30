$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:DotnetExecutable = Join-Path $script:ProjectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $script:DotnetExecutable)) {
    $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installedDotnet) { throw 'Run .\scripts\setup.ps1 first to install the local .NET SDK.' }
    $script:DotnetExecutable = $installedDotnet.Source
}
$env:DOTNET_ROOT = Split-Path -Parent $script:DotnetExecutable
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Invoke-ProjectDotnet {
    & $script:DotnetExecutable @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE)." }
}

function Get-AppExecutable([string]$Configuration = 'Release') {
    return Join-Path $script:ProjectRoot "src\Promptlet\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\Promptlet.exe"
}

function Stop-ProjectApp {
    # Graceful IPC shutdown releases the global shortcut and removes the tray icon.
    $applicationPath = Get-AppExecutable
    $publishedPath = Join-Path $script:ProjectRoot 'artifacts\Promptlet-win-x64\Promptlet.exe'
    $installed = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet' -ErrorAction SilentlyContinue).InstallLocation
    $installedPath = if ($installed) { Join-Path $installed 'Promptlet.exe' } else { '' }
    $applicationProcesses = @(Get-Process -Name Promptlet -ErrorAction SilentlyContinue | Where-Object { $_.Path -in @($applicationPath, $publishedPath, $installedPath) })
    if ($applicationProcesses.Count -eq 0) { return }
    $signal = $null
    if ([System.Threading.EventWaitHandle]::TryOpenExisting('Local\Promptlet.Desktop.Quit', [ref]$signal)) {
        try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
        foreach ($applicationProcess in $applicationProcesses) {
            if (-not $applicationProcess.WaitForExit(10000)) { throw 'Promptlet did not exit. Please exit it from the tray and retry.' }
        }
    }
    else { throw 'Promptlet is running without an IPC endpoint. Please exit it before building.' }
}
