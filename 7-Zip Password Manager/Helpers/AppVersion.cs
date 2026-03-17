using System.Reflection;

namespace _7_Zip_Password_Manager.Helpers;

/// <summary>
/// 从入口程序集读取应用显示版本（与 csproj / CI 注入的 Version 一致）。
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// 返回用于界面显示的版本字符串，如 "v0.0.1"。
    /// </summary>
    public static string GetDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version == null)
            return "v0.0.0";
        return "v" + version.ToString(3);
    }
}
