[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$NuGetConfig = ".github\NuGet.Config",

    [switch]$KeepOutput
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "outputs\NoticeMemo-win-x64"))
$nugetConfig = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $NuGetConfig))
$project = Join-Path $repositoryRoot "src\NoticeMemo.App\NoticeMemo.App.csproj"

if (-not (Test-Path -LiteralPath $nugetConfig)) {
    throw "找不到 NuGet 配置：$nugetConfig"
}

if (-not $KeepOutput -and (Test-Path -LiteralPath $outputDirectory)) {
    $resolvedOutput = (Resolve-Path -LiteralPath $outputDirectory).Path
    if (-not $resolvedOutput.Equals($outputDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedOutput.StartsWith($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理仓库范围之外的目录：$resolvedOutput"
    }

    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

Push-Location $repositoryRoot
try {
    dotnet restore $project -r win-x64 --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore 失败。"
    }

    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败。"
    }

    $executable = Get-Item -LiteralPath (Join-Path $outputDirectory "NoticeMemo.exe")
    Write-Host ""
    Write-Host ("发布完成：{0}" -f $executable.FullName)
    Write-Host ("文件大小：{0:N1} MB" -f ($executable.Length / 1MB))
}
finally {
    Pop-Location
}

