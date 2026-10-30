param([switch]$SkipTests)
. (Join-Path $PSScriptRoot 'common.ps1')
Stop-ProjectApp
Push-Location $script:ProjectRoot
try {
    Invoke-ProjectDotnet build 'src\Promptlet\Promptlet.csproj' -c Release '-p:Platform=x64' --nologo '-p:RestoreLockedMode=true'
    if (-not $SkipTests) {
        & (Join-Path $PSScriptRoot 'test.ps1')
    }
}
finally { Pop-Location }
