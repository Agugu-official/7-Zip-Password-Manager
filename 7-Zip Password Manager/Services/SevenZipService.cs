using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Services;

public class SevenZipService : ISevenZipService
{
    private readonly string _sevenZipPath;

    /// <summary>
    /// 每个压缩包的探测结果缓存：避免对同一压缩包重复列举。
    /// 键含路径 + 最后写入时间 + 大小，文件变化时自动失效。
    /// 用 Lazy 保证并行测试同一压缩包时只列举一次。
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<ArchiveProbe>>> _probeCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>列举（探测）压缩包的看门狗超时：列举只读元数据，很快，超时仅作防卡死。</summary>
    private const int ProbeTimeoutMs = 30_000;

    public SevenZipService(string sevenZipPath = "")
    {
        _sevenZipPath = string.IsNullOrEmpty(sevenZipPath)
            ? AppConstants.SevenZipCandidatePaths[0]
            : sevenZipPath;
    }

    public bool IsAvailable => File.Exists(_sevenZipPath);

    /// <summary>
    /// 测试密码是否正确，返回 true 表示密码正确。
    /// 优化：尽量只验证“密码”本身而非解压整个压缩包：
    ///  - 头部加密的 7z：用 7z l -p 列举验证（成功即密码正确，完全不解压文件数据，最快）。
    ///  - 其它（数据加密、头部明文）：只测试包内一个最小/最靠前的文件，避免整包解压。
    ///  - 无法安全定位测试条目时回退为整包测试（错误密码仍返回非 0，不会误判为正确）。
    /// 约定：password 为空或 null 时不加 -p，相当于“无密码测试”，保持整包语义。
    /// 注意：单次测试不再设置超时（改为依赖取消令牌），避免大压缩包下正确密码因超时被误判为错误。
    /// </summary>
    public async Task<bool> TestPasswordAsync(string archivePath, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            // 无密码测试：保持整包语义（仅开始时执行一次，不在逐个密码的热路径上）。
            var exitNoPwd = await RunProcessAsync(BuildTestArgs(archivePath, password), cancellationToken);
            return exitNoPwd == 0;
        }

        var probe = await GetProbeAsync(archivePath, cancellationToken);

        IReadOnlyList<string> args = probe switch
        {
            { IsHeaderEncrypted: true } => BuildHeaderVerifyArgs(archivePath, password),
            { TestEntryName: { } entry } => BuildTestEntryArgs(archivePath, password, entry),
            _ => BuildTestArgs(archivePath, password),
        };

        var exitCode = await RunProcessAsync(args, cancellationToken);
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

    // ── 参数构造 ──

    /// <summary>
    /// 构造“测试密码”命令（7z t 整包）的参数列表。
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
    /// 构造“只测试包内单个文件”的参数列表。entryName 作为独立元素传递，
    /// 转义交由运行时；调用方须保证 entryName 不含通配符且不以 '-' 开头（见 <see cref="PickTestEntry"/>）。
    /// </summary>
    internal static List<string> BuildTestEntryArgs(string archivePath, string password, string entryName)
    {
        var args = new List<string> { "t", archivePath };
        if (!string.IsNullOrEmpty(password))
            args.Add("-p" + password);
        args.Add("-bso0");
        args.Add("-bsp0");
        args.Add(entryName);
        return args;
    }

    /// <summary>
    /// 构造“头部加密包用列举验证密码”的参数列表（7z l -p）。
    /// 列举成功（退出码 0）即代表密码正确，且完全不解压任何文件数据。
    /// </summary>
    internal static List<string> BuildHeaderVerifyArgs(string archivePath, string password)
    {
        var args = new List<string> { "l", archivePath };
        if (!string.IsNullOrEmpty(password))
            args.Add("-p" + password);
        args.Add("-bso0");
        return args;
    }

