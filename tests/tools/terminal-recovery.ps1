param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourceRoot=(Resolve-Path -LiteralPath $AppDirectory).Path
if(!(Test-Path -LiteralPath (Join-Path $sourceRoot 'Orbit.exe'))){throw 'AppDirectory must contain the built Orbit.exe'}
$probeRoot=Join-Path $projectRoot '.tools\terminal-recovery-test\app'
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
Copy-Item -Path (Join-Path $sourceRoot '*') -Destination $probeRoot -Recurse -Force
$probeExe=Join-Path $probeRoot 'TerminalRecoveryProbe.exe'
$compilerArgs=@('/nologo','/target:winexe','/platform:x64',"/out:$probeExe","/resource:$PSScriptRoot\terminal-recovery.js,TerminalRecovery.js",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',"/reference:$probeRoot\Microsoft.Web.WebView2.Core.dll","/reference:$probeRoot\Microsoft.Web.WebView2.WinForms.dll","$PSScriptRoot\TerminalRecoveryProbe.cs")
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @compilerArgs
if($LASTEXITCODE -ne 0){throw 'Recovery probe compilation failed'}
$listener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$listener.Start();$testPort=$listener.LocalEndpoint.Port;$listener.Stop()
$probeProcess=Start-Process -FilePath $probeExe -ArgumentList "--remote-test-port=$testPort" -WindowStyle Hidden -PassThru
if(!$probeProcess.WaitForExit(60000)){$probeProcess.Kill();throw 'Isolated recovery probe timed out'}
$report=Join-Path $projectRoot '.tools\terminal-recovery-test\results\terminal-recovery-results.json'
Get-Content -LiteralPath $report
if($probeProcess.ExitCode -ne 0){throw 'Built terminal recovery regression failed'}
