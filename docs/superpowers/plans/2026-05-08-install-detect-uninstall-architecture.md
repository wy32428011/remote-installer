# Install Detect Uninstall Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor install, detect, and uninstall into structured operation components while preserving existing scripts and behavior.

**Architecture:** Keep `InstallerService` as the public compatibility facade at first, then move command execution, script protocol parsing, script resolution, result decision, package resolution, status normalization, and app-specific hooks into focused services. Each stage is test-first and must leave the app buildable.

**Tech Stack:** C#/.NET 10 WPF, xUnit, SSH.NET, existing Bash/PowerShell scripts, `CommunityToolkit.Mvvm`.

---

## File Structure

Create focused operation services under `RemoteInstaller/Services/Operations/` using namespace `RemoteInstaller.Services.Operations`.

- Create: `RemoteInstaller/Services/Operations/RemoteCommandResult.cs`
  - Owns structured command execution output.
- Create: `RemoteInstaller/Services/Operations/IRemoteCommandRunner.cs`
  - Interface for remote command execution.
- Create: `RemoteInstaller/Services/Operations/SshRemoteCommandRunner.cs`
  - Adapter from `SshService` to `IRemoteCommandRunner`.
- Create: `RemoteInstaller/Services/Operations/ScriptProtocolEvent.cs`
  - Event and enum types for parsed script output.
- Create: `RemoteInstaller/Services/Operations/ScriptProtocolParser.cs`
  - Parses existing text protocol and future JSONL protocol.
- Create: `RemoteInstaller/Services/Operations/ApplicationStatusEvidence.cs`
  - Evidence model for binary, package, service, process, port, and residue state.
- Create: `RemoteInstaller/Services/Operations/ApplicationStatusNormalizer.cs`
  - Central status normalization policy.
- Create: `RemoteInstaller/Services/Operations/OperationDecisionPolicy.cs`
  - Installation and uninstallation final result arbitration.
- Create: `RemoteInstaller/Services/Operations/ScriptResolver.cs`
  - Resolves script file references and wraps Linux scripts for here-doc execution.
- Create: `RemoteInstaller/Services/Operations/PackageResolution.cs`
  - Shared local package selection result.
- Create: `RemoteInstaller/Services/Operations/IPackageResolver.cs`
  - Interface for package selection.
- Create: `RemoteInstaller/Services/Operations/DefaultPackageResolver.cs`
  - Moves offline package selection out of ViewModel in later tasks.
- Create: `RemoteInstaller/Services/Operations/IAppHandler.cs`
  - App-specific operation hooks.
- Create: `RemoteInstaller/Services/Operations/DefaultAppHandler.cs`
  - No-op default app handler.
- Create: `RemoteInstaller/Services/Operations/AppHandlerRegistry.cs`
  - Resolves per-app handler.
- Modify: `RemoteInstaller/Services/SshService.cs`
  - Adds structured command execution method while preserving existing API.
- Modify: `RemoteInstaller/Services/InstallerService.cs`
  - Gradually delegates to new operation services.
- Modify: `RemoteInstaller/Services/LogCollector.cs`
  - Uses `ScriptProtocolParser` for progress parsing.