    /// <summary>
    /// 构造“探测列举”的参数列表（7z l -slt -sccUTF-8）。-sccUTF-8 保证非 ASCII 文件名以 UTF-8 输出，
    /// 便于正确解析并回传匹配。可选 nameFilter 用于二次校验某条目确实存在。
    /// </summary>
    internal static List<string> BuildListArgs(string archivePath, string? nameFilter)
    {
        var args = new List<string> { "l", "-slt", "-sccUTF-8", archivePath };
        if (!string.IsNullOrEmpty(nameFilter))
            args.Add(nameFilter);
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

    // ── 压缩包探测：决定如何“轻量”验证密码 ──

    /// <summary>
    /// 探测结果：是否头部加密；若否，可用于测试的单个内部条目名（null 表示回退整包测试）。
    /// </summary>
    private sealed record ArchiveProbe(bool IsHeaderEncrypted, string? TestEntryName)
    {
        public static readonly ArchiveProbe WholeArchive = new(false, null);
        public static readonly ArchiveProbe HeaderEncrypted = new(true, null);
    }

    private Task<ArchiveProbe> GetProbeAsync(string archivePath, CancellationToken cancellationToken)
    {
        var key = BuildProbeKey(archivePath);
        var lazy = _probeCache.GetOrAdd(key,
            _ => new Lazy<Task<ArchiveProbe>>(() => ProbeArchiveAsync(archivePath)));
        return AwaitProbeAsync(key, lazy, cancellationToken);
    }

    private async Task<ArchiveProbe> AwaitProbeAsync(
        string key, Lazy<Task<ArchiveProbe>> lazy, CancellationToken cancellationToken)
    {
        try
        {
            // WaitAsync：调用方取消时停止等待，但不会取消（污染）其它调用方共享的探测任务。
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Archive probe await failed: {ex.Message}");
            _probeCache.TryRemove(key, out _);
            return ArchiveProbe.WholeArchive;
        }
    }

    private static string BuildProbeKey(string archivePath)
    {
        try
        {
            var info = new FileInfo(archivePath);
            return $"{archivePath}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        }
        catch
        {
            return archivePath;
        }
    }

    /// <summary>
    /// 列举压缩包以决定验证策略：
    ///  - 列举失败（非 0 退出）通常意味着头部加密 → 用 l -p 验证密码。
    ///  - 列举成功 → 解析条目并挑选一个最小/最靠前的文件作为测试目标，再二次校验该名称确实匹配，
    ///    以杜绝“内部名不匹配 → 任何密码都返回成功”的误判。
    /// 任何异常或超时都安全回退为整包测试。
    /// </summary>
    private async Task<ArchiveProbe> ProbeArchiveAsync(string archivePath)
    {
        try
        {
            var (exitCode, stdout, timedOut) = await RunListAsync(archivePath, null);
            if (timedOut)
                return ArchiveProbe.WholeArchive;
            if (exitCode != 0)
                return ArchiveProbe.HeaderEncrypted;

            var candidate = PickTestEntry(stdout);
            if (candidate is null)
                return ArchiveProbe.WholeArchive;

            // 二次校验：用名称过滤再列举一次，确认该名称确实匹配到条目（仅靠退出码不够，
            // 因为名称不匹配时 7z 仍返回 0），从而避免把错误密码误判为正确。
            var (verifyCode, verifyOut, verifyTimedOut) = await RunListAsync(archivePath, candidate);
            if (verifyTimedOut || verifyCode != 0 || !ListingContainsEntry(verifyOut, candidate))
                return ArchiveProbe.WholeArchive;

            return new ArchiveProbe(false, candidate);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Archive probe failed: {ex.Message}");
            return ArchiveProbe.WholeArchive;
        }
    }

    /// <summary>
    /// 运行 7z l -slt -sccUTF-8 archive [name]（关闭 stdin，避免头部加密时交互等待密码而卡死）。
    /// 返回退出码、UTF-8 解码后的 stdout、是否超时。
    /// </summary>
    private async Task<(int ExitCode, string StdOut, bool TimedOut)> RunListAsync(
        string archivePath, string? nameFilter)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in BuildListArgs(archivePath, nameFilter))
            startInfo.ArgumentList.Add(arg);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        // 排空 stderr，避免管道缓冲写满导致进程阻塞。
        process.ErrorDataReceived += static (_, _) => { };

        process.Start();
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(ProbeTimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            return (1, string.Empty, true);
        }

        var stdout = await stdoutTask;
        return (process.ExitCode, stdout, false);
    }

