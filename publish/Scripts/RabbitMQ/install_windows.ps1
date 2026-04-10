# =================================================================
# RabbitMQ Windows 安装脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [string]$PackagePath = "",
    [string]$InstallPath = "C:\Program Files\RabbitMQ Server",
    [int]$AmqpPort = 5672,
    [int]$ManagementPort = 15672,
    [string]$ClusterName = "rabbitmq-cluster",
    [string]$NodeName = "rabbit@localhost",
    [switch]$EnableRemoteAccess = $true
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
Write-Color "      RabbitMQ Windows 安装脚本" "Cyan"
Write-Color "========================================" "Cyan"
Write-Color "安装路径：$InstallPath" "Yellow"
Write-Color "AMQP 端口：$AmqpPort" "Yellow"
Write-Color "管理端口：$ManagementPort" "Yellow"

# 1. 检查管理员权限
Write-Progress "CheckingPermissions" 10
Write-Color "`n1. 检查管理员权限:" "Yellow"
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误：请以管理员身份运行此脚本" "Red"
    exit 1
}
Write-Color "管理员权限：已获取" "Green"

# 2. 检查 Erlang 环境
Write-Progress "CheckingErlang" 15
Write-Color "`n2. 检查 Erlang 环境:" "Yellow"

$erlPath = Get-Command erl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if ($erlPath) {
    Write-Color "Erlang 路径：$erlPath" "Green"
    Write-Color "Erlang 已安装" "Green"
} else {
    Write-Color "警告：未找到 Erlang，需要先安装 Erlang" "Yellow"
    Write-Color "请从 https://www.erlang.org/downloads 下载并安装 Erlang/OTP" "Yellow"
    Write-Color "或者使用 winget: winget install Erlang.OTP" "Yellow"
}

# 3. 检查安装包
Write-Progress "CheckingPackage" 20
Write-Color "`n3. 检查安装包:" "Yellow"

$UseOnlineInstall = $false
$ExtractPath = ""

if ([string]::IsNullOrEmpty($PackagePath) -or -not (Test-Path $PackagePath)) {
    Write-Color "未提供安装包，将使用在线安装" "Yellow"
    $UseOnlineInstall = $true
} else {
    Write-Color "安装包路径：$PackagePath" "Green"
    $ExtractPath = $InstallPath
}

