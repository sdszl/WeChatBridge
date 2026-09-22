[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [string]$DotnetPath = "dotnet",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$packageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $packageRoot "WeChatBridge.Windows\WeChatBridge.Windows.csproj"
$manifestPath = Join-Path $PSScriptRoot "AppxManifest.xml"
$outputRootPath = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $packageRoot "artifacts\windows-package"
} else {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
$stagingPath = Join-Path $outputRootPath "staging"
$publishPath = Join-Path $stagingPath "publish"
$assetsPath = Join-Path $stagingPath "Assets"
$msixPath = Join-Path $outputRootPath "WeChatBridge.msix"
$certificateFullPath = $null

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $certificateFullPath = (Resolve-Path -LiteralPath $CertificatePath).Path
    if (-not (Test-Path -LiteralPath $certificateFullPath -PathType Leaf)) {
        throw "Certificate file not found: $CertificatePath"
    }
}

if (Test-Path -LiteralPath $outputRootPath) {
    $resolvedOutput = (Resolve-Path -LiteralPath $outputRootPath).Path
    $relativeOutput = [System.IO.Path]::GetRelativePath($packageRoot, $resolvedOutput)
    if ($relativeOutput -eq "." -or
        $relativeOutput.StartsWith("..", [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relativeOutput)) {
        throw "Refusing to clean output outside the Windows package directory: $resolvedOutput"
    }
    if ($null -ne $certificateFullPath) {
        $relativeCertificate = [System.IO.Path]::GetRelativePath($resolvedOutput, $certificateFullPath)
        if ($relativeCertificate -eq "." -or
            (-not $relativeCertificate.StartsWith("..", [System.StringComparison]::Ordinal)) -and
            (-not [System.IO.Path]::IsPathRooted($relativeCertificate))) {
            throw "CertificatePath must not be inside the cleaned output directory: $certificateFullPath"
        }
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

New-Item -ItemType Directory -Path $publishPath, $assetsPath -Force | Out-Null

$publishArgs = @(
    "publish",
    $projectPath,
    "--configuration", $Configuration,
    "--runtime", $Runtime,
    "--self-contained", ($(if ($SelfContained) { "true" } else { "false" })),
    "--output", $publishPath,
    "-p:PublishSingleFile=false",
    "-p:DebugType=None"
)
& $DotnetPath @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingPath "AppxManifest.xml") -Force
$iconPath = Join-Path $packageRoot "..\Resources\AppIcon.png"
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Application icon not found: $iconPath"
}
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetsPath "StoreLogo.png") -Force
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetsPath "Square150x150Logo.png") -Force
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetsPath "Square44x44Logo.png") -Force

$makeAppx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
    Sort-Object FullName |
    Select-Object -Last 1
if ($null -eq $makeAppx) {
    throw "makeappx.exe was not found. Install the Windows 10/11 SDK."
}

& $makeAppx.FullName pack /d $stagingPath /p $msixPath /nv
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName |
        Select-Object -Last 1
    if ($null -eq $signtool) {
        throw "signtool.exe was not found. Install the Windows 10/11 SDK."
    }

    $signArgs = @("sign", "/fd", "SHA256", "/a", "/f", $certificateFullPath)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArgs += @("/p", $CertificatePassword)
    }
    $signArgs += $msixPath
    & $signtool.FullName @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Windows package created: $msixPath"
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Warning "Package is unsigned. Sign it with a certificate whose subject matches CN=WeChatBridge Development before installing."
}
