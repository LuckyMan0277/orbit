param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$version='1.25.260930003'
$packageHash='02B07B349AF66D801159BDF9E440D4A1CE78BB951F37FC8609731665AFDAE7EE'
$cache=Join-Path $projectRoot ('.tools\conpty-'+$version)
$package=Join-Path $cache ('microsoft.windows.console.conpty.'+$version+'.nupkg')
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if(!(Test-Path -LiteralPath $package)) {
    Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.windows.console.conpty/$version/microsoft.windows.console.conpty.$version.nupkg" -OutFile $package
}
if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $packageHash){throw 'ConPTY package hash mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output=[IO.Path]::GetFullPath($(if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path (Get-Location).Path $OutputDirectory}))
New-Item -ItemType Directory -Force -Path $output,(Join-Path $output 'x64'),(Join-Path $output 'licenses') | Out-Null
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach($item in @(
        @{Entry='runtimes/win-x64/native/conpty.dll';Path='conpty.dll';Hash='FEEEF341D891643C62D30B6B07800BC70F0BC148F44CB8C3BEE84AA557AE805A'},
        @{Entry='build/native/runtimes/x64/OpenConsole.exe';Path='x64\OpenConsole.exe';Hash='3D66B23D0A71BB8EED2B77EDC8B9DF9BF54CE6C8FB74C863A30E760997F80586'}
    )) {
        $path=Join-Path $output $item.Path
        [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry($item.Entry),$path,$true)
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Hash){throw 'ConPTY binary hash mismatch'}
        $signature=Get-AuthenticodeSignature -LiteralPath $path
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'ConPTY Microsoft signature verification failed'}
    }
} finally {$archive.Dispose()}
Copy-Item -LiteralPath (Join-Path $projectRoot 'native\licenses\ConPTY-LICENSE.txt') -Destination (Join-Path $output 'licenses\ConPTY-LICENSE.txt') -Force
