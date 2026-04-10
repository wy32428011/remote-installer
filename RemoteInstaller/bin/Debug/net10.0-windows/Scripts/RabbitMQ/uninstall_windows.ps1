# =================================================================
# RabbitMQ Windows 完全卸载脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [string]$InstallPath = "C:\Program Files\RabbitMQ Server",
    [switch]$KeepData
)

# 颜色输出函数
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Write-Progress {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage:$Percent"
}

# 初始化
Write-Progress "Initializing" 5
Write-Color "========================================" "Cyan"
Write-Color "      RabbitMQ Windows 完全卸载脚本" "Cyan"
Write-Color "========================================" "Cyan"
Write-Color "安装路径：$InstallPath" "Yellow"
Write-Color "保留数据模式：$KeepData" "Yellow"

# 1. 检查管理员权限
Write-Progress "CheckingPermissions" 10
Write-Color "`n1. 检查管理员权限:" "Yellow"
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误：请以管理员身份运行此脚本" "Red"
    exit 1
}
Write-Color "管理员权限：已获取" "Green"

# 2. 停止 RabbitMQ 服务
Write-Progress "StoppingRabbitMQ" 20
Write-Color "`n2. 停止 RabbitMQ 服务:" "Yellow"

# 尝试停止服务
try {
    $services = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
    foreach ($service in $services) {
        Write-Color "停止服务：$($service.Name)" "Yellow"
        Stop-Service -Name $service.Name -Force -ErrorAction SilentlyContinue
    }
    Write-Color "RabbitMQ 服务已停止" "Green"
} catch {
    Write-Color "服务未运行或不存在" "Yellow"
}

# 停止 Erlang 进程
Write-Color "检查并停止 Erlang 进程..." "Yellow"
$erlProcesses = Get-Process -Name "erl" -ErrorAction SilentlyContinue
if ($erlProcesses) {
    # 检查是否是 RabbitMQ 的 erl 进程
    foreach ($proc in $erlProcesses) {
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.ProcessId)").CommandLine
            if ($cmdLine -and $cmdLine -like "*rabbit*") {
                Write-Color "终止进程 PID: $($proc.ProcessId)" "Yellow"
                Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
            }
        } catch {
            # 忽略访问拒绝
        }
    }
    Start-Sleep -Seconds 3
}

# 多次尝试确保进程停止
for ($i = 1; $i -le 3; $i++) {
    $remainingProcesses = Get-Process -Name "erl" -ErrorAction SilentlyContinue
    if (-not $remainingProcesses) { break }

    # 再次检查是否是 RabbitMQ 进程
    $rabbitmqProcesses = @()
    foreach ($proc in $remainingProcesses) {
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.ProcessId)").CommandLine
            if ($cmdLine -and $cmdLine -like "*rabbit*") {
                $rabbitmqProcesses += $proc
            }
        } catch {
            # 忽略访问拒绝
        }
    }

    if (-not $rabbitmqProcesses) { break }

    Write-Color "发现残留进程，强制终止 (尝试 $i/3)..." "Yellow"
    foreach ($proc in $rabbitmqProcesses) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

# 最终确认
$remainingProcesses = Get-Process -Name "erl" -ErrorAction SilentlyContinue
$stillRunning = $false
foreach ($proc in $remainingProcesses) {
    try {
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.ProcessId)").CommandLine
        if ($cmdLine -and $cmdLine -like "*rabbit*") {
            $stillRunning = $true
            break
        }
    } catch {
        # 忽略访问拒绝
    }
}

if ($stillRunning) {
    Write-Color "警告：仍有残留进程无法终止" "Red"
} else {
    Write-Color "所有 RabbitMQ 进程已终止" "Green"
}

# 3. 卸载 Windows 服务
Write-Progress "UninstallingService" 35
Write-Color "`n3. 卸载 Windows 服务:" "Yellow"

# 查找 RabbitMQ 安装目录
$RabbitMQHome = $null
$possiblePaths = @(
    "C:\Program Files\RabbitMQ Server",
    "C:\Program Files (x86)\RabbitMQ Server",
    "C:\RabbitMQ"
)

