param()
. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $script:ProjectRoot
try {
    Invoke-ProjectDotnet test 'tests\Promptlet.Core.Tests\Promptlet.Core.Tests.csproj' -c Release --nologo '-p:RestoreLockedMode=true'
    Invoke-ProjectDotnet test 'tests\Promptlet.Windows.Tests\Promptlet.Windows.Tests.csproj' -c Release --nologo '-p:RestoreLockedMode=true'
}
finally { Pop-Location }
