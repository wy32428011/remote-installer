using System.Net;
using System.Net.Http;
using RemoteInstaller.Services;
using Xunit;

namespace RemoteInstaller.Tests;

public class UpdateCheckServiceTests
{
    [Fact]
    public void ResolveUpdateEndpoint_UsesExplicitUpdateUrl()
    {
        var endpoint = UpdateCheckService.ResolveUpdateEndpoint(
            repositoryUrl: "https://repo.example.com/packages",
            updateCheckUrl: "updates.example.com/version.json");

        Assert.Equal("https://updates.example.com/version.json", endpoint);
    }

    [Fact]
    public void ResolveUpdateEndpoint_UsesRepositoryApiVersion_WhenExplicitUrlIsEmpty()
    {
        var endpoint = UpdateCheckService.ResolveUpdateEndpoint(
            repositoryUrl: "https://repo.example.com/packages/",
            updateCheckUrl: "");

        Assert.Equal("https://repo.example.com/packages/api/version", endpoint);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpdateAvailable_WhenRemoteVersionIsHigher()
    {
        using var httpClient = new HttpClient(new JsonResponseHandler(
            """{"latestVersion":"1.1.0","downloadUrl":"https://repo.example.com/download","releaseNotes":"修复若干问题"}"""));
        var service = new UpdateCheckService(httpClient, currentVersionProvider: () => "1.0.0");

        var result = await service.CheckForUpdatesAsync(
            repositoryUrl: "",
            updateCheckUrl: "https://repo.example.com/version.json",
            repositoryToken: "");

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("1.1.0", result.LatestVersion);
        Assert.Equal("https://repo.example.com/download", result.DownloadUrl);
        Assert.Contains("发现新版本", result.StatusMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsLatest_WhenRemoteVersionMatchesCurrentWithPrefix()
    {
        using var httpClient = new HttpClient(new JsonResponseHandler("""{"version":"v1.0.0"}"""));
        var service = new UpdateCheckService(httpClient, currentVersionProvider: () => "1.0.0");

        var result = await service.CheckForUpdatesAsync(
            repositoryUrl: "",
            updateCheckUrl: "https://repo.example.com/version.json",
            repositoryToken: "");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("v1.0.0", result.LatestVersion);
        Assert.Contains("已是最新版本", result.StatusMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUnconfiguredStatus_WhenEndpointMissing()
    {
        using var httpClient = new HttpClient(new JsonResponseHandler("""{"version":"1.1.0"}"""));
        var service = new UpdateCheckService(httpClient, currentVersionProvider: () => "1.0.0");

        var result = await service.CheckForUpdatesAsync(
            repositoryUrl: "",
            updateCheckUrl: "",
            repositoryToken: "");

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Contains("未配置", result.StatusMessage);
    }

    private sealed class JsonResponseHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonResponseHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            };

            return Task.FromResult(response);
        }
    }
}
