namespace Nexora.Shared.Kernel;

/// <summary>
/// IO boundary for filesystem access, allowing domain logic to be tested without touching the disk.
/// </summary>
public interface IFileSystem
{
    bool Exists(string path);

    byte[] ReadAllBytes(string path);

    void WriteAllBytes(string path, byte[] bytes);

    string ReadAllText(string path);

    void WriteAllText(string path, string contents);

    string[] ReadAllLines(string path);

    void WriteAllLines(string path, IEnumerable<string> lines);

    IEnumerable<string> ReadLines(string path);

    void Copy(string sourceFileName, string destinationFileName, bool overwrite = false);

    void Delete(string path);

    void CreateDirectory(string path);
}
