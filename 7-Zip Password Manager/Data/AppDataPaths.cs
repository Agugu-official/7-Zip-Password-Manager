using System.Diagnostics;
using System.IO;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Data;

public static class AppDataPaths
{
    private static readonly string ExeFolder =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;

    public static readonly string PwsFolder = Path.Combine(ExeFolder, AppConstants.PasswordFolderName);
    public static readonly string ConfigFolder = Path.Combine(ExeFolder, AppConstants.ConfigFolderName);

    public static string DefaultPasswordFile => Path.Combine(PwsFolder, AppConstants.DefaultPasswordFileName);
    public static string ConfigFile => Path.Combine(ConfigFolder, AppConstants.ConfigFileName);
    public static string GuiTextFile => Path.Combine(ConfigFolder, AppConstants.GuiTextFileName);
    public static string LastExtractFile => Path.Combine(ConfigFolder, AppConstants.LastExtractFileName);

    /// <summary>
    /// 首次启动向导“已完成”标记文件。存在则不再显示向导，与 config 是否已写入无关。
    /// </summary>
    public static string FirstRunWizardDoneFile => Path.Combine(ConfigFolder, ".first_run_wizard_done");

    /// <summary>
    /// 根据语言代码返回对应的 GUI 文本文件路径。
    /// zh-CN 对应默认的 gui.json，其余语言对应 gui.{lang}.json。
    /// 若目标文件不存在则回退到默认 gui.json。
    /// </summary>
    public static string GetGuiTextFile(string language)
    {
        if (string.Equals(language, AppConstants.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            return GuiTextFile;

        var langFile = Path.Combine(ConfigFolder, $"gui.{language}.json");
        return File.Exists(langFile) ? langFile : GuiTextFile;
    }

    /// <summary>
    /// 旧版 config.json 位置（pws 文件夹内），用于自动迁移
    /// </summary>
    public static string LegacyConfigFile => Path.Combine(PwsFolder, "config.json");

    public static void EnsureDirectoryExists()
    {
        try
        {
            Directory.CreateDirectory(PwsFolder);
            Directory.CreateDirectory(ConfigFolder);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"无法创建目录: {ex.Message}");
        }
    }
}
