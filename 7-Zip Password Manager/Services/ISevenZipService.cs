namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 7-Zip 命令行调用抽象。
/// 测试时可 Mock 此接口避免依赖真实 7z.exe。
/// </summary>
public interface ISevenZipService
{
    bool IsAvailable { get; }
    Task<bool> TestPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default);
    Task<bool> ExtractAsync(string archivePath, string password, string outputDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null);
}
