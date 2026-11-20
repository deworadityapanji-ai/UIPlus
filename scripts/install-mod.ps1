param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'environment.ps1')
Assert-ModReferences
if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) {
    throw 'Close Nuclear Option before installing the test build.'
}
$sourceDll = Join-Path $script:RepoDir "UIPlus\bin\$Configuration\net472\UIPlus.dll"
if (-not (Test-Path -LiteralPath $sourceDll)) { throw 'Build UI Plus before installing it.' }
$pluginDirectory = Join-Path $script:GameDir 'BepInEx\plugins\UIPlus'
$destinationDll = Join-Path $pluginDirectory 'UIPlus.dll'
if (Test-Path -LiteralPath $destinationDll) {
    $backupDirectory = Join-Path $script:ToolsDir ('backups\UIPlus-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $destinationDll -Destination $backupDirectory
    Write-Host "Backed up the previous UIPlus.dll: $backupDirectory"
}
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination $destinationDll -Force
if ((Get-FileHash -LiteralPath $sourceDll).Hash -ne (Get-FileHash -LiteralPath $destinationDll).Hash) {
    throw 'Installed DLL does not match the build.'
}
Write-Host "Installed: $destinationDll"
