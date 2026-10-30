$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($PSScriptRoot)
$productRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Promptlet'))
$marker = Join-Path $target 'installed.json'
if (-not $target.StartsWith($productRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $marker)) { throw 'Unsafe uninstall path.' }
$manifest = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
if ($manifest.AppId -ne 'Promptlet' -or (Split-Path $target -Leaf) -ne $manifest.Version) { throw 'Invalid installation marker.' }
$exe = Join-Path $target 'Promptlet.exe'
$running = @(Get-Process -Name Promptlet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running.Count -gt 0) {
    $signal = [Threading.EventWaitHandle]::OpenExisting('Local\Promptlet.Desktop.Quit')
    try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
    foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw 'Promptlet did not exit.' } }
}
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet'
if ((Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue).InstallLocation -eq $target) {
    Remove-Item -LiteralPath $registryPath
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Promptlet.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
}
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = (Get-ItemProperty -Path $runPath -Name Promptlet -ErrorAction SilentlyContinue).Promptlet
if ($startup -and $startup.Contains($exe)) { Remove-ItemProperty -Path $runPath -Name Promptlet }
# Resolved target is a version directory under the named product root, verified above.
Remove-Item -LiteralPath $target -Recurse -Force
Write-Host 'Promptlet uninstalled. User settings and encrypted keys were preserved.'
