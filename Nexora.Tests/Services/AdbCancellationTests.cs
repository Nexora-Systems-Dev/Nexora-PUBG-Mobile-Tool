using FluentAssertions;
using Nexora.Features.GameLoop;
using Nexora.Services;
using Nexora.Shared.Infrastructure;
using Nexora.Shared.Kernel;
using Xunit;

namespace Nexora.Tests.Services;

/// <summary>
/// Verifies the P1-Step-3 cancellation contract: every cancellable ADB
/// operation must exit promptly via <see cref="OperationCanceledException"/>
/// when the token is already canceled, before touching ADB, the registry,
/// or the process list.
/// </summary>
public sealed class AdbCancellationTests
{
    private static readonly CancellationToken CanceledToken = new(canceled: true);

    private static GameLoopService CreateService() =>
        new(new RegistryService(), new AdbClient(new ProcessRunner(), new RegistryService()));

    [Fact]
    public async Task ConnectAsync_Throws_ForCanceledToken()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.ConnectAsync(CanceledToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LoadVersionAsync_Throws_ForCanceledToken()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.LoadVersionAsync("com.tencent.ig", CanceledToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ApplyGraphicsAsync_Throws_ForCanceledToken()
    {
        // Arrange
        var service = CreateService();
        var selection = new GraphicsSelection("Smooth", "Low", "Classic", EnableShadow: false, EnableKoreanFullHd: false);

        // Act
        var act = async () => await service.ApplyGraphicsAsync(selection, CanceledToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitForBootAsync_Throws_ForCanceledToken()
    {
        // Arrange: the entry guard fires before device selection, so no adb
        // process is spawned and this stays hermetic with or without GameLoop.
        var adb = new AdbClient(new ProcessRunner(), new RegistryService());

        // Act
        var act = async () => await adb.WaitForBootAsync(CanceledToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void KillGameLoopProcesses_Throws_ForCanceledToken()
    {
        // Arrange
        var facade = new WindowsToolsService(new ProcessRunner(), new RegistryService());

        // Act
        var act = () => facade.KillGameLoopProcesses(CanceledToken);

        // Assert
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task KillGameLoopProcessesAsync_Throws_ForCanceledToken()
    {
        // Arrange: Task.Run with a pre-canceled token never invokes the
        // delegate, so the process list is untouched.
        var facade = new WindowsToolsService(new ProcessRunner(), new RegistryService());

        // Act
        var act = async () => await facade.KillGameLoopProcessesAsync(CanceledToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
