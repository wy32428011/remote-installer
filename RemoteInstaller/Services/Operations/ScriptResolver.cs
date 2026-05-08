using System.IO;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class ScriptResolver
{
    public string? TryResolveConfiguredScriptFilePath(string configuredScript, OperatingSystemType osType)
    {
        var extension = osType == OperatingSystemType.Windows ? ".ps1" : ".sh";
        var scriptReference = ExtractScriptReferenceToken(configuredScript, extension);

        if (string.IsNullOrWhiteSpace(scriptReference))
        {
            return null;
        }

        foreach (var candidate in BuildConfiguredScriptCandidatePaths(scriptReference))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore invalid candidate paths and continue probing known roots.
            }
        }

        return null;
    }

    public static string BuildLinuxShellScriptCommand(string scriptContent)
    {
        var normalizedScript = (scriptContent ?? string.Empty)
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        return $"bash -s <<'REMOTE_INSTALLER_CHECK_STATUS_SCRIPT'\n{normalizedScript}\nREMOTE_INSTALLER_CHECK_STATUS_SCRIPT";
    }

    public static string? ExtractScriptReferenceToken(string token, string extension)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var cleanedToken = token
            .Replace("\"", " ")
            .Replace("'", " ");

        var parts = cleanedToken.Split(
            new[] { ' ', '\t', '\r', '\n', '&', '|', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var normalizedPart = part.Replace('\\', '/');
            if (!normalizedPart.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scriptsIndex = normalizedPart.IndexOf("Scripts/", StringComparison.OrdinalIgnoreCase);
            return scriptsIndex >= 0
                ? normalizedPart[scriptsIndex..]
                : normalizedPart;
        }

        var normalizedToken = cleanedToken.Replace('\\', '/');
        var index = normalizedToken.IndexOf("Scripts/", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var extensionIndex = normalizedToken.IndexOf(extension, index, StringComparison.OrdinalIgnoreCase);
        if (extensionIndex < 0)
        {
            return null;
        }

        return normalizedToken[index..(extensionIndex + extension.Length)].Trim();
    }

    public static IEnumerable<string> BuildConfiguredScriptCandidatePaths(string scriptReference)
    {
        var normalized = scriptReference
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), normalized));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", normalized));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", normalized));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", normalized));
        }

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