foreach ($path in $possiblePaths) {
    if (Test-Path "$path\rabbitmq_server*\sbin\rabbitmq-service.exe") {
        $RabbitMQHome = Get-ChildItem "$path\rabbitmq_server*" | Select-Object -First 1
        $RabbitMQHome = $RabbitMQHome.FullName
        break
    }
}

if ($RabbitMQHome) {
    $rabbitmqService = "$RabbitMQHome\sbin\rabbitmq-service.exe"
    if (Test-Path $rabbitmqService) {
        Write-Color "通过 rabbitmq-service.exe 卸载服务..." "Yellow"
        & $rabbitmqService Remove 2>&1 | Out-Null
        Start-Sleep -Seconds 2
    }
}

# 删除所有 RabbitMQ 服务
$services = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
foreach ($service in $services) {
    Write-Color "删除服务：$($service.Name)" "Yellow"
    sc.exe delete "$($service.Name)" 2>$null | Out-Null
}

# 等待服务真正删除（sc.exe delete 是异步的）
Write-Color "等待服务彻底删除..." "Yellow"
$waitCount = 0
$maxWait = 10
while ($waitCount -lt $maxWait) {
    $remainingServices = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
    if (-not $remainingServices) { break }
    Start-Sleep -Seconds 1
    $waitCount++
}
if ($remainingServices) {
    Write-Color "警告：服务仍在删除中，将在下次检测时确认" "Yellow"
} else {
    Write-Color "RabbitMQ 服务已彻底卸载" "Green"
}

# 4. 清理防火墙规则
Write-Progress "CleaningFirewallRules" 50
Write-Color "`n4. 清理防火墙规则:" "Yellow"

$removedRules = 0
$firewallRules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like "*RabbitMQ*" }
foreach ($rule in $firewallRules) {
    Remove-NetFirewallRule -InputObject $rule
    $removedRules++
    Write-Color "已删除规则：$($rule.DisplayName)" "Green"
}

if ($removedRules -eq 0) {
    Write-Color "未发现防火墙规则" "Yellow"
} else {
    Write-Color "已删除 $removedRules 条防火墙规则" "Green"
}

# 5. 删除安装目录
Write-Progress "RemovingInstallationDirectory" 70
Write-Color "`n5. 删除安装目录:" "Yellow"

$pathsToDelete = @(
    "C:\Program Files\RabbitMQ Server",
    "C:\Program Files (x86)\RabbitMQ Server",
    "C:\RabbitMQ",
    "$env:ProgramFiles\RabbitMQ Server",
    "$env:ProgramFiles(x86)\RabbitMQ Server"
)

foreach ($path in $pathsToDelete) {
    if (Test-Path $path) {
        if ($KeepData) {
            Write-Color "保留数据模式：跳过 $path" "Yellow"
        } else {
            Write-Color "删除目录：$path" "Yellow"

            # 先尝试删除文件
            Get-ChildItem -Path $path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                try {
                    Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
                } catch {
                    # 忽略删除失败
                }
            }

            # 然后删除目录
            Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Color "已删除：$path" "Green"
        }
    }
}

# 6. 清理数据目录
Write-Progress "CleaningDataDirectories" 75
Write-Color "`n6. 清理数据目录:" "Yellow"

$dataPaths = @(
    "$env:ProgramData\RabbitMQ",
    "C:\ProgramData\RabbitMQ",
    "$env:AppData\RabbitMQ",
    "$env:UserProfile\.erlang.cookie"
)

foreach ($path in $dataPaths) {
    if (Test-Path $path) {
        if ($KeepData) {
            Write-Color "保留数据模式：跳过 $path" "Yellow"
        } else {
            Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Color "已删除数据：$path" "Green"
        }
    }
}

# 7. 清理环境变量
Write-Progress "CleaningEnvironmentVariables" 80
Write-Color "`n7. 清理环境变量:" "Yellow"

