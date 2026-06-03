using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Services;

public class SevenZipService : ISevenZipService
{
    private readonly string _sevenZipPath;

    public SevenZipService(string sevenZipPath = "")
    {
        _sevenZipPath = string.IsNullOrEmpty(sevenZipPath)
            ? AppConstants.SevenZipCandidatePaths[0]
            : sevenZipPath;
    }

    public bool IsAvailable => File.Exists(_sevenZipPath);

    /// <summary>
    /// 用 7z t 测试密码是否正确，返回 true 表示密码正确。
    /// 约定：password 为空或 null 时，不添加 -p 参数，相当于“无密码测试”。
    /// </summary>
    public async Task<bool> TestPasswordAsync(string archivePath, string password,
        CancellationToken cancellationToken = default)
    {
        var exitCode = await RunProcessAsync(
            BuildTestArgs(archivePath, password), cancellationToken, timeoutMs: 15_000);
        return exitCode == 0;
    }

    /// <summary>
    /// 用 7z x 解压文件，返回 true 表示解压成功。
    /// 约定：password 为空或 null 时，不添加 -p 参数，相当于“无密码解压”。
    /// 当 progress 非 null 时使用 -bsp1 从 stdout 解析进度并报告。
    /// </summary>
    public async Task<bool> ExtractAsync(string archivePath, string password,
        string outputDirectory, CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        if (progress is null)
        {
            var exitCode = await RunProcessAsync(
                BuildExtractArgs(archivePath, password, outputDirectory, withProgress: false),
                cancellationToken);
            return exitCode == 0;
        }

        return await RunExtractWithProgressAsync(archivePath, password, outputDirectory, progress, cancellationToken);
    }

    /// <summary>
    /// 构造“测试密码”命令（7z t）的参数列表。
    /// 各参数作为独立元素经 ProcessStartInfo.ArgumentList 传递，转义由 .NET 运行时负责，
    /// 可正确处理含空格 / 引号 / 反斜杠（含尾部反斜杠）的密码与路径——
    /// 从根本上避免手写命令行拼接的转义缺陷（BUG-1 及其同源隐患）。
    /// password 为空时省略 -p（无密码测试）。
    /// </summary>
    internal static List<string> BuildTestArgs(string archivePath, string password)
    {
        var args = new List<string> { "t", archivePath };
        if (!string.IsNullOrEmpty(password))
            args.Add("-p" + password);
        args.Add("-bso0");
        args.Add("-bsp0");
        return args;
    }

    /// <summary>
    /// 构造“解压”命令（7z x）的参数列表。withProgress 决定使用 -bsp1（报告进度）还是 -bsp0。
    /// 输出目录作为 -o 的一部分整体传入（如 -oD:\out\），即便以反斜杠结尾也由运行时正确转义。
    /// </summary>
    internal static List<string> BuildExtractArgs(string archivePath, string password,
        string outputDirectory, bool withProgress)
    {
        var args = new List<string> { "x", archivePath };
        if (!string.IsNullOrEmpty(password))
            args.Add("-p" + password);
        args.Add("-o" + outputDirectory);
        args.Add("-aoa");
        args.Add(withProgress ? "-bsp1" : "-bsp0");
        return args;
    }

    /// <summary>
    /// 使用 -bsp1 解压并逐行解析 stdout 中的百分比，通过 progress 报告。7z 输出行可能含 "xx%" 或 "  xx%".
    /// </summary>
    private async Task<bool> RunExtractWithProgressAsync(string archivePath, string password,
        string outputDirectory, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var arg in BuildExtractArgs(archivePath, password, outputDirectory, withProgress: true))
            startInfo.ArgumentList.Add(arg);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        var progressRegex = new Regex(@"(\d{1,3})\s*%", RegexOptions.Compiled);

        void OnOutputLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            var m = progressRegex.Match(e.Data);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pctVal))
                progress.Report(Math.Clamp(pctVal, 0, 100));
        }

        process.OutputDataReceived += OnOutputLine;
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }
        finally
        {
            process.OutputDataReceived -= OnOutputLine;
        }

        return process.ExitCode == 0;
    }

    private async Task<int> RunProcessAsync(IReadOnlyList<string> arguments,
        CancellationToken cancellationToken, int? timeoutMs = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            if (timeoutMs is { } ms)
            {
                using var timeoutCts = new CancellationTokenSource(ms);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return 1;
                }
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }

        return process.ExitCode;
    }
}
