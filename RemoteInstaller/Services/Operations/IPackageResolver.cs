using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public interface IPackageResolver
{
    PackageResolution Resolve(ApplicationInfo application, RemoteHost host);
}
