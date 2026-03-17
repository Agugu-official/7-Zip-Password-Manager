using System.Windows.Markup;

namespace _7_Zip_Password_Manager.Helpers;

/// <summary>
/// XAML 标记扩展，用于在 XAML 中引用 gui.json 中的文本。
/// 用法：<c>Text="{loc:T Key=mainWindow.title}"</c>
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// gui.json 中的点分键，如 "mainWindow.title"。
    /// </summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return GuiText.Get(Key);
    }
}
