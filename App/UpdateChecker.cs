using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolumeKeyRouter;

internal static class UpdateChecker
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/whisperbtw/volume-key-router/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/whisperbtw/volume-key-router/releases";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static string CurrentVersionText => GetCurrentVersionText();

    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(release?.TagName))
        {
            throw new InvalidOperationException("A resposta do GitHub nao trouxe a versao da release.");
        }

        var latestVersionText = NormalizeVersionText(release.TagName);
        var currentVersionText = CurrentVersionText;
        if (!TryParseVersion(currentVersionText, out var currentVersion))
        {
            throw new InvalidOperationException($"Nao consegui ler a versao atual ({currentVersionText}).");
        }

        if (!TryParseVersion(latestVersionText, out var latestVersion))
        {
            throw new InvalidOperationException($"Nao consegui ler a versao da release ({release.TagName}).");
        }

        return new UpdateCheckResult(
            currentVersionText,
            ToComparableVersion(currentVersion),
            latestVersionText,
            ToComparableVersion(latestVersion),
            string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl);
    }

    public static void OpenReleasePage(string? releaseUrl = null)
    {
        Process.Start(new ProcessStartInfo(releaseUrl ?? ReleasesPageUrl)
        {
            UseShellExecute = true
        });
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VolumeKeyRouter");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetCurrentVersionText()
    {
        var assembly = typeof(UpdateChecker).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return NormalizeVersionText(informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        return Version.TryParse(NormalizeVersionText(value), out version!);
    }

    private static string NormalizeVersionText(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        var suffixIndex = text.IndexOfAny(new[] { '-', '+' });
        return suffixIndex > 0 ? text[..suffixIndex] : text;
    }

    private static Version ToComparableVersion(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

internal sealed record UpdateCheckResult(
    string CurrentVersionText,
    Version CurrentVersion,
    string LatestVersionText,
    Version LatestVersion,
    string ReleaseUrl)
{
    public bool IsUpdateAvailable => LatestVersion.CompareTo(CurrentVersion) > 0;
}
