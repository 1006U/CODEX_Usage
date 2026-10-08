using System.Windows;
using System.Windows.Input;
using CodexUsageWidget.Models;

namespace CodexUsageWidget;

public partial class TaskbarUsageWindow : Window
{
    private const double LeftOffset = 126;
    private const double BottomInset = 2;

    public TaskbarUsageWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionNextToWeather();
    }

    public void UpdateUsage(UsageSnapshot snapshot)
    {
        var five = snapshot.FiveHour?.RemainingPercent;
        var weekly = snapshot.Weekly?.RemainingPercent;

        FiveHourText.Text = five is null ? "  --%" : $"  {five}%";
        FiveHourSmallText.Text = five is null ? "--%" : $"{five}%";
        WeeklyText.Text = weekly is null ? "--%" : $"{weekly}%";

        ToolTip = BuildTooltip(snapshot);
    }

    public void PositionNextToWeather()
    {
        var workArea = SystemParameters.WorkArea;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = Math.Max(40, screenHeight - workArea.Bottom);

        Left = workArea.Left + LeftOffset;
        Top = screenHeight - taskbarHeight + Math.Max(0, (taskbarHeight - Height) / 2) - BottomInset;
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
            app.ShowWidget();
        }
    }
}
