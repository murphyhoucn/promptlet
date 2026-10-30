param([switch]$SkipPublish)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'publish.ps1') }
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'src\Promptlet\Promptlet.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$releaseRoot = Join-Path $script:ProjectRoot "artifacts\release\Promptlet-$version-win-x64"
if (Test-Path -LiteralPath $releaseRoot) { throw 'Release directory already exists; preserve previous artifacts and package in a fresh checkout.' }
New-Item -ItemType Directory -Path (Join-Path $releaseRoot 'App') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $script:ProjectRoot 'artifacts\Promptlet-win-x64') -Force | Copy-Item -Destination (Join-Path $releaseRoot 'App') -Recurse -Force
foreach ($installerScript in @('Install.cmd','Install.ps1','Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $script:ProjectRoot "packaging\$installerScript") -Destination $releaseRoot -Force
}
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'README.md') -Destination $releaseRoot -Force
foreach ($projectLicense in @('LICENSE','LICENSE-NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $script:ProjectRoot $projectLicense) -Destination $releaseRoot
}
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'docs\user-guide.md') -Destination $releaseRoot -Force
New-Item -ItemType Directory -Path (Join-Path $releaseRoot 'docs') -Force | Out-Null
foreach ($document in @('user-guide.md','development.md','release-notes.md','verification.md','licensing-review.md')) {
    Copy-Item -LiteralPath (Join-Path $script:ProjectRoot "docs\$document") -Destination (Join-Path $releaseRoot 'docs') -Force
}
@{ AppId='Promptlet'; Version=$version; Platform='Windows 11 x64'; SelfContained=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'release.json') -Encoding UTF8
$appRoot = Join-Path $releaseRoot 'App'
$licenseRoot = Join-Path $appRoot 'Licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
foreach ($projectLicense in @('LICENSE','LICENSE-NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $script:ProjectRoot $projectLicense) -Destination (Join-Path $licenseRoot "Promptlet-$projectLicense")
}
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    $runtimeNotice = Join-Path (Split-Path -Parent $script:DotnetExecutable) $notice
    if (Test-Path -LiteralPath $runtimeNotice) { Copy-Item -LiteralPath $runtimeNotice -Destination (Join-Path $licenseRoot "DotNet-$notice") -Force }
}
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$dependencyLock = Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'src\Promptlet\packages.lock.json') | ConvertFrom-Json
foreach ($framework in $dependencyLock.dependencies.PSObject.Properties) {
    foreach ($package in $framework.Value.PSObject.Properties) {
        if (-not $package.Value.resolved) { continue }
        $packageDirectory = Join-Path $cache "$($package.Name.ToLowerInvariant())\$($package.Value.resolved)"
        if (-not (Test-Path -LiteralPath $packageDirectory)) { continue }
        Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match 'license|notice' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseRoot "$($package.Name)-$($_.Name)") -Force
        }
    }
}
$hashes = @(Get-ChildItem -LiteralPath $appRoot -File -Recurse | ForEach-Object { @{ Path=[IO.Path]::GetRelativePath($appRoot,$_.FullName); Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $releaseRoot 'files.sha256.json') -Encoding UTF8
$zip = Join-Path $script:ProjectRoot "artifacts\release\Promptlet-$version-win-x64.zip"
Compress-Archive -LiteralPath $releaseRoot -DestinationPath $zip -Force -CompressionLevel Optimal
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host "Release: $zip"
