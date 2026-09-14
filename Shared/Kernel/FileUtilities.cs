namespace Nexora.Shared.Kernel;

/// <summary>
/// File system utilities for resilient file and directory deletion.
/// </summary>
public static class FileUtilities
{
    /// <summary>
    /// Recursively removes a directory and its contents on a best-effort basis,
    /// clearing read-only attributes and suppressing IO errors for locked files.
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            DeleteContentsBestEffort(path);
            try { Directory.Delete(path, recursive: false); } catch { }
        }
        catch
        {
            // Suppress errors during best-effort directory deletion.
        }
    }

    private static void DeleteContentsBestEffort(string directory)
    {
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory).ToArray();
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            try { File.Delete(file); } catch { }
        }

        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            TryDeleteDirectory(child);
        }
    }
}
