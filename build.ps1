# Jable 安卓版编译脚本
#   .\build.ps1            给 Android Studio 运行 / 性能分析用（手机 + 模拟器两种 CPU）→ android-studio\Jable.apk
#   .\build.ps1 release    正式安装包（只有手机的 64 位 ARM）→ release\Jable-Android-<版本>.apk
#   .\build.ps1 emulator   快速调试版（只给电脑上的模拟器，几十秒编完，不做提前编译）
param([ValidateSet('studio', 'release', 'emulator')][string]$Mode = 'studio')

$ErrorActionPreference = 'Stop'
$dev = 'C:\Users\Mindray\AndroidDev'
$env:DOTNET_ROOT = "$dev\dotnet"
$dotnet = "$dev\dotnet\dotnet.exe"
$root = $PSScriptRoot
$project = "$root\src\JableLens.Mobile\JableLens.Mobile.csproj"
$sdk = @("-p:AndroidSdkDirectory=$dev\android-sdk", "-p:JavaSdkDirectory=$dev\jdk17")

if ($Mode -eq 'emulator') {
    & $dotnet build $project -c Debug -p:Emulator=true @sdk
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host "完成：$root\src\JableLens.Mobile\bin\Debug\net10.0-android\com.jable.app-Signed.apk"
    exit 0
}

# 正式签名（每次都必须用同一个密钥，否则手机拒绝覆盖安装）
$pw = (Get-Content "$dev\jable-release.password" -Raw).Trim()
$sign = @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$dev\jable-release.keystore", '-p:AndroidSigningKeyAlias=jable',
    "-p:AndroidSigningKeyPass=$pw", "-p:AndroidSigningStorePass=$pw")
$out = "$root\artifacts\$Mode"
$extra = if ($Mode -eq 'studio') { @('-p:Studio=true') } else { @() }

& $dotnet publish $project -c Release @extra @sdk @sign -o $out
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$apk = "$out\com.jable.app-Signed.apk"
if ($Mode -eq 'studio') {
    $target = "$root\android-studio\Jable.apk"
} else {
    $version = ([xml](Get-Content $project)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
    $target = "$root\release\Jable-Android-$version.apk"
}
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Copy-Item $apk $target -Force
Write-Host "完成：$target"
