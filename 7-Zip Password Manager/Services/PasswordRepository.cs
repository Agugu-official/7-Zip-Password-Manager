using System.IO;
using System.Text.Json;
using _7_Zip_Password_Manager.Helpers;
using _7_Zip_Password_Manager.Models;

namespace _7_Zip_Password_Manager.Services;

public class PasswordRepository : IPasswordRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string FilePath { get; set; }

    public PasswordRepository(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// 确保密码文件及其所在目录存在。首次运行时自动创建空文件。
    /// 从 ViewModel 中拆出的原因：文件/目录创建属于数据访问层职责。
    /// </summary>
    /// <returns>true 表示文件已就绪，false 表示创建失败</returns>
    public (bool Success, string? Error) EnsureFileExists()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        if (!File.Exists(FilePath))
        {
            try
            {
                Save(new List<PasswordEntry>());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        return (true, null);
    }

    /// <summary>
    /// 加载密码文件。支持新版包装格式和旧版纯数组格式。
    /// 如果文件既不是合法的密码数组，也不是带有效 format 头的包装对象，
    /// 则抛出 <see cref="InvalidPasswordFileException"/>。
    /// </summary>
    public List<PasswordEntry> Load()
    {
        if (!File.Exists(FilePath))
            return new List<PasswordEntry>();

        var json = File.ReadAllText(FilePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<PasswordEntry>();

        json = json.TrimStart();

        if (json.StartsWith('['))
            return LoadLegacyArray(json);

        if (json.StartsWith('{'))
            return LoadWrapped(json);

        throw new InvalidPasswordFileException(
            GuiText.Format("services.invalidFileUnrecognized", Path.GetFileName(FilePath)));
    }

    public void Save(List<PasswordEntry> entries)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var wrapper = new PasswordFileWrapper
        {
            Format = PasswordFileWrapper.ExpectedFormat,
            Version = PasswordFileWrapper.CurrentVersion,
            Entries = entries
        };
        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// 兼容旧版：纯 List&lt;PasswordEntry&gt; 数组。
    /// 先用 JsonDocument 校验结构（元素须含 "Password" 字段），再反序列化。
    /// </summary>
    private List<PasswordEntry> LoadLegacyArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.ValueKind != JsonValueKind.Object ||
                    !first.TryGetProperty("Password", out _))
                {
                    throw new InvalidPasswordFileException(
                        GuiText.Format("services.invalidFileArrayNoEntries", Path.GetFileName(FilePath)));
                }
            }

            return JsonSerializer.Deserialize<List<PasswordEntry>>(json, JsonOptions)
                   ?? new List<PasswordEntry>();
        }
        catch (InvalidPasswordFileException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidPasswordFileException(
                GuiText.Format("services.invalidFileArrayParseFailed", Path.GetFileName(FilePath)), ex);
        }
    }

    /// <summary>
    /// 新版：带 format 标识头的包装对象
    /// </summary>
    private List<PasswordEntry> LoadWrapped(string json)
    {
        PasswordFileWrapper? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize<PasswordFileWrapper>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidPasswordFileException(
                GuiText.Format("services.invalidFileJsonParseFailed", Path.GetFileName(FilePath)), ex);
        }

        if (wrapper is null)
            return new List<PasswordEntry>();

        if (!string.Equals(wrapper.Format, PasswordFileWrapper.ExpectedFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPasswordFileException(
                GuiText.Format("services.invalidFileWrongFormat",
                    Path.GetFileName(FilePath),
                    PasswordFileWrapper.ExpectedFormat,
                    wrapper.Format ?? GuiText.Get("services.emptyValue")));
        }

        return wrapper.Entries ?? new List<PasswordEntry>();
    }
}
