using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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

    // On the user's Windows 11 layout the weather/widgets tile is immediately to the
    // left of the notification area. We anchor against TrayNotifyWnd and reserve the
    // weather tile width, instead of assuming that weather is on the far-left side.
    private const double EstimatedWeatherWidth = 190;
    private const double GapNextToWeather = 6;

    private readonly DispatcherTimer _pinTimer;

    public TaskbarUsageWindow()
    {
        InitializeComponent();

        _pinTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _pinTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                return;
            }

            PositionOnTaskbar();
            ForceTopmost();
        };

        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loaded += (_, _) =>
        {
            PositionOnTaskbar();
            ForceTopmost();
            _pinTimer.Start();
        };
        Closed += (_, _) => _pinTimer.Stop();
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
        if (!TryGetTaskbarRect(out var taskbarRect))
        {
            PositionFallback();
            return;
        }

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        var taskbarLeft = taskbarRect.Left * dpiX;
        var taskbarTop = taskbarRect.Top * dpiY;
        var taskbarRight = taskbarRect.Right * dpiX;
        var taskbarHeight = (taskbarRect.Bottom - taskbarRect.Top) * dpiY;

        var miniWidth = ActualWidth > 0 ? ActualWidth : Width;
        double desiredLeft;

        if (TryGetNotificationAreaRect(out var trayRect))
        {
            var trayLeft = trayRect.Left * dpiX;
            desiredLeft = trayLeft - EstimatedWeatherWidth - GapNextToWeather - miniWidth;
        }
        else
        {
            // Conservative fallback for centered-taskbar Windows 11 layouts.
            desiredLeft = taskbarRight - 430 - EstimatedWeatherWidth - GapNextToWeather - miniWidth;
        }

        var minLeft = taskbarLeft + 4;
        var maxLeft = Math.Max(minLeft, taskbarRight - miniWidth - 4);
        Left = Math.Clamp(desiredLeft, minLeft, maxLeft);
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

    private void PositionFallback()
    {
        var workArea = SystemParameters.WorkArea;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = Math.Max(40, screenHeight - workArea.Bottom);
        var miniWidth = ActualWidth > 0 ? ActualWidth : Width;

        Left = Math.Max(workArea.Left + 4, workArea.Right - 620 - miniWidth);
        Top = screenHeight - taskbarHeight + Math.Max(0, (taskbarHeight - Height) / 2);
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
        rect = default;
        var hwnd = FindWindow("Shell_TrayWnd", null);
        return hwnd != nint.Zero && GetWindowRect(hwnd, out rect);
    }

    private static bool TryGetNotificationAreaRect(out RectNative rect)
    {
        rect = default;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero)
        {
            return false;
        }

        var tray = FindWindowEx(taskbar, nint.Zero, "TrayNotifyWnd", null);
        return tray != nint.Zero && GetWindowRect(tray, out rect);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(
        nint hWndParent,
        nint hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

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
