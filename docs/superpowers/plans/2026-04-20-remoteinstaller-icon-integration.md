# RemoteInstaller Icon Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建一套可复用的 RemoteInstaller 项目图标资源，生成包含 `16/24/32/48/64/128/256` 尺寸的 Windows `.ico`，并接入 WPF 主程序与 Inno Setup 安装器。

**Architecture:** 使用一个仓库内的 PowerShell 图标生成脚本作为单一真相源，按已确认的“科技蓝圆角方形底板 + 服务器机柜 + `>_` 终端符号”设计直接绘制多尺寸位图，并打包为多帧 `.ico`。生成后的 `.ico` 被主程序 `ApplicationIcon` 和安装器 `SetupIconFile` 共同复用，避免维护两套图标资产。

**Tech Stack:** PowerShell、System.Drawing、.ico 多帧格式、WPF `.csproj`、Inno Setup

---

## File Map

- Create: `tools/verify-app-icon.ps1` — 校验图标文件、尺寸集合、主程序接线和安装器接线
- Create: `tools/generate-app-icon.ps1` — 生成预览 PNG 和多帧 `.ico`
- Create: `RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico` — 应用与安装器共用图标
- Create: `RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png` — 256x256 预览图，便于肉眼确认
- Modify: `RemoteInstaller/RemoteInstaller.csproj:3-16` — 设置 `ApplicationIcon`
- Modify: `installer/installer.iss:13-37` — 设置 `SetupIconFile`
- Modify: `README.md:111-129` — 记录图标文件位置与重新生成命令

### Task 1: 建立图标校验脚本

**Files:**
- Create: `tools/verify-app-icon.ps1`
- Test: `tools/verify-app-icon.ps1`

- [ ] **Step 1: 创建失败优先的图标校验脚本**

```powershell
$repoRoot = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $repoRoot "RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico"
$previewPath = Join-Path $repoRoot "RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png"
$csprojPath = Join-Path $repoRoot "RemoteInstaller/RemoteInstaller.csproj"
$issPath = Join-Path $repoRoot "installer/installer.iss"

$errors = New-Object System.Collections.Generic.List[string]
$expectedSizes = @(16, 24, 32, 48, 64, 128, 256)

if (-not (Test-Path -Path $iconPath -PathType Leaf)) {
    $errors.Add("缺少图标文件: $iconPath")
}

if (-not (Test-Path -Path $previewPath -PathType Leaf)) {
    $errors.Add("缺少 256 预览图: $previewPath")
}

if (Test-Path -Path $iconPath -PathType Leaf) {
    $bytes = [System.IO.File]::ReadAllBytes($iconPath)
    if ($bytes.Length -lt 256) {
        $errors.Add("图标文件过小，疑似未正确生成。")
    }
    else {
        $imageCount = [BitConverter]::ToUInt16($bytes, 4)
        if ($imageCount -ne $expectedSizes.Count) {
            $errors.Add("图标帧数不正确，期望 $($expectedSizes.Count)，实际 $imageCount。")
        }
        else {
            $actualSizes = @()
            for ($index = 0; $index -lt $imageCount; $index++) {
                $entryOffset = 6 + ($index * 16)
                $width = if ($bytes[$entryOffset] -eq 0) { 256 } else { [int]$bytes[$entryOffset] }
                $height = if ($bytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$bytes[$entryOffset + 1] }
                if ($width -ne $height) {
                    $errors.Add("发现非正方形图标帧: ${width}x${height}")
                }
                $actualSizes += $width
            }

            if (@($actualSizes | Sort-Object) -join ',' -ne @($expectedSizes | Sort-Object) -join ',') {
                $errors.Add("图标尺寸集合不正确，实际为: $($actualSizes -join ', ')")
            }
        }
    }
}

$csprojContent = Get-Content -Path $csprojPath -Raw
if ($csprojContent -notmatch '<ApplicationIcon>Assets\\Brand\\remoteinstaller-icon\.ico</ApplicationIcon>') {
    $errors.Add('RemoteInstaller.csproj 未配置 ApplicationIcon。')
}

$issContent = Get-Content -Path $issPath -Raw
if ($issContent -notmatch 'SetupIconFile=\{#MySetupIconFile\}') {
    $errors.Add('installer.iss 未配置 SetupIconFile。')
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'RemoteInstaller 图标资源与接线校验通过。' -ForegroundColor Green
```

