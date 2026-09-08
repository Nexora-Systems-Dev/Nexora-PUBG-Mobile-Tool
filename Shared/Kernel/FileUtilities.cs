namespace Nexora.Shared.Kernel;

/// <summary>
/// Provides resilient filesystem utilities that handle locked, read-only, or transient files safely.
/// </summary>
public static class FileUtilities
{
    /// <summary>
    /// Best-effort recursive removal of a directory and all of its contents.
    /// Clears read-only attributes first, deletes everything deletable, and ignores
    /// unreadable or genuinely locked entries without throwing exceptions.
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
            // Best-effort cleanup only: never crash caller and never report on locked items.
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
