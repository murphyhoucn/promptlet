param()
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -Raw -LiteralPath (Join-Path $projectDirectory 'global.json') | ConvertFrom-Json).sdk.version
$sdkDirectory = Join-Path $projectDirectory '.tools\dotnet'
$dotnetExecutable = Join-Path $sdkDirectory 'dotnet.exe'
if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory "sdk\$sdkVersion"))) {
    $toolDirectory = Join-Path $projectDirectory '.tools'
    New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null
    $installerPath = Join-Path $toolDirectory 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerPath
    & $installerPath -Version $sdkVersion -Architecture x64 -InstallDir $sdkDirectory -NoPath
    if (-not (Test-Path -LiteralPath $dotnetExecutable)) { throw 'The local SDK installation did not complete.' }
}
$env:DOTNET_ROOT = $sdkDirectory
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetExecutable --version
if ($LASTEXITCODE -ne 0) { throw 'The SDK is not available.' }
Write-Host 'Local SDK ready. Run .\scripts\build.ps1, then .\scripts\run.ps1.'