- Modify: `RemoteInstaller/ViewModels/InstallConfigViewModel.cs`
  - Delegates package resolution after resolver exists.
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs`
  - Removes app operation details after services are ready.
- Test: `RemoteInstaller.Tests/OperationModelsTests.cs`
- Test: `RemoteInstaller.Tests/ScriptProtocolParserTests.cs`
- Test: `RemoteInstaller.Tests/OperationDecisionPolicyTests.cs`
- Test: `RemoteInstaller.Tests/ScriptResolverTests.cs`
- Test: `RemoteInstaller.Tests/ApplicationStatusNormalizerTests.cs`
- Test: `RemoteInstaller.Tests/RemoteCommandRunnerContractTests.cs`
- Test: `RemoteInstaller.Tests/PackageResolverTests.cs`
- Test: `RemoteInstaller.Tests/AppHandlerRegistryTests.cs`
- Test: existing `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs`
- Test: existing `RemoteInstaller.Tests/RabbitMqOfflineSelectionTests.cs`

## Task 1: Operation Model Foundation

**Files:**
- Create: `RemoteInstaller/Services/Operations/RemoteCommandResult.cs`
- Create: `RemoteInstaller/Services/Operations/ApplicationStatusEvidence.cs`
- Create: `RemoteInstaller/Services/Operations/PackageResolution.cs`
- Test: `RemoteInstaller.Tests/OperationModelsTests.cs`

- [ ] **Step 1: Write failing tests for default model behavior**

Create `RemoteInstaller.Tests/OperationModelsTests.cs`:

```csharp
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationModelsTests
{
    [Fact]
    public void RemoteCommandResult_SuccessReflectsExitCodeZeroWithoutTimeout()
    {
        var result = new RemoteCommandResult
        {
            Command = "echo ok",
            ExitCode = 0,
            Stdout = "ok",
            Stderr = string.Empty,
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(15)
        };

        Assert.True(result.Succeeded);
        Assert.False(result.Failed);
    }

    [Fact]
    public void ApplicationStatusEvidence_TreatsRuntimeEvidenceAsInstalledEvidence()
    {
        var evidence = new ApplicationStatusEvidence
        {
            ProcessFound = true
        };

        Assert.True(evidence.HasRuntimeEvidence);
        Assert.True(evidence.HasInstalledEvidence);
        Assert.False(evidence.HasOnlyResidue);
    }

    [Fact]
    public void PackageResolution_NotFoundCarriesHintAndMissingDependencies()
    {
        var resolution = PackageResolution.NotFound(
            "缺少 RabbitMQ 离线依赖",
            new[] { "erlang-base", "logrotate" });

        Assert.False(resolution.Found);
        Assert.Equal("缺少 RabbitMQ 离线依赖", resolution.Hint);
        Assert.Equal(new[] { "erlang-base", "logrotate" }, resolution.MissingDependencies);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationModelsTests --no-restore
```

Expected: compile failure because `RemoteInstaller.Services.Operations` types do not exist.

- [ ] **Step 3: Add model implementations**

Create `RemoteInstaller/Services/Operations/RemoteCommandResult.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public sealed class RemoteCommandResult
{
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public TimeSpan Duration { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
    public bool Failed => !Succeeded;

    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Stderr))
            {
                return Stdout;
            }

            if (string.IsNullOrWhiteSpace(Stdout))
            {
                return Stderr;
            }

            return Stdout + Environment.NewLine + Stderr;
        }
    }
}
```

Create `RemoteInstaller/Services/Operations/ApplicationStatusEvidence.cs`:

```csharp
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
```

Create `RemoteInstaller/Services/Operations/PackageResolution.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests and verify they pass**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationModelsTests --no-restore
```

Expected: `OperationModelsTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add RemoteInstaller/Services/Operations/RemoteCommandResult.cs RemoteInstaller/Services/Operations/ApplicationStatusEvidence.cs RemoteInstaller/Services/Operations/PackageResolution.cs RemoteInstaller.Tests/OperationModelsTests.cs
git commit -m "feat: add operation result models"
```

## Task 2: Script Protocol Parser

**Files:**
- Create: `RemoteInstaller/Services/Operations/ScriptProtocolEvent.cs`
- Create: `RemoteInstaller/Services/Operations/ScriptProtocolParser.cs`
- Test: `RemoteInstaller.Tests/ScriptProtocolParserTests.cs`

- [ ] **Step 1: Write failing parser tests**

Create `RemoteInstaller.Tests/ScriptProtocolParserTests.cs`:

```csharp
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ScriptProtocolParserTests
{
    [Fact]
    public void ParseTextProtocol_ReturnsProgressAndStatusEvents()
    {
        var output = string.Join('\n', new[]
        {
            "PROGRESS:Installing:40",
            "INSTALLED:true",
            "VERSION:7.2.3",
            "RUNNING:true",
            "PORT:6379",
            "SERVICE_ONLY_STALE:false",
            "STAGE:SUCCESS"
        });

        var events = ScriptProtocolParser.Parse(output).ToList();

        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Progress && item.Stage == "Installing" && item.Percent == 40);
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "INSTALLED" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Result && item.Stage == "SUCCESS");
    }

    [Fact]
    public void ParseJsonLineProtocol_ReturnsEquivalentEvents()
    {
        var output = string.Join('\n', new[]
        {
            "{\"type\":\"progress\",\"stage\":\"Verifying\",\"percent\":90}",
            "{\"type\":\"status\",\"key\":\"RUNNING\",\"value\":\"true\"}",
            "{\"type\":\"result\",\"stage\":\"success\"}"
        });

        var events = ScriptProtocolParser.Parse(output).ToList();

        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Progress && item.Stage == "Verifying" && item.Percent == 90);
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Status && item.Key == "RUNNING" && item.Value == "true");
        Assert.Contains(events, item => item.Kind == ScriptProtocolEventKind.Result && item.Stage == "success");
    }

    [Fact]
    public void Parse_PreservesPlainLogLines()
    {
        var events = ScriptProtocolParser.Parse("Redis 安装完成").ToList();

        var log = Assert.Single(events);
        Assert.Equal(ScriptProtocolEventKind.Log, log.Kind);
        Assert.Equal("Redis 安装完成", log.Message);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ScriptProtocolParserTests --no-restore
```

Expected: compile failure because parser types do not exist.

- [ ] **Step 3: Add parser types**

Create `RemoteInstaller/Services/Operations/ScriptProtocolEvent.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public enum ScriptProtocolEventKind
{
    Log,
    Progress,
    Status,
    Result
}

public sealed class ScriptProtocolEvent
{
    public ScriptProtocolEventKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
```

Create `RemoteInstaller/Services/Operations/ScriptProtocolParser.cs`:

```csharp
using System.Text.Json;

namespace RemoteInstaller.Services.Operations;

public static class ScriptProtocolParser
{
    public static IEnumerable<ScriptProtocolEvent> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var rawLine in output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseJsonLine(line, out var jsonEvent))
            {
                yield return jsonEvent;
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
            var separatorIndex = line.IndexOf(':');
            return new ScriptProtocolEvent
            {
                Kind = ScriptProtocolEventKind.Result,
                Stage = line[(separatorIndex + 1)..].Trim(),
                Message = line
            };
        }

        var statusKeys = new[]
        {
            "INSTALLED",
            "VERSION",
            "RUNNING",
            "PORT",
            "SERVICE_ONLY_STALE",
            "CONFIG_ONLY_RESIDUE",
            "UNINSTALLED"
        };

        foreach (var key in statusKeys)
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

    private static bool TryParseJsonLine(string line, out ScriptProtocolEvent scriptEvent)
    {
        scriptEvent = new ScriptProtocolEvent();

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

            scriptEvent = type.ToLowerInvariant() switch
            {
                "progress" => new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Progress,
                    Stage = root.TryGetProperty("stage", out var stageProperty) ? stageProperty.GetString() ?? string.Empty : string.Empty,
                    Percent = root.TryGetProperty("percent", out var percentProperty) ? percentProperty.GetDouble() : 0,
                    Message = line
                },
                "status" => new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Status,
                    Key = root.TryGetProperty("key", out var keyProperty) ? keyProperty.GetString() ?? string.Empty : string.Empty,
                    Value = root.TryGetProperty("value", out var valueProperty) ? valueProperty.ToString() : string.Empty,
                    Message = line
                },
                "result" => new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Result,
                    Stage = root.TryGetProperty("stage", out var resultStageProperty) ? resultStageProperty.GetString() ?? string.Empty : string.Empty,
                    Message = line
                },
                _ => new ScriptProtocolEvent
                {
                    Kind = ScriptProtocolEventKind.Log,
                    Message = line
                }
            };

            return true;
        }
        catch (JsonException)
        {
            scriptEvent = new ScriptProtocolEvent
            {
                Kind = ScriptProtocolEventKind.Log,
                Message = line
            };
            return true;
        }
    }
}
```

- [ ] **Step 4: Run parser tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ScriptProtocolParserTests --no-restore
```

Expected: `ScriptProtocolParserTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add RemoteInstaller/Services/Operations/ScriptProtocolEvent.cs RemoteInstaller/Services/Operations/ScriptProtocolParser.cs RemoteInstaller.Tests/ScriptProtocolParserTests.cs
git commit -m "feat: add script protocol parser"
```

## Task 3: Operation Decision Policy

**Files:**
- Create: `RemoteInstaller/Services/Operations/OperationDecisionPolicy.cs`
- Test: `RemoteInstaller.Tests/OperationDecisionPolicyTests.cs`

- [ ] **Step 1: Write failing decision policy tests**

