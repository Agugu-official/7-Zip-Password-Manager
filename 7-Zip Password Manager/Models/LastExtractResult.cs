namespace _7_Zip_Password_Manager.Models;

/// <summary>
/// 右键菜单智能解压成功后的持久化记录，供下次启动时回显。
/// 存储为 config/last_extract.json，读取后即删除。
/// </summary>
public class LastExtractResult
{
    public string ArchiveFileName { get; set; } = string.Empty;
    public string ArchiveFilePath { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public DateTime ExtractTime { get; set; }
    public bool WasAutoClose { get; set; }
}
