using System.Diagnostics;
using System.Text.Json;
using CodexUsageWidget.Models;

namespace CodexUsageWidget.Services;

public sealed class CodexUsageReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(Timeout);

        using var process = StartCodexAppServer();
        try
        {
            await SendAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new
                {
                    clientInfo = new
                    {
                        name = "codex_usage_widget",
                        title = "Codex Usage Widget",
                        version = "0.1.0"
                    },
                    capabilities = (object?)null
                }
            }, linkedCts.Token);

            await ReadResponseForIdAsync(process, 1, linkedCts.Token);

            await SendAsync(process, new
            {
                method = "initialized"
            }, linkedCts.Token);

            await SendAsync(process, new
            {
                method = "account/rateLimits/read",
                id = 2,
                @params = new
                {
                    excludeResetCreditDetails = true
                }
            }, linkedCts.Token);

            using var response = await ReadResponseForIdAsync(process, 2, linkedCts.Token);
            var root = response.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"Codex app-server error: {error}");
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw new InvalidOperationException("Codex app-server returned no result.");
            }

            var windows = new List<UsageWindow>();
            if (result.TryGetProperty("rateLimits", out var rateLimits))
            {
                CollectWindows(rateLimits, windows);
            }

            if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
                byId.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in byId.EnumerateObject())
                {
                    CollectWindows(property.Value, windows);
                }
            }

            var fiveHour = windows.FirstOrDefault(window => window.WindowDurationMinutes == 300);
            var weekly = windows.FirstOrDefault(window => window.WindowDurationMinutes == 10080);

            return new UsageSnapshot(fiveHour, weekly, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex usage request timed out.");
        }
        finally
        {
            TryTerminate(process);
        }
    }

    private static Process StartCodexAppServer()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEX_USAGE_WIDGET_CODEX_PATH");
        var executable = string.IsNullOrWhiteSpace(configuredPath) ? "codex" : configuredPath;
        var command = $"\"{executable.Replace("\"", "\"\"")}\" app-server";

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/d /s /c \"{command}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start Codex CLI.");
        }

        return process;
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseForIdAsync(
        Process process,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Codex app-server exited before returning usage data."
                        : $"Codex app-server exited: {error.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var value) &&
                value == expectedId)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static void CollectWindows(JsonElement snapshot, ICollection<UsageWindow> destination)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (snapshot.TryGetProperty("primary", out var primary))
        {
            TryAddWindow(primary, destination);
        }

        if (snapshot.TryGetProperty("secondary", out var secondary))
        {
            TryAddWindow(secondary, destination);
        }
    }

    private static void TryAddWindow(JsonElement element, ICollection<UsageWindow> destination)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("usedPercent", out var usedPercentElement) ||
            !usedPercentElement.TryGetInt32(out var usedPercent) ||
            !element.TryGetProperty("windowDurationMins", out var durationElement) ||
            !durationElement.TryGetInt32(out var durationMinutes))
        {
            return;
        }

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resetsAt", out var resetElement) &&
            resetElement.ValueKind == JsonValueKind.Number &&
            resetElement.TryGetInt64(out var unixSeconds))
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Leave reset time unavailable if the server returned an invalid timestamp.
            }
        }

        var remaining = Math.Clamp(100 - usedPercent, 0, 100);
        if (!destination.Any(existing => existing.WindowDurationMinutes == durationMinutes))
        {
            destination.Add(new UsageWindow(remaining, resetsAt, durationMinutes));
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
