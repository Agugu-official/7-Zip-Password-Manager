namespace _7_Zip_Password_Manager.Constants;

/// <summary>
/// 应用级常量：固定不变、不需要用户配置的标识符和默认值。
/// 可配置项请放在 <see cref="Data.AppConfig"/>。
/// </summary>
public static class AppConstants
{
    // ── 单实例 / IPC ──

    public const string MutexName = "7ZPM_SingleInstance";
    public const string PipeName = "7ZPM_IPC_Pipe";
    public const int IpcConnectTimeoutMs = 3000;
    public const int IpcRetryDelayMs = 200;

    // ── UI 布局 ──

    public const double ColumnMinWidth = 28.0;

    // ── 文件与文件夹名称 ──

    public const string PasswordFolderName = "pws";
    public const string ConfigFolderName = "config";
    public const string DefaultPasswordFileName = "pws.json";
    public const string ConfigFileName = "config.json";
    public const string GuiTextFileName = "gui.json";
    public const string LastExtractFileName = "last_extract.json";
    public const string LogFileName = "app.log";

    // ── 格式标识与版本 ──

    public const string ConfigFormat = "7zpm-config";
    public const string PasswordFileFormat = "7zpm-passwords";
    public const string GuiFormat = "7zpm-gui";
    public const int CurrentConfigVersion = 1;
    public const int CurrentPasswordFileVersion = 1;
    public const int CurrentGuiVersion = 1;

    // ── 7-Zip 检测 ──

    public const string DefaultBrowseDirectory = @"C:\Program Files";

    public static readonly string[] SevenZipCandidatePaths =
    [
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files\7-Zip-Zstandard\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip-Zstandard\7z.exe",
    ];

    /// <summary>HKLM 64-bit view: 64-bit 7-Zip 或 32-bit 安装时部分写入此处。</summary>
    public static readonly string[] SevenZipRegistryKeys =
    [
        @"SOFTWARE\7-Zip",
        @"SOFTWARE\7-Zip-Zstandard",
    ];

    /// <summary>HKLM 32-bit view (Wow6432Node)：32 位 7-Zip 在 64 位系统上的注册表。</summary>
    public static readonly string[] SevenZipRegistryKeysWow6432Node =
    [
        @"SOFTWARE\Wow6432Node\7-Zip",
        @"SOFTWARE\Wow6432Node\7-Zip-Zstandard",
    ];

    /// <summary>HKCU：当前用户安装的 7-Zip。</summary>
    public static readonly string[] SevenZipRegistryKeysHkcu =
    [
        @"SOFTWARE\7-Zip",
        @"SOFTWARE\7-Zip-Zstandard",
    ];

    // ── 右键菜单 ──

    public const string ContextMenuName = "7ZPM_SmartExtract";
    public const int ContextMenuMuiStringId = 101;
    public const string ContextMenuMuiDllName = "7ZPM.Strings.dll";

    /// <summary>旧版「所有文件」注册路径，用于注册/注销时清理残留。</summary>
    public const string ContextMenuRegistryPathLegacy = @"Software\Classes\*\shell\" + ContextMenuName;

    /// <summary>右键菜单仅对以下扩展名显示，与 7-Zip / filterArchive 对齐。元素含前导点，如 .7z。</summary>
    public static readonly string[] ContextMenuArchiveExtensions =
    [
        ".7z", ".zip", ".rar", ".tar", ".gz", ".bz2", ".xz", ".zst", ".lz", ".lzma",
        ".cab", ".iso", ".wim", ".arj", ".lzh", ".z", ".cpio", ".rpm", ".deb",
        ".tgz", ".tbz2", ".txz", ".001", ".r00", ".r01", ".z01",
    ];

    /// <summary>返回某扩展名对应的右键菜单注册表路径。ext 需含前导点，如 .7z。</summary>
    public static string GetContextMenuRegistryPathForExtension(string ext) =>
        @"Software\Classes\" + ext + @"\shell\" + ContextMenuName;

    /// <summary>7-Zip 关联压缩格式时使用的 ProgID；在此 ProgID 下注册后，右击已关联 7-Zip 的压缩文件才会显示我们的菜单。</summary>
    public const string SevenZipProgId = "7z_auto_file";

    /// <summary>右键菜单在 ProgID 下的注册表路径（与 7-Zip 显示范围一致）。</summary>
    public static string ContextMenuRegistryPathForProgId =>
        @"Software\Classes\" + SevenZipProgId + @"\shell\" + ContextMenuName;

    // ── 默认值 ──

    public const string DefaultLanguage = "zh-CN";
}
