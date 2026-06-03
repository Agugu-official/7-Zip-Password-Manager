using System.Diagnostics;
using System.IO;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager.Tests;

/// <summary>
/// 探测/选文件纯函数测试：验证从 7z l -slt 输出中挑选“用于测试密码的内部文件”的逻辑。
/// 不依赖真实 7z，使用真实抓取的 -slt 样例文本。
/// </summary>
public class SevenZipProbeParsingTests
{
    // 7z solid 包：所有文件同属 Block 0。第一个文件不是最小，用于验证 solid 选“最靠前”而非“最小”。
    private const string Solid7zSlt =
        "Path = archive.7z\n" +
        "Type = 7z\n" +
        "Solid = +\n" +
        "\n" +
        "----------\n" +
        "Path = first.bin\n" +
        "Size = 100\n" +
        "Attributes = A\n" +
        "Encrypted = +\n" +
        "Block = 0\n" +
        "\n" +
        "Path = tiny.txt\n" +
        "Size = 2\n" +
        "Attributes = A\n" +
        "Encrypted = +\n" +
        "Block = 0\n";

    // 非 solid（zip）：无 Block，第一个是大文件，用于验证选“最小”而非“最靠前”。
    private const string ZipSlt =
        "Path = aes.zip\n" +
        "Type = zip\n" +
        "\n" +
        "----------\n" +
        "Path = big.txt\n" +
        "Folder = -\n" +
        "Size = 200002\n" +
        "Attributes = A\n" +
        "Encrypted = +\n" +
        "\n" +
        "Path = a.txt\n" +
        "Folder = -\n" +
        "Size = 3\n" +
        "Attributes = A\n" +
        "Encrypted = +\n";

    [Fact]
    public void Solid7z_PicksFirstFileInLowestBlock()
    {
        Assert.Equal("first.bin", SevenZipService.PickTestEntry(Solid7zSlt));
    }

    [Fact]
    public void NonSolidZip_PicksSmallestFile()
    {
        Assert.Equal("a.txt", SevenZipService.PickTestEntry(ZipSlt));
    }

    [Fact]
    public void SkipsDirectoriesAndEmptyFiles()
    {
        var slt =
            "----------\n" +
            "Path = somedir\n" +
            "Folder = +\n" +
            "Size = 0\n" +
            "Attributes = D\n" +
            "Encrypted = -\n" +
            "\n" +
            "Path = empty.txt\n" +
            "Folder = -\n" +
            "Size = 0\n" +
            "Encrypted = +\n" +
            "\n" +
            "Path = real.txt\n" +
            "Folder = -\n" +
            "Size = 42\n" +
            "Encrypted = +\n";
        Assert.Equal("real.txt", SevenZipService.PickTestEntry(slt));
    }

    [Fact]
    public void PrefersEncryptedFile_OverPlainSmallerFile()
    {
        // 混合加密包：更小的明文文件不应被选（否则任意密码都会“通过”而误判）。
        var slt =
            "----------\n" +
            "Path = plain_small.txt\n" +
            "Folder = -\n" +
            "Size = 1\n" +
            "Encrypted = -\n" +
            "\n" +
            "Path = secret_big.txt\n" +
            "Folder = -\n" +
            "Size = 999\n" +
            "Encrypted = +\n";
        Assert.Equal("secret_big.txt", SevenZipService.PickTestEntry(slt));
    }

    [Fact]
    public void ChineseFileName_ParsedAndPicked()
    {
        var slt =
            "----------\n" +
            "Path = 中文文件.txt\n" +
            "Folder = -\n" +
            "Size = 5\n" +
            "Encrypted = +\n";
        Assert.Equal("中文文件.txt", SevenZipService.PickTestEntry(slt));
    }

