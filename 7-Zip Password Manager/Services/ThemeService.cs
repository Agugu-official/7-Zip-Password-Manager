using System.Windows;

namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 主题切换服务。
/// 将 WPF ResourceDictionary 的操作封装在 View 层基础设施中，
/// 使 ViewModel 不直接依赖 Application.Current。
/// </summary>
public class ThemeService : IThemeService
{
    public void ApplyTheme(bool dark)
    {
        var themePath = dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        var newTheme = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };

        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        mergedDicts.Clear();
        mergedDicts.Add(newTheme);
    }
}
