using System.Text;

namespace Nexora.Features.GameLoop;

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
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return defaultValue;
        }

        var header = CreateHeader(propertyName);
        var headerIndex = FindSequence(_content, header);
        var valueIndex = headerIndex + header.Length;

        return headerIndex >= 0 && valueIndex < _content.Length
            ? _content[valueIndex]
            : defaultValue;
    }

    /// <summary>
    /// Updates the byte value following an IntProperty header in-place.
    /// </summary>
    public bool ChangeProperty(string propertyName, byte value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var header = CreateHeader(propertyName);
        var headerIndex = FindSequence(_content, header);
        var valueIndex = headerIndex + header.Length;

        if (headerIndex < 0 || valueIndex >= _content.Length)
        {
            return false;
        }

        _content[valueIndex] = value;
        return true;
    }

    /// <summary>
    /// Generates the binary search header for an Unreal Engine IntProperty.
    /// </summary>
    public static byte[] CreateHeader(string propertyName)
    {
        return Encoding.UTF8.GetBytes(propertyName + IntPropertyMarker);
    }

    /// <summary>
    /// Finds the first index of a byte sequence within a buffer, or -1 if not found.
    /// </summary>
    public static int FindSequence(byte[] source, byte[] sequence)
    {
        if (source is null || sequence is null || sequence.Length == 0 || source.Length < sequence.Length)
        {
            return -1;
        }

        for (var i = 0; i <= source.Length - sequence.Length; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (source[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