Create `RemoteInstaller.Tests/OperationDecisionPolicyTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class OperationDecisionPolicyTests
{
    [Fact]
    public void DecideInstall_CompletesWhenStatusIsInstalledEvenIfScriptFailed()
    {
        var command = new RemoteCommandResult { ExitCode = 1, Stderr = "service warmup failed" };
        var status = new ApplicationStatus { IsInstalled = true, IsRunning = true, InstalledVersion = "7.2.3" };

        var decision = OperationDecisionPolicy.DecideInstall(command, status);

        Assert.Equal(OperationOutcome.Completed, decision.Outcome);
        Assert.True(decision.HasWarning);
        Assert.Contains("状态检测已确认安装", decision.Message);
    }

    [Fact]
    public void DecideInstall_FailsWhenScriptSucceededButStatusIsNotInstalled()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = false, IsRunning = false };

        var decision = OperationDecisionPolicy.DecideInstall(command, status);

        Assert.Equal(OperationOutcome.Failed, decision.Outcome);
        Assert.Contains("安装验证失败", decision.Message);
    }

    [Fact]
    public void DecideUninstall_FailsWhenRuntimeEvidenceStillExists()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = true, IsRunning = true };
        var evidence = new ApplicationStatusEvidence { ProcessFound = true };

        var decision = OperationDecisionPolicy.DecideUninstall(command, status, evidence);

        Assert.Equal(OperationOutcome.Failed, decision.Outcome);
        Assert.Contains("仍有运行证据", decision.Message);
    }

    [Fact]
    public void DecideUninstall_CompletesWithWarningForResidueOnly()
    {
        var command = new RemoteCommandResult { ExitCode = 0 };
        var status = new ApplicationStatus { IsInstalled = false, IsRunning = false };
        var evidence = new ApplicationStatusEvidence { ServiceOnlyResidue = true };

        var decision = OperationDecisionPolicy.DecideUninstall(command, status, evidence);

        Assert.Equal(OperationOutcome.Completed, decision.Outcome);
        Assert.True(decision.HasWarning);
        Assert.Contains("残留", decision.Message);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationDecisionPolicyTests --no-restore
```

Expected: compile failure because `OperationDecisionPolicy` types do not exist.

- [ ] **Step 3: Add decision policy**

Create `RemoteInstaller/Services/Operations/OperationDecisionPolicy.cs`:

```csharp
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
```

