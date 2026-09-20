param([switch]$SkipFrontend)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.8이 필요합니다.' }
$sdk = Join-Path $projectRoot '.tools\webview2'
if (!(Test-Path -LiteralPath "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll")) {
    New-Item -ItemType Directory -Force -Path '.tools' | Out-Null
    Invoke-WebRequest -UseBasicParsing 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3650.58/microsoft.web.webview2.1.0.3650.58.nupkg' -OutFile '.tools\webview2.zip'
    Expand-Archive -LiteralPath '.tools\webview2.zip' -DestinationPath $sdk -Force
}
if (!$SkipFrontend) {
    if (!(Test-Path -LiteralPath 'node_modules\esbuild')) {
        if (Get-Command npm.cmd -ErrorAction SilentlyContinue) { & npm.cmd ci --no-audit --no-fund }
        elseif (Test-Path -LiteralPath '.tools\package\bin\npm-cli.js') { & node '.tools\package\bin\npm-cli.js' ci --no-audit --no-fund }
        else { throw 'Node.js와 npm을 설치한 다음 npm ci를 실행해 주세요.' }
        if ($LASTEXITCODE -ne 0) { throw '의존성 설치 실패' }
    }
    & node scripts/build.mjs
    if ($LASTEXITCODE -ne 0) { throw '프런트엔드 빌드 실패' }
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'scripts\generate-icon.ps1'
if ($LASTEXITCODE -ne 0) { throw '아이콘 생성 실패' }
$remoteTools = Join-Path $projectRoot '.tools\remote'
$cloudflared = Join-Path $remoteTools 'cloudflared.exe'
$cloudflaredLicense = Join-Path $remoteTools 'CLOUDFLARED-LICENSE.txt'
$cloudflaredVersion = '2026.9.1'
$cloudflaredHash = '2837888cc0f5d58f15b6dc478376de90b4d3ba5241c7947455d1e0a0df429712'
New-Item -ItemType Directory -Force -Path $remoteTools | Out-Null
if (!(Test-Path -LiteralPath $cloudflared)) {
    Invoke-WebRequest -UseBasicParsing "https://github.com/cloudflare/cloudflared/releases/download/$cloudflaredVersion/cloudflared-windows-amd64.exe" -OutFile $cloudflared
}
$actualCloudflaredHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cloudflared).Hash.ToLowerInvariant()
if ($actualCloudflaredHash -ne $cloudflaredHash) { throw "cloudflared SHA256 검증 실패: $actualCloudflaredHash" }
if (!(Test-Path -LiteralPath $cloudflaredLicense)) {
    Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/cloudflare/cloudflared/$cloudflaredVersion/LICENSE" -OutFile $cloudflaredLicense
}
New-Item -ItemType Directory -Force -Path 'release\Orbit\web' | Out-Null
Copy-Item -Path 'dist\*' -Destination 'release\Orbit\web' -Recurse -Force
Copy-Item -LiteralPath "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll","$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$sdk\runtimes\win-x64\native\WebView2Loader.dll" -Destination 'release\Orbit' -Force
$sources = Get-ChildItem -LiteralPath native -Filter '*.cs' | ForEach-Object FullName
# Version. A release build (CI sets APP_VERSION from the tag) uses exactly that. A local build reports the latest release tag it is based on; with
# commits or changes on top of the tag it gets revision 1, shown as "0.2.15+". Without git or tags, native\AssemblyInfo.cs is used as it is.
$version = $null; $describe = 'APP_VERSION'
if ($env:APP_VERSION -match '^\d+\.\d+\.\d+$') { $version = "$($env:APP_VERSION).0" }
else {
    try { $describe = (& git describe --tags --long --dirty --match 'v*.*.*' 2>$null) } catch { $describe = $null }
    if ($LASTEXITCODE -eq 0 -and $describe -match '^v(\d+)\.(\d+)\.(\d+)-(\d+)-g[0-9a-f]+(-dirty)?$') {
        $ahead = if ([int]$Matches[4] -gt 0 -or $Matches[5]) { 1 } else { 0 }
        $version = "$($Matches[1]).$($Matches[2]).$($Matches[3]).$ahead"
    }
}
if ($version) {
    $stamp = Join-Path $env:TEMP 'OrbitAssemblyInfo.generated.cs'
    Set-Content -LiteralPath $stamp -Encoding ASCII -Value @('using System.Reflection;', '[assembly: AssemblyTitle("Orbit")]', '[assembly: AssemblyProduct("Orbit Agent Workspace")]', "[assembly: AssemblyVersion(`"$version`")]", "[assembly: AssemblyFileVersion(`"$version`")]")
    $sources = @($sources | Where-Object { (Split-Path -Leaf $_) -ne 'AssemblyInfo.cs' }) + $stamp
    Write-Host "버전: $version ($describe)"
}
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+', '/win32icon:assets\orbit.ico','/resource:assets\orbit.ico,Orbit.Icon','/out:release\Orbit\Orbit.exe','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Security.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',"/reference:$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll","/reference:$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll") + $sources
& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '네이티브 빌드 실패' }
Copy-Item -LiteralPath 'native\Orbit.exe.config' -Destination 'release\Orbit' -Force
# orbit-secret: the command an AI runs inside an Orbit terminal to use stored API keys (put on that terminal's PATH).
New-Item -ItemType Directory -Force -Path 'release\Orbit\bin' | Out-Null
& $csc /nologo /target:exe /optimize+ '/out:release\Orbit\bin\orbit-secret.exe' /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll 'native\cli\OrbitSecret.cs'
if ($LASTEXITCODE -ne 0) { throw 'orbit-secret 빌드 실패' }
New-Item -ItemType Directory -Force -Path 'release\Orbit\licenses' | Out-Null
New-Item -ItemType Directory -Force -Path 'release\Orbit\tools' | Out-Null
Copy-Item -LiteralPath $cloudflared -Destination 'release\Orbit\tools\cloudflared.exe' -Force
Copy-Item -LiteralPath $cloudflaredLicense -Destination 'release\Orbit\licenses\CLOUDFLARED-LICENSE.txt' -Force
Copy-Item -LiteralPath "$sdk\LICENSE.txt" -Destination 'release\Orbit\licenses\WebView2-LICENSE.txt' -Force
Get-ChildItem -LiteralPath 'node_modules' -Recurse -File -Filter 'LICENSE*' | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $projectRoot 'node_modules').Length).TrimStart('\') -replace '[\\/]', '_'
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path 'release\Orbit\licenses' $relative) -Force
}
Write-Host '빌드 완료: release\Orbit\Orbit.exe'
