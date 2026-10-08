using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CodexUsageWidget.Models;

namespace CodexUsageWidget;

public partial class TaskbarUsageWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    // The Windows 11 weather/widgets button normally occupies the far-left taskbar area.
    // Keep the Codex mini widget immediately to its right. This is deliberately an overlay,
    // not a child of Explorer, so Explorer restarts do not own this window's lifetime.
    private const double WeatherAreaWidth = 150;
    private const double GapAfterWeather = 4;

    public TaskbarUsageWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loaded += (_, _) =>
        {
            PositionOnTaskbar();
            ForceTopmost();
        };
    }

    public void UpdateUsage(UsageSnapshot snapshot)
    {
        var five = snapshot.FiveHour?.RemainingPercent;
        var weekly = snapshot.Weekly?.RemainingPercent;

        FiveHourText.Text = five is null ? "  --%" : $"  {five}%";
        FiveHourSmallText.Text = five is null ? "--%" : $"{five}%";
        WeeklyText.Text = weekly is null ? "--%" : $"{weekly}%";
        ToolTip = BuildTooltip(snapshot);

        PositionOnTaskbar();
        ForceTopmost();
    }

    public void PositionOnTaskbar()
    {
        if (!TryGetTaskbarRect(out var rect))
        {
            var workArea = SystemParameters.WorkArea;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var fallbackHeight = Math.Max(40, screenHeight - workArea.Bottom);
            Left = workArea.Left + WeatherAreaWidth + GapAfterWeather;
            Top = screenHeight - fallbackHeight + Math.Max(0, (fallbackHeight - Height) / 2);
            return;
        }

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        var taskbarLeft = rect.Left * dpiX;
        var taskbarTop = rect.Top * dpiY;
        var taskbarHeight = (rect.Bottom - rect.Top) * dpiY;

        Left = taskbarLeft + WeatherAreaWidth + GapAfterWeather;
        Top = taskbarTop + Math.Max(0, (taskbarHeight - Height) / 2);
    }

    public System.Windows.Point GetPopupAnchor()
    {
        return new System.Windows.Point(Left, Top);
    }

    public void ForceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            HwndTopmost,
            (int)Math.Round(Left),
            (int)Math.Round(Top),
            (int)Math.Round(ActualWidth > 0 ? ActualWidth : Width),
            (int)Math.Round(ActualHeight > 0 ? ActualHeight : Height),
            SwpNoActivate | SwpShowWindow);
    }

    private void ConfigureNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(style));
    }

    private static bool TryGetTaskbarRect(out RectNative rect)
    {
        var hwnd = FindWindow("Shell_TrayWnd", null);
        return hwnd != nint.Zero && GetWindowRect(hwnd, out rect);
    }

    private static string BuildTooltip(UsageSnapshot snapshot)
    {
        static string Line(string label, UsageWindow? window)
        {
            if (window is null)
            {
                return $"{label}: --";
            }

            var reset = window.ResetsAt is null
                ? "초기화 시간 없음"
                : $"초기화 {window.ResetsAt:MM/dd HH:mm}";
            return $"{label}: {window.RemainingPercent}% 남음 · {reset}";
        }

        return $"{Line("5시간", snapshot.FiveHour)}\n{Line("주간", snapshot.Weekly)}";
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ToggleWidgetFromTaskbar();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RectNative lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    private static nint GetWindowLongPtr(nint hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new nint(GetWindowLong32(hWnd, nIndex));

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new nint(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
}
