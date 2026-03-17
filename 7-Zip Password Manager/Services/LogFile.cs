using System.Diagnostics;
using System.IO;
using System.Text;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 持久化日志写入器。
/// 将运行日志追加到 config/app.log，密码内容由调用方通过 fileSafeMessage 脱敏后传入。
/// 每次会话写入分隔符，文件超过上限时自动裁剪前半部分。
/// </summary>
public class LogFileService : ILogService
{
    private readonly string _filePath;
    private readonly long _maxFileSize;
    private readonly object _lock = new();

    public LogFileService(string filePath, long maxFileSize)
    {
        _filePath = filePath;
        _maxFileSize = maxFileSize;
    }

    public LogFileService()
        : this(
            Path.Combine(AppDataPaths.ConfigFolder, AppConstants.LogFileName),
            new AppConfig().LogFileMaxSizeBytes)
    {
    }

    public void WriteSessionStart()
    {
        TrimIfNeeded();
        var line = $"{Environment.NewLine}=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
        SafeAppend(line);
    }

    public void Append(DateTime timestamp, LogLevel level, string message)
    {
        var tag = level switch
        {
            LogLevel.Success => "OK",
            LogLevel.Warning => "WARN",
            LogLevel.Error   => "ERR",
            _                => "INFO"
        };
        var line = $"[{timestamp:HH:mm:ss}] [{tag}] {message}{Environment.NewLine}";
        SafeAppend(line);
    }

    private void SafeAppend(string text)
    {
        try
        {
            lock (_lock)
            {
                AppDataPaths.EnsureDirectoryExists();
                File.AppendAllText(_filePath, text, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"写入日志文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 若日志文件超过上限则裁剪前半部分。与 SafeAppend 共用 _lock，避免与并发 Append 竞态。
    /// </summary>
    private void TrimIfNeeded()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                    return;

                var info = new FileInfo(_filePath);
                if (info.Length <= _maxFileSize)
                    return;

                var lines = File.ReadAllLines(_filePath, Encoding.UTF8);
                var half = lines.Length / 2;
                File.WriteAllLines(_filePath, lines[half..], Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"裁剪日志文件失败: {ex.Message}");
        }
    }
}
