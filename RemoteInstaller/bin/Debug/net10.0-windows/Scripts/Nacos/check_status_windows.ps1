# =================================================================
# Nacos 状态检测脚本 (Windows)
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8

# 颜色输出函数
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Write-Progress {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage:$Percent"
}

function Check-Port {
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

Write-Color "========================================" "Cyan"
Write-Color "      Nacos 状态检测" "Cyan"
Write-Color "========================================" "Cyan"

$isInstalled = "false"
$isRunning = "false"
$version = "未知"
$HTTP_PORT = 8848
$RAFT_PORT = 9848
$GRPC_PORT = 9849

# 1. 检查安装情况
Write-Progress "CheckingInstallation" 10
Write-Color "`n1. 检查安装情况:" "Yellow"

# 检查安装目录
$commonInstallPaths = @("C:\nacos", "D:\nacos", "C:\Program Files\nacos")
foreach ($path in $commonInstallPaths) {
    if (Test-Path "$path\conf\application.properties") {
        Write-Color "Nacos 安装目录存在：$path" "Green"
        $isInstalled = "true"
        $NacosDir = $path
        break
    }
}

# 检查 Java 命令
$javaCommand = Get-Command java -ErrorAction SilentlyContinue
if ($javaCommand) {
    Write-Color "Java 命令存在：$($javaCommand.Source)" "Green"
    if (-not $isInstalled) {
        $isInstalled = "true"
    }
}

if (-not $isInstalled) {
    Write-Color "Nacos 已安装：否" "Red"
}

# 2. 检查进程
Write-Progress "CheckingProcesses" 30
Write-Color "`n2. 检查运行进程:" "Yellow"

$nacosProcesses = Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -like "*nacos.nacos*" -or $_.CommandLine -like "*nacos-server.jar*"
}

if ($nacosProcesses) {
    Write-Color "Nacos 进程：发现" "Green"
    foreach ($proc in $nacosProcesses) {
        Write-Color "  PID: $($proc.ProcessId)" "Gray"
        Write-Color "  命令行：$($proc.CommandLine)" "Gray"
    }
    $isRunning = "true"
} else {
    # 检查 java 进程
    $javaProcesses = Get-Process -Name "java" -ErrorAction SilentlyContinue
    if ($javaProcesses) {
        Write-Color "Java 进程：发现" "Yellow"
        foreach ($proc in $javaProcesses) {
            try {
                $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)").CommandLine
                if ($cmdLine -and $cmdLine -like "*nacos*") {
                    Write-Color "  Nacos Java 进程 PID: $($proc.Id)" "Green"
                    $isRunning = "true"
                }
            } catch {
                # 忽略访问拒绝
            }
        }
    } else {
        Write-Color "Nacos 运行状态：未运行" "Red"
    }
}

# 3. 检查端口
Write-Progress "CheckingPorts" 50
Write-Color "`n3. 检查端口监听:" "Yellow"

$httpOpen = Check-Port $HTTP_PORT "HTTP"
$raftOpen = Check-Port $RAFT_PORT "Raft"
$grpcOpen = Check-Port $GRPC_PORT "gRPC"

if ($httpOpen -or $raftOpen -or $grpcOpen) {
    $isRunning = "true"
    Write-Color "端口检测表明 Nacos 运行中" "Green"
}

# 4. HTTP 健康检查
Write-Progress "CheckingHealth" 65
Write-Color "`n4. HTTP 健康检查:" "Yellow"

if ($httpOpen) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$HTTP_PORT/nacos/" -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue
        if ($response -and ($response.StatusCode -eq 200 -or $response.StatusCode -eq 302)) {
            Write-Color "HTTP 健康状态：正常 (HTTP $($response.StatusCode))" "Green"
            $isRunning = "true"
        } else {
            Write-Color "HTTP 健康状态：异常 (HTTP $($response.StatusCode))" "Yellow"
        }
    } catch {
        Write-Color "HTTP 健康状态：无法访问" "Red"
    }
} else {
    Write-Color "HTTP 健康状态：端口未监听" "Red"
}

# 5. 远程访问检查
Write-Progress "CheckingRemoteAccess" 75
Write-Color "`n5. 远程访问检查:" "Yellow"

if ($httpOpen) {
    Write-Color "远程访问状态：可用" "Green"
} else {
    Write-Color "远程访问状态：不可用 (服务未运行)" "Red"
}

# 6. 版本检查
Write-Progress "CheckingVersion" 85
Write-Color "`n6. 版本检查:" "Yellow"

if ($isInstalled -and (Test-Path "$NacosDir\lib")) {
    $versionFiles = Get-ChildItem -Path "$NacosDir\lib" -Filter "nacos-server-*.jar" -ErrorAction SilentlyContinue
    if ($versionFiles) {
        $versionFile = $versionFiles[0].Name
        $version = $versionFile -replace 'nacos-server-', '' -replace '\.jar', ''
        Write-Color "Nacos 版本：$version" "Green"
    }
}

# 输出机器可读状态
Write-Progress "OutputtingStatus" 95
Write-Color "`n--- MACHINE READABLE ---" "Cyan"
Write-Host "INSTALLED: $isInstalled"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $isRunning"
Write-Host "PORT: $HTTP_PORT,$RAFT_PORT,$GRPC_PORT"
Write-Host "------------------------"

# 最终状态摘要
Write-Color "`n 最终状态:" "Yellow"
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
