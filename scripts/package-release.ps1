[CmdletBinding()]
param(
    [string]$Version = "v1.0.11",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Nexora.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseName = "Nexora-$Version-$Runtime"
$publishDir = Join-Path $artifactsRoot "publish-$Runtime"
$packageDir = Join-Path $artifactsRoot $releaseName
$releaseExe = Join-Path $artifactsRoot "$releaseName.exe"
$releaseZip = Join-Path $artifactsRoot "$releaseName.zip"
$releaseNotes = Join-Path $artifactsRoot "$releaseName-README.txt"

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
foreach ($path in @($publishDir, $packageDir, $releaseExe, $releaseZip, $releaseNotes)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

$publishArgs = @(
    $project,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "--output", $publishDir,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir "Nexora PUBG Mobile Tool.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "The published executable was not found: $publishedExe"
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force
$packageExe = Join-Path $packageDir "Nexora PUBG Mobile Tool.exe"
if (Test-Path -LiteralPath $packageExe) {
    Rename-Item -LiteralPath $packageExe -NewName "$releaseName.exe"
}

Copy-Item -LiteralPath $publishedExe -Destination $releaseExe -Force
@"
Nexora PUBG Mobile Tool $Version
Runtime: $Runtime

This is a self-contained Windows release. It includes the .NET runtime.
Run $releaseName.exe. The application requests Administrator permission because some features modify GameLoop and Windows settings.

For a portable folder release, extract $releaseName.zip and run the executable inside it.
"@ | Set-Content -LiteralPath $releaseNotes -Encoding UTF8

Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $packageDir "RELEASE-README.txt") -Force
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $releaseZip -CompressionLevel Optimal

$hashes = @($releaseExe, $releaseZip) | ForEach-Object {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($_))"
}
$hashes | Set-Content -LiteralPath (Join-Path $artifactsRoot "$releaseName-SHA256SUMS.txt") -Encoding ASCII

[pscustomobject]@{
    Executable = $releaseExe
    Archive = $releaseZip
    Notes = $releaseNotes
    Runtime = $Runtime
} | Format-List
