using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace RemoteInstaller.Services;

/// <summary>
/// 检测客户端自身是否有新版本。
/// </summary>
public sealed class UpdateCheckService
{
    private static readonly string[] VersionPropertyNames =
    [
        "latestVersion",
        "version",
        "tag_name",
        "tagName"
    ];

    private readonly HttpClient _httpClient;
    private readonly Func<string> _currentVersionProvider;

    public UpdateCheckService(HttpClient? httpClient = null, Func<string>? currentVersionProvider = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _currentVersionProvider = currentVersionProvider ?? GetCurrentApplicationVersion;
    }

    public string CurrentVersion => CleanVersionForDisplay(_currentVersionProvider());

    public static string GetCurrentApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static string? ResolveUpdateEndpoint(string? repositoryUrl, string? updateCheckUrl)
    {
        if (!string.IsNullOrWhiteSpace(updateCheckUrl))
        {
            return NormalizeAbsoluteUrl(updateCheckUrl);
        }

        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        var normalizedRepository = NormalizeAbsoluteUrl(repositoryUrl);
        if (string.IsNullOrWhiteSpace(normalizedRepository))
        {
            return null;
        }

        return $"{normalizedRepository.TrimEnd('/')}/api/version";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string? repositoryUrl,
        string? updateCheckUrl,
        string? repositoryToken,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = CurrentVersion;
        var endpoint = ResolveUpdateEndpoint(repositoryUrl, updateCheckUrl);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return UpdateCheckResult.NotAvailable(
                currentVersion,
                "未配置更新检测地址，请在系统设置中填写仓库地址或更新检测地址。");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(repositoryToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", repositoryToken.Trim());
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.NotAvailable(
                    currentVersion,
                    $"更新检测失败：服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}",
                    endpoint);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var versionInfo = ParseVersionInfo(content);
            if (string.IsNullOrWhiteSpace(versionInfo.LatestVersion))
            {
                return UpdateCheckResult.NotAvailable(
                    currentVersion,
                    "更新检测失败：响应中没有版本号。",
                    endpoint);
            }

            var comparison = CompareVersions(currentVersion, versionInfo.LatestVersion);
            var isUpdateAvailable = comparison < 0;
            var statusMessage = isUpdateAvailable
                ? $"发现新版本 v{CleanVersionForDisplay(versionInfo.LatestVersion)}，当前版本 v{currentVersion}。"
                : $"当前已是最新版本 v{currentVersion}。";

            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                LatestVersion = versionInfo.LatestVersion,
                DownloadUrl = versionInfo.DownloadUrl,
                ReleaseNotes = versionInfo.ReleaseNotes,
                PublishedAt = versionInfo.PublishedAt,
                CheckedEndpoint = endpoint,
                IsUpdateAvailable = isUpdateAvailable,
                StatusMessage = statusMessage
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.NotAvailable(currentVersion, "更新检测超时，请稍后重试。", endpoint);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.NotAvailable(currentVersion, $"更新检测失败：{ex.Message}", endpoint);
        }
    }

    private static VersionInfo ParseVersionInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new VersionInfo
        {
            LatestVersion = FindStringProperty(root, VersionPropertyNames),
            DownloadUrl = FindStringProperty(root, "downloadUrl", "download_url", "html_url", "url"),
            ReleaseNotes = FindStringProperty(root, "releaseNotes", "release_notes", "body", "notes"),
            PublishedAt = TryParseDateTimeOffset(FindStringProperty(root, "publishedAt", "published_at", "createdAt", "created_at"))
        };
    }

    private static string? FindStringProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null
                };
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nestedValue = FindStringProperty(property.Value, names);
            if (!string.IsNullOrWhiteSpace(nestedValue))
            {
                return nestedValue;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }

    private static int CompareVersions(string? currentVersion, string? latestVersion)
    {
        if (!TryParseVersion(currentVersion, out var current) ||
            !TryParseVersion(latestVersion, out var latest))
        {
            return string.Compare(
                currentVersion ?? string.Empty,
                latestVersion ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        return current.CompareTo(latest);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        var normalized = NormalizeVersionCore(value);
        return Version.TryParse(normalized, out version!);
    }

    private static string CleanVersionForDisplay(string? value)
    {
        var normalized = NormalizeVersionCore(value);
        return string.IsNullOrWhiteSpace(normalized) ? "0.0.0" : normalized;
    }

    private static string NormalizeVersionCore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        normalized = new string(normalized
            .TakeWhile(c => char.IsDigit(c) || c == '.')
            .ToArray())
            .Trim('.');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "0.0.0";
        }

        var parts = normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .ToList();

        while (parts.Count < 3)
        {
            parts.Add("0");
        }

        return string.Join('.', parts);
    }

    private static string? NormalizeAbsoluteUrl(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"https://{normalized}";
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri.ToString().TrimEnd('/') : null;
    }

    private sealed class VersionInfo
    {
        public string? LatestVersion { get; init; }
        public string? DownloadUrl { get; init; }
        public string? ReleaseNotes { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
    }
}

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; init; } = "0.0.0";
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? CheckedEndpoint { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string StatusMessage { get; init; } = string.Empty;

    public static UpdateCheckResult NotAvailable(string currentVersion, string statusMessage, string? checkedEndpoint = null)
    {
        return new UpdateCheckResult
        {
            CurrentVersion = currentVersion,
            CheckedEndpoint = checkedEndpoint,
            StatusMessage = statusMessage,
            IsUpdateAvailable = false
        };
    }
}
