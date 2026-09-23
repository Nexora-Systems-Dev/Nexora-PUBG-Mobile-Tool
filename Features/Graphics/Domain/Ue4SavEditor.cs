using System.Text;

namespace Nexora.Features.Graphics.Domain;

/// <summary>
/// In-memory binary parser and editor for Unreal Engine 4 SaveGame (.sav) IntProperty values.
/// </summary>
public sealed class Ue4SavEditor
{
    private const string IntPropertyMarker = "\0\f\0\0\0IntProperty\0\x04\0\0\0\0\0\0\0\0";
    private readonly byte[] _content;

    public Ue4SavEditor(byte[] content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public byte[] ToBytes() => (byte[])_content.Clone();

    /// <summary>
    /// Reads the byte value following an IntProperty header.
    /// </summary>
    public byte ReadProperty(string propertyName, byte defaultValue = 0)
    {
        var valueIndex = ValueIndexOf(propertyName);
        return valueIndex >= 0 && valueIndex < _content.Length
            ? _content[valueIndex]
            : defaultValue;
    }

    /// <summary>
    /// Updates the byte value following an IntProperty header in-place.
    /// </summary>
    public bool ChangeProperty(string propertyName, byte value)
    {
        var valueIndex = ValueIndexOf(propertyName);

        if (valueIndex < 0 || valueIndex >= _content.Length)
        {
            return false;
        }

        _content[valueIndex] = value;
        return true;
    }

    /// <summary>
    /// The index of an IntProperty's value byte, or -1 when the property name is
    /// blank, its header is absent, or the value byte is truncated out of the
    /// buffer. Both readers funnel through here so the locate step cannot drift
    /// between a read and a write.
    /// </summary>
    private int ValueIndexOf(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return -1;
        }

        var header = CreateHeader(propertyName);
        var headerIndex = FindSequence(_content, header);
        return headerIndex < 0 ? -1 : headerIndex + header.Length;
    }

    /// <summary>
    /// Generates the binary search header for an Unreal Engine IntProperty.
    /// </summary>
    public static byte[] CreateHeader(string propertyName) =>
        Encoding.UTF8.GetBytes(propertyName + IntPropertyMarker);

    /// <summary>
    /// Finds the first index of a byte sequence within a buffer, or -1 if not found.
    /// </summary>
    public static int FindSequence(byte[] source, byte[] sequence)
    {
        if (source is null || sequence is null || sequence.Length == 0 || source.Length < sequence.Length)
        {
            return -1;
        }

        return source.AsSpan().IndexOf(sequence);
    }
}
