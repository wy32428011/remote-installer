# =================================================================
# RabbitMQ 状态检测脚本 (Windows)
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8

# 颜色模拟
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Write-Progress {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage:$Percent"
}

function Check-Port {
    param([int]$Port, [string]$Name, [string]$ProcessPattern)
    Write-Color "3. 检查端口监听 ($Port):" "Yellow"
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        if ($ProcessPattern -and $process) {
            if ($process.Name -notmatch $ProcessPattern -and $process.MainModule.FileName -notmatch $ProcessPattern) {
                Write-Color "$Name 端口监听：否 (端口 $Port 被 $($process.Name) 占用)" "Red"
                return $false
            }
        }
        Write-Color "$Name 端口监听：是" "Green"
        $listener | Format-Table -Property LocalAddress, LocalPort, OwningProcess
        return $true
    } else {
        Write-Color "$Name 端口监听：否 (端口未开放)" "Red"
        return $false
    }
}

Write-Progress "Initializing" 5
Write-Color "========================================" "Cyan"
Write-Color "      RabbitMQ 状态检测" "Cyan"
Write-Color "========================================" "Cyan"

$isInstalled = "false"
$isRunning = "false"
$version = "未知"
$AMQP_PORT = 5672
$MANAGEMENT_PORT = 15672

# 1. 检查安装情况
Write-Progress "CheckingInstallation" 10
Write-Color "`n1. 检查安装情况:" "Yellow"

# 检查服务
$rabbitmqService = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
if ($rabbitmqService) {
    Write-Color "RabbitMQ 服务存在：是" "Green"
    $rabbitmqService | Format-Table -Property Name, Status, StartType -AutoSize
    $isInstalled = "true"
}

# 检查安装目录
$commonInstallPaths = @(
    "C:\Program Files\RabbitMQ Server",
    "C:\Program Files (x86)\RabbitMQ Server",
    "C:\RabbitMQ",
    "C:\Program Files\erl*"
)

foreach ($path in $commonInstallPaths) {
    if (Test-Path $path) {
        Write-Color "安装目录存在：$path" "Green"
        $isInstalled = "true"
        break
    }
}

# 检查命令是否存在
$erlCommand = Get-Command erl -ErrorAction SilentlyContinue
$rabbitmqctlCommand = Get-Command rabbitmqctl -ErrorAction SilentlyContinue

if ($erlCommand -or $rabbitmqctlCommand) {
    Write-Color "RabbitMQ 命令存在：是" "Green"
    if ($erlCommand) { Write-Color "  erl: $($erlCommand.Source)" "Gray" }
    if ($rabbitmqctlCommand) { Write-Color "  rabbitmqctl: $($rabbitmqctlCommand.Source)" "Gray" }
    $isInstalled = "true"
}

if (-not $isInstalled) {
    Write-Color "RabbitMQ 已安装：否" "Red"
}

# 2. 检查进程
Write-Progress "CheckingProcesses" 30
Write-Color "`n2. 检查运行进程:" "Yellow"

# RabbitMQ Windows 使用 Erlang VM，进程名通常是 erl.exe
$erlProcesses = Get-Process -Name "erl" -ErrorAction SilentlyContinue
if ($erlProcesses) {
    Write-Color "Erlang VM 进程：发现" "Green"
    $erlProcesses | Format-Table -Property Id, Name, Path -AutoSize

    # 检查是否是 RabbitMQ 的 erl 进程
    foreach ($proc in $erlProcesses) {
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)").CommandLine
            if ($cmdLine -and $cmdLine -like "*rabbit*") {
                Write-Color "RabbitMQ 运行状态：运行中 (PID: $($proc.Id))" "Green"
                $isRunning = "true"
                break
            }
        } catch {
            # 忽略访问拒绝
        }
    }

    if (-not $isRunning) {
        Write-Color "Erlang 进程存在，但可能不是 RabbitMQ" "Yellow"
    }
} else {
    Write-Color "Erlang VM 进程：未运行" "Red"
}

# 3. 检查端口
Write-Progress "CheckingPorts" 50
Write-Color "`n3. 检查端口监听:" "Yellow"

# 增强端口检查函数，显示监听地址
function Check-PortWithBinding {
    param([int]$Port, [string]$Name)
    Write-Color "$Name 端口检查 ($Port):" "Yellow"
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        Write-Color "  端口监听：是" "Green"
        Write-Color "  监听地址：$($listener.LocalAddress):$($listener.LocalPort)" "Gray"

        # 检查是否监听在所有接口
        $allInterfaces = @("0.0.0.0", "::", "::ffff:0.0.0.0")
        if ($listener.LocalAddress -In $allInterfaces -or $listener.LocalAddress -eq "::") {
            Write-Color "  远程访问：${Name} 允许远程访问" "Green"
        } else {
            Write-Color "  远程访问：${Name} 仅允许本地访问" "Yellow"
        }
        return $true
    } else {
        Write-Color "  端口监听：否" "Red"
        return $false
    }
}

$amqpOpen = Check-PortWithBinding $AMQP_PORT "AMQP"
$mgmtOpen = Check-PortWithBinding $MANAGEMENT_PORT "Management"

if ($amqpOpen -or $mgmtOpen) {
    $isRunning = "true"
    Write-Color "端口检测表明 RabbitMQ 运行中" "Green"
}

# 远程访问检查
Write-Progress "CheckingRemoteAccess" 55
Write-Color "`n4. 远程访问检查:" "Yellow"
if ($amqpOpen -and $mgmtOpen) {
    Write-Color "远程访问状态：可用" "Green"
} else {
    Write-Color "远程访问状态：不可用 (服务未完全运行)" "Red"
}

# 5. 使用 rabbitmqctl 检查状态
Write-Progress "CheckingRabbitmqStatus" 65
Write-Color "`n5. RabbitMQ 状态检查:" "Yellow"

if ($rabbitmqctlCommand) {
    Write-Color "执行 rabbitmqctl status..." "Yellow"
    try {
        $statusOutput = & rabbitmqctl status 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Color "rabbitmqctl status: 正常" "Green"
            $isRunning = "true"
            $isInstalled = "true"

            # 尝试获取版本
            $versionMatch = $statusOutput -match 'RabbitMQ ([0-9]+\.[0-9]+\.[0-9]+)'
            if ($versionMatch) {
                $version = $matches[1]
                Write-Color "版本：$version" "Green"
            }
        } else {
            Write-Color "rabbitmqctl status: 失败" "Red"
        }
    } catch {
        Write-Color "rabbitmqctl 执行失败：$_" "Red"
    }
} else {
    Write-Color "rabbitmqctl 不可用，跳过状态检查" "Yellow"
}

# 6. 检查服务状态
Write-Progress "CheckingServiceStatus" 75
Write-Color "`n6. Windows 服务状态:" "Yellow"

if ($rabbitmqService) {
    foreach ($service in $rabbitmqService) {
        Write-Color "服务：$($service.Name)" "Gray"
        Write-Color "  状态：$($service.Status)" "Gray"
        Write-Color "  启动类型：$($service.StartType)" "Gray"

        if ($service.Status -eq "Running") {
            $isRunning = "true"
            $isInstalled = "true"
        }
    }
} else {
    Write-Color "未找到 RabbitMQ 服务" "Yellow"
}

# 输出机器可读状态
Write-Progress "OutputtingStatus" 95
Write-Color "`n--- MACHINE READABLE ---" "Cyan"
Write-Host "INSTALLED: $isInstalled"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $isRunning"
Write-Host "PORT: $AMQP_PORT,$MANAGEMENT_PORT"
Write-Host "------------------------"

# 最终状态摘要
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
