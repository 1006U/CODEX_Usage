using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageWidget.Models;

namespace ClaudeUsageWidget.Services;

/// <summary>
/// Read-only use of Claude Code's existing Claude.ai OAuth login.
/// Never writes credentials, refreshes a token, or sends model prompts.
/// The subscription usage endpoint is undocumented and may change.
/// </summary>
public sealed class ClaudeUsageReader
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        using var response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Claude 인증이 만료되었거나 권한이 없습니다. Claude Code에서 /login을 실행하세요.");
        }
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("Claude 사용량 요청 제한(429)입니다.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude 사용량 조회 실패: HTTP {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (!root.TryGetProperty("five_hour", out _) && !root.TryGetProperty("seven_day", out _)))
        {
            throw new InvalidOperationException("Claude 사용량 응답 형식이 변경되었거나 구독 사용량이 제공되지 않습니다.");
        }

        return new UsageSnapshot(
            ParseWindow(root, "five_hour", 300),
            ParseWindow(root, "seven_day", 10080),
            DateTimeOffset.Now);
    }

    private static async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : Environment.ExpandEnvironmentVariables(configured);
        var path = Path.Combine(directory, ".credentials.json");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Claude Code 로그인 파일이 없습니다. 터미널에서 claude login을 실행하세요.");
        }

        // Claude Code owns refresh. Reload on every request; never write or log tokens.
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object ||
            !oauth.TryGetProperty("accessToken", out var field) ||
            field.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(field.GetString()))
        {
            throw new InvalidOperationException("Claude.ai OAuth 로그인이 필요합니다. API 키 로그인에서는 구독 한도 조회가 지원되지 않습니다.");
        }

        if (oauth.TryGetProperty("expiresAt", out var expiresAt) &&
            expiresAt.ValueKind == JsonValueKind.Number &&
            expiresAt.TryGetInt64(out var timestamp))
        {
            // Claude Code credentials have used both seconds and milliseconds.
            var seconds = timestamp > 100_000_000_000 ? timestamp / 1000 : timestamp;
            if (DateTimeOffset.FromUnixTimeSeconds(seconds) <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("Claude Code 로그인 토큰이 만료되었습니다. Claude Code를 열어 인증을 갱신하세요.");
            }
        }

        return field.GetString()!;
    }

    private static UsageWindow? ParseWindow(JsonElement root, string name, int minutes)
    {
        if (!root.TryGetProperty(name, out var usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("utilization", out var utilization) ||
            utilization.ValueKind != JsonValueKind.Number ||
            !utilization.TryGetDouble(out var used) ||
            !double.IsFinite(used))
        {
            return null;
        }

        DateTimeOffset? reset = null;
        if (usage.TryGetProperty("resets_at", out var resetField) &&
            resetField.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(resetField.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
        {
            reset = parsed.ToLocalTime();
        }

        return new UsageWindow((int)Math.Round(Math.Clamp(100 - used, 0, 100)),
            reset, minutes);
    }
}