# 4. 在线安装 - 使用 winget
if ($UseOnlineInstall) {
    Write-Progress "OnlineInstalling" 25
    Write-Color "`n4. 在线安装 RabbitMQ:" "Yellow"

    # 检查 winget
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Color "错误：未找到 winget，请手动安装 RabbitMQ" "Red"
        Write-Color "下载地址：https://github.com/rabbitmq/rabbitmq-server/releases" "Yellow"
        exit 1
    }

    # 安装 Erlang（如果未安装）
    Write-Color "检查 Erlang 安装..." "Yellow"
    $erlInstalled = Get-Command erl -ErrorAction SilentlyContinue
    if (-not $erlInstalled) {
        Write-Color "安装 Erlang..." "Yellow"
        winget install --id Erlang.OTP --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
        Start-Sleep -Seconds 5
    }

    # 安装 RabbitMQ
    Write-Color "安装 RabbitMQ..." "Yellow"
    $installResult = winget install --id RabbitMQ.RabbitMQ.Server --silent --accept-package-agreements --accept-source-agreements 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Color "RabbitMQ 在线安装完成" "Green"
    } else {
        Write-Color "警告：在线安装可能失败，请手动安装" "Yellow"
        Write-Color "下载地址：https://github.com/rabbitmq/rabbitmq-server/releases" "Yellow"
    }

    # 获取安装路径
    $InstallPath = "C:\Program Files\RabbitMQ Server"
    if (-not (Test-Path $InstallPath)) {
        $InstallPath = "C:\Program Files (x86)\RabbitMQ Server"
    }
} else {
    # 5. 离线安装 - 解压安装包
    Write-Progress "ExtractingPackage" 30
    Write-Color "`n5. 解压安装包:" "Yellow"

    if ($PackagePath -match '\.zip$') {
        Write-Color "正在解压 ZIP 包..." "Yellow"
        Expand-Archive -Path $PackagePath -DestinationPath $InstallPath -Force
    } elseif ($PackagePath -match '\.msi$') {
        Write-Color "正在安装 MSI 包..." "Yellow"
        Start-Process msiexec.exe -ArgumentList "/i `"$PackagePath`" /quiet /norestart" -Wait
    } else {
        Write-Color "不支持的包格式，尝试作为已解压目录处理" "Yellow"
        $ExtractPath = $PackagePath
    }

    Write-Color "解压完成" "Green"
}

# 6. 配置环境变量
Write-Progress "ConfiguringEnvironment" 40
Write-Color "`n6. 配置环境变量:" "Yellow"

# 查找 RabbitMQ 安装目录
$RabbitMQHome = $null
$possiblePaths = @(
    "C:\Program Files\RabbitMQ Server",
    "C:\Program Files (x86)\RabbitMQ Server",
    "C:\RabbitMQ",
    "$env:ProgramFiles\RabbitMQ Server",
    "$env:ProgramFiles(x86)\RabbitMQ Server"
)

foreach ($path in $possiblePaths) {
    if (Test-Path "$path\rabbitmq_server*\sbin\rabbitmqctl.exe") {
        $RabbitMQHome = Get-ChildItem "$path\rabbitmq_server*" | Select-Object -First 1
        $RabbitMQHome = $RabbitMQHome.FullName
        break
    }
}

if ($RabbitMQHome) {
    Write-Color "RabbitMQ 安装目录：$RabbitMQHome" "Green"

    # 设置 RABBITMQ_HOME
    [Environment]::SetEnvironmentVariable("RABBITMQ_HOME", $RabbitMQHome, "Machine")
    Write-Color "已设置 RABBITMQ_HOME 环境变量" "Green"

    # 更新 PATH
    $systemPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $sbinPath = "$RabbitMQHome\sbin"
    if ($systemPath -notlike "*$sbinPath*") {
        $newSystemPath = $systemPath + ";$sbinPath"
        [Environment]::SetEnvironmentVariable("Path", $newSystemPath, "Machine")
        Write-Color "已更新 PATH 环境变量" "Green"
    }
} else {
    Write-Color "警告：未找到 RabbitMQ 安装目录" "Yellow"
}

# 7. 配置防火墙
Write-Progress "ConfiguringFirewall" 50
Write-Color "`n7. 配置防火墙:" "Yellow"

# 添加 AMQP 端口规则
if (-not (Get-NetFirewallRule -DisplayName "RabbitMQ AMQP" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "RabbitMQ AMQP" -Direction Inbound -LocalPort $AmqpPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 AMQP 端口：$AmqpPort" "Green"
}

# 添加管理端口规则
if (-not (Get-NetFirewallRule -DisplayName "RabbitMQ Management" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "RabbitMQ Management" -Direction Inbound -LocalPort $ManagementPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放管理端口：$ManagementPort" "Green"
}

# 添加集群通信端口 (25672)
$clusterPort = 25672
if (-not (Get-NetFirewallRule -DisplayName "RabbitMQ Cluster" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "RabbitMQ Cluster" -Direction Inbound -LocalPort $clusterPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放集群端口：$clusterPort" "Green"
}

# 7.1 配置 RabbitMQ 允许远程访问
Write-Progress "ConfiguringRemoteAccess" 55
Write-Color "`n7.1 配置 RabbitMQ 远程访问:" "Yellow"

if ($EnableRemoteAccess) {
    # 创建 RabbitMQ 配置目录
    $rabbitmqConfDir = "$env:ProgramData\rabbitmq"
    if (-not (Test-Path $rabbitmqConfDir)) {
        New-Item -ItemType Directory -Path $rabbitmqConfDir -Force | Out-Null
    }

    # 创建 rabbitmq.conf 配置文件
    $rabbitmqConfContent = @"
# 监听配置 - 绑定到所有网络接口
listeners.tcp.default = $AmqpPort
listeners.sasl.default = $AmqpPort

# 管理插件端口 - 绑定到所有网络接口
management.tcp.port = $ManagementPort
management.ip_address = 0.0.0.0

# 允许所有 IP 访问 (远程访问)
loopback_users.guest = false

# 默认 vhost
default_vhost = /

# 默认用户配置
default_user = guest
default_pass = guest
default_user_tags.administrator = true

# 集群配置 (可选)
cluster_name = $ClusterName
"@
    Set-Content -Path "$rabbitmqConfDir\rabbitmq.conf" -Value $rabbitmqConfContent -Encoding UTF8
    Write-Color "RabbitMQ 远程访问配置已创建" "Green"
    Write-Color "  - 配置文件：$rabbitmqConfDir\rabbitmq.conf" "Gray"
} else {
    Write-Color "远程访问已禁用，仅允许本地连接" "Yellow"
    # 创建本地访问配置
    $rabbitmqConfDir = "$env:ProgramData\rabbitmq"
    if (-not (Test-Path $rabbitmqConfDir)) {
        New-Item -ItemType Directory -Path $rabbitmqConfDir -Force | Out-Null
    }
    $rabbitmqConfContent = @"
listeners.tcp.default = $AmqpPort
management.tcp.port = $ManagementPort
loopback_users.guest = false
"@
    Set-Content -Path "$rabbitmqConfDir\rabbitmq.conf" -Value $rabbitmqConfContent -Encoding UTF8
}

# 8. 启用管理插件
Write-Progress "EnablingPlugins" 60
Write-Color "`n8. 启用管理插件:" "Yellow"

$rabbitmqctl = "$RabbitMQHome\sbin\rabbitmqctl.exe"
$rabbitmqPlugins = "$RabbitMQHome\sbin\rabbitmq-plugins.exe"

if (Test-Path $rabbitmqPlugins) {
    Write-Color "启用 rabbitmq_management 插件..." "Yellow"
    & $rabbitmqPlugins enable rabbitmq_management 2>&1 | Out-Null
    Write-Color "管理插件已启用" "Green"
} else {
    Write-Color "警告：未找到 rabbitmq-plugins，跳过插件启用" "Yellow"
}

# 8.1 配置用户权限（允许 guest 远程访问）
Write-Progress "ConfiguringUsers" 65
Write-Color "`n8.1 配置用户权限:" "Yellow"

if (Test-Path $rabbitmqctl) {
    Write-Color "设置 guest 用户为管理员..." "Yellow"
    & $rabbitmqctl set_user_tags guest administrator 2>&1 | Out-Null
    Write-Color "设置 guest 用户权限..." "Yellow"
    & $rabbitmqctl set_permissions -p / guest ".*" ".*" ".*" 2>&1 | Out-Null
    Write-Color "guest 用户已配置为管理员，允许远程访问" "Green"
}

# 9. 安装 Windows 服务
Write-Progress "InstallingService" 70
Write-Color "`n9. 注册 Windows 服务:" "Yellow"

$rabbitmqService = "$RabbitMQHome\sbin\rabbitmq-service.exe"

if (Test-Path $rabbitmqService) {
    Write-Color "移除旧服务（如果存在）..." "Yellow"
    & $rabbitmqService Remove 2>&1 | Out-Null
    Start-Sleep -Seconds 2

    Write-Color "安装服务..." "Yellow"
    & $rabbitmqService Install 2>&1 | Out-Null

    Write-Color "启动服务..." "Yellow"
    & $rabbitmqService Start 2>&1 | Out-Null

    Write-Color "RabbitMQ 服务已注册并启动" "Green"
} else {
    Write-Color "警告：未找到 rabbitmq-service.exe，跳过服务注册" "Yellow"
}

# 10. 等待服务启动
Write-Progress "WaitingForService" 85
Write-Color "`n10. 等待服务启动:" "Yellow"

$success = $false
for ($i = 1; $i -le 30; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$ManagementPort" -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue
        if ($response -and $response.StatusCode -eq 200) {
            Write-Color "RabbitMQ 服务已成功启动" "Green"
            $success = $true
            break
        }
    } catch {
        # 忽略错误，继续等待
    }

    # 检查端口
    $listener = Get-NetTCPConnection -LocalPort $AmqpPort -State Listen -ErrorAction SilentlyContinue
    if ($listener) {
        Write-Color "AMQP 端口已监听 (尝试 $i/30)..." "Gray"
    } else {
        Write-Color "等待服务启动 (尝试 $i/30)..." "Gray"
    }
    Start-Sleep -Seconds 3
}

if (-not $success) {
    Write-Color "警告：RabbitMQ 在 90 秒内未能启动" "Yellow"
    Write-Color "请检查事件查看器或手动启动服务" "Yellow"
}

# 11. 验证安装
Write-Progress "Verifying" 90
Write-Color "`n11. 验证安装:" "Yellow"

$version = "未知"
if (Test-Path $rabbitmqctl) {
    try {
        $statusOutput = & $rabbitmqctl status 2>&1
        $versionMatch = $statusOutput -match 'RabbitMQ ([0-9]+\.[0-9]+\.[0-9]+)'
        if ($versionMatch) {
            $version = $matches[1]
        }
    } catch {
        # 忽略错误
    }
}

Write-Color "RabbitMQ 版本：$version" "Green"

# 输出机器可读的状态信息
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: true"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $success"
Write-Host "PORT: $AmqpPort,$ManagementPort"
Write-Host "------------------------"
Write-Host "`nSTAGE:SUCCESS" "Green"

Write-Color "`n========================================" "Cyan"
Write-Color "      RabbitMQ 安装完成！" "Green"
Write-Color "========================================" "Cyan"
Write-Color "安装目录：$RabbitMQHome" "Yellow"
Write-Color "AMQP 端口：$AmqpPort" "Yellow"
Write-Color "管理端口：$ManagementPort" "Yellow"
Write-Color "管理界面：http://<服务器 IP>:$ManagementPort" "Yellow"
Write-Color "默认用户：guest" "Yellow"
Write-Color "默认密码：guest" "Yellow"
Write-Color "注意：guest 用户已允许远程访问" "Yellow"
