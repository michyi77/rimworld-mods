<#
  build.ps1 - 编译 IsoRim ProjectZ Windows（窗扩展）

  用法:
    .\build.ps1                 只编译
    .\build.cmd                 同上（绕过执行策略，双击也行）
    .\build.ps1 -Deploy         编译 + 部署到游戏 Mods 目录
    .\build.ps1 -LiveRefs       不冻结引用，直接对游戏目录编译（排查 API 变动时用）

  输出: .\1.7\Assemblies\IsoRimProjectZWindows.dll
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Deploy,
    [switch]$LiveRefs
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$WorkRoot = Split-Path $Root -Parent
$Proj = Join-Path $Root "Source\IsoRimProjectZWindows.csproj"
$RimDir = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"

# --- 让 dotnet 完全在项目内工作 -------------------------------------------------
# 不这么做会在两处失败：
#   1. dotnet 首次运行要创建 ~\.dotnet（沙箱/权限拒绝）
#   2. NuGet 要读 %APPDATA%\NuGet\NuGet.Config（读不到就整个 restore 失败）
$env:DOTNET_CLI_HOME                   = Join-Path $WorkRoot ".dotnet-home"
$env:NUGET_PACKAGES                    = Join-Path $WorkRoot ".nuget-packages"
$env:APPDATA                           = Join-Path $WorkRoot ".appdata"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT       = "1"
$env:DOTNET_NOLOGO                     = "1"
$env:MSBUILDDISABLENODEREUSE           = "1"
foreach ($d in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:APPDATA)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

function Find-Dotnet {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:DOTNET_ROOT) { $candidates.Add((Join-Path $env:DOTNET_ROOT "dotnet.exe")) }
    $candidates.Add((Join-Path $WorkRoot ".dotnet\dotnet.exe"))
    $candidates.Add("C:\Program Files\dotnet\dotnet.exe")
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

$dotnet = Find-Dotnet
if (-not $dotnet) {
    throw "找不到 dotnet.exe。需要 .NET 10 SDK。"
}

$sdks = @(& $dotnet --list-sdks 2>$null)
$hasNet10 = @($sdks | Where-Object { $_ -match '^\s*10\.' })
if ($hasNet10.Count -eq 0) {
    throw "有 dotnet 但没有 10.x SDK，编不了 net10.0。当前 SDK:`n$($sdks -join "`n")"
}

Write-Host "dotnet : $dotnet"
Write-Host "sdk    : $($hasNet10[0])"
Write-Host "proj   : $Proj"

$buildArgs = @(
    "build", $Proj, "-c", $Configuration,
    "-nologo", "-v", "minimal", "-nodeReuse:false",
    "-p:RestoreConfigFile=$(Join-Path $Root 'NuGet.config')"
)
if ($LiveRefs) {
    Write-Host "refs   : LIVE (game folder)" -ForegroundColor Yellow
    $buildArgs += "-p:RefsDir=$RimDir"
} else {
    Write-Host "refs   : frozen ($(Join-Path $Root 'refs'))"
}

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$dll = Join-Path $Root "1.7\Assemblies\IsoRimProjectZWindows.dll"
if (-not (Test-Path $dll)) { throw "编译返回成功但没找到输出: $dll" }
$item = Get-Item $dll
Write-Host ("OK  {0}  {1} KB  {2}" -f $item.FullName, [math]::Round($item.Length / 1KB), $item.LastWriteTime) -ForegroundColor Green

if ($Deploy) {
    & (Join-Path $Root "deploy.ps1")
}
