<#
  deploy.ps1 - 把 workspace 里的 mod 同步到游戏的 Mods 目录

  用法:
    .\deploy.ps1
    .\deploy.ps1 -Target "D:\somewhere\Mods\IsoRimProjectZ"

  注意:
    - 用 /E（只增不删），不会删掉目标目录里多出来的文件。
    - refs\、_incoming\、构建脚本永远不会被同步过去。
    - 目标在 Program Files 下，可能需要管理员权限。被拒绝就以管理员身份运行。
    - 游戏正在运行时 DLL 被占用，同步会失败。先退出游戏。
#>
[CmdletBinding()]
param(
    [string]$Target = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\IsoRimProjectZ-Windows"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Target = [System.IO.Path]::GetFullPath($Target)

if (-not (Test-Path $Root)) { throw "源目录不存在: $Root" }

# 目标不能落在源目录里面，否则 robocopy 会把自己复制进自己
$normRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
if ($Target.StartsWith($normRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "目标目录不能在源目录内部: $Target"
}

$excludeDirs = @(
    (Join-Path $Root "refs"),
    (Join-Path $Root "_incoming"),
    (Join-Path $Root "tools"),
    (Join-Path $Root "Source\obj"),
    (Join-Path $Root "Source\bin")
)
$excludeFiles = @("build.ps1", "build.cmd", "deploy.ps1", "NuGet.config")

Write-Host "src : $Root"
Write-Host "dst : $Target"

$running = @(Get-Process -Name "RimWorld" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "游戏正在运行（PID: $($running.Id -join ', ')），DLL 被占用。先退出游戏。"
}

if (-not (Test-Path $Target)) {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Write-Host "已创建目标目录" -ForegroundColor Yellow
}

$roboArgs = @($Root, $Target, "/E", "/NDL", "/NFL", "/NJH", "/NJS", "/NP",
              "/XD") + $excludeDirs + @("/XF") + $excludeFiles
& robocopy @roboArgs | Out-Null
$code = $LASTEXITCODE
if ($code -ge 8) { throw "robocopy 失败 (exit $code)" }

# 校验关键文件
$checks = @(
    "About\About.xml",
    "1.7\Assemblies\IsoRimProjectZWindows.dll",
    "LoadFolders.xml"
)
foreach ($c in $checks) {
    $p = Join-Path $Target $c
    if (Test-Path $p) {
        $f = Get-Item $p
        Write-Host ("  ok  {0,-36} {1,8} KB  {2}" -f $c, [math]::Round($f.Length / 1KB), $f.LastWriteTime)
    } else {
        Write-Host ("  !!  缺失: {0}" -f $c) -ForegroundColor Red
    }
}

Write-Host "部署完成。启动游戏前确认 ModsConfig.xml 里 peren.isorimprojectzwindows 在 peren.isorimprojectz 之后。" -ForegroundColor Green
