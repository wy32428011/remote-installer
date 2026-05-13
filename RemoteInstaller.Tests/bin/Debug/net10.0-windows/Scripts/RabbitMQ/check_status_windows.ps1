# =================================================================
# RabbitMQ 状态检测脚本 (Windows)
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Write-Progress {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:${Stage}:$Percent"
}

function Get-ServiceImagePath {
    param([string]$ServiceName)

    try {
        $item = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -ErrorAction Stop
        return [string]$item.ImagePath
    } catch {
        return ""
    }
}

function Resolve-ExecutablePathFromImagePath {
    param([string]$ImagePath)

    if ([string]::IsNullOrWhiteSpace($ImagePath)) {
        return ""
    }

    $trimmed = $ImagePath.Trim()
    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -gt 1) {
            return $trimmed.Substring(1, $end - 1)
        }
    }

    return ($trimmed -replace '\s+.*$', '')
}

function Test-RabbitMqCommandLine {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return $false
    }

    return $CommandLine -match 'rabbitmq|rabbit@|rabbit_prelaunch|rabbitmq_server|rabbit_boot'
}

function Get-RabbitMqProcesses {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.Name -match '^(erl|erl\.exe|beam\.smp|beam\.smp\.exe)$') -and
            (Test-RabbitMqCommandLine $_.CommandLine)
        }
}

function Test-RabbitMqPortOwner {
    param([int]$Port, [int[]]$RabbitMqProcessIds)

    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    foreach ($listener in $listeners) {
        if ($RabbitMqProcessIds -contains [int]$listener.OwningProcess) {
            return $true
        }

        $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction SilentlyContinue
        if ($owner -and (Test-RabbitMqCommandLine $owner.CommandLine)) {
            return $true
        }
    }

    return $false
}

function Test-AnyPortListening {
    param([int]$Port)

    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)
}

Write-Progress "Initializing" 5
Write-Color "========================================" "Cyan"
Write-Color "      RabbitMQ 状态检测" "Cyan"
Write-Color "========================================" "Cyan"

$isInstalled = $false
$isRunning = $false
$version = "未知"
$binaryFound = $false
$serviceFound = $false
$serviceActive = $false
$processFound = $false
$portListening = $false
$serviceOnlyStale = $false
$configOnlyResidue = $false
$serviceName = "unknown"
$serviceStatus = "not-found"
$AMQP_PORT = 5672
$MANAGEMENT_PORT = 15672

Write-Progress "CheckingInstallation" 10
Write-Color "`n1. 检查安装证据:" "Yellow"

$serverCommands = @(
    (Get-Command rabbitmq-server -ErrorAction SilentlyContinue),
    (Get-Command rabbitmq-server.bat -ErrorAction SilentlyContinue)
) | Where-Object { $_ }

if ($serverCommands) {
    $binaryFound = $true
    Write-Color "RabbitMQ 服务端命令存在：是" "Green"
}

$serverBinaries = @()
$serverBinaries += Get-ChildItem -Path "C:\Program Files\RabbitMQ Server" -Filter "rabbitmq-server.bat" -Recurse -ErrorAction SilentlyContinue
$serverBinaries += Get-ChildItem -Path "C:\Program Files (x86)\RabbitMQ Server" -Filter "rabbitmq-server.bat" -Recurse -ErrorAction SilentlyContinue
$serverBinaries += Get-ChildItem -Path "C:\RabbitMQ" -Filter "rabbitmq-server.bat" -Recurse -ErrorAction SilentlyContinue

if ($serverBinaries) {
    $binaryFound = $true
    Write-Color "RabbitMQ 服务端文件存在：是" "Green"
}

$rabbitmqServices = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
if ($rabbitmqServices) {
    $serviceFound = $true
    $service = $rabbitmqServices | Select-Object -First 1
    $serviceName = $service.Name
    $serviceStatus = [string]$service.Status
    Write-Color "RabbitMQ 服务存在：$serviceName ($serviceStatus)" "Green"

    foreach ($svc in $rabbitmqServices) {
        $imagePath = Get-ServiceImagePath -ServiceName $svc.Name
        $exePath = Resolve-ExecutablePathFromImagePath -ImagePath $imagePath
        if ($exePath -and (Test-Path $exePath) -and ($imagePath -match 'rabbitmq')) {
            $binaryFound = $true
        }

        if ($svc.Status -eq "Running") {
            $serviceActive = $true
        }
    }
} else {
    Write-Color "RabbitMQ 服务存在：否" "Yellow"
}

$rabbitmqProcesses = @(Get-RabbitMqProcesses)
if ($rabbitmqProcesses.Count -gt 0) {
    $processFound = $true
    Write-Color "RabbitMQ Erlang 进程：发现" "Green"
    $rabbitmqProcesses | Select-Object ProcessId, Name, CommandLine | Format-Table -AutoSize
} else {
    Write-Color "RabbitMQ Erlang 进程：未发现" "Red"
}

