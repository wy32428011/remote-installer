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

        foreach (var reference in BuildOperatingSystemScriptReferences(scriptReference, osType))
        {
            foreach (var candidate in BuildConfiguredScriptCandidatePaths(reference))
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
        }

        return null;
    }

    /// <summary>
    /// 获取指定操作系统的脚本文件名后缀优先级。
    /// </summary>
    public static IReadOnlyList<string> GetScriptFileNameSuffixes(OperatingSystemType osType)
    {
        return osType switch
        {
            OperatingSystemType.Windows => ["windows"],
            OperatingSystemType.CentOS => ["centos", "linux"],
            OperatingSystemType.Ubuntu => ["ubuntu", "linux"],
            _ => ["linux"]
        };
    }

    /// <summary>
    /// 基于配置里的通用脚本引用生成当前操作系统的候选脚本引用。
    /// </summary>
    public static IEnumerable<string> BuildOperatingSystemScriptReferences(string scriptReference, OperatingSystemType osType)
    {
        if (string.IsNullOrWhiteSpace(scriptReference))
        {
            yield break;
        }

        var normalizedReference = scriptReference.Replace('\\', '/');
        var extension = osType == OperatingSystemType.Windows ? ".ps1" : ".sh";
        if (!normalizedReference.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            yield return scriptReference;
            yield break;
        }

        var nameWithoutExtension = normalizedReference[..^extension.Length];
        var baseName = RemoveKnownOperatingSystemSuffix(nameWithoutExtension);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var suffix in GetScriptFileNameSuffixes(osType))
        {
            var candidate = $"{baseName}_{suffix}{extension}";
            if (emitted.Add(candidate))
            {
                yield return candidate;
            }
        }

        if (emitted.Add(normalizedReference))
        {
            yield return normalizedReference;
        }
    }

    /// <summary>
    /// 移除配置引用末尾已有的操作系统后缀，便于生成系统专属脚本名。
    /// </summary>
    private static string RemoveKnownOperatingSystemSuffix(string nameWithoutExtension)
    {
        foreach (var suffix in new[] { "_windows", "_centos", "_ubuntu", "_linux" })
        {
            if (nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return nameWithoutExtension[..^suffix.Length];
            }
        }

        return nameWithoutExtension;
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
