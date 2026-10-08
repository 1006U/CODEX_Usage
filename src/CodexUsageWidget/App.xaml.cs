using System.Drawing;
using System.IO;
using System.Windows;
using CodexUsageWidget.Services;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;
    private TaskbarUsageWindow? _taskbarWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _customTrayIcon;
    private Forms.ToolStripMenuItem? _startupMenuItem;
    private bool _isExiting;
    private bool _suppressStartupToggle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _window = new MainWindow();
        _taskbarWindow = new TaskbarUsageWindow();
        _window.UsageUpdated += snapshot => _taskbarWindow?.UpdateUsage(snapshot);

        CreateTrayIcon();
        _taskbarWindow.Show();
        _taskbarWindow.ForceTopmost();

        // Start collapsed. Clicking the taskbar mini widget opens the detail panel.
        _window.Hide();
        _ = _window.RefreshUsageAsync();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem("위젯 열기");
        openItem.Click += (_, _) => ShowWidget();
        menu.Items.Add(openItem);

        var refreshItem = new Forms.ToolStripMenuItem("사용량 새로고침");
        refreshItem.Click += async (_, _) =>
        {
            if (_window is not null)
            {
                await _window.RefreshUsageAsync();
            }
        };
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        _startupMenuItem = new Forms.ToolStripMenuItem("Windows 시작 시 자동 실행")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        _startupMenuItem.CheckedChanged += StartupMenuItem_CheckedChanged;
        menu.Items.Add(_startupMenuItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _customTrayIcon = LoadCustomTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _customTrayIcon ?? SystemIcons.Application,
            Text = "Codex Usage Monitor",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => ShowWidget();
    }

    private static Icon? LoadCustomTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "CodexUsageMonitor.ico");
            return File.Exists(iconPath) ? new Icon(iconPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private void StartupMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressStartupToggle || _startupMenuItem is null)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(_startupMenuItem.Checked);
        }
        catch
        {
            _suppressStartupToggle = true;
            _startupMenuItem.Checked = StartupManager.IsEnabled();
            _suppressStartupToggle = false;
        }
    }

    public bool IsExiting => _isExiting;

    public void ToggleWidgetFromTaskbar()
    {
        // The taskbar mini widget only opens/activates the detail panel.
        // Hiding is intentionally controlled only by the detail panel's minimize button.
        ShowWidget(positionAboveTaskbar: true);
    }

    public void ShowWidget(bool positionAboveTaskbar = false)
    {
        if (_window is null)
        {
            return;
        }

        if (positionAboveTaskbar && _taskbarWindow is not null)
        {
            _taskbarWindow.PositionOnTaskbar();
            var anchor = _taskbarWindow.GetPopupAnchor();
            _window.PositionAboveTaskbar(anchor.X, anchor.Y, _taskbarWindow.ActualWidth > 0 ? _taskbarWindow.ActualWidth : _taskbarWindow.Width);
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _taskbarWindow?.ForceTopmost();
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _customTrayIcon?.Dispose();
        _customTrayIcon = null;

        if (_taskbarWindow is not null)
        {
            _taskbarWindow.Close();
            _taskbarWindow = null;
        }

        _window?.PrepareForExit();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _customTrayIcon?.Dispose();
        _customTrayIcon = null;

        base.OnExit(e);
    }
}
