$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$PackagePath = "",
    [string]$InstallPath = "C:\Program Files\mosquitto",
    [int]$MqttPort = 1883,
    [string]$Username = "",
    [string]$PasswordFile = ""
)

$ServiceName = "mosquitto"
$ConfigFile = Join-Path $InstallPath "mosquitto.conf"
$RemoteConfigFile = Join-Path $InstallPath "remote-installer.conf"
$PasswordOutputFile = Join-Path $InstallPath "passwd"

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

function Resolve-MosquittoRoot {
    param([string]$SourcePath)

    if (Test-Path (Join-Path $SourcePath "mosquitto.exe")) {
        return $SourcePath
    }

    $exe = Get-ChildItem -Path $SourcePath -Recurse -Filter mosquitto.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($exe) {
        return $exe.Directory.FullName
    }

    return $null
}

function Copy-MosquittoFiles {
    param([string]$SourceRoot, [string]$TargetRoot)

    if (Test-Path $TargetRoot) {
        Remove-Item -Path $TargetRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
    Get-ChildItem -Path $SourceRoot -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $TargetRoot $_.Name) -Recurse -Force
    }
}

function Test-PortListening {
    param([int]$Port)
    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Wait-PortListening {
    param([int]$Port, [int]$TimeoutSeconds = 20)

    for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
        if (Test-PortListening -Port $Port) {
            return $true
        }
        Start-Sleep -Seconds 1
    }

    return $false
}

function Read-PasswordFromFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if (-not (Test-Path $Path)) {
        Fail "密码临时文件不存在：$Path"
    }

    try {
        return (Get-Content -Path $Path -TotalCount 1 -ErrorAction Stop).TrimEnd("`r", "`n")
    }
    finally {
        Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
    }
}

function Get-AuthMode {
    param([string]$User, [string]$Pass)

    $hasUser = -not [string]::IsNullOrWhiteSpace($User)
    $hasPass = -not [string]::IsNullOrWhiteSpace($Pass)

    if ($hasUser -and $hasPass) {
        return "password"
    }

    if (-not $hasUser -and -not $hasPass) {
        return "anonymous"
    }

    Fail "Mosquitto 用户名和密码必须同时提供，或同时留空以启用匿名访问"
}

function Test-MosquittoRuntimeFiles {
    param([string]$Root)

    $requiredFiles = @(
        (Join-Path $Root "mosquitto.exe"),
        (Join-Path $Root "mosquitto_passwd.exe")
    )

    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path $_) })
    if ($missingFiles.Count -gt 0) {
        Fail ("Mosquitto Windows 离线资源缺少文件：" + (($missingFiles | ForEach-Object { Split-Path $_ -Leaf }) -join ", "))
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
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Seconds 1
            if (-not (Get-Service -Name $Name -ErrorAction SilentlyContinue)) {
                break
            }
        }
    }
}

function Configure-Mosquitto {
    param([string]$Mode, [string]$User, [string]$Pass)

    if ($Mode -eq 'password') {
        & (Join-Path $InstallPath 'mosquitto_passwd.exe') -b -c $PasswordOutputFile $User $Pass | Out-Null
        @"
listener $MqttPort
allow_anonymous false
password_file $PasswordOutputFile
"@ | Set-Content -Path $RemoteConfigFile -Encoding UTF8
    }
    else {
        Remove-Item -Path $PasswordOutputFile -Force -ErrorAction SilentlyContinue
        @"
listener $MqttPort
allow_anonymous true
"@ | Set-Content -Path $RemoteConfigFile -Encoding UTF8
    }

    if (-not (Test-Path $ConfigFile)) {
        @"
per_listener_settings false
"@ | Set-Content -Path $ConfigFile -Encoding UTF8
    }
}

Write-ProgressInfo "Initializing" 5
Write-Host "Mosquitto Windows 离线安装开始..."

if (-not (Test-Admin)) {
    Fail "请以管理员身份运行此脚本"
}

