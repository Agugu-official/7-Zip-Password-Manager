using System.IO;
using System.IO.Pipes;
using System.Windows;
using _7_Zip_Password_Manager.Constants;
using _7_Zip_Password_Manager.Data;
using _7_Zip_Password_Manager.Helpers;
using _7_Zip_Password_Manager.Services;

namespace _7_Zip_Password_Manager;

public partial class App : Application
{

    private static Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;

    /// <summary>
    /// 首次启动时由命令行传入的压缩包路径（右键菜单场景）。
    /// ViewModel 构造时读取此值。
    /// </summary>
    public static string? StartupArchivePath { get; private set; }

    /// <summary>
    /// 已有实例运行时，第二个实例通过管道发来的压缩包路径。
    /// MainWindow 订阅此事件来接收文件并激活窗口。
    /// </summary>
    public static event Action<string>? ArchivePathReceived;

    /// <summary>
    /// 本次进程启动时，在首次 Load 前配置文件是否已存在（含迁移后）。
    /// 为 false 表示“本次是首次运行”，应显示首次启动向导（即使用户未手动删 config，或 ViewModel 构造时已写入 config）。
    /// </summary>
    public static bool ConfigFileExistedAtStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? archivePath = e.Args.Length > 0
            ? Path.GetFullPath(e.Args[0])
            : null;

        _mutex = new Mutex(true, AppConstants.MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!string.IsNullOrEmpty(archivePath))
                SendPathToRunningInstance(archivePath);
            Shutdown();
            return;
        }

        AppConfig.EnsureLegacyConfigMigrated();
        ConfigFileExistedAtStartup = File.Exists(AppDataPaths.ConfigFile);

        var config = AppConfig.Load();
        GuiText.Load(AppDataPaths.GetGuiTextFile(config.Language));
        ContextMenuService.RefreshIfRegistered();

        if (!string.IsNullOrEmpty(archivePath) && File.Exists(archivePath))
            StartupArchivePath = archivePath;

        StartPipeServer();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        AppConstants.PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var path = await reader.ReadLineAsync(token);

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        Dispatcher.Invoke(() => ArchivePathReceived?.Invoke(path));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(AppConstants.IpcRetryDelayMs, token);
                }
            }
        }, token);
    }

    private static void SendPathToRunningInstance(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppConstants.PipeName, PipeDirection.Out);
            client.Connect(AppConstants.IpcConnectTimeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path);
        }
        catch
        {
            // 连接失败则静默退出，用户可再次尝试
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
