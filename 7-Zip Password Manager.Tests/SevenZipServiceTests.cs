using System.Diagnostics;
using System.IO;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// SevenZipService 参数构造测试。
/// 重构后命令参数通过 ProcessStartInfo.ArgumentList 逐个传递，转义由 .NET 运行时负责，
/// 从根本上消除了 BUG-1（密码尾部反斜杠）及其同源隐患（输出目录 / 压缩包路径未转义）。
/// 这里验证“参数列表元素是否正确”；端到端真实 7z 验证见 SevenZipServiceRealIntegrationTests。
/// </summary>
public class SevenZipArgumentBuildingTests
{
    [Fact]
    public void BuildTestArgs_IncludesPasswordAsSingleToken()
    {
        var args = SevenZipService.BuildTestArgs(@"C:\a.7z", "pwd");
        Assert.Equal(new[] { "t", @"C:\a.7z", "-ppwd", "-bso0", "-bsp0" }, args);
    }

    [Fact]
    public void BuildTestArgs_OmitsPasswordSwitch_WhenEmpty()
    {
        var args = SevenZipService.BuildTestArgs(@"C:\a.7z", "");
        Assert.Equal(new[] { "t", @"C:\a.7z", "-bso0", "-bsp0" }, args);
        Assert.DoesNotContain(args, a => a.StartsWith("-p"));
    }

    [Theory]
    [InlineData(@"secret\")]              // 尾部反斜杠（BUG-1 原始场景）
    [InlineData("a b\"c\\")]              // 空格 + 引号 + 尾部反斜杠
    [InlineData(@"C:\path with space\")]  // 类路径密码，含空格与尾部反斜杠
    public void BuildTestArgs_PasswordKeptVerbatimInSingleToken(string password)
    {
        var args = SevenZipService.BuildTestArgs("x.7z", password);
        // 密码原样拼在 -p 之后并作为单一元素；转义交由运行时，故元素内容须等于 -p + 原文
        Assert.Contains("-p" + password, args);
    }

    [Fact]
    public void BuildExtractArgs_BuildsExpectedTokens_WithProgress()
    {
        var args = SevenZipService.BuildExtractArgs(@"C:\a.7z", "pw", @"D:\out\", withProgress: true);
        Assert.Equal(new[] { "x", @"C:\a.7z", "-ppw", @"-oD:\out\", "-aoa", "-bsp1" }, args);
    }

    [Fact]
    public void BuildExtractArgs_UsesBsp0_AndOmitsPassword_WhenNoProgressNoPassword()
    {
        var args = SevenZipService.BuildExtractArgs(@"C:\a.7z", "", @"D:\out", withProgress: false);
        Assert.Equal(new[] { "x", @"C:\a.7z", @"-oD:\out", "-aoa", "-bsp0" }, args);
    }

    [Fact]
    public void BuildExtractArgs_OutputDirWithTrailingBackslash_KeptVerbatim()
    {
        // 同源隐患场景：输出目录以 \ 结尾（如盘符根目录 D:\）。作为独立 token，运行时会正确转义。
        var args = SevenZipService.BuildExtractArgs("a.7z", "pw", @"D:\", withProgress: false);
        Assert.Contains(@"-oD:\", args);
    }
}

/// <summary>
/// 端到端：用真实 7z.exe 创建带密码压缩包，再经 SevenZipService 测试 / 解压，
/// 验证“尾部反斜杠等特殊密码”能被正确传递并还原（覆盖 BUG-1 全链路）。
/// 若环境无 7z 则早退（本机及含 7z 的 CI 会真实执行）。
/// </summary>
public class SevenZipServiceRealIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _sevenZip;

    public SevenZipServiceRealIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "7zpm_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var detected = AppConfig.Detect7ZipPath();
        _sevenZip = File.Exists(detected) ? detected : null;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>用真实 7z 直接（经 ArgumentList，可靠转义）造一个加密压缩包作为夹具。</summary>
    private string CreateEncryptedArchive(string password, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, "payload.txt"), content);
        var archive = Path.Combine(_tempDir, "a_" + Guid.NewGuid().ToString("N") + ".7z");

        var psi = new ProcessStartInfo
        {
            FileName = _sevenZip!,
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "a", archive, "-mhe=on", "-p" + password, "-y", "payload.txt" })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(archive));
        return archive;
    }

    [Theory]
    [InlineData(@"secret\")]        // BUG-1 原始场景：尾部反斜杠
    [InlineData("p@ss w0rd\\")]     // 空格 + 尾部反斜杠
    public async Task TestPassword_RealArchive_TrailingBackslashPasswordWorks(string password)
    {
        if (_sevenZip is null) return; // 环境无 7z，早退

        var archive = CreateEncryptedArchive(password, "hello-content");
        var svc = new SevenZipService(_sevenZip);

        Assert.True(await svc.TestPasswordAsync(archive, password));         // 正确密码 → true
        Assert.False(await svc.TestPasswordAsync(archive, password + "X"));  // 错误密码 → false
    }

    [Fact]
    public async Task Extract_RealArchive_TrailingBackslashPassword_RestoresContent()
    {
        if (_sevenZip is null) return;

        const string password = @"unzip\me\";
        const string content = "round-trip-payload-12345";
        var archive = CreateEncryptedArchive(password, content);
        var svc = new SevenZipService(_sevenZip);
        var outDir = Path.Combine(_tempDir, "out");

        Assert.True(await svc.ExtractAsync(archive, password, outDir));

        var extracted = Path.Combine(outDir, "payload.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal(content, File.ReadAllText(extracted));
    }
}
