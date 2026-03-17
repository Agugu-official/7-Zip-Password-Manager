using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Models;

/// <summary>
/// pws.json 的顶层包装结构，通过 Format 字段标识文件归属，
/// 防止用户误选其它 JSON 文件导致反序列化失败。
/// <para>
/// Format 属性故意不设默认值，
/// 这样反序列化不含 "Format" 字段的 JSON 时值为 null，校验自然失败。
/// </para>
/// </summary>
public class PasswordFileWrapper
{
    public const string ExpectedFormat = AppConstants.PasswordFileFormat;
    public const int CurrentVersion = AppConstants.CurrentPasswordFileVersion;

    public string? Format { get; set; }
    public int Version { get; set; }
    public List<PasswordEntry>? Entries { get; set; }
}
