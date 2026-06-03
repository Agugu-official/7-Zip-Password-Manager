using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using _7_Zip_Password_Manager.Constants;

namespace _7_Zip_Password_Manager.Helpers;

/// <summary>
/// 界面缩放（UI 大小）的全局状态与实时应用。离散档位见 <see cref="AppConstants.UiScalePresets"/>。
///
/// 缩放是跨窗口的全局 UI 状态：各窗口在加载时按 <see cref="Current"/> 应用，并订阅 <see cref="Changed"/>
/// 以实时跟随（设置窗口选档位时，主窗口与设置窗口同时即时缩放）。
/// 持久化通过 <see cref="Initialize"/> 注入的回调完成（组合根在 App 启动时接线，写入 AppConfig）。
/// </summary>
public static class UiScale
{
    private static Action<double>? _persist;

    /// <summary>当前生效的缩放比例（已吸附到合法档位）。</summary>
    public static double Current { get; private set; } = AppConstants.DefaultUiScale;

    /// <summary>缩放变更时触发，供已打开的窗口实时重新应用。</summary>
    public static event Action? Changed;

    /// <summary>启动时由组合根调用：设定初始档位并注入持久化回调（不触发 Changed）。</summary>
    public static void Initialize(double scale, Action<double> persist)
    {
        Current = Snap(scale);
        _persist = persist;
    }

    /// <summary>设置缩放档位：吸附 → 持久化 → 通知所有窗口实时重绘。无变化则跳过。</summary>
    public static void Set(double scale)
    {
        var s = Snap(scale);
        if (s == Current)
            return;
        Current = s;
        _persist?.Invoke(s);
        Changed?.Invoke();
    }

    /// <summary>吸附到最近的合法档位。</summary>
    public static double Snap(double scale)
    {
        var best = AppConstants.UiScalePresets[0];
        var bestDist = double.MaxValue;
        foreach (var p in AppConstants.UiScalePresets)
        {
            var d = Math.Abs(p - scale);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// 把当前缩放应用到某窗口的根元素（LayoutTransform），并联动标题栏可拖拽区高度。
    /// 窗口尺寸约束（Min/Max/Width 等）因各窗口而异，由各窗口自行处理。
    /// </summary>
    public static void ApplyTransform(FrameworkElement root, Window window, double baseCaptionHeight)
    {
        var s = Current;
        root.LayoutTransform = s == 1.0 ? Transform.Identity : new ScaleTransform(s, s);
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is not null)
            chrome.CaptionHeight = baseCaptionHeight * s;
    }
}
