using Nexora.Features.Tuning.Domain;
using Nexora.Shared.Contracts;
namespace Nexora.Features.Tuning.Application;

/// <summary>
/// Contract for the manual Emulator Tuning page: load current GameLoop user-hive
/// values and force user-chosen values back with write-then-read-back verification.
/// </summary>
public interface IEmulatorSettingsService
{
    Task<EmulatorTuningState> LoadAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyAsync(
        EmulatorTuningSelection selection,
        CancellationToken cancellationToken = default);
}
