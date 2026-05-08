using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class AppHandlerRegistryTests
{
    [Fact]
    public void Resolve_ReturnsDefaultHandlerWhenAppSpecificHandlerIsMissing()
    {
        var registry = new AppHandlerRegistry(Array.Empty<IAppHandler>());

        var handler = registry.Resolve("redis");

        Assert.IsType<DefaultAppHandler>(handler);
        Assert.Equal("default", handler.AppId);
    }

    [Fact]
    public void Resolve_ReturnsRegisteredHandlerIgnoringCase()
    {
        var registry = new AppHandlerRegistry(new IAppHandler[] { new TestHandler("Mosquitto") });

        var handler = registry.Resolve("mosquitto");

        Assert.Equal("Mosquitto", handler.AppId);
    }

    private sealed class TestHandler : DefaultAppHandler
    {
        public TestHandler(string appId)
        {
            AppId = appId;
        }

        public override string AppId { get; }
    }
}
