using System.Globalization;
using FluentAssertions;
using Microsoft.Win32;
using Nexora.Features.Updates.Infrastructure;
using Xunit;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Positive and negative controls for the file-binding half of Authenticode
/// verification. The primitive is exercised directly rather than through the
/// composed <c>IsAuthenticodeSigned</c> predicate, because every candidate
/// binary is published by Microsoft, not by the configured publisher.
/// </summary>
public sealed class UpdateFileSignatureBindingTests
{
    // Embedded-signed Windows binaries, ordered by how stable the embedded
    // signature has been across versions. Catalog-only binaries (notepad, cmd,
    // reg) are deliberately absent: WTD_CHOICE_FILE finds no signature on them
    // even though Get-AuthenticodeSignature reports them as Valid.
    private static readonly string[] EmbeddedSignedSystemBinaries =
    {
        @"C:\Windows\System32\fsutil.exe",
        @"C:\Windows\System32\svchost.exe",
        @"C:\Windows\explorer.exe"
    };

    // The positive control is the only test here whose outcome depends on the
    // machine trusting a subject at all. The two negative controls never do, so
    // they stay plain [Fact] on every machine and are not decorated.
    [FactUnlessCodeIntegrityEnforced]
    public void HasValidFileSignature_AcceptsGenuinelySignedSystemBinary()
    {
        var signed = EmbeddedSignedSystemBinaries.FirstOrDefault(File.Exists);
        signed.Should().NotBeNull("an embedded-signed Windows binary is the positive control");

        UpdateArchiveValidator.HasValidFileSignature(signed!).Should().BeTrue(
            $"{signed} carries an embedded Microsoft signature, so a signature over its own bytes must verify." +
            Environment.NewLine +
            "This machine reports no enforced code-integrity policy, so a TRUST_E_SUBJECT_NOT_TRUSTED result " +
            "here means the WinTrustData layout no longer matches wintrust.h — not a policy rejection.");
    }

    [Fact]
    public void HasValidFileSignature_RejectsUnsignedTempFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"NexoraUnsignedBinding-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            UpdateArchiveValidator.HasValidFileSignature(tempFile).Should().BeFalse(
                "a file with no signature must fall through to the checksum path");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasValidFileSignature_RejectsMissingFile()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), $"NexoraMissingBinding-{Guid.NewGuid():N}.exe");

        UpdateArchiveValidator.HasValidFileSignature(missingFile).Should().BeFalse(
            "an absent file has no signature to verify and must not throw");
    }
}

/// <summary>
/// A hard <c>[Fact]</c> that yields a genuine skipped outcome — never a silent
/// pass — on a machine whose enforced code-integrity policy makes S_OK
/// unreachable for every file. Under such a policy WinVerifyTrust applies the CI
/// decision before the Authenticode policy provider consults any signature, so
/// genuinely signed Windows binaries and the test host itself all return
/// TRUST_E_SUBJECT_NOT_TRUSTED and the positive control loses all power to
/// discriminate a correct interop from a broken one.
/// </summary>
/// <remarks>
/// The predicate is the machine's published policy state, never a try-and-see
/// on the interop call, so the skip carries no information about the code under
/// test. A real layout break asserts on every machine that is not skipping.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class FactUnlessCodeIntegrityEnforcedAttribute : FactAttribute
{
    private const string CodeIntegrityPolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
    private const string PolicyStateValueName = "VerifiedAndReputablePolicyState";

    // Win32_DeviceGuard.VerifiedAndReputablePolicyState: 0 Off, 1 Audit, 2 Enforce.
    // Audit (1) logs but does not block, so only Enforce grants a skip.
    private const int PolicyStateEnforce = 2;

    public FactUnlessCodeIntegrityEnforcedAttribute()
    {
        if (ReadPolicyState() == PolicyStateEnforce)
        {
            Skip = "Enforced code-integrity policy (VerifiedAndReputablePolicyState=2, Smart App Control / WDAC) " +
                   "is active on this machine: WinVerifyTrust returns TRUST_E_SUBJECT_NOT_TRUSTED (0x800B0003) " +
                   "for every file, including genuinely signed Windows binaries, so this positive control cannot " +
                   "pass or fail on interop merit. Run on a machine with the policy off for the hard assertion.";
        }
    }

    /// <summary>
    /// Reads the enforced-policy state from the same registry value the WMI
    /// provider behind <c>Win32_DeviceGuard.VerifiedAndReputablePolicyState</c>
    /// reports, so this is the diagnosis's enforcement read reached without a
    /// System.Management reference. Every failure path returns Off, because a
    /// skip must never be granted on an unreadable or absent key.
    /// </summary>
    private static int ReadPolicyState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CodeIntegrityPolicyKey);
            return key?.GetValue(PolicyStateValueName) is { } raw
                ? Convert.ToInt32(raw, CultureInfo.InvariantCulture)
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}
