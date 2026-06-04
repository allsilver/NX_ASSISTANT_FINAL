# scripts/start_db_server.ps1
# DB MCP 서버 시작 스크립트
# 사내 네트워크 IP로 바인딩해서 오픈

param(
    [string]$Host = "0.0.0.0",   # 전체 인터페이스 (사내 네트워크 접근 허용)
    [int]$Port    = 8766
)

$Root      = Split-Path -Parent $PSScriptRoot
$ServerDir = Join-Path $Root "db-mcp"
$Python    = if ($env:DB_MCP_PYTHON) { $env:DB_MCP_PYTHON } else { "python" }

# 환경변수 설정
$env:DB_MCP_HOST = $Host
$env:DB_MCP_PORT = $Port

Write-Host "DB MCP 서버 시작: http://$Host`:$Port" -ForegroundColor Green
Write-Host "서버 디렉토리: $ServerDir"
Write-Host "종료하려면 Ctrl+C"

& $Python (Join-Path $ServerDir "server.py")
