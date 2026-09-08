using Nexora.Configuration;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;

namespace Nexora.Services;

public sealed class TempCleanupService : ITempCleanupService
{
    private readonly IRegistryService _registry;
    private readonly TempCleanupOptions _options;

    public TempCleanupService(IRegistryService registry, TempCleanupOptions? options = null)
    {
        _registry = registry;
        _options = options ?? new TempCleanupOptions();
    }

    public OperationResult CleanTemp() => CleanTempCore(CancellationToken.None);

    public Task<OperationResult> CleanTempAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CleanTempCore(cancellationToken), cancellationToken);
    }

    private OperationResult CleanTempCore(CancellationToken cancellationToken)
    {
        try
        {
            var skipped = new List<string>();
            foreach (var directory in _options.TargetDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClearChildren(directory, skipped, cancellationToken);
            }

            var installPath = _registry.GetLocalString(AppConstants.Registry.ValueInstallPath, AppConstants.Registry.BranchUI);
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClearChildren(Path.Combine(installPath, _options.ShaderCacheFolderName), skipped, cancellationToken);
            }

            return skipped.Count == 0
                ? OperationResult.Ok("Temporary files cleaned.")
                : OperationResult.Ok($"Temporary files cleaned. {skipped.Count} protected or in-use location(s) were skipped.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Temp cleaner could not finish: {ex.Message}");
        }
    }

    private static void ClearChildren(string directory, ICollection<string> skipped, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory).ToArray();
        }
        catch
        {
            skipped.Add(directory);
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Delete(file); } catch { skipped.Add(file); }
        }

        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch
        {
            skipped.Add(directory);
            return;
        }

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Best-effort sweep: one locked file inside no longer spares the
            // whole subtree — everything deletable is removed. The location
            // is reported as skipped only if anything actually survives.
            FileUtilities.TryDeleteDirectory(child);
            try { if (Directory.Exists(child)) skipped.Add(child); } catch { skipped.Add(child); }
        }
    }
}
