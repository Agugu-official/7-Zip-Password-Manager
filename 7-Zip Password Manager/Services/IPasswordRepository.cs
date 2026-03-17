using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 密码列表的持久化读写抽象。
/// 测试时可用内存实现替代文件 I/O。
/// </summary>
public interface IPasswordRepository
{
    string FilePath { get; set; }
    (bool Success, string? Error) EnsureFileExists();
    List<PasswordEntry> Load();
    void Save(List<PasswordEntry> entries);
}
