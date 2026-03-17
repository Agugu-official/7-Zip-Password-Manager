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
        string args;
        if (string.IsNullOrEmpty(password))
        {
            // 无密码测试：不添加 -p
            args = $"t \"{archivePath}\" -bso0 -bsp0";
        }
        else
        {
            var escaped = EscapeForQuotedArg(password);
            args = $"t \"{archivePath}\" -p\"{escaped}\" -bso0 -bsp0";
        }

        var exitCode = await RunProcessAsync(args, cancellationToken, timeoutMs: 15_000);
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
            string args;
            if (string.IsNullOrEmpty(password))
                args = $"x \"{archivePath}\" -o\"{outputDirectory}\" -aoa -bsp0";
            else
            {
                var escaped = EscapeForQuotedArg(password);
                args = $"x \"{archivePath}\" -p\"{escaped}\" -o\"{outputDirectory}\" -aoa -bsp0";
            }
            var exitCode = await RunProcessAsync(args, cancellationToken);
            return exitCode == 0;
        }

        return await RunExtractWithProgressAsync(archivePath, password, outputDirectory, progress, cancellationToken);
    }

    /// <summary>
    /// 使用 -bsp1 解压并逐行解析 stdout 中的百分比，通过 progress 报告。7z 输出行可能含 "xx%" 或 "  xx%".
    /// </summary>
    private async Task<bool> RunExtractWithProgressAsync(string archivePath, string password,
        string outputDirectory, IProgress<double> progress, CancellationToken cancellationToken)
    {
        string args;
        if (string.IsNullOrEmpty(password))
            args = $"x \"{archivePath}\" -o\"{outputDirectory}\" -aoa -bsp1";
        else
        {
            var escaped = EscapeForQuotedArg(password);
            args = $"x \"{archivePath}\" -p\"{escaped}\" -o\"{outputDirectory}\" -aoa -bsp1";
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _sevenZipPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            },
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

    /// <summary>
    /// 转义用于 Windows 带引号参数中的特殊字符。
    /// 规则：先将 \ 后紧跟 " 的序列中的 \ 翻倍，再将 " 转为 \"。
    /// </summary>
    private static string EscapeForQuotedArg(string value)
    {
        if (!value.Contains('"') && !value.Contains('\\'))
            return value;

        var sb = new System.Text.StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                int backslashCount = 0;
                while (i < value.Length && value[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i < value.Length && value[i] == '"')
                {
                    sb.Append('\\', backslashCount * 2);
                    sb.Append("\\\"");
                }
                else
                {
                    sb.Append('\\', backslashCount);
                    i--;
                }
            }
            else if (value[i] == '"')
            {
                sb.Append("\\\"");
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    private async Task<int> RunProcessAsync(string arguments, CancellationToken cancellationToken, int? timeoutMs = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _sevenZipPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            },
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
