using System.Text.Json;

namespace RemoteInstaller.Services.Operations;

public static class ScriptProtocolParser
{
    private static readonly string[] StatusKeys =
    {
        "INSTALLED",
        "VERSION",
        "RUNNING",
        "PORT",
        "SERVICE_ONLY_STALE",
        "CONFIG_ONLY_RESIDUE",
        "UNINSTALLED",
        "PACKAGE_INSTALLED",
        "BINARY_FOUND",
        "SERVICE_FOUND",
        "SERVICE_ACTIVE",
        "SERVICE_NAME",
        "SERVICE_STATUS",
        "PROCESS_FOUND",
        "PORT_LISTENING",
        "REMOTE_ACCESS_AVAILABLE",
        "MANAGEMENT_PLUGIN_ENABLED",
        "MANAGEMENT_HTTP_READY",
        "AMQP_BIND_ALL",
        "MGMT_BIND_ALL",
        "MANAGEMENT_OPEN"
    };

    public static IEnumerable<ScriptProtocolEvent> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        var normalizedOutput = output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (var rawLine in normalizedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseJsonLine(line, out var jsonEvents))
            {
                foreach (var jsonEvent in jsonEvents)
                {
                    yield return jsonEvent;
                }

                continue;
            }

            yield return ParseTextLine(line);
        }
    }

    private static ScriptProtocolEvent ParseTextLine(string line)
    {
        if (line.StartsWith("PROGRESS:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line["PROGRESS:".Length..].Split(':');
            var stage = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            var percentText = parts.Length > 1 ? parts[1].Trim() : "0";
            _ = double.TryParse(percentText, out var percent);

            return new ScriptProtocolEvent
            {
                Kind = ScriptProtocolEventKind.Progress,
                Stage = stage,
                Percent = Math.Clamp(percent, 0, 100),
                Message = line
            };
        }

        if (line.StartsWith("STAGE:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            return new ScriptProtocolEvent
            {
                Kind = ScriptProtocolEventKind.Result,
                Stage = line[(separatorIndex + 1)..].Trim(),
                Message = line
            };
        }

        foreach (var key in StatusKeys)
        {
            var prefix = key + ":";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Status,
                    Key = key,
                    Value = line[prefix.Length..].Trim(),
                    Message = line
                };
            }
        }

        return new ScriptProtocolEvent
        {
            Kind = ScriptProtocolEventKind.Log,
            Message = line
        };
    }

    private static bool TryParseJsonLine(string line, out IReadOnlyList<ScriptProtocolEvent> events)
    {
        events = Array.Empty<ScriptProtocolEvent>();

        if (!line.StartsWith("{", StringComparison.Ordinal) || !line.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeProperty)
                ? typeProperty.GetString() ?? string.Empty
                : string.Empty;

            events = type.ToLowerInvariant() switch
            {
                "progress" => new[]
                {
                    new ScriptProtocolEvent
                    {
                        Kind = ScriptProtocolEventKind.Progress,
                        Stage = GetString(root, "stage"),
                        Percent = Math.Clamp(GetDouble(root, "percent"), 0, 100),
                        Message = line
                    }
                },
                "status" => ParseJsonStatus(root, line),
                "result" => new[]
                {
                    new ScriptProtocolEvent
                    {
                        Kind = ScriptProtocolEventKind.Result,
                        Stage = GetString(root, "stage"),
                        Message = line
                    }
                },
                _ => new[]
                {
                    new ScriptProtocolEvent
                    {
                        Kind = ScriptProtocolEventKind.Log,
                        Message = line
                    }
                }
            };

            return true;
        }
        catch (JsonException)
        {
            events = new[]
            {
                new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Log,
                    Message = line
                }
            };
            return true;
        }
    }

    private static IReadOnlyList<ScriptProtocolEvent> ParseJsonStatus(JsonElement root, string line)
    {
        if (root.TryGetProperty("key", out var keyProperty))
        {
            return new[]
            {
                new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Status,
                    Key = keyProperty.GetString() ?? string.Empty,
                    Value = root.TryGetProperty("value", out var valueProperty) ? JsonValueToString(valueProperty) : string.Empty,
                    Message = line
                }
            };
        }

        var events = new List<ScriptProtocolEvent>();
        AddJsonStatus(events, root, line, "installed", "INSTALLED");
        AddJsonStatus(events, root, line, "running", "RUNNING");
        AddJsonStatus(events, root, line, "version", "VERSION");
        AddJsonStatus(events, root, line, "port", "PORT");
        AddJsonStatus(events, root, line, "serviceOnlyStale", "SERVICE_ONLY_STALE");
        AddJsonStatus(events, root, line, "service_only_stale", "SERVICE_ONLY_STALE");
        AddJsonStatus(events, root, line, "configOnlyResidue", "CONFIG_ONLY_RESIDUE");
        AddJsonStatus(events, root, line, "config_only_residue", "CONFIG_ONLY_RESIDUE");

        if (events.Count == 0)
        {
            events.Add(new ScriptProtocolEvent
            {
                Kind = ScriptProtocolEventKind.Log,
                Message = line
            });
        }

        return events;
    }

    private static void AddJsonStatus(
        ICollection<ScriptProtocolEvent> events,
        JsonElement root,
        string line,
        string jsonProperty,
        string statusKey)
    {
        if (!root.TryGetProperty(jsonProperty, out var property))
        {
            return;
        }

        events.Add(new ScriptProtocolEvent
        {
            Kind = ScriptProtocolEventKind.Status,
            Key = statusKey,
            Value = JsonValueToString(property),
            Message = line
        });
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static double GetDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
        {
            return number;
        }

        return double.TryParse(JsonValueToString(property), out var parsed)
            ? parsed
            : 0;
    }

    private static string JsonValueToString(JsonElement property)
    {
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => property.ToString()
        };
    }
}
