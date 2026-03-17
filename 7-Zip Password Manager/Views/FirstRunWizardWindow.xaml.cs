using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using _7_Zip_Password_Manager.Helpers;

namespace _7_Zip_Password_Manager.Views;

/// <summary>
/// 首次启动向导：无配置文件时显示一次。展示 7-Zip 未找到提示（含官网链接）、系统集成两个选项及底部提示。
/// </summary>
public partial class FirstRunWizardWindow : Window
{
    private const string SevenZipUrl = "https://www.7-zip.org/";

    public bool EnableContextMenu => ContextMenuCheck.IsChecked == true;
    public bool EnableAutoClose => AutoCloseCheck.IsChecked == true;

    public FirstRunWizardWindow(bool sevenZipNotFound)
    {
        InitializeComponent();

        if (sevenZipNotFound)
        {
            SevenZipNoticePanel.Visibility = Visibility.Visible;
            var message = GuiText.Get("firstRun.message7ZipNotFound");
            var linkText = GuiText.Get("firstRun.link7ZipText");
            SevenZipMessageBlock.Inlines.Clear();
            SevenZipMessageBlock.Inlines.Add(new Run(message + " "));
            var link = new Hyperlink(new Run(linkText))
            {
                NavigateUri = new Uri(SevenZipUrl),
                Foreground = System.Windows.Media.Brushes.CornflowerBlue
            };
            link.RequestNavigate += Hyperlink_RequestNavigate;
            SevenZipMessageBlock.Inlines.Add(link);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch { }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 显示首次启动向导，Owner 为 MainWindow。返回 (用户是否点击确定, 是否启用右键菜单, 是否启用解压后自动关闭)。
    /// </summary>
    public static (bool Confirmed, bool EnableContextMenu, bool EnableAutoClose) ShowWizard(
        Window owner, bool sevenZipNotFound)
    {
        var win = new FirstRunWizardWindow(sevenZipNotFound)
        {
            Owner = owner
        };
        var result = win.ShowDialog() == true;
        return (result, win.EnableContextMenu, win.EnableAutoClose);
    }
}