- [ ] **Step 4: Run decision policy tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter OperationDecisionPolicyTests --no-restore
```

Expected: `OperationDecisionPolicyTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add RemoteInstaller/Services/Operations/OperationDecisionPolicy.cs RemoteInstaller.Tests/OperationDecisionPolicyTests.cs
git commit -m "feat: add operation decision policy"
```

## Task 4: Structured Remote Command Runner

**Files:**
- Create: `RemoteInstaller/Services/Operations/IRemoteCommandRunner.cs`
- Create: `RemoteInstaller/Services/Operations/SshRemoteCommandRunner.cs`
- Modify: `RemoteInstaller/Services/SshService.cs`
- Test: `RemoteInstaller.Tests/RemoteCommandRunnerContractTests.cs`

- [ ] **Step 1: Write failing API contract tests**

Create `RemoteInstaller.Tests/RemoteCommandRunnerContractTests.cs`:

```csharp
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class RemoteCommandRunnerContractTests
{
    [Fact]
    public async Task FakeRunner_ReturnsNonZeroExitCodeWithoutThrowing()
    {
        IRemoteCommandRunner runner = new FakeRemoteCommandRunner(new RemoteCommandResult
        {
            Command = "false",
            ExitCode = 7,
            Stdout = "partial output",
            Stderr = "script failed",
            TimedOut = false,
            Duration = TimeSpan.FromMilliseconds(5)
        });

        var result = await runner.ExecuteAsync("false", output => { }, CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.True(result.Failed);
        Assert.Equal("script failed", result.Stderr);
    }

    [Fact]
    public void SshService_ExposesStructuredCommandResultMethod()
    {
        var method = typeof(RemoteInstaller.Services.SshService).GetMethod("ExecuteCommandResultAsync");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RemoteCommandResult>), method!.ReturnType);
    }

    private sealed class FakeRemoteCommandRunner : IRemoteCommandRunner
    {
        private readonly RemoteCommandResult _result;

        public FakeRemoteCommandRunner(RemoteCommandResult result)
        {
            _result = result;
        }

        public Task<RemoteCommandResult> ExecuteAsync(
            string command,
            Action<string>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            onOutput?.Invoke(_result.CombinedOutput);
            return Task.FromResult(_result);
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter RemoteCommandRunnerContractTests --no-restore
```

Expected: compile failure because `IRemoteCommandRunner` does not exist and `SshService.ExecuteCommandResultAsync` is absent.

- [ ] **Step 3: Add runner interface and adapter**

Create `RemoteInstaller/Services/Operations/IRemoteCommandRunner.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public interface IRemoteCommandRunner
{
    Task<RemoteCommandResult> ExecuteAsync(
        string command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}
```

Create `RemoteInstaller/Services/Operations/SshRemoteCommandRunner.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public sealed class SshRemoteCommandRunner : IRemoteCommandRunner
{
    private readonly SshService _sshService;

    public SshRemoteCommandRunner(SshService sshService)
    {
        _sshService = sshService;
    }

    public Task<RemoteCommandResult> ExecuteAsync(
        string command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        return _sshService.ExecuteCommandResultAsync(command, onOutput, cancellationToken);
    }
}
```

- [ ] **Step 4: Add structured method to SshService**

Modify `RemoteInstaller/Services/SshService.cs`:

- Add `using System.Diagnostics;`
- Add `using RemoteInstaller.Services.Operations;`
- Add a public method named `ExecuteCommandResultAsync`.
- Update existing `ExecuteCommandAsync` to call the new method and preserve current `throwOnError` behavior.

The wrapper should have this shape:

```csharp
public async Task<string> ExecuteCommandAsync(
    string command,
    Action<string>? onOutput = null,
    CancellationToken cancellationToken = default,
    bool throwOnError = false)
{
    var result = await ExecuteCommandResultAsync(command, onOutput, cancellationToken);

    if (throwOnError && result.ExitCode != 0)
    {
        var error = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
        throw new Exception($"远程命令执行失败 (ExitCode: {result.ExitCode}): {error}");
    }

    return result.CombinedOutput;
}
```

The new method should reuse the existing command execution body and return:

```csharp
return new RemoteCommandResult
{
    Command = cleanCommand,
    ExitCode = cmd.ExitStatus,
    Stdout = stdoutOutput,
    Stderr = stderrOutput,
    TimedOut = false,
    Duration = stopwatch.Elapsed
};
```

When the command object is disposed after partial output, return `ExitCode = -1`, `Stdout = fullOutput.ToString()`, `Stderr = string.Empty`, and `TimedOut = false`.

- [ ] **Step 5: Run contract tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter RemoteCommandRunnerContractTests --no-restore
```

Expected: `RemoteCommandRunnerContractTests` pass.

- [ ] **Step 6: Run SSH-sensitive existing tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusCoverageTests|ElasticsearchStatusTests" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git add RemoteInstaller/Services/Operations/IRemoteCommandRunner.cs RemoteInstaller/Services/Operations/SshRemoteCommandRunner.cs RemoteInstaller/Services/SshService.cs RemoteInstaller.Tests/RemoteCommandRunnerContractTests.cs
git commit -m "feat: add structured remote command runner"
```

## Task 5: Script Resolver Extraction

**Files:**
- Create: `RemoteInstaller/Services/Operations/ScriptResolver.cs`
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Test: `RemoteInstaller.Tests/ScriptResolverTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Create `RemoteInstaller.Tests/ScriptResolverTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ScriptResolverTests
{
    [Fact]
    public void BuildLinuxShellScriptCommand_WrapsScriptInQuotedHereDoc()
    {
        var command = ScriptResolver.BuildLinuxShellScriptCommand("#!/bin/bash\r\necho ok\r\n");

        Assert.StartsWith("bash -s <<'REMOTE_INSTALLER_CHECK_STATUS_SCRIPT'", command);
        Assert.Contains("echo ok", command);
        Assert.EndsWith("REMOTE_INSTALLER_CHECK_STATUS_SCRIPT", command);
        Assert.DoesNotContain("\r", command);
    }

    [Fact]
    public void ExtractScriptReferenceToken_ExtractsBashScriptPath()
    {
        var token = ScriptResolver.ExtractScriptReferenceToken("export PORT=6379 && bash Scripts/Redis/check_status_linux.sh", ".sh");

        Assert.Equal("Scripts/Redis/check_status_linux.sh", token);
    }

    [Fact]
    public void ResolveConfiguredScriptFilePath_FindsProjectScript()
    {
        var resolver = new ScriptResolver();

        var path = resolver.TryResolveConfiguredScriptFilePath(
            "Scripts/Redis/check_status_linux.sh",
            OperatingSystemType.Ubuntu);

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith(Path.Combine("Scripts", "Redis", "check_status_linux.sh"), path);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ScriptResolverTests --no-restore
```

Expected: compile failure because `ScriptResolver` does not exist.

- [ ] **Step 3: Implement ScriptResolver by moving existing logic**

Create `RemoteInstaller/Services/Operations/ScriptResolver.cs` and move the logic currently in `InstallerService` for:

- `BuildLinuxShellScriptCommand`
- `TryResolveConfiguredScriptFilePath`
- `ExtractScriptReferenceToken`
- `BuildConfiguredScriptCandidatePaths`

Expose these members:

```csharp
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
                continue;
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

        var normalizedToken = token.Trim().Trim('"', '\'');
        if (normalizedToken.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedToken.Replace('\\', '/');
        }

        var parts = normalizedToken.Split(new[] { ' ', '\t', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return parts
            .Select(part => part.Trim().Trim('"', '\'').Replace('\\', '/'))
            .FirstOrDefault(part => part.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<string> BuildConfiguredScriptCandidatePaths(string scriptReference)
    {
        var normalized = scriptReference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.IsPathRooted(normalized) ? normalized : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized),
            Path.Combine(Directory.GetCurrentDirectory(), normalized),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", normalized),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", normalized),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", normalized)
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Update InstallerService to use ScriptResolver**

Modify `RemoteInstaller/Services/InstallerService.cs`:

- Add `using RemoteInstaller.Services.Operations;`
- Add field:

```csharp
private readonly ScriptResolver _scriptResolver = new();
```

- Replace calls to local `BuildLinuxShellScriptCommand` with `ScriptResolver.BuildLinuxShellScriptCommand`.
- Replace calls to local `TryResolveConfiguredScriptFilePath` with `_scriptResolver.TryResolveConfiguredScriptFilePath`.
- Remove the moved private methods after all call sites compile.

- [ ] **Step 5: Run resolver and status tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ScriptResolverTests|ApplicationStatusCoverageTests|ElasticsearchStatusTests" --no-restore
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add RemoteInstaller/Services/Operations/ScriptResolver.cs RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/ScriptResolverTests.cs
git commit -m "refactor: extract script resolver"
```

## Task 6: Status Normalization Engine

**Files:**
- Create: `RemoteInstaller/Services/Operations/ApplicationStatusNormalizer.cs`
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Test: `RemoteInstaller.Tests/ApplicationStatusNormalizerTests.cs`

- [ ] **Step 1: Write failing normalization tests**

Create `RemoteInstaller.Tests/ApplicationStatusNormalizerTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class ApplicationStatusNormalizerTests
{
    [Fact]
    public void Normalize_RuntimeEvidenceMarksInstalledAndRunning()
    {
        var status = new ApplicationStatus
        {
            IsInstalled = false,
            IsRunning = false,
            InstalledVersion = string.Empty
        };
        var evidence = new ApplicationStatusEvidence { PortListening = true };

        ApplicationStatusNormalizer.Normalize(status, evidence);

        Assert.True(status.IsInstalled);
        Assert.True(status.IsRunning);
        Assert.Equal("未知", status.InstalledVersion);
    }

    [Fact]
    public void Normalize_ServiceResidueDoesNotMarkInstalled()
    {
        var status = new ApplicationStatus
        {
            IsInstalled = false,
            IsRunning = false,
            InstalledVersion = string.Empty
        };
        var evidence = new ApplicationStatusEvidence { ServiceOnlyResidue = true };

        ApplicationStatusNormalizer.Normalize(status, evidence);

        Assert.False(status.IsInstalled);
        Assert.False(status.IsRunning);
    }

    [Fact]
    public void BuildEvidenceFromProtocolEvents_ReadsResidueAndRunningFlags()
    {
        var events = ScriptProtocolParser.Parse(string.Join('\n', new[]
        {
            "RUNNING:true",
            "PORT:6379",
            "SERVICE_ONLY_STALE:false",
            "CONFIG_ONLY_RESIDUE:false"
        }));

        var evidence = ApplicationStatusNormalizer.BuildEvidence(events);

        Assert.True(evidence.ProcessFound);
        Assert.True(evidence.PortListening);
        Assert.False(evidence.ServiceOnlyResidue);
        Assert.False(evidence.ConfigOnlyResidue);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ApplicationStatusNormalizerTests --no-restore
```

Expected: compile failure because `ApplicationStatusNormalizer` does not exist.

- [ ] **Step 3: Implement normalizer**

Create `RemoteInstaller/Services/Operations/ApplicationStatusNormalizer.cs`:

```csharp
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

        if (status.IsRunning && !status.IsInstalled)
        {
            status.IsInstalled = true;
        }

        if (status.IsInstalled && string.IsNullOrWhiteSpace(status.InstalledVersion))
        {
            status.InstalledVersion = "未知";
        }

        if (evidence.HasOnlyResidue && !evidence.HasInstalledEvidence)
        {
            status.IsInstalled = false;
            status.IsRunning = false;
        }
    }

    public static ApplicationStatusEvidence BuildEvidence(IEnumerable<ScriptProtocolEvent> events)
    {
        var processFound = false;
        var portListening = false;
        var serviceOnlyResidue = false;
        var configOnlyResidue = false;
        var packageFound = false;
        var binaryFound = false;

        foreach (var item in events.Where(item => item.Kind == ScriptProtocolEventKind.Status))
        {
            if (item.Key.Equals("RUNNING", StringComparison.OrdinalIgnoreCase) && ParseBool(item.Value))
            {
                processFound = true;
            }
            else if (item.Key.Equals("PORT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Value))
            {
                portListening = true;
            }
            else if (item.Key.Equals("SERVICE_ONLY_STALE", StringComparison.OrdinalIgnoreCase))
            {
                serviceOnlyResidue = ParseBool(item.Value);
            }
            else if (item.Key.Equals("CONFIG_ONLY_RESIDUE", StringComparison.OrdinalIgnoreCase))
            {
                configOnlyResidue = ParseBool(item.Value);
            }
            else if (item.Key.Equals("PACKAGE_INSTALLED", StringComparison.OrdinalIgnoreCase))
            {
                packageFound = ParseBool(item.Value);
            }
            else if (item.Key.Equals("INSTALLED", StringComparison.OrdinalIgnoreCase))
            {
                binaryFound = ParseBool(item.Value);
            }
        }

        return new ApplicationStatusEvidence
        {
            BinaryFound = binaryFound,
            PackageFound = packageFound,
            ProcessFound = processFound,
            PortListening = portListening,
            ServiceOnlyResidue = serviceOnlyResidue,
            ConfigOnlyResidue = configOnlyResidue
        };
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
}
```

- [ ] **Step 4: Use normalizer in InstallerService parsing**

Modify `RemoteInstaller/Services/InstallerService.cs`:

- In `ParseCheckOutput`, call `ScriptProtocolParser.Parse(output).ToList()`.
- Set `status` fields from status events.
- Build evidence using `ApplicationStatusNormalizer.BuildEvidence(events)`.
- Call `ApplicationStatusNormalizer.Normalize(status, evidence)`.
- Keep the existing `ParseBool` method until all call sites are migrated.

- [ ] **Step 5: Run normalization and existing status tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusNormalizerTests|ElasticsearchStatusTests|LinuxServiceResidueScriptTests" --no-restore
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add RemoteInstaller/Services/Operations/ApplicationStatusNormalizer.cs RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/ApplicationStatusNormalizerTests.cs
git commit -m "refactor: centralize status normalization"
```

## Task 7: Integrate Structured Script Result in Install Path

**Files:**
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Test: `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs`

- [ ] **Step 1: Extend source-level regression test**

Modify `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs` and add:

```csharp
[Fact]
public void InstallerService_InstallPathUsesOperationDecisionPolicy()
{
    var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
    var installAsync = ExtractMethod(source, "public async Task<InstallTask> InstallAsync");

    Assert.Contains("OperationDecisionPolicy.DecideInstall", installAsync);
    Assert.Contains("RemoteCommandResult?", installAsync);
    Assert.Contains("scriptResult", installAsync);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusCoverageTests.InstallerService_InstallPathUsesOperationDecisionPolicy" --no-restore
```

Expected: assertion failure because install path still uses ad hoc exception storage.

- [ ] **Step 3: Change ExecuteInstallScriptAsync to return RemoteCommandResult**

Modify `RemoteInstaller/Services/InstallerService.cs`:

- Add `using RemoteInstaller.Services.Operations;`
- Change `ExecuteInstallScriptAsync` return type from `Task` to `Task<RemoteCommandResult>`.
- Replace `_sshService.ExecuteCommandAsync(... throwOnError: true)` calls inside that method with `_sshService.ExecuteCommandResultAsync(...)`.
- Return the command result.
- Do not throw for non-zero script exit from `ExecuteInstallScriptAsync`.

The Linux command execution block should end in this shape:

```csharp
var result = await _sshService.ExecuteCommandResultAsync(
    $"bash -c '{command.Replace("'", "'\"'\"'")}'",
    output => logCollector.ProcessOutput(output),
    cancellationToken);

_logger.Success("脚本执行完成");
return result;
```

The Windows branch should mirror the same pattern:

```csharp
var result = await _sshService.ExecuteCommandResultAsync(
    command,
    output => logCollector.ProcessOutput(output),
    cancellationToken);

_logger.Success("脚本执行完成");
return result;
```

- [ ] **Step 4: Use OperationDecisionPolicy in InstallAsync**

Modify the install section:

```csharp
RemoteCommandResult? scriptResult = null;
```

After script execution:

```csharp
scriptResult = await ExecuteInstallScriptAsync(host, app, parameters, logCollector, remoteScriptPath, cancellationToken);
if (scriptResult.Failed)
{
    AddTaskLog(LogLevel.Warning, $"安装脚本退出异常，继续通过状态检测确认真实安装结果：{scriptResult.ExitCode}");
    _logger.Warning($"安装脚本退出异常，将继续验证真实安装状态：{scriptResult.ExitCode}");
}
```

After final `CheckStatusAsync`, replace manual final decision with:

```csharp
var decision = OperationDecisionPolicy.DecideInstall(scriptResult, status ?? new ApplicationStatus());
if (decision.HasWarning)
{
    AddTaskLog(LogLevel.Warning, decision.Message);
    _logger.Warning(decision.Message);
}

if (decision.Outcome == OperationOutcome.Completed)
{
    task.Complete();
    progressReporter?.Report(task);
    _logger.Success($"安装完成！{app.Name} {(status?.IsRunning == true ? "已成功启动" : "已安装但未运行")}");
}
else
{
    task.Fail(decision.Message);
    progressReporter?.Report(task);
    _logger.Error(decision.Message);
}
```

- [ ] **Step 5: Run install path tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusCoverageTests|OperationDecisionPolicyTests" --no-restore
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs
git commit -m "refactor: use structured install decision"
```

## Task 8: Integrate Structured Script Result in Uninstall Path

**Files:**
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Test: `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs`

- [ ] **Step 1: Add uninstall decision regression test**

Modify `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs` and add:

```csharp
[Fact]
public void InstallerService_UninstallPathUsesOperationDecisionPolicy()
{
    var source = ReadProjectFile("RemoteInstaller", "Services", "InstallerService.cs");
    var uninstallAsync = ExtractMethod(source, "public async Task<InstallTask> UninstallAsync");

    Assert.Contains("OperationDecisionPolicy.DecideUninstall", uninstallAsync);
    Assert.Contains("RemoteCommandResult?", uninstallAsync);
    Assert.Contains("ApplicationStatusNormalizer.BuildEvidence", uninstallAsync);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusCoverageTests.InstallerService_UninstallPathUsesOperationDecisionPolicy" --no-restore
```

Expected: assertion failure because uninstall path still has ad hoc result logic.

- [ ] **Step 3: Capture uninstall script result**

Modify `UninstallAsync` in `RemoteInstaller/Services/InstallerService.cs`:

- Add `RemoteCommandResult? scriptResult = null;`.
- Replace uninstall script execution calls with `ExecuteCommandResultAsync`.
- Preserve log output through `logCollector.ProcessOutput`.

Linux script execution should set:

```csharp
scriptResult = await _sshService.ExecuteCommandResultAsync(
    ScriptResolver.BuildLinuxShellScriptCommand(scriptContent),
    output => logCollector.ProcessOutput(output),
    cancellationToken);
```

Windows script execution should set:

```csharp
scriptResult = await _sshService.ExecuteCommandResultAsync(
    scriptContent,
    output => logCollector.ProcessOutput(output),
    cancellationToken);
```

- [ ] **Step 4: Build evidence from final status output**

After final `CheckStatusAsync`, use the status and parsed logs:

```csharp
var protocolEvents = ScriptProtocolParser.Parse(string.Join(Environment.NewLine, logCollector.GetLogs().Select(log => log.Message))).ToList();
var evidence = ApplicationStatusNormalizer.BuildEvidence(protocolEvents);
if (finalStatus.IsRunning)
{
    evidence = new ApplicationStatusEvidence
    {
        BinaryFound = evidence.BinaryFound,
        PackageFound = evidence.PackageFound,
        ServiceFound = evidence.ServiceFound,
        ServiceActive = true,
        ProcessFound = evidence.ProcessFound,
        PortListening = evidence.PortListening,
        ConfigOnlyResidue = evidence.ConfigOnlyResidue,
        ServiceOnlyResidue = evidence.ServiceOnlyResidue
    };
}
```

Then decide:

```csharp
var decision = OperationDecisionPolicy.DecideUninstall(scriptResult, finalStatus, evidence);
if (decision.Outcome == OperationOutcome.Completed)
{
    task.Complete();
}
else
{
    task.Fail(decision.Message);
}
```

- [ ] **Step 5: Run uninstall-related tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "ApplicationStatusCoverageTests|LinuxServiceResidueScriptTests|ElasticsearchStatusTests" --no-restore
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add RemoteInstaller/Services/InstallerService.cs RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs
git commit -m "refactor: use structured uninstall decision"
```

## Task 9: Package Resolver Extraction for RabbitMQ First

**Files:**
- Create: `RemoteInstaller/Services/Operations/IPackageResolver.cs`
- Create: `RemoteInstaller/Services/Operations/DefaultPackageResolver.cs`
- Modify: `RemoteInstaller/ViewModels/InstallConfigViewModel.cs`
- Test: `RemoteInstaller.Tests/PackageResolverTests.cs`
- Test: existing `RemoteInstaller.Tests/RabbitMqOfflineSelectionTests.cs`

- [ ] **Step 1: Write failing PackageResolver tests**

Create `RemoteInstaller.Tests/PackageResolverTests.cs`:

```csharp
using RemoteInstaller.Models;
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class PackageResolverTests
{
    [Fact]
    public void ResolveRabbitMqUbuntu_WithRequiredPackages_ReturnsDirectory()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerPackageResolver_{Guid.NewGuid():N}");
        try
        {
            var root = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");
            Directory.CreateDirectory(Path.Combine(root, "deps"));
            File.WriteAllText(Path.Combine(root, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "deps", "logrotate_3.19.0-1ubuntu1.1_amd64.deb"), string.Empty);

            var resolver = new DefaultPackageResolver(() => new[] { tempScriptsRoot });
            var app = new ApplicationInfo { Id = "rabbitmq", Name = "RabbitMQ", Version = "3.12.0" };
            var host = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

            var result = resolver.Resolve(app, host);

            Assert.True(result.Found);
            Assert.Equal(root, result.Path);
        }
        finally
        {
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }

    [Fact]
    public void ResolveRabbitMqUbuntu_WithoutLogrotate_ReturnsMissingDependency()
    {
        var tempScriptsRoot = Path.Combine(Path.GetTempPath(), $"RemoteInstallerPackageResolver_{Guid.NewGuid():N}");
        try
        {
            var root = Path.Combine(tempScriptsRoot, "RabbitMQ", "rabbitmq-ubuntu");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "rabbitmq-server_3.12.0-1_all.deb"), string.Empty);
            File.WriteAllText(Path.Combine(root, "erlang-base_26.2.5.13-1_amd64.deb"), string.Empty);

            var resolver = new DefaultPackageResolver(() => new[] { tempScriptsRoot });
            var app = new ApplicationInfo { Id = "rabbitmq", Name = "RabbitMQ", Version = "3.12.0" };
            var host = new RemoteHost { OsType = OperatingSystemType.Ubuntu };

            var result = resolver.Resolve(app, host);

            Assert.False(result.Found);
            Assert.Contains("logrotate", result.MissingDependencies);
        }
        finally
        {
            if (Directory.Exists(tempScriptsRoot))
            {
                Directory.Delete(tempScriptsRoot, true);
            }
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter PackageResolverTests --no-restore
```

Expected: compile failure because package resolver types do not exist.

- [ ] **Step 3: Add package resolver interface and RabbitMQ implementation**

Create `RemoteInstaller/Services/Operations/IPackageResolver.cs`:

```csharp
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public interface IPackageResolver
{
    PackageResolution Resolve(ApplicationInfo application, RemoteHost host);
}
```

Create `RemoteInstaller/Services/Operations/DefaultPackageResolver.cs`:

```csharp
using RemoteInstaller.Models;

namespace RemoteInstaller.Services.Operations;

public sealed class DefaultPackageResolver : IPackageResolver
{
    private readonly Func<IEnumerable<string>> _scriptRootsFactory;

    public DefaultPackageResolver(Func<IEnumerable<string>>? scriptRootsFactory = null)
    {
        _scriptRootsFactory = scriptRootsFactory ?? DefaultScriptRoots;
    }

    public PackageResolution Resolve(ApplicationInfo application, RemoteHost host)
    {
        if (application.Id.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase) ||
            application.Name.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveRabbitMq(application, host);
        }

        return PackageResolution.NotFound($"当前应用未配置自动本地资源解析：{application.Name}");
    }

    private PackageResolution ResolveRabbitMq(ApplicationInfo application, RemoteHost host)
    {
        var offlineFolder = host.OsType switch
        {
            OperatingSystemType.CentOS => "rabbitmq-centos7",
            OperatingSystemType.Ubuntu => "rabbitmq-ubuntu",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(offlineFolder))
        {
            return PackageResolution.NotFound("当前系统未配置 RabbitMQ 本地资源目录。");
        }

        var packagePattern = host.OsType == OperatingSystemType.CentOS
            ? "rabbitmq-server-*.el7*.rpm"
            : "rabbitmq-server*.deb";

        foreach (var root in _scriptRootsFactory().Select(baseRoot => Path.Combine(baseRoot, "RabbitMQ", offlineFolder)))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var selectedPath = Directory.GetFiles(root, packagePattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(selectedPath))
            {
                continue;
            }

            if (host.OsType == OperatingSystemType.Ubuntu)
            {
                var dependencyFileNames = new[] { Path.Combine(root, "deps"), root }
                    .Where(Directory.Exists)
                    .SelectMany(directory => Directory.GetFiles(directory, "*.deb", SearchOption.TopDirectoryOnly))
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                var requiredDebPrefixes = new[] { "erlang-base", "logrotate" };
                var missingDependencies = requiredDebPrefixes
                    .Where(prefix => !dependencyFileNames.Any(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (missingDependencies.Length > 0)
                {
                    return PackageResolution.NotFound(
                        $"RabbitMQ Ubuntu 离线资源目录缺少依赖：{string.Join(", ", missingDependencies)}。请补齐后再点击刷新检测。",
                        missingDependencies);
                }
            }

            return PackageResolution.FoundPackage(
                root,
                application.Version,
                $"已从 Scripts 目录自动匹配 RabbitMQ 本地资源目录：{root}");
        }

        return PackageResolution.NotFound($"未在 Scripts/RabbitMQ/{offlineFolder} 中找到可用本地资源。");
    }

    private static IEnumerable<string> DefaultScriptRoots()
    {
        return new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "RemoteInstaller", "Scripts"),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RemoteInstaller", "Scripts")
        }.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Make InstallConfigViewModel use resolver for RabbitMQ**

Modify `TryResolveRabbitMqLocalPackage` in `RemoteInstaller/ViewModels/InstallConfigViewModel.cs`:

```csharp
private bool TryResolveRabbitMqLocalPackage(out string packagePath, out string hint)
{
    var resolver = ScriptRootOverridesFactory is null
        ? new DefaultPackageResolver()
        : new DefaultPackageResolver(ScriptRootOverridesFactory);

    var resolution = resolver.Resolve(_application, _host);
    packagePath = resolution.Path;
    hint = resolution.Hint;
    return resolution.Found;
}
```

Add `using RemoteInstaller.Services.Operations;`.

- [ ] **Step 5: Run package and RabbitMQ tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "PackageResolverTests|RabbitMqOfflineSelectionTests" --no-restore
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add RemoteInstaller/Services/Operations/IPackageResolver.cs RemoteInstaller/Services/Operations/DefaultPackageResolver.cs RemoteInstaller/ViewModels/InstallConfigViewModel.cs RemoteInstaller.Tests/PackageResolverTests.cs
git commit -m "refactor: extract RabbitMQ package resolver"
```

## Task 10: App Handler Registry Foundation

**Files:**
- Create: `RemoteInstaller/Services/Operations/IAppHandler.cs`
- Create: `RemoteInstaller/Services/Operations/DefaultAppHandler.cs`
- Create: `RemoteInstaller/Services/Operations/AppHandlerRegistry.cs`
- Test: `RemoteInstaller.Tests/AppHandlerRegistryTests.cs`

- [ ] **Step 1: Write failing handler registry tests**

Create `RemoteInstaller.Tests/AppHandlerRegistryTests.cs`:

```csharp
using RemoteInstaller.Services.Operations;
using Xunit;

namespace RemoteInstaller.Tests;

public class AppHandlerRegistryTests
{
    [Fact]
    public void Resolve_ReturnsDefaultHandlerWhenAppSpecificHandlerIsMissing()
    {
        var registry = new AppHandlerRegistry(Array.Empty<IAppHandler>());

        var handler = registry.Resolve("redis");

        Assert.IsType<DefaultAppHandler>(handler);
        Assert.Equal("default", handler.AppId);
    }

    [Fact]
    public void Resolve_ReturnsRegisteredHandlerIgnoringCase()
    {
        var registry = new AppHandlerRegistry(new IAppHandler[] { new TestHandler("Mosquitto") });

        var handler = registry.Resolve("mosquitto");

        Assert.Equal("Mosquitto", handler.AppId);
    }

    private sealed class TestHandler : DefaultAppHandler
    {
        public TestHandler(string appId)
        {
            AppId = appId;
        }

        public override string AppId { get; }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter AppHandlerRegistryTests --no-restore
```

Expected: compile failure because handler types do not exist.

- [ ] **Step 3: Add handler foundation**

Create `RemoteInstaller/Services/Operations/IAppHandler.cs`:

```csharp
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
```

Create `RemoteInstaller/Services/Operations/DefaultAppHandler.cs`:

```csharp
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
```

Create `RemoteInstaller/Services/Operations/AppHandlerRegistry.cs`:

```csharp
namespace RemoteInstaller.Services.Operations;

public sealed class AppHandlerRegistry
{
    private readonly Dictionary<string, IAppHandler> _handlers;
    private readonly DefaultAppHandler _defaultHandler = new();

    public AppHandlerRegistry(IEnumerable<IAppHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.AppId, StringComparer.OrdinalIgnoreCase);
    }

    public IAppHandler Resolve(string appId)
    {
        return _handlers.TryGetValue(appId, out var handler)
            ? handler
            : _defaultHandler;
    }
}
```

- [ ] **Step 4: Run handler tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter AppHandlerRegistryTests --no-restore
```

Expected: `AppHandlerRegistryTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add RemoteInstaller/Services/Operations/IAppHandler.cs RemoteInstaller/Services/Operations/DefaultAppHandler.cs RemoteInstaller/Services/Operations/AppHandlerRegistry.cs RemoteInstaller.Tests/AppHandlerRegistryTests.cs
git commit -m "feat: add app handler registry"
```

## Task 11: Manifest Validation Guardrails

**Files:**
- Modify: `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs`
- Test: existing `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs`

- [ ] **Step 1: Extend manifest validation tests**

Modify `RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs` and add:

```csharp
[Fact]
public void AppConfiguration_DeclaredLinuxScriptsResolveToBundledFiles()
{
    var projectRoot = GetProjectRoot();
    using var document = JsonDocument.Parse(ReadProjectFile("Scripts", "app-configuration.json"));
    var missing = new List<string>();

    foreach (var app in document.RootElement.GetProperty("applications").EnumerateArray())
    {
        var id = app.GetProperty("id").GetString() ?? string.Empty;
        var scripts = app.GetProperty("scripts");
        foreach (var operationName in new[] { "install", "uninstall", "detect" })
        {
            var linuxScript = scripts.GetProperty(operationName).GetProperty("linux").GetString() ?? string.Empty;
            var token = RemoteInstaller.Services.Operations.ScriptResolver.ExtractScriptReferenceToken(linuxScript, ".sh");
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var expectedPath = Path.Combine(projectRoot, "RemoteInstaller", token.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(expectedPath))
            {
                missing.Add($"{id}:{operationName}:{token}");
            }
        }
    }

    Assert.True(missing.Count == 0, "配置声明的 Linux 脚本必须存在：" + Environment.NewLine + string.Join(Environment.NewLine, missing));
}
```

- [ ] **Step 2: Run manifest tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ApplicationStatusCoverageTests --no-restore
```

Expected: test may fail if config references legacy inline commands or scripts outside `RemoteInstaller/Scripts`.

- [ ] **Step 3: Fix manifest references only when the test reveals real drift**

If the test reports a missing script that exists under `RemoteInstaller/Scripts/<App>/`, update `Scripts/app-configuration.json` to reference that bundled script path.

For inline commands that intentionally do not reference a file, leave them unchanged because `ExtractScriptReferenceToken` returns empty.

- [ ] **Step 4: Re-run manifest tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter ApplicationStatusCoverageTests --no-restore
```

Expected: `ApplicationStatusCoverageTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add RemoteInstaller.Tests/ApplicationStatusCoverageTests.cs Scripts/app-configuration.json
git commit -m "test: validate configured script references"
```

## Task 12: First Cleanup Pass and Full Verification

**Files:**
- Modify: `RemoteInstaller/Services/InstallerService.cs`
- Modify: `RemoteInstaller/ViewModels/InstallConfigViewModel.cs`
- Modify: `RemoteInstaller/ViewModels/MainViewModel.cs`
- Test: all tests

- [ ] **Step 1: Remove dead private methods after extraction**

Inspect:

```powershell
rg -n "BuildLinuxShellScriptCommand|TryResolveConfiguredScriptFilePath|ExtractScriptReferenceToken|BuildConfiguredScriptCandidatePaths|NormalizeApplicationStatus" RemoteInstaller/Services/InstallerService.cs
```

Expected: no duplicate private script resolver methods remain in `InstallerService`. If duplicate methods remain and all call sites use operation services, delete the duplicate private methods.

- [ ] **Step 2: Check InstallerService size trend**

Run:

```powershell
(Get-Content RemoteInstaller/Services/InstallerService.cs).Count
```

Expected: line count is lower than the baseline of `2630` lines from the design investigation. If line count is not lower, identify remaining moved code and delete only code proven unused by compiler and `rg`.

- [ ] **Step 3: Run targeted operation tests**

Run:

```powershell
dotnet test RemoteInstaller.Tests\RemoteInstaller.Tests.csproj --filter "OperationModelsTests|ScriptProtocolParserTests|OperationDecisionPolicyTests|RemoteCommandRunnerContractTests|ScriptResolverTests|ApplicationStatusNormalizerTests|PackageResolverTests|AppHandlerRegistryTests|ApplicationStatusCoverageTests" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 4: Run full build**

Run:

```powershell
dotnet build RemoteInstaller.sln --no-restore
```

Expected: build succeeds with `0 个错误`.

- [ ] **Step 5: Run full test suite**

Run:

```powershell
dotnet test RemoteInstaller.sln --no-restore
```

Expected: all tests pass. The current suite baseline after the latest fixes is `180/180` passing.

- [ ] **Step 6: Commit cleanup**

```powershell
git add RemoteInstaller/Services/InstallerService.cs RemoteInstaller/ViewModels/InstallConfigViewModel.cs RemoteInstaller/ViewModels/MainViewModel.cs
git commit -m "refactor: clean operation service integration"
```

## Self-Review

Spec coverage:

- Structured command results are covered by Tasks 1 and 4.
- Script protocol parsing is covered by Task 2.
- Install decision arbitration is covered by Tasks 3 and 7.
- Uninstall decision arbitration is covered by Tasks 3 and 8.
- Status evidence and normalization are covered by Task 6.
- Package resolution movement begins with RabbitMQ in Task 9 and establishes the pattern for Redis, MariaDB, Nginx, Elasticsearch, Mosquitto, Consul, Traefik, and JDK in later follow-up plans.
- App handler extension points are covered by Task 10.
- Manifest drift guardrails are covered by Task 11.
- Full verification is covered by Task 12.

Scope note:

This plan intentionally implements the platform foundation and migrates one package resolver path first. It does not migrate every application handler in one pass, because that would bundle too much behavior into a single risky change. After Task 12, create follow-up plans for `Mosquitto/JDK handlers`, then `database package resolvers`, then `web/middleware package resolvers`.
