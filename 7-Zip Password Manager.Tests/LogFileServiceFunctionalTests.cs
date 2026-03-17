using System.IO;
using System.Text;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// LogFileService 功能性测试：真实文件写入与裁剪，使用临时路径与小 maxFileSize。
/// 覆盖 WriteSessionStart、Append、超限 TrimIfNeeded。
/// </summary>
public class LogFileServiceFunctionalTests : IDisposable
{
    private readonly string _tempLogPath;
    private const long MaxFileSize = 512;

    public LogFileServiceFunctionalTests()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"7zpm_log_{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        if (File.Exists(_tempLogPath))
            File.Delete(_tempLogPath);
    }

    [Fact]
    public void WriteSessionStart_CreatesFileWithSessionSeparator()
    {
        var service = new LogFileService(_tempLogPath, MaxFileSize);
        service.WriteSessionStart();

        Assert.True(File.Exists(_tempLogPath));
        var content = File.ReadAllText(_tempLogPath, Encoding.UTF8);
        Assert.Contains("===", content);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", content);
    }

    [Fact]
    public void Append_WritesTagAndTimestamp()
    {
        var service = new LogFileService(_tempLogPath, MaxFileSize);
        var ts = new DateTime(2025, 3, 15, 10, 30, 0);

        service.Append(ts, LogLevel.Success, "msg1");
        service.Append(ts, LogLevel.Warning, "msg2");
        service.Append(ts, LogLevel.Error, "msg3");
        service.Append(ts, LogLevel.Info, "msg4");

        var content = File.ReadAllText(_tempLogPath, Encoding.UTF8);
        Assert.Contains("[OK]", content);
        Assert.Contains("msg1", content);
        Assert.Contains("[WARN]", content);
        Assert.Contains("msg2", content);
        Assert.Contains("[ERR]", content);
        Assert.Contains("msg3", content);
        Assert.Contains("[INFO]", content);
        Assert.Contains("msg4", content);
        Assert.Contains("10:30:00", content);
    }

    [Fact]
    public void TrimIfNeeded_ShrinksFile_WhenOverMaxSize()
    {
        var service = new LogFileService(_tempLogPath, MaxFileSize);
        // Write many lines so file exceeds MaxFileSize (512 bytes)
        for (int i = 0; i < 80; i++)
            service.Append(DateTime.Now, LogLevel.Info, $"line {i} with some padding to grow size");

        var sizeBeforeTrim = new FileInfo(_tempLogPath).Length;
        Assert.True(sizeBeforeTrim > MaxFileSize, $"File should exceed {MaxFileSize} bytes before trim, was {sizeBeforeTrim}");

        // WriteSessionStart calls TrimIfNeeded() first
        service.WriteSessionStart();

        var sizeAfterTrim = new FileInfo(_tempLogPath).Length;
        Assert.True(sizeAfterTrim <= sizeBeforeTrim, "File should be trimmed (same or smaller)");
        // Trim keeps second half of lines; then we append session start, so total can still be > max until next trim
        // At least verify trim ran: line count or size reduced
        var linesAfter = File.ReadAllLines(_tempLogPath, Encoding.UTF8);
        Assert.True(linesAfter.Length < 85, "Roughly half of original lines kept plus new session line");
    }
}
