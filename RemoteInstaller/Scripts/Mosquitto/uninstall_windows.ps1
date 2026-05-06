$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$InstallPath = "C:\Program Files\mosquitto"
)

$ServiceName = "mosquitto"
$DefaultInstallPath = "C:\Program Files\mosquitto"
$MainConfigFile = Join-Path $InstallPath "mosquitto.conf"
$RemoteConfigFile = Join-Path $InstallPath "remote-installer.conf"
$PasswordFile = Join-Path $InstallPath "passwd"
$MqttPort = 1883

function Write-ProgressInfo {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage`:$Percent"
}

function Fail {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Test-Admin {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Load-Port {
    foreach ($configFile in @($RemoteConfigFile, $MainConfigFile)) {
        if (Test-Path $configFile) {
            $match = Select-String -Path $configFile -Pattern '^\s*listener\s+([0-9]+)' -AllMatches | Select-Object -First 1
            if ($match -and $match.Matches.Count -gt 0) {
                $script:MqttPort = [int]$match.Matches[0].Groups[1].Value
                break
            }
        }
    }
}

function Remove-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne 'Stopped') {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }

        sc.exe delete $Name | Out-Null
        Start-Sleep -Seconds 2
    }
}

Write-ProgressInfo "Initializing" 5
Write-Host "Mosquitto Windows 卸载开始..."

if (-not (Test-Admin)) {
    Fail "请以管理员身份运行此脚本"
}

Load-Port

if ([string]::IsNullOrWhiteSpace($InstallPath)) {
    $InstallPath = $DefaultInstallPath
}

$resolvedInstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$resolvedDefaultPath = [System.IO.Path]::GetFullPath($DefaultInstallPath)
if ($resolvedInstallPath -ne $resolvedDefaultPath) {
    Fail "卸载脚本仅允许删除默认 Mosquitto 安装目录：$DefaultInstallPath"
}

Write-ProgressInfo "StoppingService" 20
Remove-ServiceIfExists -Name $ServiceName
Get-Process -Name mosquitto -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-ProgressInfo "CleaningFirewall" 55
Remove-NetFirewallRule -DisplayName "Mosquitto MQTT" -ErrorAction SilentlyContinue | Out-Null

Write-ProgressInfo "CleaningFiles" 80
Remove-Item -Path $RemoteConfigFile, $PasswordFile -Force -ErrorAction SilentlyContinue
if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-ProgressInfo "Complete" 100
Write-Host "INSTALLED:false"
Write-Host "VERSION:未知"
Write-Host "RUNNING:false"
Write-Host "PORT:$MqttPort"
