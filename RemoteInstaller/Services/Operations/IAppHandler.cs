using System.Collections.Generic;
using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public interface IAppHandler
{
    string AppId { get; }

    Task BeforeInstallAsync(OperationContext context);

    Task AfterInstallAsync(OperationContext context, ApplicationStatus status);

    Task BeforeUninstallAsync(OperationContext context);

    PackageResolution? ResolvePackage(OperationContext context);

    void NormalizeStatus(ApplicationStatus status, ApplicationStatusEvidence evidence);
}

public sealed class OperationContext
{
    public required RemoteHost Host { get; init; }

    public required ApplicationInfo Application { get; init; }

    public required Dictionary<string, string> Parameters { get; init; }
}
