param(
    [string]$Configuration = "Release",
    [string]$ProjectPath = "RemoteInstaller/RemoteInstaller.csproj",
    [string]$PublishDir,
    [string]$OutputDir = "artifacts/installer",
    [string]$IconDirectory = "RemoteInstaller/Assets/Brand",
    [string]$IconFileName = "zending.ico",
    [string]$IconPath,
    [string]$Version,
    [string]$AppName = "RemoteInstaller",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [string]$InnoCompilerPath,
    [switch]$Clean,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile
    )

    [xml]$projectXml = Get-Content -Path $ProjectFile -Raw
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Unable to read Version from project file: $ProjectFile"
    }

    return $versionNode.Trim()
}

function Resolve-DotnetCommand {
    $candidatePaths = @(
        "C:/Users/WY/.dotnet-10/dotnet.exe",
        "C:/Program Files/dotnet/dotnet.exe"
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path -Path $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        return $dotnetCommand.Source
    }

    throw "Unable to find dotnet executable. Install the .NET SDK or configure the dotnet path first."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Resolve-FullPath -Path $ProjectPath -BasePath $scriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "RemoteInstaller/bin/$Configuration/net10.0-windows/publish"
}

$publishDirectory = Resolve-FullPath -Path $PublishDir -BasePath $scriptRoot
$installerScript = Resolve-FullPath -Path "installer/installer.iss" -BasePath $scriptRoot
$outputDirectory = Resolve-FullPath -Path $OutputDir -BasePath $scriptRoot
$dotnetCommand = Resolve-DotnetCommand

# 默认使用仓库内的 ZENDING 图标，避免安装包构建依赖外部磁盘路径。
if ([string]::IsNullOrWhiteSpace($IconPath)) {
    $IconPath = Join-Path $IconDirectory $IconFileName
}

$iconFile = Resolve-FullPath -Path $IconPath -BasePath $scriptRoot

if (-not (Test-Path -Path $projectFile -PathType Leaf)) {
    throw "Project file not found: $projectFile"
}

if (-not (Test-Path -Path $installerScript -PathType Leaf)) {
    throw "Installer script not found: $installerScript"
}

if (-not (Test-Path -Path $iconFile -PathType Leaf)) {
    throw "Icon file not found: $iconFile"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -ProjectFile $projectFile
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $defaultCompilerPaths = @(
        "C:/Program Files (x86)/Inno Setup 6/ISCC.exe",
        "C:/Program Files/Inno Setup 6/ISCC.exe"
    )

    $InnoCompilerPath = $defaultCompilerPaths | Where-Object { Test-Path -Path $_ -PathType Leaf } | Select-Object -First 1
}
else {
    $InnoCompilerPath = Resolve-FullPath -Path $InnoCompilerPath -BasePath $scriptRoot
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -Path $InnoCompilerPath -PathType Leaf)) {
    throw "Inno Setup compiler ISCC.exe was not found. Specify it explicitly with -InnoCompilerPath."
}

if ($Clean) {
    if (Test-Path -Path $publishDirectory) {
        Remove-Item -Path $publishDirectory -Recurse -Force
    }

    if (Test-Path -Path $outputDirectory) {
        Remove-Item -Path $outputDirectory -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if (-not $SkipPublish) {
    $selfContainedValue = if ($SelfContained -eq $true) { 'true' } else { 'false' }
    & $dotnetCommand publish $projectFile -c $Configuration -r $Runtime --self-contained $selfContainedValue -o $publishDirectory "-p:ApplicationIcon=$iconFile"

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed."
    }
}

if (-not (Test-Path -Path $publishDirectory -PathType Container)) {
    throw "Publish directory not found: $publishDirectory"
}

$publishExecutable = Join-Path $publishDirectory "$AppName.exe"
if (-not (Test-Path -Path $publishExecutable -PathType Leaf)) {
    throw "Published application executable not found: $publishExecutable"
}

$arguments = @(
    "/DMyAppName=$AppName",
    "/DMyAppVersion=$Version",
    "/DMyAppSourceDir=$publishDirectory",
    "/DMyOutputDir=$outputDirectory",
    "/DMySetupIconFile=$iconFile",
    $installerScript
)

& $InnoCompilerPath @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

Write-Host "Installer package created: $outputDirectory" -ForegroundColor Green
