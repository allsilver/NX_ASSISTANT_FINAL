# NX Remoting MCP Tomorrow Test

내일 바로 테스트할 순서입니다.

## 1. NX 준비

1. NX를 실행합니다.
2. 새 part를 만들거나 기존 part를 엽니다.
3. `Ctrl+U`를 누릅니다.
4. 아래 DLL을 선택해서 실행합니다.

```text
C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo\remoting_bridge\bin\NxMcpSessionServer.dll
```

정상 로드되면 NX 안에서 별도 UI가 뜨지 않아도 됩니다. 서버는 로컬 포트
`127.0.0.1:8792`에서 대기합니다.

## 2. PowerShell 확인

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python verify_remoting_ready.py
```

정상이라면 `status.ok`가 `true`로 나옵니다.

## 3. NX 제어 테스트

먼저 단순한 커브 생성으로 제어 여부를 확인합니다.

```powershell
python verify_remoting_ready.py --curves
```

그 다음 스케치 생성을 확인합니다.

```powershell
python verify_remoting_ready.py --sketch
```

MEG 살두께 기준값을 넣은 힌지 하우징 기본 단면을 확인합니다.

```powershell
python verify_remoting_ready.py --hinge-section
```

또는 MCP helper를 직접 호출해도 됩니다.

```powershell
python remoting_client_via_mcp.py status
python remoting_client_via_mcp.py curves "Live Remoting Rectangle Curves" 60 40
python remoting_client_via_mcp.py sketch "Live Remoting Rectangle" 60 40
python remoting_client_via_mcp.py hinge-section "MEG Hinge Housing Section" 80 12 0.38 0.50 0.40
```

Codex/Cline 실행 환경에서 `python`이 권한 오류로 막히면 시간을 쓰지 말고
바로 절대 경로 Python을 사용합니다.

```powershell
F:\python313\python.exe remoting_client_via_mcp.py status
F:\python313\python.exe remoting_client_via_mcp.py hinge-section "MEG Hinge Housing Section" 80 12 0.38 0.50 0.40
```

MEG DB 검색부터 NX 생성까지 한 번에 확인하려면:

```powershell
F:\python313\python.exe meg_nx_hinge_section_flow.py
```

## 3-1. NX를 여러 개 열었을 때

NX를 여러 개 열어도 됩니다. 단, MCP 제어는 `NxMcpSessionServer.dll`을
로드한 NX 세션 하나에만 연결됩니다.

명령 실행 전 반드시 `nx_remoting_status`의 `work_part_name`을 확인하세요.
여러 NX 세션에 동시에 `NxMcpSessionServer.dll`을 로드하지 마세요.

## 4. 자주 나는 상태

### Port Closed

```text
NX Remoting bridge is not reachable on 127.0.0.1:8792
```

NX에서 `NxMcpSessionServer.dll`이 아직 로드되지 않은 상태입니다. `Ctrl+U`로
DLL을 다시 실행한 뒤 재시도합니다.

### No Work Part

```text
No work part is currently loaded
```

NX에서 part를 하나 열거나 새로 만든 뒤 재시도합니다.

### Signing 또는 License 오류

NXOpen DLL 실행 권한, signing, authoring license 관련 메시지가 나오면 그
메시지를 그대로 복사해서 Codex에 전달합니다.

## 5. 정리

예전 실험 산출물인 `NxMcpSessionClientV2.exe`는 현재 사용하지 않습니다.
Windows가 오래된 프로세스 핸들을 풀면 나중에 삭제해도 됩니다.
