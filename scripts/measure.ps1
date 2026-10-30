param([ValidateRange(5, 120)][int]$Seconds = 60, [switch]$Published, [switch]$Installed)
. (Join-Path $PSScriptRoot 'common.ps1')
$applicationPath = if ($Published) { Join-Path $script:ProjectRoot 'artifacts\Promptlet-win-x64\Promptlet.exe' } else { Get-AppExecutable }
if ($Installed) {
    $installed = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet_is1' -ErrorAction SilentlyContinue).InstallLocation
    if (-not $installed) { $installed = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet').InstallLocation }
    $applicationPath = Join-Path $installed 'Promptlet.exe'
}
$applicationProcess = Get-Process -Name Promptlet -ErrorAction Stop | Where-Object { $_.Path -eq $applicationPath } | Select-Object -First 1
if (-not $applicationProcess) { throw 'Start Promptlet first.' }
$initialCpu = $applicationProcess.TotalProcessorTime.TotalSeconds
$timer = [Diagnostics.Stopwatch]::StartNew()
$samples = @()
for ($sampleIndex = 0; $sampleIndex -lt $Seconds; $sampleIndex++) {
    Start-Sleep -Seconds 1
    $applicationProcess.Refresh()
    if ($applicationProcess.HasExited) { throw 'Promptlet exited during measurement.' }
    $samples += [pscustomobject]@{
        WorkingSetMiB = [math]::Round($applicationProcess.WorkingSet64 / 1MB, 2)
        PrivateBytesMiB = [math]::Round($applicationProcess.PrivateMemorySize64 / 1MB, 2)
        Handles = $applicationProcess.HandleCount
    }
}
$timer.Stop()
$cpuPercent = ($applicationProcess.TotalProcessorTime.TotalSeconds - $initialCpu) / $timer.Elapsed.TotalSeconds / [Environment]::ProcessorCount * 100
[pscustomobject]@{
    ProcessId = $applicationProcess.Id
    DurationSeconds = [math]::Round($timer.Elapsed.TotalSeconds, 1)
    AverageCpuPercentOfMachine = [math]::Round($cpuPercent, 3)
    AverageWorkingSetMiB = [math]::Round(($samples | Measure-Object WorkingSetMiB -Average).Average, 2)
    AveragePrivateBytesMiB = [math]::Round(($samples | Measure-Object PrivateBytesMiB -Average).Average, 2)
    FinalHandles = $samples[-1].Handles
    Note = 'Working set and private bytes are different metrics; neither is private working set. Record window visibility separately.'
} | ConvertTo-Json