    private static readonly char[] UnsafeEntryChars = ['*', '?'];

    /// <summary>
    /// 从 7z l -slt 输出中挑选一个用于测试密码的内部文件：
    ///  - 跳过目录与空文件；跳过名称含通配符或以 '-' 开头（会被当作开关）的条目。
    ///  - 优先选“已加密”的文件，避免在混合加密包中选到明文文件而误判。
    ///  - solid 包：选最低块中最靠前的文件（解压成本最小）。
    ///  - 非 solid / zip：选体积最小的文件。
    /// 返回 null 表示无合适条目，调用方回退为整包测试。
    /// </summary>
    internal static string? PickTestEntry(string sltOutput)
    {
        var (entries, anyEncryptedField) = ParseEntries(sltOutput);

        var candidates = entries
            .Where(e => !e.IsDir && e.Size > 0
                        && e.Path.IndexOfAny(UnsafeEntryChars) < 0
                        && !e.Path.StartsWith('-'))
            .ToList();
        if (candidates.Count == 0)
            return null;

        List<ProbeEntry> pool;
        var encrypted = candidates.Where(e => e.Encrypted).ToList();
        if (encrypted.Count > 0)
            pool = encrypted;
        else if (!anyEncryptedField)
            pool = candidates;   // 该格式未提供 Encrypted 字段，按最小文件测试
        else
            return null;         // 明确没有加密文件 → 交回整包测试以防误判

        bool isSolid = pool.Where(e => e.Block.HasValue)
                           .GroupBy(e => e.Block!.Value)
                           .Any(g => g.Count() > 1);

        ProbeEntry pick;
        if (isSolid)
        {
            var minBlock = pool.Where(e => e.Block.HasValue).Min(e => e.Block!.Value);
            pick = pool.Where(e => e.Block == minBlock).OrderBy(e => e.Index).First();
        }
        else
        {
            pick = pool.OrderBy(e => e.Size).ThenBy(e => e.Index).First();
        }

        return pick.Path;
    }

    /// <summary>
    /// 解析 7z l -slt 输出中分隔线 “----------” 之后的文件条目块。
    /// 返回条目列表，以及该输出是否出现过 Encrypted 字段（用于区分“格式不提供”与“确实未加密”）。
    /// </summary>
    private static (List<ProbeEntry> Entries, bool AnyEncryptedField) ParseEntries(string sltOutput)
    {
        var entries = new List<ProbeEntry>();
        var anyEncryptedField = false;
        if (string.IsNullOrEmpty(sltOutput))
            return (entries, anyEncryptedField);

        var lines = sltOutput.Split('\n');

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').StartsWith("----------", StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }
        if (start < 0)
            return (entries, anyEncryptedField);

        string? path = null;
        long size = 0;
        int? block = null;
        bool isDir = false;
        bool encrypted = false;
        int index = 0;

        void Flush()
        {
            if (path is not null)
                entries.Add(new ProbeEntry(path, size, block, isDir, encrypted, index++));
            path = null; size = 0; block = null; isDir = false; encrypted = false;
        }

        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0) continue;
            var key = line[..eq];
            var value = line[(eq + 3)..];

            switch (key)
            {
                case "Path": path = value; break;
                case "Size": long.TryParse(value, out size); break;
                case "Block": if (int.TryParse(value, out var b)) block = b; break;
                case "Folder": if (value == "+") isDir = true; break;
                case "Attributes": if (value.StartsWith('D')) isDir = true; break;
                case "Encrypted": anyEncryptedField = true; encrypted = value == "+"; break;
            }
        }
        Flush();

        return (entries, anyEncryptedField);
    }

    private static bool ListingContainsEntry(string sltOutput, string entryName)
    {
        var (entries, _) = ParseEntries(sltOutput);
        return entries.Any(e => string.Equals(e.Path, entryName, StringComparison.Ordinal));
    }

    private sealed record ProbeEntry(string Path, long Size, int? Block, bool IsDir, bool Encrypted, int Index);
}
