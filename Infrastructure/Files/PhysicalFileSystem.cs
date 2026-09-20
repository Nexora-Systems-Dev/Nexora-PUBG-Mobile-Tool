namespace Nexora.Infrastructure.Files;
using Nexora.Shared.Kernel;

/// <summary>
/// <see cref="IFileSystem"/> implementation delegating directly to <see cref="System.IO"/>.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool Exists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public void WriteAllLines(string path, IEnumerable<string> lines) => File.WriteAllLines(path, lines);

    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    public void Copy(string sourceFileName, string destinationFileName, bool overwrite = false) =>
        File.Copy(sourceFileName, destinationFileName, overwrite);

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string path) => _ = Directory.CreateDirectory(path);
}
