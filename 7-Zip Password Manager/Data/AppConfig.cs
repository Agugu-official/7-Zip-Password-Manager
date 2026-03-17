using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Data;

public class ColumnWidthsConfig
{
    public double Password { get; set; }
    public double SuccessCount { get; set; }
    public double UseCount { get; set; }
    public double LastUsed { get; set; }
    public double Success { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Password <= 0 && SuccessCount <= 0 && UseCount <= 0 && LastUsed <= 0 && Success <= 0;
}

/// <summary>
/// 密码排名评分权重配置。
/// 总分 = SceneMatchScore + RecentSuccessMaxScore + SuccessRateMaxScore + ManualPriorityMaxScore。
/// </summary>
public class RankingConfig
{
    public double SceneMatchScore { get; set; } = 40;
    public double RecentSuccessMaxScore { get; set; } = 25;
    public double SuccessRateMaxScore { get; set; } = 20;
    public int ManualPriorityMaxScore { get; set; } = 15;
    public double UnusedDefaultScore { get; set; } = 10;
    public double DecayDays { get; set; } = 365;
}

public class AppConfig
{
    public const string ExpectedFormat = AppConstants.ConfigFormat;
    public const int CurrentVersion = AppConstants.CurrentConfigVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 格式标识，不设默认值；旧版文件反序列化后为 null，视为合法（向下兼容）。
    /// </summary>
    public string? Format { get; set; }
    public int Version { get; set; }

    public bool IsDarkMode { get; set; }
    public bool IsTopMost { get; set; }
    public bool CloseAfterContextMenuExtract { get; set; } = true;
    public string PasswordFilePath { get; set; } = AppDataPaths.DefaultPasswordFile;
    public string SevenZipPath { get; set; } = string.Empty;
    public int MaxParallelism { get; set; } = 2;
    public string Language { get; set; } = AppConstants.DefaultLanguage;
    public int AutoCloseDelayMs { get; set; } = 1000;
    public long LogFileMaxSizeBytes { get; set; } = 512 * 1024;
    public ColumnWidthsConfig? ColumnWidths { get; set; }
    public RankingConfig? Ranking { get; set; }

    /// <summary>
    /// 是否已显示过首次启动向导。默认 true，使老用户（配置中无此键）不再弹出向导；
    /// 仅当无配置文件时在 Load() 中设为 false。
    /// </summary>
    public bool FirstRunWizardShown { get; set; } = true;

    public static int CpuCoreCount => Environment.ProcessorCount;
    public static string DefaultPasswordFile => AppDataPaths.DefaultPasswordFile;

    /// <summary>
    /// 自动检测 7z.exe 路径：进程目录 → 固定路径 → HKLM 注册表 → Wow6432Node → PATH → HKCU。
    /// 支持官方 7-Zip、7-Zip-Zstandard 及非默认安装（PATH、32 位注册表、当前用户安装）。
    /// </summary>
    public static string Detect7ZipPath()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var local7z = Path.Combine(exeDir, "7z.exe");
            if (File.Exists(local7z))
                return local7z;
        }

        foreach (var path in AppConstants.SevenZipCandidatePaths)
        {
            if (File.Exists(path))
                return path;
        }

        foreach (var keyPath in AppConstants.SevenZipRegistryKeys)
        {
            var found = TryGet7ZipPathFromRegistry(Microsoft.Win32.Registry.LocalMachine, keyPath);
            if (!string.IsNullOrEmpty(found))
                return found;
        }

        foreach (var keyPath in AppConstants.SevenZipRegistryKeysWow6432Node)
        {
            var found = TryGet7ZipPathFromRegistry(Microsoft.Win32.Registry.LocalMachine, keyPath);
            if (!string.IsNullOrEmpty(found))
                return found;
        }

        var fromPath = Detect7ZipPathFromPath();
        if (!string.IsNullOrEmpty(fromPath))
            return fromPath;

        foreach (var keyPath in AppConstants.SevenZipRegistryKeysHkcu)
        {
            var found = TryGet7ZipPathFromRegistry(Microsoft.Win32.Registry.CurrentUser, keyPath);
            if (!string.IsNullOrEmpty(found))
                return found;
        }

        return string.Empty;
    }

    private static string TryGet7ZipPathFromRegistry(Microsoft.Win32.RegistryKey hive, string keyPath)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue("Path") is string regPath && !string.IsNullOrWhiteSpace(regPath))
            {
                var full = Path.Combine(regPath.Trim(), "7z.exe");
                if (File.Exists(full))
                    return full;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string Detect7ZipPathFromPath()
    {
        var pathEnv = string.Empty;
        try
        {
            var machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            pathEnv = $"{machine ?? ""};{user ?? ""}";
        }
        catch { }

        if (string.IsNullOrWhiteSpace(pathEnv))
            return string.Empty;

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = Path.Combine(dir.Trim(), "7z.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return string.Empty;
    }

    /// <summary>
    /// 返回有效的 7z.exe 路径：已配置 > 自动检测
    /// </summary>
    public string GetEffective7ZipPath()
    {
        if (!string.IsNullOrEmpty(SevenZipPath) && File.Exists(SevenZipPath))
            return SevenZipPath;

        return Detect7ZipPath();
    }

    /// <summary>
    /// 从 JSON 字符串反序列化配置。不涉及文件 I/O，适合单元测试。
    /// </summary>
    public static AppConfig? LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        if (config is null) return null;

        if (config.Format is not null &&
            !string.Equals(config.Format, ExpectedFormat, StringComparison.OrdinalIgnoreCase))
            return null;

        return config;
    }

    /// <summary>
    /// 将当前配置序列化为 JSON 字符串。不涉及文件 I/O，适合单元测试。
    /// </summary>
    public string ToJson()
    {
        Format = ExpectedFormat;
        Version = CurrentVersion;
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static AppConfig Load()
    {
        MigrateLegacyConfigFile();

        try
        {
            if (File.Exists(AppDataPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppDataPaths.ConfigFile);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (config is null)
                        return new AppConfig();

                    if (config.Format is not null &&
                        !string.Equals(config.Format, ExpectedFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.TraceWarning(
                            $"配置文件格式标识不匹配（期望 \"{ExpectedFormat}\"，" +
                            $"实际 \"{config.Format}\"），已使用默认配置。");
                        return new AppConfig();
                    }

                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"加载配置失败: {ex.Message}");
        }

        var fresh = new AppConfig();
        var noFile = !File.Exists(AppDataPaths.ConfigFile);
        if (noFile)
            fresh.FirstRunWizardShown = false;
        return fresh;
    }

    public void Save()
    {
        try
        {
            Format = ExpectedFormat;
            Version = CurrentVersion;

            AppDataPaths.EnsureDirectoryExists();
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(AppDataPaths.ConfigFile, json);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"保存配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 在首次 Load 前调用，将旧版 pws/config.json 迁移到 config/config.json，便于判断“启动时配置是否存在”。
    /// </summary>
    public static void EnsureLegacyConfigMigrated()
    {
        MigrateLegacyConfigFile();
    }

    /// <summary>
    /// 将旧版 pws/config.json 迁移到 config/config.json
    /// </summary>
    private static void MigrateLegacyConfigFile()
    {
        try
        {
            var legacy = AppDataPaths.LegacyConfigFile;
            var target = AppDataPaths.ConfigFile;

            if (!File.Exists(legacy) || File.Exists(target))
                return;

            AppDataPaths.EnsureDirectoryExists();
            File.Move(legacy, target);
            Trace.TraceInformation($"已将配置文件从 {legacy} 迁移到 {target}");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"迁移配置文件失败: {ex.Message}");
        }
    }
}
