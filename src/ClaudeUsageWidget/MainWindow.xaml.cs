using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ClaudeUsageWidget.Models;
using ClaudeUsageWidget.Services;

namespace ClaudeUsageWidget;

public partial class MainWindow : Window
{
    private readonly ClaudeUsageReader _reader = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;
    private static readonly TimeSpan MinimumUsageQuerySpacing = TimeSpan.FromSeconds(30);
    private DateTimeOffset _nextUsageQueryAtUtc = DateTimeOffset.MinValue;
    private bool _isRefreshing;
    private bool _allowClose;
    private int _consecutiveFailures;
    private UsageSnapshot? _lastSnapshot;

    public event Action<UsageSnapshot>? UsageUpdated;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();

        Loaded += (_, _) =>
        {
            _refreshTimer.Start();
            _countdownTimer.Start();
        };

        _refreshTimer.Start();
        _countdownTimer.Start();
        Closing += MainWindow_Closing;
    }

    public async Task RefreshUsageAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        // Repeated manual clicks must never start an inference/model request.
        // All refreshes use only the usage endpoint; a short cooldown also
        // prevents unnecessary API polling and accidental rate-limit errors.
        var timeUntilNextQuery = _nextUsageQueryAtUtc - DateTimeOffset.UtcNow;
        if (timeUntilNextQuery > TimeSpan.Zero)
        {
            StatusText.Text = $"최근 사용량 유지 · {Math.Ceiling(timeUntilNextQuery.TotalSeconds):0}초 후 새로고침 가능";
            return;
        }

        _isRefreshing = true;
        _nextUsageQueryAtUtc = DateTimeOffset.UtcNow + MinimumUsageQuerySpacing;
        StatusText.Text = "Claude Code 사용량만 조회 중 (모델 호출 없음)...";

        try
        {
            var snapshot = await _reader.ReadAsync();
            _lastSnapshot = snapshot;
            _consecutiveFailures = 0;
            _refreshTimer.Interval = Jitter(TimeSpan.FromMinutes(5));

            ApplyWindow(snapshot.FiveHour, FiveHourProgress, FiveHourPercentText,
                FiveHourResetText, FiveHourRemainingText);
            ApplyWindow(snapshot.Weekly, WeeklyProgress, WeeklyPercentText,
                WeeklyResetText, WeeklyRemainingText);
            UpdateCountdowns();
            UsageUpdated?.Invoke(snapshot);

            StatusText.Text = snapshot.FiveHour is null && snapshot.Weekly is null
                ? "사용량 창을 찾지 못했습니다. Claude Code 로그인 상태를 확인하세요."
                : "정상 · 모델 호출 없음 · 약 5분마다 자동 갱신";
            UpdatedText.Text = snapshot.CheckedAt.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            var backoffMinutes = Math.Min(60, 5 * Math.Pow(2, Math.Min(_consecutiveFailures - 1, 4)));
            _refreshTimer.Interval = Jitter(TimeSpan.FromMinutes(backoffMinutes));
            if (ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase))
            {
                // Respect the request limit even if the user repeatedly presses refresh.
                _nextUsageQueryAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(backoffMinutes);
            }

            StatusText.Text = $"{ToUserMessage(ex)} · {backoffMinutes:0}분 후 재시도";
            // Preserve the timestamp of the last successful usage fetch.
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void PositionAboveTaskbar(double anchorLeft, double anchorTop, double anchorWidth)
    {
        var workArea = SystemParameters.WorkArea;
        var desiredLeft = anchorLeft + (anchorWidth - Width) / 2.0;
        var minLeft = workArea.Left + 8;
        var maxLeft = Math.Max(minLeft, workArea.Right - Width - 8);

        Left = Math.Clamp(desiredLeft, minLeft, maxLeft);
        Top = Math.Max(workArea.Top + 8, anchorTop - Height - 8);
    }

    private static void ApplyWindow(
        UsageWindow? window,
        System.Windows.Controls.ProgressBar progress,
        System.Windows.Controls.TextBlock percentText,
        System.Windows.Controls.TextBlock resetText,
        System.Windows.Controls.TextBlock remainingText)
    {
        if (window is null)
        {
            progress.Value = 0;
            percentText.Text = "--%";
            resetText.Text = "RESET --";
            remainingText.Text = "--% 남음";
            return;
        }

        progress.Value = window.RemainingPercent;
        percentText.Text = $"{window.RemainingPercent}%";
        remainingText.Text = $"{window.RemainingPercent}% 남음";
        resetText.Text = window.ResetsAt is null
            ? "RESET --"
            : window.WindowDurationMinutes == 300
                ? $"RESET {window.ResetsAt:HH:mm}"
                : $"RESET {window.ResetsAt:MM/dd HH:mm}";
    }

    private void UpdateCountdowns()
    {
        FiveHourCountdownText.Text = FormatCountdown(_lastSnapshot?.FiveHour?.ResetsAt);
        WeeklyCountdownText.Text = FormatCountdown(_lastSnapshot?.Weekly?.ResetsAt);
    }

    private static string FormatCountdown(DateTimeOffset? resetAt)
    {
        if (resetAt is null)
        {
            return "초기화 --";
        }

        var remaining = resetAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "초기화 대기 중";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}일 {remaining.Hours}시간 후 초기화";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분 후 초기화";
        }

        return $"{Math.Max(0, remaining.Minutes)}분 {Math.Max(0, remaining.Seconds)}초 후 초기화";
    }

    private static TimeSpan Jitter(TimeSpan baseInterval)
    {
        var factor = 0.90 + Random.Shared.NextDouble() * 0.20;
        return TimeSpan.FromMilliseconds(baseInterval.TotalMilliseconds * factor);
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException => "시간 초과 · Claude Code 로그인을 확인하세요",
            InvalidOperationException invalid when invalid.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                => "Claude Code 인증 정보가 없습니다",
            _ => Trim(exception.Message, 48)
        };
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "사용량을 불러오지 못했습니다";
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || (System.Windows.Application.Current as App)?.IsExiting == true)
        {
            return;
        }

        e.Cancel = true;
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        _refreshTimer.Stop();
        _countdownTimer.Stop();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
