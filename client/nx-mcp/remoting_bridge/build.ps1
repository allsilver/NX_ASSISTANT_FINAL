$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$bin = Join-Path $root "bin"
$nxManaged = "C:\SCAD\NX2406\NXBIN\managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$contractDll = Join-Path $bin "NxMcpBridgeContract.dll"
$serverDll = Join-Path $bin "NxMcpBridgeServer.dll"
$clientExe = Join-Path $bin "NxMcpBridgeClient.exe"
$contractSrc = Join-Path $src "NxMcpBridgeContract.cs"
$serverSrc = Join-Path $src "NxMcpBridgeServer.cs"
$clientSrc = Join-Path $src "NxMcpBridgeClient.cs"
$sessionServerDll = Join-Path $bin "NxMcpSessionServer.dll"
$sessionClientExe = Join-Path $bin "NxMcpSessionClient.exe"
$sessionServerSrc = Join-Path $src "NxMcpSessionServer.cs"
$sessionClientSrc = Join-Path $src "NxMcpSessionClient.cs"
$buildLegacyBridge = $env:NX_MCP_BUILD_LEGACY_BRIDGE -eq "1"
$buildSessionServer = $env:NX_MCP_BUILD_SESSION_SERVER -ne "0"
$buildSessionClient = $env:NX_MCP_BUILD_SESSION_CLIENT -ne "0"

New-Item -ItemType Directory -Force -Path $bin | Out-Null
$buildTemp = Join-Path $bin "tmp"
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp

function Invoke-Csc {
  & $csc @args
  if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
  }
}

if ($buildSessionServer) {
  Invoke-Csc /nologo /target:library /platform:x64 "/out:$sessionServerDll" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.UF.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.Utilities.dll')" `
    /reference:"System.Runtime.Remoting.dll" `
    $sessionServerSrc
}

if ($buildSessionClient) {
  Invoke-Csc /nologo /target:exe /platform:x64 "/out:$sessionClientExe" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.UF.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.Utilities.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpenUI.dll')" `
    /reference:"System.Runtime.Remoting.dll" `
    $sessionClientSrc
}

if ($buildLegacyBridge) {
  Invoke-Csc /nologo /target:library "/out:$contractDll" $contractSrc

  Invoke-Csc /nologo /target:library /platform:x64 "/out:$serverDll" `
    "/reference:$contractDll" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.UF.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.Utilities.dll')" `
    /reference:"System.Runtime.Remoting.dll" `
    $serverSrc

  Invoke-Csc /nologo /target:exe /platform:x64 "/out:$clientExe" `
    "/reference:$contractDll" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.UF.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpen.Utilities.dll')" `
    "/reference:$(Join-Path $nxManaged 'NXOpenUI.dll')" `
    /reference:"System.Runtime.Remoting.dll" `
    $clientSrc
}

Write-Output "Built:"
Get-ChildItem $bin | Select-Object Name, Length