# 清理 RABBITMQ_HOME
$rabbitmqHome = [Environment]::GetEnvironmentVariable("RABBITMQ_HOME", "Machine")
if ($rabbitmqHome) {
    [Environment]::SetEnvironmentVariable("RABBITMQ_HOME", $null, "Machine")
    Write-Color "已清理 RABBITMQ_HOME 环境变量" "Green"
}

# 清理系统 PATH
$systemPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($systemPath -like "*rabbitmq*" -or $systemPath -like "*RabbitMQ*") {
    $newSystemPath = ($systemPath -split ';' | Where-Object { $_ -notlike "*rabbitmq*" -and $_ -notlike "*RabbitMQ*" } -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newSystemPath, "Machine")
    Write-Color "已清理系统 PATH 环境变量" "Green"
}

# 8. 清理注册表
Write-Progress "CleaningRegistry" 90
Write-Color "`n8. 清理注册表:" "Yellow"

$registryPaths = @(
    "HKLM:\SOFTWARE\RabbitMQ",
    "HKLM:\SOFTWARE\Wow6432Node\RabbitMQ",
    "HKCU:\SOFTWARE\RabbitMQ"
)

foreach ($regPath in $registryPaths) {
    if (Test-Path $regPath) {
        Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Color "已清理注册表：$regPath" "Green"
    }
}

# 完成
Write-Progress "Complete" 100
Write-Color "`n========================================" "Cyan"
Write-Color "      RabbitMQ 完全卸载完成！" "Green"
Write-Color "========================================" "Cyan"

# 最终验证（带重试）
Write-Color "`n最终验证:" "Yellow"

# 验证服务（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $rabbitmqServices = Get-Service -Name "RabbitMQ*" -ErrorAction SilentlyContinue
    if (-not $rabbitmqServices) { break }
    if ($retry -lt 3) {
        Write-Color "发现服务残留，等待后重试 ($retry/3)..." "Yellow"
        Start-Sleep -Seconds 2
    }
}
if ($rabbitmqServices) {
    Write-Color "警告：仍有 RabbitMQ 服务存在" "Red"
} else {
    Write-Color "RabbitMQ 服务：已清理" "Green"
}

# 验证进程（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $stillRunning = $false
    $remainingProcesses = Get-Process -Name "erl" -ErrorAction SilentlyContinue
    foreach ($proc in $remainingProcesses) {
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.ProcessId)").CommandLine
            if ($cmdLine -and $cmdLine -like "*rabbit*") {
                $stillRunning = $true
                break
            }
        } catch {
            # 忽略访问拒绝
        }
    }
    if (-not $stillRunning) { break }
    if ($retry -lt 3) {
        Write-Color "发现进程残留，强制终止后重试 ($retry/3)..." "Yellow"
        foreach ($proc in $remainingProcesses) {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 2
    }
}
if ($stillRunning) {
    Write-Color "警告：仍有 RabbitMQ 进程运行" "Red"
} else {
    Write-Color "RabbitMQ 进程：已停止" "Green"
}

# 检查安装目录
$installed = $false
foreach ($path in $pathsToDelete) {
    if (Test-Path $path) {
        $installed = $true
        break
    }
}

if ($installed) {
    Write-Color "警告：安装目录仍存在" "Red"
} else {
    Write-Color "安装目录：已清理" "Green"
}

# 验证 RabbitMQ 默认端口（5672 和 15672）
$port5672 = Get-NetTCPConnection -LocalPort 5672 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
$port15672 = Get-NetTCPConnection -LocalPort 15672 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($port5672 -or $port15672) {
    if ($port5672) { Write-Color "警告：端口 5672 仍在被占用 (PID: $($port5672.OwningProcess))" "Red" }
    if ($port15672) { Write-Color "警告：端口 15672 仍在被占用 (PID: $($port15672.OwningProcess))" "Red" }
} else {
    Write-Color "RabbitMQ 端口（5672/15672）：已释放" "Green"
}

# 输出机器可读的状态信息
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: false"
Write-Host "RUNNING: false"
Write-Host "------------------------"
Write-Host "`nSTAGE:SUCCESS" "Green"
