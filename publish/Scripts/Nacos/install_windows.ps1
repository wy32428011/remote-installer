# =================================================================
# Nacos Windows 安装脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8

# 参数
param(
    [string]$PackagePath = "",
    [string]$InstallPath = "C:\nacos",
    [int]$HttpPort = 8848,
    [int]$RaftPort = 9848,
    [int]$GrpcPort = 9849,
    [string]$Mode = "standalone",
    [string]$Username = "nacos",
    [string]$Password = "nacos"
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

# 1. 检查 Java 环境
Write-Progress "CheckingJava" 10
Write-Color "`n1. 检查 Java 环境:" "Yellow"

$javaPath = Get-Command java -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if ($javaPath) {
    $javaVersion = java -version 2>&1 | Select-Object -First 1
    Write-Color "Java 路径：$javaPath" "Green"
    Write-Color "Java 版本：$javaVersion" "Green"
} else {
    Write-Color "错误：未找到 Java，请先安装 JDK 1.8+" "Red"
    Write-Color "下载地址：https://www.oracle.com/java/technologies/downloads/" "Yellow"
    exit 1
}

# 2. 检查安装包
Write-Progress "CheckingPackage" 15
Write-Color "`n2. 检查安装包:" "Yellow"

if ([string]::IsNullOrEmpty($PackagePath) -or -not (Test-Path $PackagePath)) {
    Write-Color "错误：未提供 Nacos 安装包" "Red"
    exit 1
}
Write-Color "安装包路径：$PackagePath" "Green"

# 3. 解压安装包
Write-Progress "ExtractingPackage" 20
Write-Color "`n3. 解压 Nacos 安装包:" "Yellow"

if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

if ($PackagePath -match '\.zip$') {
    Expand-Archive -Path $PackagePath -DestinationPath $InstallPath -Force
} else {
    Write-Color "错误：不支持的包格式，需要 .zip 文件" "Red"
    exit 1
}

# 查找解压后的目录
$nacosDirs = Get-ChildItem -Path $InstallPath -Directory -Filter "nacos*"
if ($nacosDirs.Count -gt 0) {
    $NacosDir = $nacosDirs[0].FullName
} else {
    $NacosDir = $InstallPath
}

Write-Color "解压完成：$NacosDir" "Green"

# 4. 配置 Nacos
Write-Progress "ConfiguringNacos" 30
Write-Color "`n4. 配置 Nacos:" "Yellow"

$confDir = Join-Path $NacosDir "conf"
$applicationProperties = Join-Path $confDir "application.properties"

Write-Color "配置 application.properties..." "Yellow"

$nacosConfig = @"
# 服务端口配置
server.port=${HttpPort}

# 运行模式
spring.datasource.platform=native

# 集群配置
nacos.naming.client.expired.time=180
nacos.core.auth.enabled=true
nacos.core.auth.default.token.secret.key=SecretKey012345678901234567890123456789012345678901234567890123456789
nacos.core.auth.server.identity.key=nacos-server-identity
nacos.core.auth.server.identity.value=nacos-server-identity-value

# 内置数据库配置
nacos.standalone=true

# 用户配置
nacos.core.auth.default.user.name=${Username}
nacos.core.auth.default.user.password=${Password}

# gRPC 端口 (2.x 版本需要)
nacos.grpc.server.port=${GrpcPort}
"@

Set-Content -Path $applicationProperties -Value $nacosConfig -Encoding UTF8
Write-Color "application.properties 配置完成" "Green"

# 5. 配置防火墙
Write-Progress "ConfiguringFirewall" 50
Write-Color "`n5. 配置防火墙:" "Yellow"

# 添加 HTTP 端口规则
if (-not (Get-NetFirewallRule -DisplayName "Nacos HTTP" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "Nacos HTTP" -Direction Inbound -LocalPort $HttpPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 HTTP 端口：$HttpPort" "Green"
}

# 添加 Raft 端口规则
if (-not (Get-NetFirewallRule -DisplayName "Nacos Raft" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "Nacos Raft" -Direction Inbound -LocalPort $RaftPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 Raft 端口：$RaftPort" "Green"
}

# 添加 gRPC 端口规则
if (-not (Get-NetFirewallRule -DisplayName "Nacos gRPC" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "Nacos gRPC" -Direction Inbound -LocalPort $GrpcPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 gRPC 端口：$GrpcPort" "Green"
}

# 6. 启动 Nacos
Write-Progress "StartingNacos" 70
Write-Color "`n6. 启动 Nacos:" "Yellow"

$binDir = Join-Path $NacosDir "bin"
$startupScript = Join-Path $binDir "startup.cmd"

if (Test-Path $startupScript) {
    # 设置环境变量
    $env:NACOS_JVM = "-Xms512m -Xmx512m -Xmn256m"
    $env:MODE = $Mode

    Write-Color "启动 Nacos..." "Yellow"
    Start-Process -FilePath $startupScript -ArgumentList "-m", $Mode -NoNewWindow -PassThru | Out-Null
    Write-Color "Nacos 启动命令已执行" "Yellow"
} else {
    Write-Color "错误：未找到 startup.cmd" "Red"
    exit 1
}

# 7. 等待服务启动
Write-Progress "WaitingForService" 80
Write-Color "`n7. 等待 Nacos 服务启动:" "Yellow"

$success = $false
for ($i = 1; $i -le 60; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$HttpPort/nacos/" -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue
        if ($response -and ($response.StatusCode -eq 200 -or $response.StatusCode -eq 302)) {
            Write-Color "Nacos 服务已成功启动" "Green"
            $success = $true
            break
        }
    } catch {
        # 忽略错误，继续等待
    }

    # 检查端口
    $listener = Get-NetTCPConnection -LocalPort $HttpPort -State Listen -ErrorAction SilentlyContinue
    if ($listener) {
        Write-Color "端口已监听 (尝试 $i/60)..." "Gray"
    } else {
        Write-Color "等待服务启动 (尝试 $i/60)..." "Gray"
    }
    Start-Sleep -Seconds 3
}

if (-not $success) {
    Write-Color "警告：Nacos 在 180 秒内未能启动" "Yellow"
    Write-Color "请检查日志：$NacosDir\logs\start.out" "Yellow"
}

# 8. 验证安装
Write-Progress "Verifying" 90
Write-Color "`n8. 验证 Nacos 安装:" "Yellow"

$version = "未知"
$nacosLibPath = Join-Path $NacosDir "lib"
if (Test-Path $nacosLibPath) {
    $versionFile = Get-ChildItem -Path $nacosLibPath -Filter "nacos-server-*.jar" | Select-Object -First 1
    if ($versionFile) {
        $version = $versionFile.Name -replace 'nacos-server-', '' -replace '\.jar', ''
    }
}

Write-Color "Nacos 版本：$version" "Green"
Write-Color "HTTP 端口：$HttpPort" "Green"
Write-Color "gRPC 端口：$GrpcPort" "Green"

# 输出机器可读的状态信息
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: true"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $success"
Write-Host "PORT: $HttpPort,$RaftPort,$GrpcPort"
Write-Host "------------------------"
Write-Host "`nSTAGE:SUCCESS" -ForegroundColor Green

Write-Color "`n========================================" "Cyan"
Write-Color "      Nacos 安装完成！" "Green"
Write-Color "========================================" "Cyan"
Write-Color "安装目录：$NacosDir" "Yellow"
Write-Color "HTTP 端口：$HttpPort" "Yellow"
Write-Color "gRPC 端口：$GrpcPort" "Yellow"
Write-Color "管理界面：http://localhost:$HttpPort/nacos" "Yellow"
Write-Color "用户名：$Username" "Yellow"
Write-Color "密码：$Password" "Yellow"
Write-Color ""
