param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'background-assets.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repo "UIPlus\bin\$Configuration\net472\UIPlus.dll"
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Build UI Plus before packaging it.' }

if (-not $OutputPath) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repo 'UIPlus\UIPlus.csproj') -Raw
    $version = (Select-Xml -Xml $project -XPath '/Project/PropertyGroup/Version').Node.InnerText
    $OutputPath = Join-Path $repo "release\UIPlus-v$version-beta.zip"
}
$archive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputDirectory = Split-Path -Parent $archive
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$backgrounds = Join-Path $repo 'assets\Backgrounds'
$media = @(Get-BundledBackgrounds $backgrounds)
$titleVideo = 'Nuclear Option Title - At0micVen0m_.mp4'
if ($titleVideo -notin $media.Name) {
    Write-Warning "Bundled title video '$titleVideo' is missing. Continuing without it."
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$temporary = Join-Path $outputDirectory ([Guid]::NewGuid().ToString() + '.tmp')
try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $prefix = 'UIPlus'
        $compression = [IO.Compression.CompressionLevel]::Optimal
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $dll, "$prefix/UIPlus.dll", $compression) | Out-Null
        foreach ($name in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $repo $name), "$prefix/$name", $compression) | Out-Null
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, (Join-Path $backgrounds 'README.md'), "$prefix/Backgrounds/README.md", $compression) | Out-Null
        foreach ($file in $media) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, "$prefix/Backgrounds/$($file.Name)", [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $archive -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Host "Packaged: $archive"
