using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using _7_Zip_Password_Manager.Constants;
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

        BuildUiScaleButtons();
        ApplyUiScale();
        UiScale.Changed += OnUiScaleChanged;
        Closed += (_, _) => UiScale.Changed -= OnUiScaleChanged;
    }

    // ── 界面缩放（档位按钮：点选即时全局生效并持久化）──
    // 首次启动即可调整，照顾低分辨率设备。

    private void BuildUiScaleButtons()
    {
        UiScalePanel.Children.Clear();
        foreach (var preset in AppConstants.UiScalePresets)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = (int)Math.Round(preset * 100) + "%",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Tag = preset,
            };
            btn.Click += UiScalePreset_Click;
            UiScalePanel.Children.Add(btn);
        }
        HighlightActiveUiScale();
    }

    private void UiScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is double preset)
            UiScale.Set(preset);
    }

    private void HighlightActiveUiScale()
    {
        var activeBg = FindResource("ButtonHover") as System.Windows.Media.Brush;
        var normalBg = FindResource("ButtonBg") as System.Windows.Media.Brush;
        var activeBorder = FindResource("WindowFg") as System.Windows.Media.Brush;
        var normalBorder = FindResource("ControlBorder") as System.Windows.Media.Brush;
        foreach (var child in UiScalePanel.Children)
        {
            if (child is System.Windows.Controls.Button b && b.Tag is double preset)
            {
                var active = Math.Abs(preset - UiScale.Current) < 0.001;
                b.Background = active ? activeBg : normalBg;
                b.BorderBrush = active ? activeBorder : normalBorder;
            }
        }
    }

    private void OnUiScaleChanged()
    {
        ApplyUiScale();
        HighlightActiveUiScale();
    }

    private void ApplyUiScale()
    {
        UiScale.ApplyTransform(WizardRoot, this, 32.0);
        var s = UiScale.Current;
        MaxWidth = 460 * s; MinWidth = 360 * s; Width = 400 * s;
        MaxHeight = 480 * s; MinHeight = 260 * s; // 高度由 SizeToContent 自适应，仅放宽上下限
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
        catch (Exception ex)
        {
            // 打开浏览器失败不应影响向导（OBS-4）
            Trace.TraceWarning($"打开链接失败: {ex.Message}");
        }
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
