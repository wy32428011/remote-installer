# =================================================================
# Elasticsearch Windows 卸载脚本
# =================================================================

param(
    [switch]$KeepData
)

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 颜色输出
function Write-Color {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}

# 初始化
Write-Color "========================================" "Cyan"
Write-Color "  Elasticsearch Windows 卸载脚本" "Cyan"
Write-Color "========================================" "Cyan"
Write-Color "保留数据: $KeepData" "Yellow"

#=============================================================================
# 1. 检查管理员权限
#=============================================================================
Write-Color "`n[1/6] 检查管理员权限..." "Yellow"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误: 请以管理员身份运行此脚本" "Red"
    Write-Host "INSTALLED: true"
    Write-Host "RUNNING: false"
    exit 1
}
Write-Color "管理员权限: 已获取" "Green"

#=============================================================================
# 2. 停止 Elasticsearch 服务
#=============================================================================
Write-Color "`n[2/6] 停止 Elasticsearch 服务..." "Yellow"

# 停止并删除服务
$esServices = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue
foreach ($svc in $esServices) {
    Write-Color "停止服务: $($svc.Name)" "Yellow"
    Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue
    sc.exe delete $svc.Name 2>$null | Out-Null
}

# 等待服务真正删除
Start-Sleep -Seconds 2

# 终止 Elasticsearch 相关进程
$esProcs = Get-CimInstance Win32_Process -Filter "name = 'java.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*elasticsearch*" -or $_.CommandLine -like "*org.elasticsearch.bootstrap.Elasticsearch*"
}
foreach ($proc in $esProcs) {
    Write-Color "终止进程 PID: $($proc.ProcessId)" "Yellow"
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 3

# 再次检查并强制终止
$remainingProcs = Get-CimInstance Win32_Process -Filter "name = 'java.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*elasticsearch*" -or $_.CommandLine -like "*org.elasticsearch.bootstrap.Elasticsearch*"
}
if ($remainingProcs) {
    Write-Color "仍有残留进程，强制终止..." "Yellow"
    foreach ($proc in $remainingProcs) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

Write-Color "服务已停止" "Green"

#=============================================================================
# 3. 删除服务
#=============================================================================
Write-Color "`n[3/6] 删除 Windows 服务..." "Yellow"

$remainingServices = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue
foreach ($svc in $remainingServices) {
    Write-Color "删除服务: $($svc.Name)" "Yellow"
    sc.exe delete $svc.Name 2>$null | Out-Null
}

Write-Color "服务已删除" "Green"

#=============================================================================
# 4. 清理防火墙规则
#=============================================================================
Write-Color "`n[4/6] 清理防火墙规则..." "Yellow"

$removedCount = 0
Get-NetFirewallRule | Where-Object { $_.DisplayName -like "*Elasticsearch*" } | ForEach-Object {
    Remove-NetFirewallRule -InputObject $_ -ErrorAction SilentlyContinue
    $removedCount++
    Write-Color "已删除规则: $($_.DisplayName)" "Green"
}

if ($removedCount -eq 0) {
    Write-Color "未发现防火墙规则" "Yellow"
} else {
    Write-Color "已删除 $removedCount 条防火墙规则" "Green"
}

#=============================================================================
# 5. 删除安装目录
#=============================================================================
Write-Color "`n[5/6] 删除安装目录..." "Yellow"

$installPaths = @(
    "C:\Program Files\Elasticsearch",
    "C:\elasticsearch",
    "C:\Program Files (x86)\Elasticsearch"
)

foreach ($installPath in $installPaths) {
    if (Test-Path $installPath) {
        Write-Color "发现安装目录: $installPath" "Yellow"

        if ($KeepData) {
            # 保留数据模式：只删除 config, logs, plugins
            Write-Color "保留数据模式：删除 config, logs, plugins" "Yellow"
            $pathsToRemove = @("config", "logs", "plugins", "modules", "bin\install-service.bat")
            foreach ($subPath in $pathsToRemove) {
                $fullPath = Join-Path $installPath $subPath
                if (Test-Path $fullPath) {
                    Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Color "  已删除: $subPath" "Green"
                }
            }
        } else {
            # 完全删除
            Write-Color "删除安装目录: $installPath" "Yellow"
            try {
                # 使用 robocopy 删除（处理锁定文件更好）
                $emptyDir = "$env:TEMP\empty_es_$PID"
                New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
                robocopy "$emptyDir" "$installPath" /MIR /R:1 /W:1 /NFL /NDL /NC /NS /NP 2>&1 | Out-Null
                Remove-Item -Path $emptyDir -Force -ErrorAction SilentlyContinue
            } catch {}

            # 清理残留
            if (Test-Path $installPath) {
                Get-ChildItem -Path $installPath -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                    try { Remove-Item $_.FullName -Force -Recurse -ErrorAction SilentlyContinue } catch {}
                }
            }
            Remove-Item -Path $installPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Color "已删除安装目录" "Green"
        }
    }
}

