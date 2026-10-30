param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'release.json') | ConvertFrom-Json
if ($manifest.AppId -ne 'Promptlet' -or $manifest.Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release manifest.' }
if ([Environment]::OSVersion.Version.Build -lt 22000 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'Windows 11 x64 is required.' }
$source = Join-Path $PSScriptRoot 'App'
$target = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs\Promptlet\$($manifest.Version)"
$running = @(Get-Process -Name Promptlet -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $signal = $null
    if (-not [Threading.EventWaitHandle]::TryOpenExisting('Local\Promptlet.Desktop.Quit', [ref]$signal)) { throw 'Please exit Promptlet first.' }
    try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
    foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw 'Promptlet did not exit.' } }
}
$hashes = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'files.sha256.json') | ConvertFrom-Json
foreach ($file in $hashes) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $source $file.Path))
    if (-not $filePath.StartsWith([IO.Path]::GetFullPath($source) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path.' }
    if ((Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $file.Hash) { throw "Checksum failed: $($file.Path)" }
}
if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath (Join-Path $target 'installed.json'))) { throw 'Target exists without an installation marker; refusing to overwrite.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination (Join-Path $target 'Uninstall.ps1') -Force
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'installed.json') -Encoding UTF8
$executable = Join-Path $target 'Promptlet.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Promptlet.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath); $shortcut.TargetPath = $executable; $shortcut.WorkingDirectory = $target; $shortcut.IconLocation = "$executable,0"; $shortcut.Save()
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet'
New-Item -Path $registryPath -Force | Out-Null
$values = @{ DisplayName='Promptlet'; DisplayVersion=$manifest.Version; Publisher='Promptlet'; InstallLocation=$target; DisplayIcon=$executable;
    UninstallString="powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $target 'Uninstall.ps1')`"" }
foreach ($entry in $values.GetEnumerator()) { New-ItemProperty -Path $registryPath -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null }
New-ItemProperty -Path $registryPath -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $registryPath -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
# Keep an already-enabled startup registration pointing at the newly installed version.
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startPreference = $false
$settingsPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Promptlet\settings.json'
if (Test-Path -LiteralPath $settingsPath) {
    try { $startPreference = [bool]((Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json).Settings.StartAtLogin) } catch { }
}
if ($startPreference -or (Get-ItemProperty -Path $runPath -Name Promptlet -ErrorAction SilentlyContinue).Promptlet) {
    New-Item -Path $runPath -Force | Out-Null
    Set-ItemProperty -Path $runPath -Name Promptlet -Value "`"$executable`" --background"
}
Write-Host "Installed: $executable"
if (-not $NoLaunch) { Start-Process -FilePath $executable -ArgumentList '--settings' -WorkingDirectory $target }
