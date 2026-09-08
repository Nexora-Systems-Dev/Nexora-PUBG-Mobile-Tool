using System.Text;
using FluentAssertions;
using Nexora.Features.GameLoop;
using Xunit;

namespace Nexora.Tests.Services;

public sealed class Ue4SavEditorTests
{
    [Fact]
    public void Ue4SavEditor_Constructor_ThrowsForNullContent()
    {
        var act = () => new Ue4SavEditor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ue4SavEditor_ReadProperty_ReturnsDefaultWhenNotFound()
    {
        var editor = new Ue4SavEditor(new byte[] { 0x01, 0x02, 0x03 });
        editor.ReadProperty("NonExistentProperty", defaultValue: 99).Should().Be(99);
    }

    [Fact]
    public void Ue4SavEditor_ReadProperty_ReturnsDefaultWhenPropertyEmpty()
    {
        var editor = new Ue4SavEditor(new byte[] { 0x01, 0x02, 0x03 });
        editor.ReadProperty("", defaultValue: 42).Should().Be(42);
    }

    [Fact]
    public void Ue4SavEditor_ReadAndChangeProperty_RoundTripsCorrectly()
    {
        // Arrange
        var header = Ue4SavEditor.CreateHeader("BattleFPS");
        var buffer = new byte[header.Length + 4];
        header.CopyTo(buffer, 0);
        buffer[header.Length] = 0x04; // initial value: High

        var editor = new Ue4SavEditor(buffer);

        // Act & Assert Initial Read
        editor.ReadProperty("BattleFPS").Should().Be(0x04);

        // Act Modify
        var updated = editor.ChangeProperty("BattleFPS", 0x06); // update to Extreme
        updated.Should().BeTrue();

        // Assert Modified Read
        editor.ReadProperty("BattleFPS").Should().Be(0x06);
    }

    [Fact]
    public void Ue4SavEditor_ChangeProperty_ReturnsFalseWhenNotFound()
    {
        var editor = new Ue4SavEditor(new byte[] { 0x10, 0x20 });
        editor.ChangeProperty("Unknown", 0x01).Should().BeFalse();
        editor.ChangeProperty("", 0x01).Should().BeFalse();
    }

    [Fact]
    public void Ue4SavEditor_FindSequence_ReturnsMinusOne_ForEdgeCases()
    {
        Ue4SavEditor.FindSequence(null!, new byte[] { 1 }).Should().Be(-1);
        Ue4SavEditor.FindSequence(new byte[] { 1 }, null!).Should().Be(-1);
        Ue4SavEditor.FindSequence(new byte[] { 1 }, Array.Empty<byte>()).Should().Be(-1);
        Ue4SavEditor.FindSequence(new byte[] { 1 }, new byte[] { 1, 2 }).Should().Be(-1);
    }

    [Theory]
    [InlineData("r.ShadowQuality", "1")]
    [InlineData("r.UserShadowSwitch", "0")]
    public void UnrealCVarCodec_RoundTrips_NameAndValue(string name, string value)
    {
        var encoded = UnrealCVarCodec.EncodeCVar(name, value);
        var decoded = UnrealCVarCodec.DecodeCVar(encoded);
        decoded.Should().Be($"{name}={value}");
    }

    [Fact]
    public void UnrealCVarCodec_TryApplyShadowPreset_EnablesAndDisablesShadows()
    {
        // Arrange
        var initialLines = new[]
        {
            "[UserCustom]",
            "+CVars=" + UnrealCVarCodec.EncodeCVar("r.ShadowQuality", "0"),
            "+CVars=" + UnrealCVarCodec.EncodeCVar("r.UserShadowSwitch", "0")
        };

        // Act: Enable
        var enableSuccess = UnrealCVarCodec.TryApplyShadowPreset(initialLines, enable: true, out var enabledLines);

        // Assert: Enable
        enableSuccess.Should().BeTrue();
        enabledLines[1].Should().EndWith("48"); // '1' ^ 0x79 = 0x48

        // Act: Disable
        var disableSuccess = UnrealCVarCodec.TryApplyShadowPreset(enabledLines, enable: false, out var disabledLines);

        // Assert: Disable
        disableSuccess.Should().BeTrue();
        disabledLines[1].Should().EndWith("49"); // '0' ^ 0x79 = 0x49
    }

    [Fact]
    public void UnrealCVarCodec_TryApplyShadowPreset_ReturnsFalseWhenNoCVarsPresent()
    {
        var lines = new[] { "[Header]", "Key=Value" };
        var success = UnrealCVarCodec.TryApplyShadowPreset(lines, enable: true, out var updatedLines);
        success.Should().BeFalse();
        updatedLines.Should().Equal(lines);
    }
}
