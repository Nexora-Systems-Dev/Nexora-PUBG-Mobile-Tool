using FluentAssertions;
using Xunit;
using Nexora.Features.GameLoop.Application;
using Nexora.Features.GameLoop.Domain;
using Nexora.Features.GameLoop.Infrastructure;

namespace Nexora.Tests.Features.GameLoop;

/// <summary>
/// Pins the P1.3 extraction: the `adb devices` parsing pipeline lives in
/// <see cref="AdbClient.ParseDeviceSerials"/> and both <c>TrySelectDevice</c>
/// passes share it.
/// </summary>
public sealed class AdbDeviceSelectionTests
{
    [Fact]
    public void ParseDeviceSerials_ReturnsOnlyReadyDevices()
    {
        const string output = "List of devices attached\n" +
            "emulator-5554\tdevice\n" +
            "127.0.0.1:5555\toffline\n" +
            "emulator-5556\tunauthorized\n";

        AdbClient.ParseDeviceSerials(output).Should().Equal("emulator-5554");
    }

    [Fact]
    public void ParseDeviceSerials_MatchesStateCaseInsensitively()
    {
        const string output = "List of devices attached\nemulator-5554\tDevice\n";

        AdbClient.ParseDeviceSerials(output).Should().Equal("emulator-5554");
    }

    [Fact]
    public void ParseDeviceSerials_ReturnsEmpty_WhenNoDevicesAttached()
    {
        AdbClient.ParseDeviceSerials("List of devices attached\n").Should().BeEmpty();
        AdbClient.ParseDeviceSerials(string.Empty).Should().BeEmpty();
    }
}