- [ ] **Step 2: 运行校验脚本，确认当前状态失败**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\tools\verify-app-icon.ps1"
```

Expected: FAIL，至少包含以下错误之一：
- `缺少图标文件`
- `缺少 256 预览图`
- `RemoteInstaller.csproj 未配置 ApplicationIcon`
- `installer.iss 未配置 SetupIconFile`

- [ ] **Step 3: 如果当前会话允许提交，提交校验脚本骨架**

```bash
git add tools/verify-app-icon.ps1
git commit -m "test: add icon asset verification script"
```

### Task 2: 生成图标资源文件

**Files:**
- Create: `tools/generate-app-icon.ps1`
- Create: `RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico`
- Create: `RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png`
- Test: `tools/verify-app-icon.ps1`

- [ ] **Step 1: 创建图标生成脚本**

```powershell
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $repoRoot "RemoteInstaller/Assets/Brand"
$icoPath = Join-Path $assetDir "remoteinstaller-icon.ico"
$previewPath = Join-Path $assetDir "remoteinstaller-icon-256.png"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

New-Item -ItemType Directory -Path $assetDir -Force | Out-Null

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

function Write-IcoFile {
    param(
        [System.Drawing.Bitmap[]]$Bitmaps,
        [string]$Path
    )

    $frames = foreach ($bitmap in $Bitmaps) {
        [PSCustomObject]@{
            Size = $bitmap.Width
            Bytes = Convert-BitmapToPngBytes -Bitmap $bitmap
        }
    }

    $iconStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($iconStream)

    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $dataOffset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimensionByte = if ($frame.Size -ge 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimensionByte)
        $writer.Write($dimensionByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Bytes.Length)
        $writer.Write([UInt32]$dataOffset)
        $dataOffset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame.Bytes)
    }

    [System.IO.File]::WriteAllBytes($Path, $iconStream.ToArray())
    $writer.Dispose()
    $iconStream.Dispose()
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $blueLight = [System.Drawing.ColorTranslator]::FromHtml('#64B5F6')
    $blueMain = [System.Drawing.ColorTranslator]::FromHtml('#1E88E5')
    $blueDark = [System.Drawing.ColorTranslator]::FromHtml('#1565C0')
    $white = [System.Drawing.Color]::FromArgb(248, 255, 255, 255)

    $backgroundRect = [System.Drawing.RectangleF]::new($Size * 0.06, $Size * 0.06, $Size * 0.88, $Size * 0.88)
    $backgroundPath = New-RoundedRectPath -X $backgroundRect.X -Y $backgroundRect.Y -Width $backgroundRect.Width -Height $backgroundRect.Height -Radius ($Size * 0.18)
    $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($backgroundRect, $blueLight, $blueDark, 90.0)
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $serverRect = [System.Drawing.RectangleF]::new($Size * 0.28, $Size * 0.18, $Size * 0.44, $Size * 0.54)
    $serverPath = New-RoundedRectPath -X $serverRect.X -Y $serverRect.Y -Width $serverRect.Width -Height $serverRect.Height -Radius ($Size * 0.06)
    $serverBrush = New-Object System.Drawing.SolidBrush($white)
    $graphics.FillPath($serverBrush, $serverPath)

    $lineBrush = New-Object System.Drawing.SolidBrush($blueLight)
    foreach ($topFactor in @(0.29, 0.40, 0.51)) {
        $barRect = [System.Drawing.RectangleF]::new($Size * 0.36, $Size * $topFactor, $Size * 0.28, [Math]::Max(1, $Size * 0.04))
        $graphics.FillRectangle($lineBrush, $barRect)
    }

    $badgeRect = [System.Drawing.RectangleF]::new($Size * 0.39, $Size * 0.50, $Size * 0.26, $Size * 0.17)
    $badgePath = New-RoundedRectPath -X $badgeRect.X -Y $badgeRect.Y -Width $badgeRect.Width -Height $badgeRect.Height -Radius ($Size * 0.04)
    $badgeBrush = New-Object System.Drawing.SolidBrush($blueMain)
    $graphics.FillPath($badgeBrush, $badgePath)

    $glyphPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.6, $Size * 0.045))
    $glyphPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $glyphPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($glyphPen, @(
        [System.Drawing.PointF]::new($Size * 0.44, $Size * 0.555),
        [System.Drawing.PointF]::new($Size * 0.49, $Size * 0.585),
        [System.Drawing.PointF]::new($Size * 0.44, $Size * 0.615)
    ))
    $graphics.DrawLine($glyphPen, $Size * 0.52, $Size * 0.615, $Size * 0.58, $Size * 0.615)

    $outlinePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(40, 255, 255, 255), [Math]::Max(1, $Size * 0.015))
    $graphics.DrawPath($outlinePen, $backgroundPath)

    $backgroundBrush.Dispose()
    $serverBrush.Dispose()
    $lineBrush.Dispose()
    $badgeBrush.Dispose()
    $glyphPen.Dispose()
    $outlinePen.Dispose()
    $backgroundPath.Dispose()
    $serverPath.Dispose()
    $badgePath.Dispose()
    $graphics.Dispose()

    return $bitmap
}