    [Fact]
    public void SkipsNamesWithWildcardOrLeadingDash()
    {
        var slt =
            "----------\n" +
            "Path = -weird.txt\n" +
            "Folder = -\n" +
            "Size = 1\n" +
            "Encrypted = +\n" +
            "\n" +
            "Path = wild*card.txt\n" +
            "Folder = -\n" +
            "Size = 1\n" +
            "Encrypted = +\n" +
            "\n" +
            "Path = good.txt\n" +
            "Folder = -\n" +
            "Size = 9\n" +
            "Encrypted = +\n";
        Assert.Equal("good.txt", SevenZipService.PickTestEntry(slt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no separator here\nPath = x.txt\nSize = 5\n")]
    public void NoUsableListing_ReturnsNull(string slt)
    {
        Assert.Null(SevenZipService.PickTestEntry(slt));
    }

    [Fact]
    public void AllEntriesPlain_WhenEncryptedFieldPresent_ReturnsNull()
    {
        // 明确存在 Encrypted 字段但全为 - → 回退整包测试（返回 null）。
        var slt =
            "----------\n" +
            "Path = a.txt\n" +
            "Folder = -\n" +
            "Size = 3\n" +
            "Encrypted = -\n";
        Assert.Null(SevenZipService.PickTestEntry(slt));
    }
}

/// <summary>
/// 端到端：用真实 7z.exe 创建各类加密包（头部加密 / 数据加密 solid / zip-AES），
/// 验证优化后的 TestPasswordAsync 在三条分支上“正确密码→true、错误密码→false（不误判）”，
/// 并覆盖含空格 / 反斜杠 / 引号的复杂密码。无 7z 环境则早退。
/// </summary>
public class SevenZipProbeRealIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _sevenZip;
    // 含空格、反斜杠、引号的复杂密码，端到端验证 ArgumentList 转义。
    private const string Pwd = "Sp ace\\and\"quote";
    private const string WrongPwd = "definitely-wrong-123";

    public SevenZipProbeRealIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "7zpm_probe_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var detected = AppConfig.Detect7ZipPath();
        _sevenZip = File.Exists(detected) ? detected : null;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void Run7z(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _sevenZip!,
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        Assert.Equal(0, p.ExitCode);
    }

    private string PrepareArchive(string name, params string[] extraSwitches)
    {
        File.WriteAllText(Path.Combine(_tempDir, "small.txt"), "hello small");
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_tempDir, "big.txt"), new string('x', 120_000));

        var args = new List<string> { "a", "-p" + Pwd, "-y" };
        args.AddRange(extraSwitches);
        args.Add(name);
        args.Add("small.txt");
        args.Add("a.txt");
        args.Add("big.txt");
        Run7z(args.ToArray());
        return Path.Combine(_tempDir, name);
    }

    [Fact]
    public async Task NonHeaderEncrypted7z_SingleFileBranch_CorrectAndWrong()
    {
        if (_sevenZip is null) return;
        var archive = PrepareArchive("noheader.7z"); // 默认：数据加密、头部明文、solid
        var svc = new SevenZipService(_sevenZip);

        Assert.True(await svc.TestPasswordAsync(archive, Pwd));
        Assert.False(await svc.TestPasswordAsync(archive, WrongPwd));
    }

    [Fact]
    public async Task HeaderEncrypted7z_ListVerifyBranch_CorrectAndWrong()
    {
        if (_sevenZip is null) return;
        var archive = PrepareArchive("header.7z", "-mhe=on"); // 头部加密
        var svc = new SevenZipService(_sevenZip);

        Assert.True(await svc.TestPasswordAsync(archive, Pwd));
        Assert.False(await svc.TestPasswordAsync(archive, WrongPwd));
    }

    [Fact]
    public async Task ZipAes_SingleFileBranch_CorrectAndWrong()
    {
        if (_sevenZip is null) return;
        var archive = PrepareArchive("aes.zip", "-tzip", "-mem=AES256");
        var svc = new SevenZipService(_sevenZip);

        Assert.True(await svc.TestPasswordAsync(archive, Pwd));
        Assert.False(await svc.TestPasswordAsync(archive, WrongPwd));
    }
}