if ([string]::IsNullOrWhiteSpace($PackagePath) -or -not (Test-Path $PackagePath)) {
    Fail "Mosquitto 仅支持离线安装，必须显式提供有效的 PackagePath"
}

$resolvedInstallPath = [System.IO.Path]::GetFullPath($InstallPath)
$resolvedDefaultPath = [System.IO.Path]::GetFullPath("C:\Program Files\mosquitto")
if ($resolvedInstallPath -ne $resolvedDefaultPath) {
    Fail "安装脚本仅允许使用默认 Mosquitto 安装目录：C:\Program Files\mosquitto"
}

$Password = Read-PasswordFromFile -Path $PasswordFile
$authMode = Get-AuthMode -User $Username -Pass $Password
$tempExtractRoot = Join-Path $env:TEMP ("mosquitto-install-{0}" -f ([guid]::NewGuid().ToString('N')))
$sourceRoot = $null

try {
    Write-ProgressInfo "PreparingPackage" 15
    if (Test-Path $PackagePath -PathType Container) {
        $sourceRoot = Resolve-MosquittoRoot -SourcePath $PackagePath
    }
    else {
        if (-not $PackagePath.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
            Fail "Mosquitto Windows 离线资源仅支持 zip 或已解压目录。"
        }

        New-Item -ItemType Directory -Path $tempExtractRoot -Force | Out-Null
        Expand-Archive -Path $PackagePath -DestinationPath $tempExtractRoot -Force
        $sourceRoot = Resolve-MosquittoRoot -SourcePath $tempExtractRoot
    }

    if ([string]::IsNullOrWhiteSpace($sourceRoot)) {
        Fail "未能从离线资源中定位 mosquitto.exe"
    }

    Test-MosquittoRuntimeFiles -Root $sourceRoot

    Write-ProgressInfo "InstallingFiles" 35
    Copy-MosquittoFiles -SourceRoot $sourceRoot -TargetRoot $InstallPath
    Test-MosquittoRuntimeFiles -Root $InstallPath

    Write-ProgressInfo "Configuring" 55
    Configure-Mosquitto -Mode $authMode -User $Username -Pass $Password

    Remove-ServiceIfExists -Name $ServiceName

    Write-ProgressInfo "ConfiguringFirewall" 70
    Remove-NetFirewallRule -DisplayName "Mosquitto MQTT" -ErrorAction SilentlyContinue | Out-Null
    New-NetFirewallRule -DisplayName "Mosquitto MQTT" -Direction Inbound -Protocol TCP -LocalPort $MqttPort -Action Allow -Profile Any | Out-Null

    Write-ProgressInfo "StartingService" 82
    $mosquittoExe = Join-Path $InstallPath "mosquitto.exe"
    New-Service -Name $ServiceName -BinaryPathName "`"$mosquittoExe`" -c `"$ConfigFile`"" -DisplayName "Mosquitto" -StartupType Automatic | Out-Null
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service -or $service.Status -ne 'Running') {
        Fail "Mosquitto 服务未成功启动"
    }

    if (-not (Get-Process -Name mosquitto -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        Fail "未检测到 mosquitto.exe 进程"
    }

    if (-not (Wait-PortListening -Port $MqttPort -TimeoutSeconds 15)) {
        Fail "MQTT 端口未监听：$MqttPort"
    }

    $version = "未知"
    $output = & $mosquittoExe -h 2>&1
    $match = [regex]::Match(($output | Out-String), '\d+\.\d+\.\d+')
    if ($match.Success) {
        $version = $match.Value
    }

    Write-ProgressInfo "Complete" 100
    Write-Host "INSTALLED:true"
    Write-Host "VERSION:$version"
    Write-Host "RUNNING:true"
    Write-Host "PORT:$MqttPort"
}
finally {
    Remove-Item -Path $tempExtractRoot -Recurse -Force -ErrorAction SilentlyContinue
}
