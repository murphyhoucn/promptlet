param([string]$CompilerPath, [string]$PackagePath)
$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'src\Promptlet\Promptlet.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $PackagePath) { $PackagePath = Join-Path $script:ProjectRoot "artifacts\release\Promptlet-$version-win-x64" }
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not $CompilerPath) { $CompilerPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 6.7.4 or later and pass -CompilerPath <ISCC.exe>.' }
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
foreach ($required in @('App\Promptlet.exe','App\Licenses\Promptlet-LICENSE','App\Licenses\Promptlet-LICENSE-NOTICE.md','LICENSE','LICENSE-NOTICE.md','files.sha256.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackagePath $required))) { throw "Missing package input: $required" }
}
$hashes = Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'files.sha256.json') | ConvertFrom-Json
foreach ($file in $hashes) {
    $path = [IO.Path]::GetFullPath((Join-Path (Join-Path $PackagePath 'App') $file.Path))
    if (-not $path.StartsWith((Join-Path $PackagePath 'App') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $file.Hash) { throw "Checksum failed: $($file.Path)" }
}
$output = Join-Path $script:ProjectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$notice = Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'LICENSE-NOTICE.md')
$gpl = Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'LICENSE')
$licensePath = Join-Path $output 'installer-license.txt'
[IO.File]::WriteAllText($licensePath, $notice + "`r`n`r`n" + $gpl, [Text.UTF8Encoding]::new($true))
& $CompilerPath '/Qp' "/DAppVersion=$version" "/DPackageDir=$PackagePath" "/DOutputDir=$output" "/DLicenseFile=$licensePath" "/DCompilerLicenseFile=$(Join-Path (Split-Path -Parent $CompilerPath) 'License.txt')" (Join-Path $script:ProjectRoot 'packaging\Promptlet.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $output "Promptlet-$version-Setup-x64.exe"
Get-FileHash -LiteralPath $installer | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath ($installer + '.sha256') -Encoding ascii
Write-Host "EXE installer: $installer"
