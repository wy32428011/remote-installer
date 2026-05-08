namespace RemoteInstaller.Services.Operations;

public sealed class PackageResolution
{
    public bool Found { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public IReadOnlyList<string> MissingDependencies { get; init; } = Array.Empty<string>();

    public static PackageResolution FoundPackage(string path, string version, string hint)
    {
        return new PackageResolution
        {
            Found = true,
            Path = path,
            Version = version,
            Hint = hint,
            MissingDependencies = Array.Empty<string>()
        };
    }

    public static PackageResolution NotFound(string hint, IEnumerable<string>? missingDependencies = null)
    {
        return new PackageResolution
        {
            Found = false,
            Hint = hint,
            MissingDependencies = missingDependencies?.ToArray() ?? Array.Empty<string>()
        };
    }
}
