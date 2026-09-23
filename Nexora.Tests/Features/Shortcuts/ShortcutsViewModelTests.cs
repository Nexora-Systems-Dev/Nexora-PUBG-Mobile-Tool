using System.Windows.Media.Imaging;
using FluentAssertions;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.Shortcuts.Application;
using Nexora.Features.Shortcuts.Presentation;
using Nexora.Shared.Contracts;
using Nexora.UI.Presentation;
using Xunit;

namespace Nexora.Tests.Features.Shortcuts;

/// <summary>
/// The ViewModel is the Shortcuts page's single gate for preview modeling
/// and shortcut creation — logic that used to live inline in the shell — so
/// the bus contract, the failure translation, and the preview text contract
/// are pinned here rather than in the view.
/// </summary>
public sealed class ShortcutsViewModelTests
{
    private static readonly PubgVersion Version = new("com.tencent.ig", "PUBG Mobile Global");

    [Fact]
    public void GetPreview_NullVersion_ReturnsTheEmptyChooseACard()
    {
        var preview = ShortcutsViewModel.GetPreview(null);

        preview.DisplayName.Should().Be("Choose a PUBG Mobile version");
        preview.PackageName.Should().Be("No package selected");
        preview.Destination.Should().Be("The shortcut will be placed on your desktop.");
        preview.CanCreate.Should().BeFalse();
    }

    [Fact]
    public void GetPreview_SelectedVersion_ReturnsTheCreatableCard()
    {
        var preview = ShortcutsViewModel.GetPreview(Version);

        preview.DisplayName.Should().Be("PUBG Mobile Global");
        preview.PackageName.Should().Be("com.tencent.ig");
        preview.Destination.Should().Be("Desktop shortcut: PUBG Mobile Global.lnk");
        preview.CanCreate.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheCreationAndReleasesTheBus()
    {
        var vm = Build();
        var statuses = new List<(string, bool)>();
        vm.StatusChanged += (message, isError) => statuses.Add((message, isError));

        var result = await vm.ExecuteAsync(_ => Task.FromResult(OperationResult.Ok("Desktop shortcut created.")));

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Desktop shortcut created.");
        statuses.Should().ContainSingle().Which.Should().Be(("Working...", false));
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhileBusy_RefusesWithoutRunningTheAction()
    {
        var bus = new PageOperationBus();
        bus.TryAcquire().Should().BeTrue();
        var vm = Build(operationBus: bus);
        var ran = false;

        var result = await vm.ExecuteAsync(_ =>
        {
            ran = true;
            return Task.FromResult(OperationResult.Ok("Desktop shortcut created."));
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Another operation is already running.");
        ran.Should().BeFalse();
        bus.Release();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_IsTranslatedToASkippedResult()
    {
        var vm = Build();

        var result = await vm.ExecuteAsync(_ => Task.FromException<OperationResult>(new OperationCanceledException()));

        result.Success.Should().BeTrue();
        result.IsSkipped.Should().BeTrue();
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Message.Should().Be("Operation canceled.");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_IsTranslatedToAFailedResult()
    {
        var vm = Build();

        var result = await vm.ExecuteAsync(_ => Task.FromException<OperationResult>(new InvalidOperationException("shortcut blew up")));

        result.Success.Should().BeFalse();
        result.Message.Should().Be("shortcut blew up");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void Cancel_OnAnIdleViewModel_IsANoOp()
    {
        var vm = Build();

        var act = () => vm.Cancel();

        act.Should().NotThrow();
    }

    private static ShortcutsViewModel Build(
        FakeShortcutService? shortcuts = null,
        PageOperationBus? operationBus = null) =>
        new(shortcuts ?? new FakeShortcutService(),
            operationBus ?? new PageOperationBus());

    private sealed class FakeShortcutService : IShortcutService
    {
        public OperationResult CreateShortcut(string displayName, string packageName) =>
            OperationResult.Ok("Desktop shortcut created.");

        public BitmapImage? GetIcon(string? packageName) => null;
    }
}
