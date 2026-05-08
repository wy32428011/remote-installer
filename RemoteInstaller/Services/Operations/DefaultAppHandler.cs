using System.Threading.Tasks;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public class DefaultAppHandler : IAppHandler
{
    public virtual string AppId => "default";

    public virtual Task BeforeInstallAsync(OperationContext context)
    {
        return Task.CompletedTask;
    }

    public virtual Task AfterInstallAsync(OperationContext context, ApplicationStatus status)
    {
        return Task.CompletedTask;
    }

    public virtual Task BeforeUninstallAsync(OperationContext context)
    {
        return Task.CompletedTask;
    }

    public virtual PackageResolution? ResolvePackage(OperationContext context)
    {
        return null;
    }

    public virtual void NormalizeStatus(ApplicationStatus status, ApplicationStatusEvidence evidence)
    {
        ApplicationStatusNormalizer.Normalize(status, evidence);
    }
}
