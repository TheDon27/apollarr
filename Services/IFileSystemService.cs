namespace Apollarr.Services;

public interface IFileSystemService
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    Task WriteAllTextAsync(string path, string content);
}

public class FileSystemService : IFileSystemService
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    
    public Task WriteAllTextAsync(string path, string content) => File.WriteAllTextAsync(path, content);
}
