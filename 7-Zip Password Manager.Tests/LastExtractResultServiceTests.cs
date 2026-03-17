using System.IO;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// LastExtractResultService 文件读写测试。
/// 使用临时文件隔离测试，每个测试方法独立创建/清理文件。
/// </summary>
public class LastExtractResultServiceTests : IDisposable
{
    private readonly string _tempFile;
    private readonly LastExtractResultService _service;

    public LastExtractResultServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"7zpm_test_{Guid.NewGuid():N}.json");
        _service = new LastExtractResultService(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    // ── Save 基本功能 ──

    [Fact]
    public void Save_CreatesFile()
    {
        var result = CreateSampleResult();

        _service.Save(result);

        Assert.True(File.Exists(_tempFile), "Save 后文件应存在");
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        _service.Save(CreateSampleResult());

        var json = File.ReadAllText(_tempFile);
        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("test.zip", json);
        Assert.Contains("secret123", json);
    }

    // ── LoadAndDelete 基本功能 ──

    [Fact]
    public void LoadAndDelete_ReturnsNull_WhenFileDoesNotExist()
    {
        var result = _service.LoadAndDelete();

        Assert.Null(result);
    }

    [Fact]
    public void LoadAndDelete_ReturnsData_WhenFileExists()
    {
        var original = CreateSampleResult();
        _service.Save(original);

        var loaded = _service.LoadAndDelete();

        Assert.NotNull(loaded);
        Assert.Equal(original.ArchiveFileName, loaded!.ArchiveFileName);
        Assert.Equal(original.Password, loaded.Password);
        Assert.Equal(original.OutputDirectory, loaded.OutputDirectory);
        Assert.Equal(original.WasAutoClose, loaded.WasAutoClose);
    }

    [Fact]
    public void LoadAndDelete_DeletesFile_AfterReading()
    {
        _service.Save(CreateSampleResult());
        Assert.True(File.Exists(_tempFile), "Save 后文件应存在");

        _ = _service.LoadAndDelete();

        Assert.False(File.Exists(_tempFile), "LoadAndDelete 后文件应被删除");
    }

    // ── 往返一致性 ──

    [Fact]
    public void SaveThenLoad_PreservesAllFields()
    {
        var original = new LastExtractResult
        {
            ArchiveFileName = "archive.7z",
            ArchiveFilePath = @"D:\Downloads\archive.7z",
            Password = "p@$$w0rd!",
            OutputDirectory = @"D:\Downloads\archive",
            ExtractTime = new DateTime(2025, 6, 15, 14, 30, 0),
            WasAutoClose = true,
        };

        _service.Save(original);
        var loaded = _service.LoadAndDelete();

        Assert.NotNull(loaded);
        Assert.Equal(original.ArchiveFileName, loaded!.ArchiveFileName);
        Assert.Equal(original.ArchiveFilePath, loaded.ArchiveFilePath);
        Assert.Equal(original.Password, loaded.Password);
        Assert.Equal(original.OutputDirectory, loaded.OutputDirectory);
        Assert.Equal(original.ExtractTime, loaded.ExtractTime);
        Assert.Equal(original.WasAutoClose, loaded.WasAutoClose);
    }

    // ── 损坏文件 ──

    [Fact]
    public void LoadAndDelete_ReturnsNull_WhenFileIsCorrupted()
    {
        File.WriteAllText(_tempFile, "not valid json {{{");

        var result = _service.LoadAndDelete();

        // 损坏文件应返回 null，不抛异常
        Assert.Null(result);
        // 损坏文件应被清理
        Assert.False(File.Exists(_tempFile));
    }

    // ── 覆盖写入 ──

    [Fact]
    public void Save_OverwritesPreviousFile()
    {
        _service.Save(new LastExtractResult
        {
            ArchiveFileName = "old.zip",
            Password = "old-pw",
        });

        _service.Save(new LastExtractResult
        {
            ArchiveFileName = "new.zip",
            Password = "new-pw",
        });

        var loaded = _service.LoadAndDelete();

        Assert.NotNull(loaded);
        Assert.Equal("new.zip", loaded!.ArchiveFileName);
        Assert.Equal("new-pw", loaded.Password);
    }

    private static LastExtractResult CreateSampleResult() => new()
    {
        ArchiveFileName = "test.zip",
        ArchiveFilePath = @"C:\Downloads\test.zip",
        Password = "secret123",
        OutputDirectory = @"C:\Downloads\test",
        ExtractTime = DateTime.Now,
        WasAutoClose = false,
    };
}
