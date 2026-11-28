function Get-BundledBackgrounds([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    Get-ChildItem -LiteralPath $Directory -File | Where-Object {
        $_.Extension -in '.mp4', '.webm', '.png', '.jpg', '.jpeg'
    } | Sort-Object Name
}

function Copy-BundledBackgrounds([string]$Source, [string]$Destination) {
    $files = @(Get-BundledBackgrounds $Source)
    if ($files.Count -eq 0) { return }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in $files) {
        $target = Join-Path $Destination $file.Name
        if (Test-Path -LiteralPath $target) { continue }
        # File.Copy also rejects an overwrite if a file appears between the check and the copy.
        [IO.File]::Copy($file.FullName, $target, $false)
    }
}
