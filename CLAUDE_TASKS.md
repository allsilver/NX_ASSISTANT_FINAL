# CLAUDE_TASKS.md — Claude (Cowork) 작업 목록

> 시작 전 필독: `COLLABORATION.md`, `CLAUDE.md`(구조), `PROGRESS.md`(현황), `DEV_ENVIRONMENT.md`(환경 함정).
> Claude 소유 영역 전반 + 통합·검증·문서. 실환경(빌드·NX·Playwright·서버)에서 테스트하는 일은 전부 여기.

---

## C1. GPT 워커 안정화 (마무리)
- **현재 상태**: 만료 감지(ProbeAsync가 "세션 만료"/disabled composer 감지) + 전송 직전 재probe 까지 1차 적용됨(`WorkerForm.cs`, `GptProvider.cs`).
- **할 일**:
  1. **만료 DOM 정규식 튜닝** — `ShowWorker:1` 로 워커 창 띄우고, 실제 만료 화면의 정확한 문구/엘리먼트 확인 → `WorkerForm.ProbeAsync` 의 `expired` 정규식 정밀화.
  2. **자동 재시도 1회** — 첫 응답 감지 실패 시 1회 재시도 후 실패 처리.
  3. **멈춤 워치독** — N초 내 응답 시작 안 하면 "재로그인/재시도" 안내로 빠지게.
- **검증**: 세션 만료 상황을 만들어 → 첫 질문 시 "재로그인" 안내가 뜨는지. (만료 재현법: 프로필 쿠키 만료 또는 로그아웃 후 진입)
- **파일**: `app/ui/WorkerForm.cs`, `app/providers/GptProvider.cs`.

## C2. NX제어·자동화 실배선 검증
- **흡수 완료 (VDI에서 처리됨)**: 툴이 repo에 들어옴 →
  - `client/nx-mcp/` (verify_remoting_ready.py + `remoting_bridge/bin/` **빌드된 DLL 포함**)
  - `client/automation/` (knox_mail_automation 패키지 + config/selectors + requirements)
  - 빌드 시 exe 옆 `nx-mcp\`/`automation\` 으로 **자동 복사**(csproj) → appsettings 상대경로 기본값(`"nx-mcp"`/`"automation"`)으로 해석됨. **경로 수동 설정 불필요.**
- **남은 dev 세팅 (`appsettings.local.json`)**: `AutomationPython`=Playwright 깔린 파이썬(venv) 경로, `NxBridgePython`=파이썬, (자동화 테스트 시)`AutomationCdp`, `DbMcpToken`.
- **NX 제어 검증**: NX 실행 + 브리지 DLL 로드(Ctrl+U, 8792) → "스케치 그려줘" 등으로 실제 형상 생성.
  - ※ DLL은 repo에 포함됨. **NX 버전 안 맞으면** `client/nx-mcp/remoting_bridge/build.ps1` 로 재빌드(로컬, NXOpen 참조). → **이게 "NX DLL 빌드" 로컬 과제.**
- **자동화 검증**: `pip install -r client/automation/requirements.txt` + `playwright install` → CDP 브라우저 SSO 로그인 → "대동전자 이희정님에게 프론트 50개…퀵 신청" 실제 폼 작성 확인.
- **파일**: `app/providers/NxControlSession.cs`, `AutomationSession.cs`, `client/nx-mcp/`, `client/automation/`.

## C3. 계약 정의 + Codex 모듈 통합 ⭐ (시임 담당)
- **계약**: `IToolRouter.cs`, `ToolCall.cs` 는 이미 동결됨(스켈레톤 커밋됨).
- **LLM 델리게이트 제공**: `LlmToolRouter` 가 받을 `Func<string,CancellationToken,Task<string>>` 를 구성(Gauss 또는 전용 라우팅 엔드포인트에 연결).
- **세션 배선**: Codex의 `LlmToolRouter` 가 들어오면 →
  - `NxControlSession` 의 `MapAction` 키워드 매칭을 `IToolRouter.RouteAsync("nx", 명령)` 로 교체.
  - `AutomationSession` 의 하드코딩(퀵 신청)을 `RouteAsync("automation", 명령)` → `ToolCall.ToolName` 분기(`send_mail`/`quick_delivery`)로 교체.
  - `ToolCall.ToolName` → 실제 실행 매핑 작성 (예: `nx_extrude` → 브리지 `--extrude`, `send_mail` → `cdp_send_mail` 모듈 실행).
  - `Clarification` 있으면 실행 대신 사용자에게 되묻기.
- **순서**: Codex T1 완료 후 통합. 그 전엔 키워드 매칭 유지.

## C4. 배포 패키징
- **Playwright 런타임**: 자동화 툴을 PC마다 venv 없이 돌리도록 자체포함 패키징(PyInstaller exe) **또는** Playwright .NET 이식 검토.
- **앱 publish**: `publish/win-x64` 자체포함 빌드. `appsettings.json` 동봉, `appsettings.local.json` 은 배포처에서 작성.
- **NX 런처 통합**: Codex의 `nx-launcher` 산출물을 받아 `nx-customization` 버튼이 가리키도록 연결(C 영역).

## C5. 문서/마무리
- Phase 4 문서화: 설정 키·플래그를 `CLAUDE.md`/`CONFIG`에 정리.
- 각 작업 완료 시 `PROGRESS.md` 갱신.

---

### 백로그 (나중)
- 프롬프트 전면 재작성(원본 `server/db-mcp/prompts/_backup/`)
- 2차 배포 라우터(RouterClient 보존됨), DB 내부 도메인 라우터
- 임시채팅(라우터 워커) 격리 버그
