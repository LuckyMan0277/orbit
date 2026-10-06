$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location -LiteralPath $projectRoot
$probeRoot = Join-Path $projectRoot '.tools\file-panel-focus\app'
New-Item -ItemType Directory -Force -Path (Join-Path $probeRoot 'web') | Out-Null
& node scripts/build.mjs
if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
Copy-Item dist\* (Join-Path $probeRoot 'web') -Recurse -Force
$sdkRoot = Join-Path $projectRoot '.tools\webview2'
Copy-Item "$sdkRoot\lib\net462\Microsoft.Web.WebView2.Core.dll", "$sdkRoot\lib\net462\Microsoft.Web.WebView2.WinForms.dll", "$sdkRoot\runtimes\win-x64\native\WebView2Loader.dll" $probeRoot -Force
$probeExe = Join-Path $probeRoot 'FilePanelFocusProbe.exe'
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/main:Orbit.FilePanelFocusProbe',"/out:$probeExe",'/resource:assets\orbit.ico,Orbit.Icon','/resource:tests\tools\file-panel-focus.js,FilePanelFocus.js','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Security.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',"/reference:$sdkRoot\lib\net462\Microsoft.Web.WebView2.Core.dll","/reference:$sdkRoot\lib\net462\Microsoft.Web.WebView2.WinForms.dll") + @(Get-ChildItem native -Filter '*.cs' | ForEach-Object FullName) + 'tests\tools\FilePanelFocusProbe.cs'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Native probe compilation failed' }
$portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$portProbe.Start()
$testPort = $portProbe.LocalEndpoint.Port
$portProbe.Stop()
$probeProcess = Start-Process -FilePath $probeExe -ArgumentList "--remote-test-port=$testPort" -WindowStyle Hidden -PassThru
if (!$probeProcess.WaitForExit(60000)) { $probeProcess.Kill(); throw 'Isolated probe timed out' }
$report = Join-Path $projectRoot '.tools\artifacts\ui-test\file-panel-results.txt'
Get-Content -LiteralPath $report
if ($probeProcess.ExitCode -ne 0) { throw 'File panel focus regression failed' }
