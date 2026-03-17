using _7_Zip_Password_Manager.Helpers;

namespace _7_Zip_Password_Manager.Models;

public class PasswordEntry : ViewModelBase
{
    private string _password = string.Empty;

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public DateTime CreatedTime { get; set; } = DateTime.Now;
    public DateTime LastUsedTime { get; set; }
    public int UseCount { get; set; }
    public int SuccessCount { get; set; }
    public bool WasLastSuccessful { get; set; }

    /// <summary>
    /// 手动优先级，值越大越优先尝试 (0-15)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 曾成功解压过的压缩包文件名（不含路径），用于场景匹配
    /// </summary>
    public List<string> SuccessArchives { get; set; } = new();

    public void RecordUsage(bool success)
    {
        LastUsedTime = DateTime.Now;
        UseCount++;
        if (success)
        {
            SuccessCount++;
            WasLastSuccessful = true;
        }
        else
        {
            WasLastSuccessful = false;
        }
    }

    /// <summary>
    /// 记录在哪个压缩包上成功过
    /// </summary>
    public void RecordSuccessArchive(string archiveFileName)
    {
        if (!SuccessArchives.Contains(archiveFileName, StringComparer.OrdinalIgnoreCase))
            SuccessArchives.Add(archiveFileName);
    }
}
