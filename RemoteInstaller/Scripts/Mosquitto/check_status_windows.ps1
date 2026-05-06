$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$InstallPath = "C:\Program Files\mosquitto"
)

$ServiceName = "mosquitto"
$MainConfigFile = Join-Path $InstallPath "mosquitto.conf"
$RemoteConfigFile = Join-Path $InstallPath "remote-installer.conf"
$MqttPort = 1883
$isInstalled = $false
$isRunning = $false
$version = "未知"

function Test-PortListening {
    param([int]$Port)
    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)
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

function Get-VersionString {
    param([string]$MosquittoExe)

    $output = & $MosquittoExe -h 2>&1
    $match = [regex]::Match(($output | Out-String), '\d+\.\d+\.\d+')
    if ($match.Success) {
        return $match.Value
    }

    return "未知"
}

Write-Host "========================================"
Write-Host "      Mosquitto 状态检测"
Write-Host "========================================"

Load-Port

$mosquittoExe = Join-Path $InstallPath "mosquitto.exe"
if (Test-Path $mosquittoExe) {
    $isInstalled = $true
    $version = Get-VersionString -MosquittoExe $mosquittoExe
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$serviceStatus = if ($service) { $service.Status } else { 'NotFound' }
if ($service) {
    $isInstalled = $true
    if ($service.Status -eq 'Running') {
        $isRunning = $true
    }
}

$process = Get-Process -Name mosquitto -ErrorAction SilentlyContinue | Select-Object -First 1
if ($process) {
    $isRunning = $true
    $isInstalled = $true
}

$mqttListening = Test-PortListening -Port $MqttPort
if ($mqttListening) {
    $isRunning = $true
}

if ((Test-Path $MainConfigFile) -or (Test-Path $RemoteConfigFile)) {
    $isInstalled = $true
}

Write-Host "安装状态：$isInstalled"
Write-Host "版本：$version"
Write-Host "服务状态：$serviceStatus"
Write-Host "进程状态：$([bool]$process)"
Write-Host "MQTT 端口：$mqttListening ($MqttPort)"

Write-Host "INSTALLED:$($isInstalled.ToString().ToLowerInvariant())"
Write-Host "VERSION:$version"
Write-Host "RUNNING:$($isRunning.ToString().ToLowerInvariant())"
Write-Host "PORT:$MqttPort"
