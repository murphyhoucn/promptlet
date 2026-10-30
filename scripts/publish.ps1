param()
. (Join-Path $PSScriptRoot 'common.ps1')
Stop-ProjectApp
Push-Location $script:ProjectRoot
try {
    & (Join-Path $PSScriptRoot 'test.ps1')
    $publishDirectory = Join-Path $script:ProjectRoot 'artifacts\Promptlet-win-x64'
    Invoke-ProjectDotnet publish 'src\Promptlet\Promptlet.csproj' -c Release '-p:Platform=x64' -o $publishDirectory --nologo '-p:RestoreLockedMode=true' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:PathMap=$($script:ProjectRoot)=/_/Promptlet"
    foreach ($requiredFile in @('Promptlet.exe', 'Promptlet.pri', 'App.xbf', 'PromptWindow.xbf', 'SettingsWindow.xbf', 'Assets\AppIcon.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredFile))) { throw "Missing publish resource: $requiredFile" }
    }
    if (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb') { throw 'Publish output contains debug symbols; use a fresh publish directory.' }
    Write-Host "Published to $publishDirectory. Distribute the entire directory, not only Promptlet.exe."
}
finally { Pop-Location }
