using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteInstaller.Services.Operations;

public sealed class AppHandlerRegistry
{
    private readonly DefaultAppHandler _defaultHandler = new();
    private readonly Dictionary<string, IAppHandler> _handlers;

    public AppHandlerRegistry(IEnumerable<IAppHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.AppId, StringComparer.OrdinalIgnoreCase);
    }

    public IAppHandler Resolve(string appId)
    {
        return _handlers.TryGetValue(appId, out var handler)
            ? handler
            : _defaultHandler;
    }
}
