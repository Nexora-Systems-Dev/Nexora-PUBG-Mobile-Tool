using FluentAssertions;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Graphics.Application;
using Nexora.Shared.Contracts;
using Xunit;

namespace Nexora.Tests.Features.Graphics;

/// <summary>
/// Pins the U-03 off-thread contract at the service boundary: the ADB round
/// trips of a graphics apply run on the thread pool, not on the caller's
/// (UI-simulated) context. Before the fix the whole chain ran on the calling
/// thread, so every adb wait blocked the dispatcher and the page's
/// "Applying graphics settings…" status could never render — the status is
/// raised on the same thread that was busy. The caller here installs a real
/// <see cref="SynchronizationContext"/> to stand in for the dispatcher and
/// awaits from it, which is the shape <see cref="GraphicsViewModel.ApplyAsync"/>
/// has, so what this observes is what the page gets.
/// </summary>
public sealed class GraphicsApplyOffloadTests
{
    [Fact]
    public async Task ApplyAsync_RunsOffTheCallingThread_WithoutCapturingItsContext()
    {
        var callingThreadId = Environment.CurrentManagedThreadId;
        var callingContext = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(callingContext);
        try
        {
            var store = new RecordingProfileStore();
            var service = new GraphicsSettingsService(store, NopConnection.Instance);

            // Deliberately not awaited yet: the calling thread stays occupied in the spin
            // below and so cannot pick up the pool work item the service queued. A spin is
            // not a UI message pump — the claim is the narrower one that the work began off
            // this thread while this thread was not free to run it. A plain thread-id
            // comparison after an await would not do it: the test runner is itself on a
            // pool thread, and the await hands the caller's slot straight back to the pool.
            // The 30 s fuse is load headroom, not timing: under the full suite the pool
            // is shared with blocking process tests (a 4000-line PowerShell drain among
            // them), and a 5 s fuse flaked there while passing in isolation every time.
            var applyTask = service.ApplyAsync(GraphicsSelection.Defaults);
            SpinWait.SpinUntil(() => store.WorkThreadId.HasValue, TimeSpan.FromSeconds(30));

            store.WorkThreadId.Should().NotBeNull("the apply must start without the caller awaiting it");
            store.WorkThreadId.Should().NotBe(callingThreadId,
                "the ADB round trips must start while the calling thread is busy, so they cannot be running on this thread");

            var result = await applyTask;
            result.Should().NotBeNull("the apply must answer the caller at all");

            // The other load-bearing half: Task.Run runs the delegate on a pool thread, which
            // carries no ambient context, so the device waits cannot have been posting back
            // to anything dispatcher-like between commands either.
            store.ContextDuringWork.Should().BeNull("the apply must not capture the caller's SynchronizationContext into the work");
        }
        finally
        {
            // The context is thread-static: leaving it set would leak into whatever test
            // the pool next schedules on this work item.
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// A store whose only job is to report where the apply ran. It stands in for the
    /// twenty-plus sequential adb waits the Korean path issues, so the wait below is
    /// long enough that a non-offloaded apply would visibly stall its caller.
    /// </summary>
    private sealed class RecordingProfileStore : IGraphicsProfileStore
    {
        public int? WorkThreadId { get; private set; }

        public SynchronizationContext? ContextDuringWork { get; private set; }

        public string? GetGraphicsQuality() => null;
        public string? GetFrameRate() => null;
        public string? GetGraphicsStyle() => null;
        public Task<string?> GetShadowAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public async Task<OperationResult> ApplyGraphicsAsync(GraphicsSelection selection, CancellationToken cancellationToken)
        {
            WorkThreadId = Environment.CurrentManagedThreadId;
            ContextDuringWork = SynchronizationContext.Current;

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

            return OperationResult.Ok("Graphics settings applied successfully.");
        }
    }

    /// <summary>Connection facet the service constructor requires but the apply never touches.</summary>
    private sealed class NopConnection : IGameLoopConnection
    {
        public static readonly NopConnection Instance = new();

        public string? CurrentPackage => null;
        public bool IsAdbConnected => false;
        public bool IsConnected => false;
        public Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) =>
            throw new NotImplementedException("the apply path must not connect");
        public Task<OperationResult> LoadVersionAsync(string packageName, CancellationToken cancellationToken) =>
            throw new NotImplementedException("the apply path must not load a version");
        public void Disconnect() => throw new NotImplementedException("the apply path must not disconnect");
    }
}
