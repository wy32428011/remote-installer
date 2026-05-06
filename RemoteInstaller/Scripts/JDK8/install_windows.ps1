$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$PackagePath = "",
    [string]$InstallPath = "C:\Program Files\JDK8",
    [switch]$SetAsDefault
)

function Write-ProgressInfo {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage`:$Percent"
}

function Get-JavaVersionString {
    param([string]$JavaExe)
    & $JavaExe -version 2>&1 | Select-Object -First 1
}

function Resolve-JavaHomeFromDirectory {
    param([string]$SourcePath)

    $directJava = Join-Path $SourcePath "bin\java.exe"
    if (Test-Path $directJava) {
        return $SourcePath
    }

    $nestedJava = Get-ChildItem -Path $SourcePath -Recurse -Filter java.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\bin\java.exe' } |
        Select-Object -First 1
    if ($nestedJava) {
        return Split-Path (Split-Path $nestedJava.FullName -Parent) -Parent
    }

    return $null
}

function Get-InstalledJavaHome {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and (Test-Path $PreferredPath)) {
        $resolved = Resolve-JavaHomeFromDirectory -SourcePath $PreferredPath
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    $machineJavaHome = [Environment]::GetEnvironmentVariable("JAVA_HOME", "Machine")
    if (-not [string]::IsNullOrWhiteSpace($machineJavaHome)) {
        $machineJava = Join-Path $machineJavaHome "bin\java.exe"
        if (Test-Path $machineJava) {
            return $machineJavaHome
        }
    }

    $javaCommand = Get-Command java -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($javaCommand -and $javaCommand.Source) {
        return Split-Path (Split-Path $javaCommand.Source -Parent) -Parent
    }

    return $PreferredPath
}

function Copy-JdkDirectory {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    $resolvedSourcePath = Resolve-JavaHomeFromDirectory -SourcePath $SourcePath
    if ([string]::IsNullOrWhiteSpace($resolvedSourcePath)) {
        Write-Error "未能从目录中定位 java.exe"
        exit 1
    }

    Get-ChildItem -Path $resolvedSourcePath -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $TargetPath $_.Name) -Recurse -Force
    }
}

Write-ProgressInfo "Initializing" 5
Write-Host "JDK 8 Windows 安装脚本开始..."

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "请以管理员身份运行此脚本"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($PackagePath) -or -not (Test-Path $PackagePath)) {
    Write-Error "未提供 JDK 8 离线安装资源"
    exit 1
}

Write-ProgressInfo "Preparing" 15
if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

Write-ProgressInfo "Extracting" 35
if (Test-Path $PackagePath -PathType Container) {
    Copy-JdkDirectory -SourcePath $PackagePath -TargetPath $InstallPath
}
else {
    $extension = [System.IO.Path]::GetExtension($PackagePath).ToLowerInvariant()
    switch ($extension) {
        ".zip" {
            Expand-Archive -Path $PackagePath -DestinationPath $InstallPath -Force
            $firstLevelDirs = @(Get-ChildItem $InstallPath -Directory -ErrorAction SilentlyContinue)
            if ($firstLevelDirs.Count -eq 1) {
                $sourceDir = $firstLevelDirs[0].FullName
                Get-ChildItem $sourceDir -Force | ForEach-Object {
                    Move-Item -Path $_.FullName -Destination (Join-Path $InstallPath $_.Name) -Force
                }
                Remove-Item -Path $sourceDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        ".msi" {
            $arguments = @('/i', ('"{0}"' -f $PackagePath), '/qn', '/norestart', ('INSTALLDIR="{0}"' -f $InstallPath))
            $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru
            if ($process.ExitCode -ne 0) {
                Write-Error "msi 安装失败，退出码：$($process.ExitCode)"
                exit 1
            }
        }
        ".exe" {
            $silentArgumentSets = @(
                @('/S'),
                @('/silent'),
                @('/verysilent'),
                @('/quiet'),
                @('/S', ('/D={0}' -f $InstallPath)),
                @('/silent', ('INSTALLDIR="{0}"' -f $InstallPath)),
                @('/verysilent', ('INSTALLDIR="{0}"' -f $InstallPath)),
                @('/quiet', ('INSTALLDIR="{0}"' -f $InstallPath)),
                @('/quiet', ('/D={0}' -f $InstallPath))
            )

            $installed = $false
            $lastExitCode = $null
            foreach ($argumentSet in $silentArgumentSets) {
                $process = Start-Process -FilePath $PackagePath -ArgumentList $argumentSet -Wait -PassThru -ErrorAction SilentlyContinue
                if ($process) {
                    $lastExitCode = $process.ExitCode
                    if ($process.ExitCode -eq 0) {
                        $installed = $true
                        break
                    }
                }
            }

            if (-not $installed) {
                Write-Error "exe 安装失败，未能通过常见静默参数完成安装，最后退出码：$lastExitCode"
                exit 1
            }
        }
        default {
            Write-Error "仅支持目录、zip、msi、exe 格式的 JDK 8 Windows 离线资源"
            exit 1
        }
    }
}

$resolvedInstallPath = Get-InstalledJavaHome -PreferredPath $InstallPath
$javaExe = Join-Path $resolvedInstallPath "bin\java.exe"
if (-not (Test-Path $javaExe)) {
    Write-Error "安装后未找到 java.exe"
    exit 1
}

Write-ProgressInfo "Configuring" 65
[Environment]::SetEnvironmentVariable("JDK8_HOME", $resolvedInstallPath, "Machine")
if ($SetAsDefault.IsPresent) {
    [Environment]::SetEnvironmentVariable("JAVA_HOME", $resolvedInstallPath, "Machine")
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $jdkBin = Join-Path $resolvedInstallPath "bin"
    if (-not (($machinePath -split ';') -contains $jdkBin)) {
        [Environment]::SetEnvironmentVariable("Path", ($machinePath.TrimEnd(';') + ";" + $jdkBin), "Machine")
    }
}

Write-ProgressInfo "Verifying" 85
$versionLine = Get-JavaVersionString -JavaExe $javaExe
if ($versionLine -notmatch '1\.8|8\.') {
    Write-Error "检测到的版本不是 JDK 8：$versionLine"
    exit 1
}

Write-ProgressInfo "Complete" 100
Write-Host "INSTALLED:true"
Write-Host "VERSION:$versionLine"
Write-Host "RUNNING:inactive"
Write-Host "JAVA_HOME:$resolvedInstallPath"
