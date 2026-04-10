# =================================================================
# Nginx Windows 安装脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [string]$PackagePath = "",
    [int]$HttpPort = 80,
    [int]$HttpsPort = 443,
    [string]$InstallPath = "C:\Program Files\Nginx"
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
Write-Color "      Nginx Windows 安装脚本" "Cyan"
Write-Color "========================================" "Cyan"
Write-Color "安装路径：$InstallPath" "Yellow"
Write-Color "HTTP 端口：$HttpPort" "Yellow"
Write-Color "HTTPS 端口：$HttpsPort" "Yellow"

# 1. 检查管理员权限
Write-Progress "CheckingPermissions" 10
Write-Color "`n1. 检查管理员权限:" "Yellow"
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误：请以管理员身份运行此脚本" "Red"
    exit 1
}
Write-Color "管理员权限：已获取" "Green"

# 2. 创建安装目录
Write-Progress "CreatingDirectories" 20
Write-Color "`n2. 创建安装目录:" "Yellow"
if (Test-Path $InstallPath) {
    Write-Color "安装目录已存在，清空..." "Yellow"
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Write-Color "创建目录：$InstallPath" "Green"

# 3. 解压安装包
Write-Progress "ExtractingPackage" 30
Write-Color "`n3. 解压安装包:" "Yellow"
if ([string]::IsNullOrEmpty($PackagePath) -or -not (Test-Path $PackagePath)) {
    Write-Color "警告：未提供安装包路径，假设已解压到当前目录" "Yellow"
    $ExtractPath = Get-Location
}
else {
    $ExtractPath = $InstallPath
    if ($PackagePath -match '\.zip$') {
        Write-Color "正在解压：$PackagePath" "Yellow"
        Expand-Archive -Path $PackagePath -DestinationPath $ExtractPath -Force
        # 如果解压后是子目录，移动到安装目录
        $Dirs = Get-ChildItem $ExtractPath -Directory
        if ($Dirs.Count -eq 1) {
            $Dirs | Move-Item -Destination $InstallPath -Force
        }
    }
    Write-Color "解压完成" "Green"
}

# 4. 配置 nginx.conf
Write-Progress "ConfiguringNginx" 50
Write-Color "`n4. 配置 nginx.conf:" "Yellow"
$NginxConf = Join-Path $InstallPath "conf\nginx.conf"
if (Test-Path $NginxConf) {
    $Content = Get-Content $NginxConf -Raw

    # 修改 HTTP 端口
    if ($HttpPort -ne 80) {
        $Content = $Content -replace 'listen\s+80\s*;', "listen $HttpPort;"
        Write-Color "修改 HTTP 端口：80 -> $HttpPort" "Green"
    }

    # 修改 HTTPS 端口
    if ($HttpsPort -ne 443) {
        $Content = $Content -replace 'listen\s+443\s+ssl;', "listen $HttpsPort ssl;"
        Write-Color "修改 HTTPS 端口：443 -> $HttpsPort" "Green"
    }

    Set-Content -Path $NginxConf -Value $Content -NoNewline
    Write-Color "配置文件已更新" "Green"
}
else {
    Write-Color "警告：未找到 nginx.conf" "Yellow"
}

# 5. 注册为 Windows 服务
Write-Progress "InstallingService" 70
Write-Color "`n5. 注册为 Windows 服务:" "Yellow"

# 创建服务批处理文件
$ServiceBat = Join-Path $InstallPath "service.bat"
@"
@echo off
sc create Nginx binPath= "`"$InstallPath\nginx.exe`" displayname= Nginx start= auto
if %errorlevel% equ 0 (
    echo 服务注册成功
) else (
    echo 服务注册失败
)
"@ | Set-Content $ServiceBat

# 执行服务注册
& $ServiceBat
if ($LASTEXITCODE -eq 0) {
    Write-Color "Nginx 服务已注册" "Green"
}
else {
    Write-Color "警告：服务注册可能失败" "Yellow"
}

# 6. 配置防火墙
Write-Progress "ConfiguringFirewall" 80
Write-Color "`n6. 配置防火墙:" "Yellow"

# 添加 HTTP 端口规则
$RuleName = "Nginx HTTP ($HttpPort)"
if (-not (Get-NetFirewallRule -Name "nginx-http" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "nginx-http" -Direction Inbound -LocalPort $HttpPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 HTTP 端口：$HttpPort" "Green"
}

# 添加 HTTPS 端口规则
$RuleName = "Nginx HTTPS ($HttpsPort)"
if (-not (Get-NetFirewallRule -Name "nginx-https" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "nginx-https" -Direction Inbound -LocalPort $HttpsPort -Protocol TCP -Action Allow -Profile Any | Out-Null
    Write-Color "已开放 HTTPS 端口：$HttpsPort" "Green"
}

# 7. 启动服务
Write-Progress "StartingService" 90
Write-Color "`n7. 启动 Nginx 服务:" "Yellow"

try {
    Start-Service -Name "Nginx" -ErrorAction Stop
    Write-Color "Nginx 服务已启动" "Green"
}
catch {
    Write-Color "警告：无法启动服务，尝试直接运行..." "Yellow"
    Start-Process -FilePath "$InstallPath\nginx.exe" -WorkingDirectory $InstallPath -WindowStyle Hidden
}

# 完成
Write-Progress "Finishing" 100
Write-Color "`n========================================" "Cyan"
Write-Color "      Nginx 安装完成！" "Green"
Write-Color "========================================" "Cyan"
Write-Color "安装路径：$InstallPath" "Yellow"
Write-Color "HTTP 端口：$HttpPort" "Yellow"
Write-Color "HTTPS 端口：$HttpsPort" "Yellow"
Write-Color "`nSTAGE:SUCCESS" "Green"