$bitmaps = foreach ($size in $sizes) {
    New-IconBitmap -Size $size
}

[System.IO.File]::WriteAllBytes($previewPath, (Convert-BitmapToPngBytes -Bitmap $bitmaps[-1]))
Write-IcoFile -Bitmaps $bitmaps -Path $icoPath

foreach ($bitmap in $bitmaps) {
    $bitmap.Dispose()
}

Write-Host "图标已生成: $icoPath" -ForegroundColor Green
Write-Host "预览图已生成: $previewPath" -ForegroundColor Green
```

- [ ] **Step 2: 运行图标生成脚本，生成 `.ico` 与 256 预览图**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\tools\generate-app-icon.ps1"
```

Expected:
- 输出 `图标已生成`
- 生成 `RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico`
- 生成 `RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png`

- [ ] **Step 3: 再次运行校验脚本，确认只剩接线相关错误**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\tools\verify-app-icon.ps1"
```

Expected: FAIL，但错误应收敛为：
- `RemoteInstaller.csproj 未配置 ApplicationIcon`
- `installer.iss 未配置 SetupIconFile`

- [ ] **Step 4: 如果当前会话允许提交，提交图标生成器与资源**

```bash
git add tools/generate-app-icon.ps1 RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png
git commit -m "feat: add generated application icon assets"
```

### Task 3: 将图标接入主程序、安装器与文档

**Files:**
- Modify: `RemoteInstaller/RemoteInstaller.csproj:3-16`
- Modify: `installer/installer.iss:13-37`
- Modify: `README.md:111-129`
- Test: `tools/verify-app-icon.ps1`

- [ ] **Step 1: 在主程序项目文件中设置 `ApplicationIcon`**

将 `RemoteInstaller/RemoteInstaller.csproj` 的主属性段调整为：

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <UseWPF>true</UseWPF>
  <ImplicitUsings>enable</ImplicitUsings>
  <RootNamespace>RemoteInstaller</RootNamespace>
  <AssemblyName>RemoteInstaller</AssemblyName>
  <StartupObject>RemoteInstaller.App</StartupObject>
  <Version>1.0.0</Version>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <ApplicationIcon>Assets\Brand\remoteinstaller-icon.ico</ApplicationIcon>
  <Authors>Leon</Authors>
  <Description>跨平台服务器管理工具 - 远程安装应用</Description>
</PropertyGroup>
```

- [ ] **Step 2: 在安装器脚本中设置 `SetupIconFile`**

将 `installer/installer.iss` 的头部定义和 `[Setup]` 段调整为：

