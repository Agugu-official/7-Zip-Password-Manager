using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Models;
using _7_Zip_Password_Manager.ViewModels;
using _7_Zip_Password_Manager.Views;
using G = _7_Zip_Password_Manager.Helpers.GuiText;

namespace _7_Zip_Password_Manager;

public partial class MainWindow : Window
{
    private TextBlock? _renameDisplay;
    private TextBox? _renameBox;
    private PasswordEntry? _renamingEntry;
    private bool _isEndingRename;

    private const double ColumnMinWidth = AppConstants.ColumnMinWidth;
    private bool _isClampingColumnWidth;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += Window_PreviewKeyDown;
        UpdateMaximizeIcon();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestStartRename += OnRequestStartRename;
            vm.RequestAutoClose += OnRequestAutoClose;
            vm.RequestRestart += OnRequestRestart;
            vm.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

            foreach (var entry in vm.LogEntries)
                AppendLogToDocument(entry);
        }

        App.ArchivePathReceived += OnArchivePathReceived;
        EnforceColumnMinWidths();
    }

    private void EnforceColumnMinWidths()
    {
        var dpd = DependencyPropertyDescriptor.FromProperty(
            GridViewColumn.WidthProperty, typeof(GridViewColumn));
        if (dpd is null) return;

        foreach (var col in new[] { ColPassword, ColSuccessCount, ColUseCount, ColLastUsed, ColSuccess })
            dpd.AddValueChanged(col, OnColumnWidthChanged);
    }

    private void OnColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_isClampingColumnWidth || sender is not GridViewColumn col) return;
        if (col.Width < ColumnMinWidth)
        {
            _isClampingColumnWidth = true;
            col.Width = ColumnMinWidth;
            _isClampingColumnWidth = false;
        }
    }

    // ── 窗口加载完成 ──

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            RestoreColumnWidths(vm);

            if (!string.IsNullOrEmpty(App.StartupArchivePath))
                vm.AutoStartIfReady();

            if (!File.Exists(AppDataPaths.FirstRunWizardDoneFile))
            {
                // 延后到主窗口渲染完成后再弹窗，确保向导显示在最前且可见
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    if (DataContext is not MainWindowViewModel v) return;
                    Activate();
                    var (confirmed, enableContextMenu, enableAutoClose) =
                        FirstRunWizardWindow.ShowWizard(this, !v.Is7ZipAvailable);
                    if (confirmed)
                    {
                        try
                        {
                            AppDataPaths.EnsureDirectoryExists();
                            File.WriteAllText(AppDataPaths.FirstRunWizardDoneFile, "");
                        }
                        catch { }
                        if (enableContextMenu)
                            _7_Zip_Password_Manager.Services.ContextMenuService.Register();
                        else
                            _7_Zip_Password_Manager.Services.ContextMenuService.Unregister();
                        v.MarkFirstRunWizardComplete(enableContextMenu, enableAutoClose);
                    }
                }));
            }
        }
    }

    private void RestoreColumnWidths(MainWindowViewModel vm)
    {
        var cw = vm.GetColumnWidths();
        if (cw is null || cw.IsEmpty) return;

        if (cw.Password > 0) ColPassword.Width = cw.Password;
        if (cw.SuccessCount > 0) ColSuccessCount.Width = cw.SuccessCount;
        if (cw.UseCount > 0) ColUseCount.Width = cw.UseCount;
        if (cw.LastUsed > 0) ColLastUsed.Width = cw.LastUsed;
        if (cw.Success > 0) ColSuccess.Width = cw.Success;
    }

    private void SaveColumnWidths(MainWindowViewModel vm)
    {
        vm.SaveColumnWidths(new ColumnWidthsConfig
        {
            Password = ColPassword.ActualWidth,
            SuccessCount = ColSuccessCount.ActualWidth,
            UseCount = ColUseCount.ActualWidth,
            LastUsed = ColLastUsed.ActualWidth,
            Success = ColSuccess.ActualWidth
        });
    }

    // ── 第二实例通过管道发来的压缩包路径 ──

    private void OnArchivePathReceived(string path)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsBusy)
            return;

        vm.ApplyArchivePath(path);

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();

        vm.AutoStartIfReady();
    }

    // ── 窗口控制 ──

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
        => UpdateMaximizeIcon();

    private void UpdateMaximizeIcon()
    {
        bool isMax = WindowState == WindowState.Maximized;
        MaxRect1.Width = isMax ? 7.5 : 9;
        MaxRect1.Height = isMax ? 6.5 : 8;
        Canvas.SetLeft(MaxRect1, isMax ? 0 : 1.5);
        Canvas.SetTop(MaxRect1, isMax ? 3.5 : 2);
        MaxRect2.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 日志渲染 + 自动滚动 ──

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (LogEntry entry in e.NewItems)
                AppendLogToDocument(entry);
        }

        Dispatcher.BeginInvoke(() => LogBox.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AppendLogToDocument(LogEntry entry)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };

        var timeRun = new Run($"[{entry.FormattedTime}] ");
        timeRun.SetResourceReference(TextElement.ForegroundProperty, "LogTimeFg");
        paragraph.Inlines.Add(timeRun);

        var messageRun = new Run(entry.Message);
        var brushKey = entry.Level switch
        {
            LogLevel.Success => "LogSuccessFg",
            LogLevel.Warning => "LogWarningFg",
            LogLevel.Error => "LogErrorFg",
            _ => "LogInfoFg"
        };
        messageRun.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        paragraph.Inlines.Add(messageRun);

        LogBox.Document.Blocks.Add(paragraph);
    }

    // ── 外部启动解压成功 → 延迟自动关闭 ──

    private async void OnRequestAutoClose()
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        var delayMs = (DataContext is MainWindowViewModel vm)
            ? vm.AutoCloseDelayMs
            : 1000;
        await Task.Delay(delayMs);

        Close();
    }

    // ── 语言切换后重启提示 ──
    // 由 ViewModel.RequestRestart 事件触发。
    // ViewModel 决定"何时需要重启"，View 决定"如何提示用户并执行重启"。

    private void OnRequestRestart()
    {
        var answer = MessageBox.Show(
            G.Get("settingsWindow.restartMessage"),
            G.Get("settingsWindow.restartTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(exePath);
                Application.Current.Shutdown();
            }
        }
    }

    // ── 设置窗口 ──
    // 保留在 code-behind：显示对话框属于 UI 职责。
    // 简化：移除了内嵌的重启逻辑（已通过 RequestRestart 事件处理）。

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var result = SettingsWindow.ShowSettings(
            vm.PasswordFilePath, vm.SevenZipPath, vm.CloseAfterExtract,
            vm.MaxParallelism, vm.Language, this);

        if (result.PasswordPathChanged)
            vm.ChangePasswordFilePath(result.NewPasswordPath);

        if (result.SevenZipPathChanged)
            vm.ChangeSevenZipPath(result.NewSevenZipPath);

        if (result.AutoCloseChanged)
            vm.ChangeCloseAfterExtract(result.NewAutoCloseValue);

        if (result.MaxParallelismChanged)
            vm.ChangeMaxParallelism(result.NewMaxParallelism);

        if (result.LanguageChanged)
            vm.ChangeLanguage(result.NewLanguage);
    }

    // ── 列头点击排序 ──
    // 保留在 code-behind：将本地化列头文本映射到属性名属于 UI 翻译。
    // 排序状态管理和 CollectionView 操作已迁到 ViewModel。

    private void PasswordList_HeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Role == GridViewColumnHeaderRole.Padding)
            return;

        var sortBy = MapHeaderToSortProperty(header);
        if (sortBy is null) return;

        if (DataContext is MainWindowViewModel vm)
            vm.ApplySort(sortBy);
    }

    private static string? MapHeaderToSortProperty(GridViewColumnHeader header)
    {
        var headerText = header.Column?.Header?.ToString();
        return headerText switch
        {
            _ when headerText == G.Get("mainWindow.colPassword") => "Password",
            _ when headerText == G.Get("mainWindow.colSuccessCount") => "SuccessCount",
            _ when headerText == G.Get("mainWindow.colUseCount") => "UseCount",
            _ when headerText == G.Get("mainWindow.colLastUsed") => "LastUsedTime",
            _ => null,
        };
    }

    // ── 添加密码后自动进入编辑 ──

    private void OnRequestStartRename(PasswordEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SuspendSort();

            PasswordList.ScrollIntoView(entry);
            PasswordList.UpdateLayout();
            var item = PasswordList.ItemContainerGenerator.ContainerFromItem(entry) as ListViewItem;
            if (item is not null)
                StartRename(item, entry);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── 资源管理器式重命名 ──

    private void PasswordList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PasswordList.SelectedItem is PasswordEntry entry)
        {
            var item = PasswordList.ItemContainerGenerator.ContainerFromItem(entry) as ListViewItem;
            if (item is not null && IsHitOnPasswordColumn(e, item))
                StartRename(item, entry);
        }
    }

    private void PasswordList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && PasswordList.SelectedItem is PasswordEntry entry)
        {
            var item = PasswordList.ItemContainerGenerator.ContainerFromItem(entry) as ListViewItem;
            if (item is not null)
                StartRename(item, entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _renamingEntry is null
                 && DataContext is MainWindowViewModel vm
                 && vm.RemovePasswordCommand.CanExecute(null))
        {
            vm.RemovePasswordCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void StartRename(ListViewItem container, PasswordEntry entry)
    {
        if (_renamingEntry is not null)
            CommitRename();

        var (textBlock, textBox) = FindPasswordElements(container);
        if (textBlock is null || textBox is null) return;

        _renamingEntry = entry;
        _renameDisplay = textBlock;
        _renameBox = textBox;

        textBlock.Visibility = Visibility.Collapsed;
        textBox.Text = entry.Password;
        textBox.Visibility = Visibility.Visible;
        textBox.Focus();
        textBox.SelectAll();

        textBox.LostFocus += RenameBox_LostFocus;
        textBox.PreviewKeyDown += RenameBox_PreviewKeyDown;
    }

    private void CommitRename()
    {
        if (_isEndingRename || _renamingEntry is null) return;
        _isEndingRename = true;

        var entry = _renamingEntry;

        if (_renameBox is not null)
            entry.Password = _renameBox.Text;

        EndRename();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.AfterPasswordEdited(entry);
            vm.ReapplySort();
        }

        if (PasswordList.Items.Contains(entry))
        {
            PasswordList.UpdateLayout();
            PasswordList.ScrollIntoView(entry);
        }

        _isEndingRename = false;
    }

    private void CancelRename()
    {
        if (_isEndingRename) return;
        _isEndingRename = true;
        EndRename();

        if (DataContext is MainWindowViewModel vm)
            vm.ReapplySort();

        _isEndingRename = false;
    }

    private void EndRename()
    {
        if (_renameBox is not null)
        {
            _renameBox.LostFocus -= RenameBox_LostFocus;
            _renameBox.PreviewKeyDown -= RenameBox_PreviewKeyDown;
            _renameBox.Visibility = Visibility.Collapsed;
        }
        if (_renameDisplay is not null)
            _renameDisplay.Visibility = Visibility.Visible;

        _renameBox = null;
        _renameDisplay = null;
        _renamingEntry = null;
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitRename();

    // ── 点击空白区域退出重命名 + 取消选中 ──

    private void RootGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        if (_renamingEntry is not null
            && !ReferenceEquals(source, _renameBox)
            && !IsDescendantOf(source, _renameBox))
        {
            CommitRename();
        }

        var ancestorListView = FindAncestor<ListView>(source);
        var ancestorListViewItem = FindAncestor<ListViewItem>(source);

        bool clickedOnFocusable = FindAncestor<TextBox>(source) is not null
                               || FindAncestor<Button>(source) is not null
                               || (ancestorListView is not null && ancestorListViewItem is not null)
                               || FindAncestor<RichTextBox>(source) is not null;

        if (!clickedOnFocusable)
        {
            if (PasswordList.IsKeyboardFocusWithin)
                PasswordList.UnselectAll();
            Keyboard.ClearFocus();
        }
    }

    // ── 视觉树辅助 ──

    private bool IsHitOnPasswordColumn(MouseButtonEventArgs e, ListViewItem container)
    {
        var presenter = FindChild<GridViewRowPresenter>(container);
        if (presenter is null || VisualTreeHelper.GetChildrenCount(presenter) == 0)
            return false;

        var firstCol = VisualTreeHelper.GetChild(presenter, 0) as UIElement;
        if (firstCol is null) return false;

        var pos = e.GetPosition(firstCol);
        return pos.X >= 0 && pos.X <= firstCol.RenderSize.Width
            && pos.Y >= 0 && pos.Y <= firstCol.RenderSize.Height;
    }

    private (TextBlock?, TextBox?) FindPasswordElements(ListViewItem container)
    {
        var presenter = FindChild<GridViewRowPresenter>(container);
        if (presenter is null || VisualTreeHelper.GetChildrenCount(presenter) == 0)
            return (null, null);

        var firstCol = VisualTreeHelper.GetChild(presenter, 0);
        if (firstCol is null) return (null, null);

        return (FindChild<TextBlock>(firstCol), FindChild<TextBox>(firstCol));
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T found) return found;
            element = GetParentSafe(element);
        }
        return null;
    }

    private static DependencyObject? GetParentSafe(DependencyObject element)
    {
        if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(element);
        if (element is FrameworkContentElement fce)
            return fce.Parent;
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var deeper = FindChild<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? parent)
    {
        if (parent is null) return false;
        while (child is not null)
        {
            if (ReferenceEquals(child, parent)) return true;
            child = GetParentSafe(child);
        }
        return false;
    }

    // ── 密码查找（Win32 级别拦截 Ctrl+F） ──

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_KEYDOWN = 0x0100;
        const int VK_F = 0x46;

        if (msg == WM_KEYDOWN && (int)wParam == VK_F
            && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SearchText = string.Empty;
            PasswordList.Focus();
            e.Handled = true;
        }
    }

    // ── 搜索框占位符（纯 UI 外观控制，保留在 code-behind） ──

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateSearchPlaceholder();

    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        => UpdateSearchPlaceholder();

    private void UpdateSearchPlaceholder()
    {
        bool hasText = !string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholder.Visibility = (!hasText && !SearchBox.IsKeyboardFocused)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SearchText = string.Empty;
        SearchBox.Focus();
    }

    // ── 退出时保存 ──

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.ArchivePathReceived -= OnArchivePathReceived;
        CommitRename();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CancelTrying();
            vm.RequestAutoClose -= OnRequestAutoClose;
            vm.RequestRestart -= OnRequestRestart;
            SaveColumnWidths(vm);
            vm.SaveOnExit();
        }
    }
}
