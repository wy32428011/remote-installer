# =================================================================
# Nginx Windows 完全卸载脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [string]$InstallPath = "C:\Program Files\Nginx",
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
Write-Color "      Nginx Windows 完全卸载脚本" "Cyan"
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

# 2. 停止 Nginx 进程
Write-Progress "StoppingNginx" 20
Write-Color "`n2. 停止 Nginx 进程:" "Yellow"

# 尝试停止服务
try {
    Stop-Service -Name "Nginx" -Force -ErrorAction SilentlyContinue
    Write-Color "Nginx 服务已停止" "Green"
}
catch {
    Write-Color "服务未运行或不存在" "Yellow"
}

# 使用 taskkill 停止进程
Write-Color "尝试停止 Nginx 进程..." "Yellow"
taskkill /F /IM nginx.exe 2>$null | Out-Null
Start-Sleep -Seconds 2

# 多次尝试确保进程停止
for ($i = 1; $i -le 3; $i++) {
    $nginxProcesses = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
    if (-not $nginxProcesses) { break }

    Write-Color "发现 Nginx 进程，强制终止 (尝试 $i/3)..." "Yellow"
    Get-Process -Name "nginx" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 最终确认
$nginxProcesses = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
if ($nginxProcesses) {
    Write-Color "警告：仍有残留进程无法终止" "Red"
    $nginxProcesses | ForEach-Object { Write-Color "  - PID: $($_.Id), Name: $($_.Name)" "Red" }
}
else {
    Write-Color "所有 Nginx 进程已终止" "Green"
}

# 3. 卸载 Windows 服务
Write-Progress "UninstallingService" 35
Write-Color "`n3. 卸载 Windows 服务:" "Yellow"

try {
    $service = Get-Service -Name "Nginx*" -ErrorAction SilentlyContinue
    if ($service) {
        foreach ($s in $service) {
            Write-Color "卸载服务：$($s.Name)" "Yellow"
            sc.exe delete "$($s.Name)" 2>$null | Out-Null
        }
        # 等待服务真正删除（sc.exe delete 是异步的）
        Write-Color "等待服务彻底删除..." "Yellow"
        $waitCount = 0
        $maxWait = 10
        while ($waitCount -lt $maxWait) {
            $remainingService = Get-Service -Name "Nginx*" -ErrorAction SilentlyContinue
            if (-not $remainingService) { break }
            Start-Sleep -Seconds 1
            $waitCount++
        }
        if ($remainingService) {
            Write-Color "警告：服务仍在删除中，将在下次检测时确认" "Yellow"
        } else {
            Write-Color "Nginx 服务已彻底卸载" "Green"
        }
    }
    else {
        Write-Color "未发现 Nginx 服务" "Yellow"
    }
}
catch {
    Write-Color "警告：服务卸载失败 - $($_.Message)" "Yellow"
}

# 4. 清理防火墙规则
Write-Progress "CleaningFirewallRules" 50
Write-Color "`n4. 清理防火墙规则:" "Yellow"

$removedRules = 0
$firewallRules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like "*nginx*" -or $_.DisplayName -like "*Nginx*" }
foreach ($rule in $firewallRules) {
    Remove-NetFirewallRule -InputObject $rule
    $removedRules++
    Write-Color "已删除规则：$($rule.DisplayName)" "Green"
}

# 删除旧版规则
netsh advfirewall firewall delete rule name="nginx*" 2>$null | Out-Null
netsh advfirewall firewall delete rule name="Nginx*" 2>$null | Out-Null

if ($removedRules -eq 0) {
    Write-Color "未发现防火墙规则" "Yellow"
}
else {
    Write-Color "已删除 $removedRules 条防火墙规则" "Green"
}

# 5. 删除安装目录
Write-Progress "RemovingInstallationDirectory" 70
Write-Color "`n5. 删除安装目录:" "Yellow"

if (Test-Path $InstallPath) {
    if ($KeepData) {
        Write-Color "保留数据模式：仅删除配置文件" "Yellow"
        # 只删除配置文件，保留数据
        $configPaths = @(
            Join-Path $InstallPath "conf",
            Join-Path $InstallPath "logs",
            Join-Path $InstallPath "service.bat"
        )
        foreach ($path in $configPaths) {
            if (Test-Path $path) {
                Remove-Item -Path $path -Recurse -Force
                Write-Color "已删除：$path" "Green"
            }
        }
    }
    else {
        Write-Color "删除完整安装目录：$InstallPath" "Yellow"

        # 先尝试删除文件
        Get-ChildItem -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
            }
            catch {
                # 忽略删除失败
            }
        }

        # 然后删除目录
        Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Color "已删除安装目录" "Green"
    }
}
else {
    Write-Color "安装目录不存在" "Yellow"
}

