param([switch]$Settings, [switch]$Show, [switch]$Quit)
. (Join-Path $PSScriptRoot 'common.ps1')
if ($Quit) { Stop-ProjectApp; return }
$applicationPath = Get-AppExecutable
if (-not (Test-Path -LiteralPath $applicationPath)) { throw 'Build Promptlet first: .\scripts\build.ps1' }
$launchArguments = if ($Settings) { '--settings' } elseif ($Show) { '--show' } else { '--background' }
Start-Process -FilePath $applicationPath -ArgumentList $launchArguments -WorkingDirectory (Split-Path -Parent $applicationPath) -WindowStyle Hidden
Write-Host 'Promptlet is running in the system tray. Default shortcut: Ctrl + Alt + Space.'
