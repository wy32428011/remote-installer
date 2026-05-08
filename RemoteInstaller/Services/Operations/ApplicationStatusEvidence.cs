namespace RemoteInstaller.Services.Operations;

public sealed class ApplicationStatusEvidence
{
    public bool BinaryFound { get; init; }
    public bool PackageFound { get; init; }
    public bool ServiceFound { get; init; }
    public bool ServiceActive { get; init; }
    public bool ProcessFound { get; init; }
    public bool PortListening { get; init; }
    public bool ConfigOnlyResidue { get; init; }
    public bool ServiceOnlyResidue { get; init; }

    public bool HasRuntimeEvidence => ServiceActive || ProcessFound || PortListening;
    public bool HasInstalledEvidence => BinaryFound || PackageFound || HasRuntimeEvidence;
    public bool HasOnlyResidue => !HasInstalledEvidence && (ConfigOnlyResidue || ServiceOnlyResidue || ServiceFound);
}