$rabbitmqProcessIds = @($rabbitmqProcesses | ForEach-Object { [int]$_.ProcessId })

Write-Progress "CheckingPorts" 50
Write-Color "`n2. 检查端口监听:" "Yellow"
$amqpOpen = Test-AnyPortListening -Port $AMQP_PORT
$managementOpen = Test-AnyPortListening -Port $MANAGEMENT_PORT
$amqpOwnedByRabbit = Test-RabbitMqPortOwner -Port $AMQP_PORT -RabbitMqProcessIds $rabbitmqProcessIds
$managementOwnedByRabbit = Test-RabbitMqPortOwner -Port $MANAGEMENT_PORT -RabbitMqProcessIds $rabbitmqProcessIds

if ($amqpOpen) {
    Write-Color "AMQP 端口 ($AMQP_PORT)：监听中" "Yellow"
} else {
    Write-Color "AMQP 端口 ($AMQP_PORT)：未监听" "Red"
}

if ($managementOpen) {
    Write-Color "Management 端口 ($MANAGEMENT_PORT)：监听中" "Yellow"
} else {
    Write-Color "Management 端口 ($MANAGEMENT_PORT)：未监听" "Red"
}

if ($amqpOwnedByRabbit -or $managementOwnedByRabbit) {
    $portListening = $true
    Write-Color "RabbitMQ 端口归属：端口由 RabbitMQ 进程监听" "Green"
} elseif ($amqpOpen -or $managementOpen) {
    Write-Color "RabbitMQ 端口归属：端口被其他进程占用，不作为 RabbitMQ 运行证据" "Yellow"
}

Write-Progress "CheckingRabbitmqStatus" 65
Write-Color "`n3. RabbitMQ 状态检查:" "Yellow"
$rabbitmqctl = Get-Command rabbitmqctl -ErrorAction SilentlyContinue
if ($rabbitmqctl) {
    try {
        $statusOutput = & rabbitmqctl status 2>&1
        if ($LASTEXITCODE -eq 0) {
            $isRunning = $true
            $binaryFound = $true
            Write-Color "rabbitmqctl status：正常" "Green"
            if ($statusOutput -match 'RabbitMQ ([0-9]+\.[0-9]+\.[0-9]+)') {
                $version = $matches[1]
            }
        } else {
            Write-Color "rabbitmqctl status：失败" "Red"
        }
    } catch {
        Write-Color "rabbitmqctl 执行失败：$_" "Red"
    }
} else {
    Write-Color "rabbitmqctl 不可用，跳过状态检查" "Yellow"
}

if ($serviceActive -or $processFound) {
    $isRunning = $true
}

if ($binaryFound -or $serviceActive -or $processFound -or $isRunning) {
    $isInstalled = $true
}

if ($isInstalled -and $version -eq "未知") {
    try {
        $versionOutput = & rabbitmqctl version 2>&1
        if ($versionOutput -match '([0-9]+\.[0-9]+\.[0-9]+)') {
            $version = $matches[1]
        }
    } catch {
        # 版本读取失败不影响状态检测。
    }
}

if ($serviceFound -and -not $isInstalled -and -not $isRunning) {
    $serviceOnlyStale = $true
    Write-Color "RabbitMQ 服务定义存在，但未发现服务端二进制或 RabbitMQ 运行进程，按残留服务处理" "Yellow"
}

$configPaths = @(
    "$env:APPDATA\RabbitMQ",
    "C:\Program Files\RabbitMQ Server"
)

foreach ($path in $configPaths) {
    if (Test-Path $path) {
        if (-not $isInstalled -and -not $isRunning) {
            $configOnlyResidue = $true
        }
        break
    }
}

Write-Progress "OutputtingStatus" 95
Write-Color "`n--- MACHINE READABLE ---" "Cyan"
Write-Host "INSTALLED:$isInstalled"
Write-Host "VERSION:$version"
Write-Host "RUNNING:$isRunning"
Write-Host "PORT:$AMQP_PORT,$MANAGEMENT_PORT"
Write-Host "BINARY_FOUND:$binaryFound"
Write-Host "PROCESS_FOUND:$processFound"
Write-Host "SERVICE_FOUND:$serviceFound"
Write-Host "SERVICE_ACTIVE:$serviceActive"
Write-Host "SERVICE_NAME:$serviceName"
Write-Host "SERVICE_STATUS:$serviceStatus"
Write-Host "PORT_LISTENING:$portListening"
Write-Host "SERVICE_ONLY_STALE:$serviceOnlyStale"
Write-Host "CONFIG_ONLY_RESIDUE:$configOnlyResidue"
Write-Host "------------------------"

Write-Color "`n最终状态:" "Yellow"
if ($isInstalled) {
    if ($isRunning) {
        Write-Color "已安装且运行中 (v$version)" "Green"
    } else {
        Write-Color "已安装但未运行" "Yellow"
    }
} else {
    Write-Color "未安装" "Red"
}

Write-Host "`nSTAGE:SUCCESS"
