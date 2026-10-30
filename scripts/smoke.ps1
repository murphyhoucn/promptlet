param([string]$Executable)
. (Join-Path $PSScriptRoot 'common.ps1')
Stop-ProjectApp
if (-not $Executable) { $Executable = Get-AppExecutable }
$reportDirectory = Join-Path $script:ProjectRoot ('artifacts\smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$previousData = $env:PROMPTLET_DATA_DIR
$previousReport = $env:PROMPTLET_SMOKE_REPORT
$previousFrame = $env:PROMPTLET_FRAME_DIAGNOSTICS
try {
    $env:PROMPTLET_DATA_DIR = Join-Path $reportDirectory 'profile'
    $env:PROMPTLET_SMOKE_REPORT = Join-Path $reportDirectory 'result.json'
    $env:PROMPTLET_FRAME_DIAGNOSTICS = Join-Path $reportDirectory 'frame.ndjson'
    $process = Start-Process -FilePath $Executable -ArgumentList '--self-test' -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
    if (-not $process.WaitForExit(30000)) { throw 'Smoke test timed out.' }
    if (-not (Test-Path -LiteralPath $env:PROMPTLET_SMOKE_REPORT)) { throw 'Smoke test did not write a report.' }
    $result = Get-Content -Raw -LiteralPath $env:PROMPTLET_SMOKE_REPORT | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 5
    if (-not $result.Passed) { throw 'Native smoke test failed.' }
    Copy-Item -LiteralPath $env:PROMPTLET_SMOKE_REPORT -Destination (Join-Path $script:ProjectRoot 'artifacts\latest-smoke.json') -Force
} finally {
    $env:PROMPTLET_DATA_DIR = $previousData; $env:PROMPTLET_SMOKE_REPORT = $previousReport; $env:PROMPTLET_FRAME_DIAGNOSTICS = $previousFrame
}
