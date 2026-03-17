using System.IO;
using System.Runtime.InteropServices;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Helpers;
using Microsoft.Win32;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 管理 Windows 资源管理器右键菜单的注册与注销。
/// 写入 HKCU，不需要管理员权限，仅对当前用户生效。
///
/// 多语言策略（三层）：
///   1. MUIVerb — 若 7ZPM.Strings.dll 存在，写入间接字符串引用，
///      Windows Shell 按系统 UI 语言自动选取对应文本。
///   2. (Default) — 始终写入当前 App 语言的文本，作为 MUI 不可用时的回退。
///   3. RefreshIfRegistered — 每次启动时静默刷新，确保文本与当前语言同步。
/// </summary>
public static class ContextMenuService
{
    private static string MenuText => GuiText.Get("services.contextMenuText");

    public static bool IsRegistered()
    {
        var path = AppConstants.GetContextMenuRegistryPathForExtension(AppConstants.ContextMenuArchiveExtensions[0]);
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }

    /// <summary>
    /// 从注册表读取当前右键菜单指向的 exe 完整路径。未注册或无法解析时返回 null。
    /// </summary>
    public static string? GetRegisteredExePath()
    {
        foreach (var ext in AppConstants.ContextMenuArchiveExtensions)
        {
            var basePath = AppConstants.GetContextMenuRegistryPathForExtension(ext);
            using var commandKey = Registry.CurrentUser.OpenSubKey(basePath + @"\command");
            var raw = commandKey?.GetValue("") as string;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var start = raw.IndexOf('"');
            if (start < 0) continue;
            var end = raw.IndexOf('"', start + 1);
            if (end < 0) continue;
            var path = raw.Substring(start + 1, end - start - 1).Trim();
            if (!string.IsNullOrEmpty(path))
                return path;
        }
        return null;
    }

    public static void Register()
    {
        var exePath = Environment.ProcessPath
                      ?? throw new InvalidOperationException(GuiText.Get("services.cannotGetExePath"));

        var muiDll = Path.Combine(Path.GetDirectoryName(exePath)!, AppConstants.ContextMenuMuiDllName);
        var hasMui = File.Exists(muiDll);

        foreach (var ext in AppConstants.ContextMenuArchiveExtensions)
        {
            var basePath = AppConstants.GetContextMenuRegistryPathForExtension(ext);
            using (var shellKey = Registry.CurrentUser.CreateSubKey(basePath))
            {
                shellKey.SetValue("", MenuText);
                if (hasMui)
                    shellKey.SetValue("MUIVerb", $"@{muiDll},-{AppConstants.ContextMenuMuiStringId}");
                else
                    shellKey.DeleteValue("MUIVerb", throwOnMissingValue: false);
                shellKey.SetValue("Icon", $"\"{exePath}\",0");
                shellKey.SetValue("Position", "Top");
            }
            using var commandKey = Registry.CurrentUser.CreateSubKey(basePath + @"\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        Registry.CurrentUser.DeleteSubKeyTree(AppConstants.ContextMenuRegistryPathLegacy, throwOnMissingSubKey: false);

        // 在各扩展名实际关联的 ProgID 下也注册（7z_auto_file 与 7-Zip-Zstandard.7z 等），否则 Explorer 只从 ProgID\shell 读菜单
        var progIds = GetProgIdsForArchiveExtensions();
        foreach (var progId in progIds)
        {
            var progIdPath = @"Software\Classes\" + progId + @"\shell\" + AppConstants.ContextMenuName;
            using (var shellKey = Registry.CurrentUser.CreateSubKey(progIdPath))
            {
                shellKey.SetValue("", MenuText);
                if (hasMui)
                    shellKey.SetValue("MUIVerb", $"@{muiDll},-{AppConstants.ContextMenuMuiStringId}");
                else
                    shellKey.DeleteValue("MUIVerb", throwOnMissingValue: false);
                shellKey.SetValue("Icon", $"\"{exePath}\",0");
                shellKey.SetValue("Position", "Top");
            }
            using var commandKey = Registry.CurrentUser.CreateSubKey(progIdPath + @"\command");
            commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        NativeMethods.SHChangeNotifyAssocChanged();
    }

    /// <summary>返回应在其中注册右键菜单的 ProgID 集合：已知的 7z_auto_file 加上各扩展名在注册表中的实际关联 ProgID（如 7-Zip-Zstandard.7z）。</summary>
    private static HashSet<string> GetProgIdsForArchiveExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AppConstants.SevenZipProgId };
        foreach (var ext in AppConstants.ContextMenuArchiveExtensions)
        {
            var extKey = @"Software\Classes\" + ext;
            using var hkcu = Registry.CurrentUser.OpenSubKey(extKey);
            var progId = hkcu?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(progId))
                set.Add(progId.Trim());
            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\" + ext);
                progId = hklm?.GetValue("") as string;
                if (!string.IsNullOrWhiteSpace(progId))
                    set.Add(progId.Trim());
            }
            catch { /* no admin */ }
        }
        return set;
    }

    /// <summary>
    /// 若右键菜单已注册，静默重新注册以同步当前语言文本。
    /// 在 App 启动和语言切换后调用。
    /// </summary>
    public static void RefreshIfRegistered()
    {
        try
        {
            if (IsRegistered())
                Register();
        }
        catch
        {
            // 非关键路径，静默忽略
        }
    }

    public static void Unregister()
    {
        foreach (var ext in AppConstants.ContextMenuArchiveExtensions)
        {
            var path = AppConstants.GetContextMenuRegistryPathForExtension(ext);
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(AppConstants.ContextMenuRegistryPathLegacy, throwOnMissingSubKey: false);
        foreach (var progId in GetProgIdsForArchiveExtensions())
        {
            var path = @"Software\Classes\" + progId + @"\shell\" + AppConstants.ContextMenuName;
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        NativeMethods.SHChangeNotifyAssocChanged();
    }

    private static class NativeMethods
    {
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        internal static void SHChangeNotifyAssocChanged() =>
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