```iss
#ifndef MyOutputDir
  #define MyOutputDir "..\\artifacts\\installer"
#endif

#define MyLicenseFile "..\\LICENSE"
#define MySetupIconFile "..\\RemoteInstaller\\Assets\\Brand\\remoteinstaller-icon.ico"
#define MyAppPublisher "Leon"
#define MyAppExeName MyAppName + ".exe"

[Setup]
AppId={{A1572115-7D3E-4D10-A5B4-AE5C47D9E701}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
LicenseFile={#MyLicenseFile}
SetupIconFile={#MySetupIconFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
```

- [ ] **Step 3: 在 README 中补充图标资源位置与重新生成命令**

将 `README.md` 安装包说明段中的相关部分补充为：

```md
- 安装内容会保留发布目录原始结构，包含 `Assets/`、`Scripts/` 和 `Scripts/app-configuration.json`
- 安装器默认安装到当前用户目录下的 `AppData/Local/Programs/RemoteInstaller`，避免写入 `data.db` 时受到 `Program Files` 权限限制
- 安装器会显示根目录 `LICENSE` 文件中的 MIT 许可证正文
- 项目图标文件使用 `RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico`
- 如需重新生成图标，可执行 `powershell.exe -ExecutionPolicy Bypass -File ".\\tools\\generate-app-icon.ps1"`
- 如果自定义 `-InnoCompilerPath`、`-PublishDir` 或 `-OutputDir`，建议传绝对路径
```

- [ ] **Step 4: 运行校验脚本，确认全部通过**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\tools\verify-app-icon.ps1"
```

Expected: PASS，输出 `RemoteInstaller 图标资源与接线校验通过。`

- [ ] **Step 5: 如果当前会话允许提交，提交图标接线与文档**

```bash
git add RemoteInstaller/RemoteInstaller.csproj installer/installer.iss README.md
git commit -m "feat: wire application icon into app and installer"
```

### Task 4: 执行最终冒烟验证

**Files:**
- Test: `tools/verify-app-icon.ps1`
- Test: `RemoteInstaller/RemoteInstaller.csproj`
- Test: `installer/installer.iss`

- [ ] **Step 1: 构建主程序，确认图标资源不会破坏编译**

Run:

```bash
dotnet build "C:/projects/远程安装应用/RemoteInstaller/RemoteInstaller.csproj"
```

Expected: `Build succeeded.`

- [ ] **Step 2: 再次运行图标校验脚本**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\tools\verify-app-icon.ps1"
```

Expected: PASS，尺寸集合为 `16,24,32,48,64,128,256`

- [ ] **Step 3: 如果本机已安装 Inno Setup，编译安装器以确认图标已接入**

Run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\build-installer.ps1" -SkipPublish
```

Expected:
- 如果已安装 Inno Setup：成功生成安装包，安装包使用新的应用图标
- 如果未安装 Inno Setup：脚本明确提示 `未找到 Inno Setup 编译器 ISCC.exe`

- [ ] **Step 4: 人工确认图标显示效果**

检查以下位置：

```text
1. RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png
2. RemoteInstaller.exe 在资源管理器中的图标
3. 生成的安装包在资源管理器中的图标
4. 安装后桌面快捷方式图标
```

Expected:
- 图标主体清晰可识别
- 蓝色底板、服务器块面、`>_` 终端元素均可见
- 32x32 与 48x48 下不糊成一团

- [ ] **Step 5: 如果当前会话允许提交，提交最终验证结果**

```bash
git add tools/verify-app-icon.ps1 tools/generate-app-icon.ps1 RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png RemoteInstaller/RemoteInstaller.csproj installer/installer.iss README.md
git commit -m "feat: add branded icon for app and installer"
```

## Self-Review

- Spec coverage: 设计说明中的语义、色彩方向、ICO 多尺寸输出、安装器接入、应用接入、显示验证都已经覆盖
- Placeholder scan: 本计划未包含 `TODO`、`TBD` 或“后续再补”的实现性占位语句
- Type consistency: 统一使用 `remoteinstaller-icon.ico`、`remoteinstaller-icon-256.png`、`tools/generate-app-icon.ps1`、`tools/verify-app-icon.ps1` 这组名称，无跨任务命名漂移
