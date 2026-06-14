# install.ps1 - NxAssistantLauncher.dll 빌드 후 NX 커스터마이제이션 폴더에 설치
# 실행: cd client/nx-launcher; .\install.ps1

param(
    [string]$InstallDir = "F:\AX_TF\NX_Assistant\apps\nx-customization\application"
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$CsprojPath = Join-Path $ScriptDir "NxAssistantLauncher.csproj"

# 1. 빌드
Write-Host "빌드 중..."
dotnet build $CsprojPath -c Debug -p:UseSharedCompilation=false --disable-build-servers
if ($LASTEXITCODE -ne 0) { Write-Error "빌드 실패"; exit 1 }

# 2. DLL 복사
$SrcDll = Join-Path $ScriptDir "bin\Debug\net8.0-windows\NxAssistantLauncher.dll"
if (-not (Test-Path $SrcDll)) { Write-Error "DLL 없음: $SrcDll"; exit 1 }

if (-not (Test-Path $InstallDir)) {
    Write-Error "설치 대상 폴더 없음: $InstallDir"
    exit 1
}

Copy-Item $SrcDll $InstallDir -Force
Write-Host "DLL 설치 완료: $InstallDir\NxAssistantLauncher.dll"

# 3. launcher.json 복사 (없으면)
$SrcJson  = Join-Path $ScriptDir "launcher.json"
$DestJson = Join-Path $InstallDir "launcher.json"
if (-not (Test-Path $DestJson)) {
    Copy-Item $SrcJson $DestJson
    Write-Host "launcher.json 설치: $DestJson"
    Write-Host ">>> launcher.json 의 NxAssistantExe 경로를 확인하세요."
} else {
    Write-Host "launcher.json 이미 있음 (덮어쓰지 않음): $DestJson"
}

Write-Host ""
Write-Host "설치 완료. NX를 재시작하면 새 런처가 적용됩니다."
Write-Host "NxAssistant.exe 경로: $(Get-Content $DestJson | Select-String NxAssistantExe)"
