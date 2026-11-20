param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'environment.ps1')
Assert-ModSdk
Assert-ModReferences
$configPath = Join-Path $script:RepoDir 'NuGet.Config'
& $script:DotnetExe restore $script:ProjectFile --configfile $configPath
if ($LASTEXITCODE -ne 0) { throw "Package restore failed with exit code $LASTEXITCODE." }
& $script:DotnetExe build $script:ProjectFile --no-restore --configuration $Configuration "-p:GameDir=$script:GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
Write-Host "Built: $(Join-Path $script:RepoDir "UIPlus\bin\$Configuration\net472\UIPlus.dll")"
