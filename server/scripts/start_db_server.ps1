# scripts/start_db_server.ps1
# DB MCP 서버 시작 스크립트
# 사내 네트워크 IP로 바인딩해서 오픈
#
# 사용:
#   .\scripts\start_db_server.ps1                 # 0.0.0.0:8766
#   .\scripts\start_db_server.ps1 -BindHost 127.0.0.1 -Port 8766

param(
    [string]$BindHost = "0.0.0.0",   # 전체 인터페이스 (사내 네트워크 접근 허용)
    [int]$Port        = 8766
)

# $Host 는 PowerShell 예약 변수라 사용 금지 → $BindHost 사용
$Root      = Split-Path -Parent $PSScriptRoot   # server/
$ServerDir = Join-Path $Root "db-mcp"
$Python    = if ($env:DB_MCP_PYTHON) { $env:DB_MCP_PYTHON } else { "python" }

$env:DB_MCP_HOST = $BindHost
$env:DB_MCP_PORT = $Port

Write-Host "DB MCP 서버 시작: http://$BindHost`:$Port" -ForegroundColor Green
Write-Host "서버 디렉토리: $ServerDir"
Write-Host "종료하려면 Ctrl+C"

& $Python (Join-Path $ServerDir "server.py")
