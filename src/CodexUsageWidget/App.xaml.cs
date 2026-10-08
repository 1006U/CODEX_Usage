using System.Drawing;
using System.Windows;
using CodexUsageWidget.Services;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;
    private TaskbarUsageWindow? _taskbarWindow;
    private Forms.NotifyIcon? _trayIcon;
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
        _window.Show();
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

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Codex Usage Monitor",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => ShowWidget();
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

    public void ShowWidget()
    {
        if (_window is null)
        {
            return;
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

        base.OnExit(e);
    }
}