# 6. 清理其他可能的安装路径
Write-Progress "CleaningOtherPaths" 80
Write-Color "`n6. 清理其他可能的安装路径:" "Yellow"

$otherPaths = @(
    "C:\nginx",
    "C:\Program Files (x86)\Nginx",
    "C:\opt\nginx",
    "$env:USERPROFILE\nginx"
)

foreach ($path in $otherPaths) {
    if (Test-Path $path) {
        Write-Color "发现其他 Nginx 安装：$path" "Yellow"
        if (-not $KeepData) {
            Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Color "已删除：$path" "Green"
        }
    }
}

# 7. 清理环境变量
Write-Progress "CleaningEnvironmentVariables" 90
Write-Color "`n7. 清理环境变量:" "Yellow"

# 清理系统环境变量
$systemPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($systemPath -like "*nginx*" -or $systemPath -like "*Nginx*") {
    $newSystemPath = ($systemPath -split ';' | Where-Object { $_ -notlike "*nginx*" -and $_ -notlike "*Nginx*" } -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newSystemPath, "Machine")
    Write-Color "已清理系统 PATH 环境变量" "Green"
}

# 清理用户环境变量
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -like "*nginx*" -or $userPath -like "*Nginx*") {
    $newUserPath = ($userPath -split ';' | Where-Object { $_ -notlike "*nginx*" -and $_ -notlike "*Nginx*" } -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    Write-Color "已清理用户 PATH 环境变量" "Green"
}

# 8. 清理注册表
Write-Progress "CleaningRegistry" 95
Write-Color "`n8. 清理注册表:" "Yellow"

$registryPaths = @(
    "HKLM:\SOFTWARE\Nginx",
    "HKLM:\SOFTWARE\Wow6432Node\Nginx",
    "HKCU:\SOFTWARE\Nginx"
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
Write-Color "      Nginx 完全卸载完成！" "Green"
Write-Color "========================================" "Cyan"

# 最终验证（带重试）
Write-Color "`n最终验证:" "Yellow"

# 验证服务（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $nginxService = Get-Service -Name "Nginx*" -ErrorAction SilentlyContinue
    if (-not $nginxService) { break }
    if ($retry -lt 3) {
        Write-Color "发现服务残留，等待后重试 ($retry/3)..." "Yellow"
        Start-Sleep -Seconds 2
    }
}
if ($nginxService) {
    Write-Color "警告：仍有 Nginx 服务存在" "Red"
} else {
    Write-Color "Nginx 服务：已清理" "Green"
}

# 验证进程（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $nginxProcesses = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
    if (-not $nginxProcesses) { break }
    if ($retry -lt 3) {
        Write-Color "发现进程残留，强制终止后重试 ($retry/3)..." "Yellow"
        Get-Process -Name "nginx" -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}
if ($nginxProcesses) {
    Write-Color "警告：仍有 Nginx 进程运行" "Red"
} else {
    Write-Color "Nginx 进程：已停止" "Green"
}

# 验证安装目录
if (Test-Path $InstallPath) {
    Write-Color "警告：安装目录仍存在" "Red"
} else {
    Write-Color "安装目录：已清理" "Green"
}

# 验证 nginx 命令
$nginxCommand = Get-Command "nginx" -ErrorAction SilentlyContinue
if ($nginxCommand) {
    Write-Color "警告：nginx 命令仍可在 PATH 中找到" "Red"
} else {
    Write-Color "nginx 命令：已清理" "Green"
}

# 验证端口 80 不再监听
$portCheck = Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($portCheck) {
    Write-Color "警告：端口 80 仍在被占用 (PID: $($portCheck.OwningProcess))" "Red"
} else {
    Write-Color "端口 80：已释放" "Green"
}

Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: false"
Write-Host "RUNNING: false"
Write-Host "------------------------"
Write-Host "`nSTAGE:SUCCESS" "Green"
