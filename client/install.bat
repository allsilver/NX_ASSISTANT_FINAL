@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  NX Assistant 설치
echo ============================================
echo.

:: NX 커스터마이제이션 설치
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$customDir = '%~dp0client\nx-customization'; ^
   $datFile = [System.IO.Path]::Combine($env:APPDATA, 'Siemens', 'NX', 'nx_assistant_custom_dirs.dat'); ^
   [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($datFile)) | Out-Null; ^
   [System.IO.File]::WriteAllText($datFile, $customDir); ^
   $ugiiEnv = 'UGII_CUSTOM_DIRECTORY_FILE'; ^
   [System.Environment]::SetEnvironmentVariable($ugiiEnv, $datFile, 'User'); ^
   Write-Host 'NX 커스터마이제이션 설치 완료'"

if errorlevel 1 (
    echo.
    echo 설치 실패. 관리자 권한으로 다시 실행하세요.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  설치 완료!
echo  NX를 재시작하면 HEROS 탭에 AI Assistant 버튼이 나타납니다.
echo ============================================
echo.
pause
