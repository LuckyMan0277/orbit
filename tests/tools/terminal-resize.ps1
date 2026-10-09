param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourceRoot=(Resolve-Path -LiteralPath $AppDirectory).Path
if(!(Test-Path -LiteralPath (Join-Path $sourceRoot 'Orbit.exe'))){throw 'AppDirectory must contain the built Orbit.exe'}
$probeRoot=Join-Path $projectRoot '.tools\terminal-resize-test\probe\app'
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
Copy-Item -Path (Join-Path $sourceRoot '*') -Destination $probeRoot -Recurse -Force
$probeExe=Join-Path $probeRoot 'TerminalResizeProbe.exe'
$compilerArgs=@('/nologo','/target:winexe','/platform:x64',"/out:$probeExe",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',"/reference:$probeRoot\Microsoft.Web.WebView2.Core.dll","/reference:$probeRoot\Microsoft.Web.WebView2.WinForms.dll","$PSScriptRoot\TerminalResizeProbe.cs")
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @compilerArgs
if($LASTEXITCODE -ne 0){throw 'Resize probe compilation failed'}
$listener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$listener.Start();$testPort=$listener.LocalEndpoint.Port;$listener.Stop()
$probeProcess=Start-Process -FilePath $probeExe -ArgumentList "--remote-test-port=$testPort" -WindowStyle Hidden -PassThru
$probeProcess.Id | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $probeRoot) 'probe-pid.txt')
if(!$probeProcess.WaitForExit(60000)){$probeProcess.Kill();throw 'Isolated recovery probe timed out'}
$report=Join-Path $projectRoot '.tools\terminal-resize-test\probe\results\resize-recovery-results.json'
Get-Content -LiteralPath $report
if($probeProcess.ExitCode -ne 0){throw 'Built terminal resize regression failed'}
