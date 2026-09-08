using FluentAssertions;
using Nexora.Shared.Infrastructure;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class RegistryServiceExtensionsTests
{
    private const string TestSubKey = @"SOFTWARE\NexoraUnitTests\SubKeyTest";

    [Fact]
    public void SetAndGetCurrentUserString_RoundTripsCorrectly()
    {
        var registry = new RegistryService();
        var testValueName = "TestValue_" + Guid.NewGuid().ToString("N");
        var testValue = "SampleData_123";

        try
        {
            var setResult = registry.SetCurrentUserString(TestSubKey, testValueName, testValue);
            setResult.Should().BeTrue();

            var retrieved = registry.GetCurrentUserString(TestSubKey, testValueName);
            retrieved.Should().Be(testValue);
        }
        finally
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(TestSubKey, writable: true);
                key?.DeleteValue(testValueName, throwOnMissingValue: false);
            }
            catch { }
        }
    }

    [Fact]
    public void GetCurrentUserString_ReturnsNull_ForNonexistentKey()
    {
        var registry = new RegistryService();
        var retrieved = registry.GetCurrentUserString(@"SOFTWARE\NonexistentKey_" + Guid.NewGuid().ToString("N"), "NonexistentValue");
        retrieved.Should().BeNull();
    }

    [Fact]
    public void GetLocalMachineDword_ReturnsNull_ForNonexistentKey()
    {
        var registry = new RegistryService();
        var retrieved = registry.GetLocalMachineDword(@"SOFTWARE\NonexistentKey_" + Guid.NewGuid().ToString("N"), "NonexistentValue");
        retrieved.Should().BeNull();
    }
}
