using Nexora.Shared.Kernel;
using Nexora.Infrastructure.Processes;

namespace Nexora.Features.Updates.Infrastructure;

/// <summary>
/// Builds the self-update handoff PowerShell script and its encoded launch arguments.
/// </summary>
public static class UpdateHandoffBuilder
{
    /// <summary>
    /// Generates the self-update handoff PowerShell script that waits for the parent process
    /// to exit, backs up and replaces target files, launches the update, and cleans staging.
    /// </summary>
    internal static string BuildHandoffScript(
        int parentPid,
        string sourceExecutablePath,
        string targetExecutablePath,
        string extractionRoot,
        string stagingRoot)
    {
        var quotedSource = ProcessText.Quote(Path.GetFullPath(sourceExecutablePath));
        var quotedTarget = ProcessText.Quote(Path.GetFullPath(targetExecutablePath));
        var quotedExtraction = ProcessText.Quote(Path.GetFullPath(extractionRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var quotedStaging = ProcessText.Quote(Path.GetFullPath(stagingRoot));

        return $$"""
$ErrorActionPreference = 'Stop'
$parentPid = {{parentPid}}
$sourceExe = {{quotedSource}}
$targetExe = {{quotedTarget}}
$extractionRoot = {{quotedExtraction}}
$stagingRoot = {{quotedStaging}}
$targetDir = Split-Path -Parent $targetExe
$backupExe = "$targetExe.bak"
$backupCreated = $false

# 1. Wait for running application process to terminate
if ($parentPid -gt 0) {
    try {
        $parent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
        if ($parent) {
            $exited = $parent.WaitForExit(30000)
            if (-not $exited) {
                Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
                exit 1
            }
        }
    } catch { }
}
Start-Sleep -Milliseconds 500

# 2. Verify source executable exists in staging
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    exit 2
}

# 3. Create backup of current target executable
if (Test-Path -LiteralPath $targetExe -PathType Leaf) {
    try {
        Copy-Item -LiteralPath $targetExe -Destination $backupExe -Force -ErrorAction Stop
        $backupCreated = $true
    } catch {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        exit 3
    }
}

# 4. Copy payload files and replace target executable with retry
$copySuccess = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        if (Test-Path -LiteralPath $extractionRoot -PathType Container) {
            Get-ChildItem -LiteralPath $extractionRoot -Recurse | ForEach-Object {
                $item = $_
                if ($item.FullName -ne $sourceExe) {
                    $relPath = $item.FullName.Substring($extractionRoot.Length).TrimStart('\', '/')
                    $destPath = Join-Path $targetDir $relPath
                    if ($item.PSIsContainer) {
                        if (-not (Test-Path -LiteralPath $destPath)) {
                            [System.IO.Directory]::CreateDirectory($destPath) | Out-Null
                        }
                    } else {
                        $destParent = Split-Path -Parent $destPath
                        if (-not (Test-Path -LiteralPath $destParent)) {
                            [System.IO.Directory]::CreateDirectory($destParent) | Out-Null
                        }
                        Copy-Item -LiteralPath $item.FullName -Destination $destPath -Force -ErrorAction Stop
                    }
                }
            }
        }

        Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force -ErrorAction Stop
        $copySuccess = $true
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

# 5. Handle failure: rollback to original executable and abort without relaunch
if (-not $copySuccess) {
    if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {
        try {
            Copy-Item -LiteralPath $backupExe -Destination $targetExe -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
        } catch { }
    }
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    exit 4
}

# 6. Success: remove backup, launch updated application, and clean temporary staging
if ($backupCreated -and (Test-Path -LiteralPath $backupExe)) {
    Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
}

try {
    Start-Process -FilePath $targetExe -WorkingDirectory $targetDir
} catch { }

Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
""";
    }

    /// <summary>
    /// Formats the PowerShell arguments for running the handoff script with elevation.
    /// </summary>
    internal static string BuildPowerShellArguments(string script)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}";
    }
}
