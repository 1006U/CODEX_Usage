using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CodexUsageWidget.Models;
using CodexUsageWidget.Services;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private readonly CodexUsageReader _reader = new();
    private readonly DispatcherTimer _timer;
    private bool _isRefreshing;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _timer.Tick += async (_, _) => await RefreshUsageAsync();

        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshUsageAsync();
        };
    }

    private async Task RefreshUsageAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        StatusText.Text = "Codex 사용량 확인 중...";

        try
        {
            var snapshot = await _reader.ReadAsync();
            ApplyWindow(snapshot.FiveHour, FiveHourProgress, FiveHourPercentText, FiveHourResetText);
            ApplyWindow(snapshot.Weekly, WeeklyProgress, WeeklyPercentText, WeeklyResetText);

            StatusText.Text = snapshot.FiveHour is null && snapshot.Weekly is null
                ? "사용량 창을 찾지 못했습니다."
                : "Codex CLI 연결됨";
            UpdatedText.Text = $"{snapshot.CheckedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ToUserMessage(ex);
            UpdatedText.Text = DateTime.Now.ToString("HH:mm:ss");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static void ApplyWindow(
        UsageWindow? window,
        System.Windows.Controls.ProgressBar progress,
        System.Windows.Controls.TextBlock percentText,
        System.Windows.Controls.TextBlock resetText)
    {
        if (window is null)
        {
            progress.Value = 0;
            percentText.Text = "--%";
            resetText.Text = "N/A";
            return;
        }

        progress.Value = window.RemainingPercent;
        percentText.Text = $"{window.RemainingPercent}%";
        resetText.Text = window.ResetsAt is null
            ? "reset --"
            : window.WindowDurationMinutes == 300
                ? $"{window.ResetsAt:HH:mm}"
                : $"{window.ResetsAt:MM/dd HH:mm}";
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException => "시간 초과 · Codex CLI 상태를 확인하세요.",
            InvalidOperationException invalid when invalid.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                => "codex 명령을 찾을 수 없습니다.",
            _ => Trim(exception.Message, 55)
        };
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "사용량을 불러오지 못했습니다.";
        }

        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..(maxLength - 1)] + "…";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUsageAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Application.Current.Shutdown();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
