using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public static class ApplicationStatusNormalizer
{
    public static void Normalize(ApplicationStatus status, ApplicationStatusEvidence evidence)
    {
        if (evidence.HasRuntimeEvidence)
        {
            status.IsInstalled = true;
            status.IsRunning = true;
        }
        else if (evidence.HasInstalledEvidence)
        {
            status.IsInstalled = true;
        }

        if (status.IsRunning && !status.IsInstalled)
        {
            status.IsInstalled = true;
        }

        if (evidence.HasOnlyResidue && !evidence.HasInstalledEvidence)
        {
            status.IsInstalled = false;
            status.IsRunning = false;
        }

        if (status.IsInstalled && string.IsNullOrWhiteSpace(status.InstalledVersion))
        {
            status.InstalledVersion = "未知";
        }
    }

    public static ApplicationStatusEvidence BuildEvidence(IEnumerable<ScriptProtocolEvent> events)
    {
        var binaryFound = false;
        var packageFound = false;
        var serviceFound = false;
        var serviceActive = false;
        var processFound = false;
        var portListening = false;
        var configOnlyResidue = false;
        var serviceOnlyResidue = false;

        foreach (var item in events.Where(item => item.Kind == ScriptProtocolEventKind.Status))
        {
            if (item.Key.Equals("INSTALLED", StringComparison.OrdinalIgnoreCase) && ParseBool(item.Value))
            {
                binaryFound = true;
            }
            else if (item.Key.Equals("BINARY_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                binaryFound = ParseBool(item.Value);
            }
            else if (item.Key.Equals("PACKAGE_INSTALLED", StringComparison.OrdinalIgnoreCase))
            {
                packageFound = ParseBool(item.Value);
            }
            else if (item.Key.Equals("SERVICE_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                serviceFound = ParseBool(item.Value);
            }
            else if (item.Key.Equals("SERVICE_ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                serviceActive = ParseBool(item.Value);
            }
            else if (item.Key.Equals("SERVICE_NAME", StringComparison.OrdinalIgnoreCase))
            {
                serviceFound = IsKnownServiceValue(item.Value);
            }
            else if (item.Key.Equals("SERVICE_STATUS", StringComparison.OrdinalIgnoreCase))
            {
                serviceFound = serviceFound || IsKnownServiceValue(item.Value);
                serviceActive = serviceActive || ParseBool(item.Value);
            }
            else if (item.Key.Equals("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("PROCESS_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                processFound = ParseBool(item.Value);
            }
            else if (item.Key.Equals("PORT", StringComparison.OrdinalIgnoreCase))
            {
                // PORT 只是脚本上报的端口配置值，不能等同于端口正在监听。
                // 只有 PORT_LISTENING、进程或 active 服务这类事实证据才能判定为运行中。
            }
            else if (item.Key.Equals("PORT_LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                portListening = ParseBool(item.Value);
            }
            else if (item.Key.Equals("MANAGEMENT_OPEN", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("MANAGEMENT_HTTP_READY", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("REMOTE_ACCESS_AVAILABLE", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("AMQP_BIND_ALL", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("MGMT_BIND_ALL", StringComparison.OrdinalIgnoreCase))
            {
                // 这些字段只描述 RabbitMQ 的访问能力，不能单独证明服务仍在运行。
                // 运行态必须来自 RUNNING、PROCESS_FOUND、SERVICE_ACTIVE 或 PORT_LISTENING。
            }
            else if (item.Key.Equals("SERVICE_ONLY_STALE", StringComparison.OrdinalIgnoreCase) ||
                     item.Key.Equals("SERVICE_ONLY_RESIDUE", StringComparison.OrdinalIgnoreCase))
            {
                serviceOnlyResidue = ParseBool(item.Value);
            }
            else if (item.Key.Equals("CONFIG_ONLY_RESIDUE", StringComparison.OrdinalIgnoreCase))
            {
                configOnlyResidue = ParseBool(item.Value);
            }
        }

        return new ApplicationStatusEvidence
        {
            BinaryFound = binaryFound,
            PackageFound = packageFound,
            ServiceFound = serviceFound,
            ServiceActive = serviceActive,
            ProcessFound = processFound,
            PortListening = portListening,
            ConfigOnlyResidue = configOnlyResidue,
            ServiceOnlyResidue = serviceOnlyResidue
        };
    }

    public static void ApplyStatusEvents(ApplicationStatus status, IEnumerable<ScriptProtocolEvent> events)
    {
        foreach (var item in events.Where(item => item.Kind == ScriptProtocolEventKind.Status))
        {
            if (item.Key.Equals("INSTALLED", StringComparison.OrdinalIgnoreCase))
            {
                status.IsInstalled = ParseBool(item.Value);
            }
            else if (item.Key.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
            {
                status.InstalledVersion = item.Value.Trim();
            }
            else if (item.Key.Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                status.IsRunning = ParseBool(item.Value);
            }
            else if (item.Key.Equals("PORT", StringComparison.OrdinalIgnoreCase))
            {
                status.Port = item.Value.Trim();
            }
        }
    }

    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleanValue = value.Trim().ToLowerInvariant();
        return cleanValue is "true" or "1" or "running" or "active" or "installed" or "yes";
    }

    private static bool IsKnownServiceValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleanValue = value.Trim().ToLowerInvariant();
        return cleanValue is not "unknown" and not "not-found" and not "not_found" and not "none" and not "false";
    }
}
