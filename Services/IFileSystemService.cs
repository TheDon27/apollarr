namespace Apollarr.Services;

public interface IFileSystemService
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    Task WriteAllTextAsync(string path, string content);
    void DeleteFile(string path);
    string[] GetFiles(string path, string searchPattern);
    void DeleteDirectory(string path, bool recursive);
}

public class FileSystemService : IFileSystemService
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    
    public bool FileExists(string path) => File.Exists(path);
    
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    
    public Task WriteAllTextAsync(string path, string content) => File.WriteAllTextAsync(path, content);
    
    public void DeleteFile(string path) => File.Delete(path);
    
    public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
}