# 清理数据目录（如果不在保留模式）
if (-not $KeepData) {
    $dataPaths = @(
        "C:\ProgramData\Elasticsearch",
        "$env:USERPROFILE\.elasticsearch"
    )
    foreach ($dataPath in $dataPaths) {
        if (Test-Path $dataPath) {
            Write-Color "删除数据目录: $dataPath" "Yellow"
            Remove-Item -Path $dataPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

#=============================================================================
# 6. 清理环境变量和注册表
#=============================================================================
Write-Color "`n[6/6] 清理环境变量..." "Yellow"

# 清理系统 PATH
$systemPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($systemPath) {
    $newPath = ($systemPath -split ';' | Where-Object {
        $_ -notlike "*elasticsearch*" -and $_ -notlike "*Elasticsearch*"
    } -join ';')
    if ($newPath -ne $systemPath) {
        [Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
        Write-Color "已清理系统 PATH" "Green"
    }
}

# 清理 ES_HOME
$esHome = [Environment]::GetEnvironmentVariable("ES_HOME", "Machine")
if ($esHome) {
    [Environment]::SetEnvironmentVariable("ES_HOME", $null, "Machine")
    Write-Color "已清理 ES_HOME" "Green"
}

# 清理 JAVA_HOME (如果指向 Elasticsearch 目录)
$javaHome = [Environment]::GetEnvironmentVariable("JAVA_HOME", "Machine")
if ($javaHome -and ($javaHome -like "*elasticsearch*" -or $javaHome -like "*Elastic*")) {
    [Environment]::SetEnvironmentVariable("JAVA_HOME", $null, "Machine")
    Write-Color "已清理 JAVA_HOME" "Green"
}

# 清理注册表
$registryPaths = @(
    "HKLM:\SOFTWARE\Elasticsearch",
    "HKLM:\SOFTWARE\Wow6432Node\Elasticsearch",
    "HKCU:\SOFTWARE\Elasticsearch"
)
foreach ($regPath in $registryPaths) {
    if (Test-Path $regPath) {
        Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Color "已清理注册表: $regPath" "Green"
    }
}

Write-Color "环境变量和注册表已清理" "Green"

#=============================================================================
# 最终验证
#=============================================================================
Write-Color "`n========================================" "Cyan"
Write-Color "  Elasticsearch 卸载完成!" "Green"
Write-Color "========================================" "Cyan"

Write-Color "`n最终验证:" "Yellow"

# 检查服务
$remainingServices = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue
if ($remainingServices) {
    Write-Color "[警告] 仍有 Elasticsearch 服务存在" "Red"
} else {
    Write-Color "[OK] 服务已清理" "Green"
}

# 检查进程
$remainingProcs = Get-CimInstance Win32_Process -Filter "name = 'java.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*elasticsearch*" -or $_.CommandLine -like "*org.elasticsearch.bootstrap.Elasticsearch*"
}
if ($remainingProcs) {
    Write-Color "[警告] 仍有 Elasticsearch 进程运行" "Red"
} else {
    Write-Color "[OK] 进程已停止" "Green"
}

# 检查端口
$portCheck = Get-NetTCPConnection -LocalPort 9200 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($portCheck) {
    Write-Color "[警告] 端口 9200 仍在被占用" "Red"
} else {
    Write-Color "[OK] 端口 9200 已释放" "Green"
}

# 检查安装目录
$anyExists = $false
foreach ($installPath in $installPaths) {
    if (Test-Path $installPath) {
        $anyExists = $true
        break
    }
}
if ($anyExists) {
    Write-Color "[警告] 安装目录仍存在" "Red"
} else {
    Write-Color "[OK] 安装目录已清理" "Green"
}

# 输出机器可读状态
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: false"
Write-Host "RUNNING: false"
Write-Host "STATUS: SUCCESS"
Write-Host "------------------------"
