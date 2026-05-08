using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ScriptProtocolParserTests
{
    [Fact]
    public void ParseTextProtocol_ReturnsProgressAndStatusEvents()
    {
        var output = string.Join('\n', new[]
        {
            "PROGRESS:Installing:40",
            "INSTALLED:true",
            "VERSION:7.2.3",
            "RUNNING:true",
            "PORT:6379",
            "SERVICE_ONLY_STALE:false",
            "STAGE:SUCCESS"
        });

        var events = ScriptProtocolParser.Parse(output).ToList();

        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Progress && item.Stage == "Installing" && item.Percent == 40);
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "INSTALLED" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Result && item.Stage == "SUCCESS");
    }

    [Fact]
    public void ParseJsonLineProtocol_ReturnsEquivalentEvents()
    {
        var output = string.Join('\n', new[]
        {
            "{\"type\":\"progress\",\"stage\":\"Verifying\",\"percent\":90}",
            "{\"type\":\"status\",\"key\":\"RUNNING\",\"value\":\"true\"}",
            "{\"type\":\"result\",\"stage\":\"success\"}"
        });

        var events = ScriptProtocolParser.Parse(output).ToList();

        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Progress && item.Stage == "Verifying" && item.Percent == 90);
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "RUNNING" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Result && item.Stage == "success");
    }

    [Fact]
    public void ParseJsonStatusObject_ExpandsKnownStatusFields()
    {
        var output = "{\"type\":\"status\",\"installed\":true,\"running\":true,\"version\":\"8.5.3\",\"port\":\"9200\"}";

        var events = ScriptProtocolParser.Parse(output).ToList();

        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "INSTALLED" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "RUNNING" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "VERSION" && item.Value == "8.5.3");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "PORT" && item.Value == "9200");
    }

    [Fact]
    public void Parse_PreservesPlainLogLines()
    {
        var events = ScriptProtocolParser.Parse("Redis 安装完成").ToList();

        var log = Assert.Single(events);
        Assert.Equal(ScriptProtocolEventKind.Log, log.Kind);
        Assert.Equal("Redis 安装完成", log.Message);
    }
}
