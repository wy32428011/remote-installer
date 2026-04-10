# =================================================================
# Nginx 状态检测脚本 (Windows)
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8

# 颜色模拟
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
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

Write-Color "========================================" "Cyan"
Write-Color "      Nginx 状态检测" "Cyan"
Write-Color "========================================" "Cyan"

$isInstalled = "false"
$isRunning = "false"
$version = "未知"

# 1. 检查安装情况
Write-Color "1. 检查安装情况:" "Yellow"
$nginxService = Get-Service -Name "Nginx*" -ErrorAction SilentlyContinue
$nginxExe = Get-Command "nginx" -ErrorAction SilentlyContinue

if ($nginxService -or $nginxExe) {
    Write-Color "Nginx 已安装：是" "Green"
    $isInstalled = "true"

    # 获取版本号
    try {
        $versionOutput = nginx -v 2>&1
        if ($versionOutput -match '(\d+\.\d+\.\d+)') {
            $version = $Matches[1]
        }
    } catch {
        $version = "未知"
    }
    Write-Color "版本：$version" "Green"
} else {
    Write-Color "Nginx 已安装：否" "Red"
}

# 2. 检查进程
Write-Color "2. 检查运行进程:" "Yellow"
$process = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
if ($process) {
    Write-Color "Nginx 运行状态：运行中" "Green"
    $isRunning = "true"
} else {
    Write-Color "Nginx 运行状态：未运行" "Red"
}

# 3. 检查端口
if (Check-Port 80 "Nginx (HTTP)" "nginx") {
    $isRunning = "true"
}
Check-Port 443 "Nginx (HTTPS)" "nginx"

# 输出机器可读状态
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: $isInstalled"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $isRunning"
Write-Host "PORT: 80,443"
Write-Host "------------------------"
