using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexUsageWidget.Models;

namespace CodexUsageWidget;

public partial class TaskbarUsageWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTop = nint.Zero;

    private const double EstimatedWeatherWidth = 190;
    private const double GapNextToWeather = 6;

    private readonly DispatcherTimer _taskbarTimer;
    private UsageSnapshot? _lastSnapshot;
    private nint _taskbarHwnd;

    public TaskbarUsageWindow()
    {
        InitializeComponent();

        // This window is re-parented into Explorer's Shell_TrayWnd. That makes it behave
        // like part of the taskbar: when the taskbar is hidden by a fullscreen game/video,
        // the Codex mini widget is hidden with it instead of floating above the app.
        // The timer only repairs the attachment after Explorer/taskbar restarts or layout changes.
        _taskbarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _taskbarTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                return;
            }

            RenderMiniUsage();
            EnsureTaskbarAttachment();
            PositionOnTaskbar();
        };

        SourceInitialized += (_, _) =>
        {
            ConfigureNativeWindow();
            EnsureTaskbarAttachment();
        };

        Loaded += (_, _) =>
        {
            EnsureTaskbarAttachment();
            PositionOnTaskbar();
            _taskbarTimer.Start();
        };

        Closed += (_, _) => _taskbarTimer.Stop();
    }

    public void UpdateUsage(UsageSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        RenderMiniUsage();
        ToolTip = BuildTooltip(snapshot);

        EnsureTaskbarAttachment();
        PositionOnTaskbar();
    }

    private void RenderMiniUsage()
    {
        var five = _lastSnapshot?.FiveHour;
        var weekly = _lastSnapshot?.Weekly;

        FiveHourText.Text = five is null ? "  --%" : $"  {five.RemainingPercent}%";
        FiveHourSmallText.Text = FormatMiniWindow(five);
        WeeklyText.Text = FormatMiniWindow(weekly);
    }

    private static string FormatMiniWindow(UsageWindow? window)
    {
        if (window is null)
        {
            return "--%";
        }

        if (window.RemainingPercent > 0)
        {
            return $"{window.RemainingPercent}%";
        }

        if (window.ResetsAt is null)
        {
            return "RESET --";
        }

        var remaining = window.ResetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "RESET soon";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"RESET {(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"RESET {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"RESET {Math.Max(0, remaining.Minutes)}m";
    }

    private void EnsureTaskbarAttachment()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero)
        {
            return;
        }

        if (_taskbarHwnd == taskbar && GetParent(hwnd) == taskbar)
        {
            return;
        }

        _taskbarHwnd = taskbar;

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~WsPopup;
        style |= WsChild;
        SetWindowLongPtr(hwnd, GwlStyle, new nint(style));

        SetParent(hwnd, taskbar);
    }

    public void PositionOnTaskbar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        EnsureTaskbarAttachment();

        var taskbar = _taskbarHwnd != nint.Zero ? _taskbarHwnd : FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || !GetClientRect(taskbar, out var clientRect))
        {
            return;
        }

        var source = PresentationSource.FromVisual(this);
        var dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var miniWidthPx = (int)Math.Round((ActualWidth > 0 ? ActualWidth : Width) * dpiScaleX);
        var miniHeightPx = (int)Math.Round((ActualHeight > 0 ? ActualHeight : Height) * dpiScaleY);

        int desiredLeftPx;

        if (TryGetNotificationAreaRect(out var trayRect))
        {
            var trayTopLeft = new PointNative { X = trayRect.Left, Y = trayRect.Top };
            if (ScreenToClient(taskbar, ref trayTopLeft))
            {
                var weatherWidthPx = (int)Math.Round(EstimatedWeatherWidth * dpiScaleX);
                var gapPx = (int)Math.Round(GapNextToWeather * dpiScaleX);
                desiredLeftPx = trayTopLeft.X - weatherWidthPx - gapPx - miniWidthPx;
            }
            else
            {
                desiredLeftPx = clientRect.Right - miniWidthPx - 620;
            }
        }
        else
        {
            desiredLeftPx = clientRect.Right - miniWidthPx - 620;
        }

        desiredLeftPx = Math.Clamp(desiredLeftPx, 4, Math.Max(4, clientRect.Right - miniWidthPx - 4));
        var topPx = Math.Max(0, (clientRect.Bottom - clientRect.Top - miniHeightPx) / 2);

        SetWindowPos(
            hwnd,
            HwndTop,
            desiredLeftPx,
            topPx,
            miniWidthPx,
            miniHeightPx,
            SwpNoActivate | SwpShowWindow);
    }

    public System.Windows.Point GetPopupAnchor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero && GetWindowRect(hwnd, out var rect))
        {
            var source = PresentationSource.FromVisual(this);
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = fromDevice.Transform(new System.Windows.Point(rect.Left, rect.Top));
            return topLeft;
        }

        return new System.Windows.Point(Left, Top);
    }

    // Kept for callers in App.xaml.cs. The mini widget is no longer TOPMOST; it is a
    // child of the Windows taskbar. This method now simply repairs that taskbar attachment.
    public void ForceTopmost()
    {
        EnsureTaskbarAttachment();
        PositionOnTaskbar();
    }

    private void ConfigureNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(exStyle));
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

        return $"{Line("5H", snapshot.FiveHour)}\n{Line("주간", snapshot.Weekly)}";
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

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
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
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RectNative lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RectNative lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref PointNative lpPoint);

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
