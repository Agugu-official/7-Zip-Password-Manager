using System.IO;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// PasswordRepository 功能性测试：真实文件 I/O，临时路径隔离。
/// 覆盖 pws.json 读写、新旧格式、异常格式与 EnsureFileExists。
/// </summary>
public class PasswordRepositoryFunctionalTests : IDisposable
{
    private readonly string _tempFilePath;

    public PasswordRepositoryFunctionalTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"7zpm_pws_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
            File.Delete(_tempFilePath);
    }

    // ── EnsureFileExists ──

    [Fact]
    public void EnsureFileExists_CreatesDirectoryAndEmptyFile_WhenPathInNewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"7zpm_pws_dir_{Guid.NewGuid():N}");
        var filePath = Path.Combine(dir, "pws.json");
        try
        {
            var repo = new PasswordRepository(filePath);
            var (success, error) = repo.EnsureFileExists();

            Assert.True(success, error ?? "Expected success");
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(filePath));
            var loaded = repo.Load();
            Assert.Empty(loaded);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }

    [Fact]
    public void EnsureFileExists_ReturnsTrue_WhenFileAlreadyExists()
    {
        var repo = new PasswordRepository(_tempFilePath);
        repo.Save(new List<PasswordEntry>());
        Assert.True(File.Exists(_tempFilePath));

        var (success, error) = repo.EnsureFileExists();
        Assert.True(success, error ?? "Expected success");
    }

    // ── Save then Load (wrapped format) ──

    [Fact]
    public void SaveThenLoad_PreservesEntries_WrappedFormat()
    {
        var repo = new PasswordRepository(_tempFilePath);
        var entries = new List<PasswordEntry>
        {
            new()
            {
                Password = "p1",
                UseCount = 1,
                SuccessCount = 1,
                LastUsedTime = new DateTime(2025, 1, 1),
                SuccessArchives = ["a.7z", "b.zip"]
            },
            new() { Password = "p2", UseCount = 2, SuccessCount = 0 }
        };
        repo.Save(entries);

        var loaded = repo.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("p1", loaded[0].Password);
        Assert.Equal(1, loaded[0].UseCount);
        Assert.Equal(1, loaded[0].SuccessCount);
        Assert.Equal(2, loaded[0].SuccessArchives.Count);
        Assert.Contains("a.7z", loaded[0].SuccessArchives);
        Assert.Equal("p2", loaded[1].Password);
        Assert.Equal(2, loaded[1].UseCount);
        Assert.Equal(0, loaded[1].SuccessCount);
    }

    // ── Load legacy array format ──

    [Fact]
    public void Load_ReturnsEntries_WhenLegacyArrayFormat()
    {
        var json = """[{"Password":"a","SuccessArchives":[]}]""";
        File.WriteAllText(_tempFilePath, json);
        var repo = new PasswordRepository(_tempFilePath);

        var loaded = repo.Load();
        Assert.Single(loaded);
        Assert.Equal("a", loaded[0].Password);
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenLegacyEmptyArray()
    {
        File.WriteAllText(_tempFilePath, "[]");
        var repo = new PasswordRepository(_tempFilePath);
        var loaded = repo.Load();
        Assert.Empty(loaded);
    }

    // ── Load invalid / wrong format ──

    [Fact]
    public void Load_ThrowsInvalidPasswordFileException_WhenWrongFormatIdentifier()
    {
        var json = """{"Format":"other","Version":1,"Entries":[]}""";
        File.WriteAllText(_tempFilePath, json);
        var repo = new PasswordRepository(_tempFilePath);

        var ex = Assert.Throws<InvalidPasswordFileException>(() => repo.Load());
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenFileEmptyOrWhitespace()
    {
        var repo = new PasswordRepository(_tempFilePath);
        File.WriteAllText(_tempFilePath, "");
        Assert.Empty(repo.Load());

        File.WriteAllText(_tempFilePath, "   ");
        Assert.Empty(repo.Load());
    }

    [Fact]
    public void Load_ThrowsInvalidPasswordFileException_WhenInvalidJson()
    {
        File.WriteAllText(_tempFilePath, "{ invalid json ");
        var repo = new PasswordRepository(_tempFilePath);

        Assert.Throws<InvalidPasswordFileException>(() => repo.Load());
    }

    [Fact]
    public void Load_ThrowsInvalidPasswordFileException_WhenLegacyArrayWithNoPasswordProperty()
    {
        var json = """[{"NotPassword":"x"}]""";
        File.WriteAllText(_tempFilePath, json);
        var repo = new PasswordRepository(_tempFilePath);

        Assert.Throws<InvalidPasswordFileException>(() => repo.Load());
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"7zpm_nonexist_{Guid.NewGuid():N}.json");
        var repo = new PasswordRepository(path);
        Assert.Empty(repo.Load());
    }
}
