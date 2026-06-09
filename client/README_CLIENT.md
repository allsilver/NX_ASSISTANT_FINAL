# NX Assistant 클라이언트 (client/) — 사용자 PC 전용

NX 안에서 뜨는 WinForms + WebView2 앱입니다. **각 사용자 PC에 배포**합니다.
이 폴더(`client/`)만 옮기면 됩니다. (`server/` 불필요)

상세 빌드 환경(메모리 옵션, WebView2 dll 우회 등)은 루트의 `DEV_ENVIRONMENT.md` 참고.

---

## 1. 빌드 & 실행

`NxAssistant.csproj` 는 환경변수 `WEBVIEW2_CORE_DLL` 유무로 WebView2 참조 방식을 자동 분기합니다.

**로컬 PC (NuGet 사용, 권장 테스트 환경):**
```powershell
cd client\app
dotnet restore       # WebView2 NuGet 복원 (인터넷 필요)
dotnet build
.\bin\Debug\net8.0-windows\NxAssistant.exe
```
- WebView2 패키지 버전을 못 찾으면 csproj 의 `Microsoft.Web.WebView2` Version 만 가용 버전으로 조정.

**VDI (로컬 dll 참조, 메모리 제한):**
```powershell
cd client\app
dotnet build -p:UseSharedCompilation=false -m:1 --disable-build-servers
.\bin\Debug\net8.0-windows\NxAssistant.exe
```
OutOfMemory 시:
```powershell
dotnet build-server shutdown
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
```
- VDI 는 `WEBVIEW2_CORE_DLL` / `WEBVIEW2_WINFORMS_DLL` / `WEBVIEW2_LOADER_DLL` 환경변수가 설정돼 있어야 합니다 (DEV_ENVIRONMENT.md 5절).

---

## 2. 서버 연결 설정 (DB 조회 모드용)

DB 조회 모드는 서버 PC의 DB MCP 서버를 호출합니다. 두 환경변수를 서버와 맞춰야 합니다.

| 환경변수 | 값 | 설명 |
|---|---|---|
| `NX_ASSISTANT_DB_MCP_URL` | `http://<서버PC_IP>:8766` | 서버 주소 (로컬 테스트는 `http://127.0.0.1:8766`) |
| `DB_MCP_TOKEN` | (서버 `settings.json` 의 `db_mcp_token` 과 동일) | 인증 토큰 |

> 도메인 키(`MECH_STANDARD` / `MECHA_DFM` / `CMF_DFC` / `CMF_ISSUE`)는 서버 `domain_registry.json` 과 이미 일치합니다.

---

## 3. 서버 없이 단독 테스트

UI 흐름과 GPT 경로는 서버 없이 확인할 수 있습니다.

- **AI 선택 → GPT** → 분야 선택 → 채팅: GPT(WebView2)로 일반 대화 동작.
- **DB 조회 모드**는 서버가 떠 있어야 답변이 옵니다. 서버가 없으면 연결 오류 메시지가 표시됩니다(정상).

즉, 클라이언트 UI/GPT 검증은 VDI 단독으로, DB 검색 검증은 서버를 띄운 뒤 통합 테스트로 진행합니다.

---

## 4. 1차 배포 동작 흐름

```
AI 선택(Gauss/GPT) → 분야 선택(DB조회 / NX제어 / 자동화)
   → [DB조회] 도메인 선택(설계수순서/DFC/CMF/DFM) → 채팅
```

1차 배포는 1차 라우터를 쓰지 않습니다. 사용자가 모드·도메인을 직접 선택하고,
선택된 도메인 키로 서버 `/mech/ask` 를 호출합니다.
