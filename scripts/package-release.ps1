[CmdletBinding()]
param(
    [string]$Version = "v1.3.0",
    [string]$Runtime = "win-x64",
    [string]$CertificateThumbprint = $env:NEXORA_SIGN_CERT_THUMBPRINT,
    [string]$CertificatePath = $env:NEXORA_SIGN_CERT_PATH,
    [SecureString]$CertificatePassword,
    [string]$TimestampServer = "http://timestamp.digicert.com"
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

function Invoke-CodeSign {
    param([string]$FilePath)

    $cert = $null
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and (Test-Path -LiteralPath $CertificatePath)) {
        if ($CertificatePassword) {
            $cert = Get-PfxCertificate -FilePath $CertificatePath -Password $CertificatePassword
        } else {
            $cert = Get-PfxCertificate -FilePath $CertificatePath
        }
    } elseif (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $cert = Get-ChildItem -Path "Cert:\CurrentUser\My", "Cert:\LocalMachine\My" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $CertificateThumbprint } |
            Select-Object -First 1
    }

    if ($null -ne $cert) {
        Write-Host "Signing '$FilePath' with certificate: $($cert.Subject) [Thumbprint: $($cert.Thumbprint)]"
        $signParams = @{
            FilePath = $FilePath
            Certificate = $cert
            HashAlgorithm = "SHA256"
        }
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $signParams["TimestampServer"] = $TimestampServer
        }
        $sig = Set-AuthenticodeSignature @signParams
        if ($sig.Status -ne "Valid") {
            Write-Warning "Authenticode signing status for '$FilePath': $($sig.StatusMessage)"
        } else {
            Write-Host "Successfully signed '$FilePath'." -ForegroundColor Green
        }
    } else {
        Write-Host "Notice: No code signing certificate specified (NEXORA_SIGN_CERT_THUMBPRINT / NEXORA_SIGN_CERT_PATH). Artifact '$FilePath' remains unsigned." -ForegroundColor Yellow
    }
}

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

Invoke-CodeSign -FilePath $packageExe
Invoke-CodeSign -FilePath $releaseExe
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

# Unsigned artifacts are only installable by the in-app updater when the client
# can read this checksum, and it reads the checksum from the GitHub release
# body. Surface a paste-ready line so publishing it is not a manual hunt.
$exeChecksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $releaseExe).Hash.ToLowerInvariant()
$publisherLine = "SHA256: $exeChecksum"

[pscustomobject]@{
    Executable = $releaseExe
    Archive = $releaseZip
    Notes = $releaseNotes
    Runtime = $Runtime
    PublisherChecksumLine = $publisherLine
} | Format-List

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -and [string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Host "Unsigned build. Paste this line into the GitHub release body so the in-app updater can verify the payload:" -ForegroundColor Yellow
    Write-Host "  $publisherLine" -ForegroundColor Cyan
}
