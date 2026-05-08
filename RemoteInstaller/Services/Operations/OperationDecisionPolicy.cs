using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public enum OperationOutcome
{
    Completed,
    Failed
}

public sealed class OperationDecision
{
    public OperationOutcome Outcome { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool HasWarning { get; init; }
}

public static class OperationDecisionPolicy
{
    public static OperationDecision DecideInstall(RemoteCommandResult? scriptResult, ApplicationStatus status)
    {
        if (status.IsInstalled)
        {
            if (scriptResult is { Failed: true })
            {
                return new OperationDecision
                {
                    Outcome = OperationOutcome.Completed,
                    HasWarning = true,
                    Message = "脚本退出异常但状态检测已确认安装"
                };
            }

            return new OperationDecision
            {
                Outcome = OperationOutcome.Completed,
                Message = status.IsRunning ? "安装完成并运行中" : "安装完成但未运行"
            };
        }

        if (scriptResult is { Failed: true })
        {
            return new OperationDecision
            {
                Outcome = OperationOutcome.Failed,
                Message = string.IsNullOrWhiteSpace(scriptResult.Stderr)
                    ? "安装脚本执行失败且状态验证未通过"
                    : scriptResult.Stderr
            };
        }

        return new OperationDecision
        {
            Outcome = OperationOutcome.Failed,
            Message = "安装验证失败，请查看日志"
        };
    }

    public static OperationDecision DecideUninstall(
        RemoteCommandResult? scriptResult,
        ApplicationStatus status,
        ApplicationStatusEvidence evidence)
    {
        if (evidence.HasRuntimeEvidence || status.IsRunning)
        {
            return new OperationDecision
            {
                Outcome = OperationOutcome.Failed,
                Message = "卸载验证失败，仍有运行证据"
            };
        }

        if (!status.IsInstalled)
        {
            if (evidence.HasOnlyResidue)
            {
                return new OperationDecision
                {
                    Outcome = OperationOutcome.Completed,
                    HasWarning = true,
                    Message = "卸载完成，但发现残留服务或配置"
                };
            }

            return new OperationDecision
            {
                Outcome = OperationOutcome.Completed,
                Message = "卸载完成"
            };
        }

        if (scriptResult is { Failed: true })
        {
            return new OperationDecision
            {
                Outcome = OperationOutcome.Failed,
                Message = string.IsNullOrWhiteSpace(scriptResult.Stderr)
                    ? "卸载脚本执行失败且状态验证未通过"
                    : scriptResult.Stderr
            };
        }

        return new OperationDecision
        {
            Outcome = OperationOutcome.Failed,
            Message = "卸载验证失败，应用仍被检测为已安装"
        };
    }
}
