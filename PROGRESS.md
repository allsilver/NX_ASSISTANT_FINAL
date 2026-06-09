# NX Assistant 개발 진행상황

> 최종 업데이트: 2026-06-08
> 진행 상황과 다음 할 일만 기록합니다. 프로젝트 구조·환경·규칙은 CLAUDE.md 참조.

---

## ✅ 완료된 작업

### 1차 배포 UI 통합 (최근 - 커밋 9f30fb4)

- **신규 UI 4화면 완성** (단독 테스트 → 본 프로젝트 통합):
  - 화면0 AI 선택: Gauss/GPT 가로형 큰 카드 2개 ("환영합니다!" + 체크리스트 설명)
  - 화면1 분야 선택: DB조회 / NX제어 / 자동화
  - 화면2 DB 도메인: 설계수순서 / DFC / CMF / DFM
  - 화면3 채팅: AI=배경 텍스트(말풍선X), 나=오른쪽 남색 말풍선,
    상단바 2줄(뒤로/홈/설정 + 대화초기화/동의어/LLM드롭다운), 입력창 여러줄
- **파일 구성** (빠른 1차배포 위해 2파일로 묶음, 추후 분리 예정):
  - `client/app/ui/UiKit.cs` — 공통 컴포넌트 전부
  - `client/app/ui/MainForm.cs` — 화면 전환 + 4개 View
  - 기존 `AssistantForm.cs` 삭제, `Program.cs`는 MainForm 실행하도록 수정
- **GPT 실제 연결**:
  - `LlmSession.cs` (신규) — 앱 전역 LLM 선택 관리
  - `GptProvider.cs` — userWorker만 사용 (라우터 1차배포 미사용)
  - GPT 로그인 자동 처리: 이미 로그인됨 → 창 안 띄움 / 필요 시 → 띄우고 완료 감지 후 자동 숨김
  - 채팅에서 선택된 LLM으로 실제 답변 호출
- **검증 완료**: 4화면 전환, GPT 로그인 자동처리, GPT 답변, LLM 토글, 뒤로/홈
- **커밋 9f30fb4** "feat: 신규 UI 통합 (4화면 + LLM 세션), AssistantForm 대체"
  (push는 GitHub Desktop으로)

### 이전 완료 (UI 통합 전)

- VDI 개발환경 (.NET 8, 메모리제한 빌드), WebView2 로컬 dll 참조(NuGet 우회), csproj 환경변수화
- GPT WinForm 채팅 동작 (로그인 감지)
- throttling 해결: WorkerForm에 `--disable-background-timer-throttling` 등 3개 플래그
  → 화면 밖 워커도 GPT 응답 정상 읽음 (검증 완료)
- 단독 UI 테스트 4개 → 본 프로젝트 통합 완료

---

## 📋 다음 작업 (DB MCP 연결 — 1차배포 나머지)

> 주의: DB MCP 내부 동작은 로컬에서 직접 띄워봐야 정확히 파악 가능.
> 코드만 보고 추측하지 말고, 로컬 테스트로 실제 동작 확인 후 진행할 것.

### 즉시
1. [ ] 프로젝트 폴더 구조 전체 확인 (`Get-ChildItem -Recurse -Directory`)
2. [ ] src/ 폴더 정리 (구버전 키워드매칭 → 제거 또는 _archive/ 이동)
3. [ ] MEG → MECH 네이밍 일괄 변경
       - `findstr /s /i "meg" *` 로 대상 파일 전부 탐색 후 변경
       - domain_registry.json, prompts/*.txt 파일명, 코드 기본값 등
4. [ ] ZIP 분리 (배포 대상별, PowerShell 스크립트로 제공):
       - (1) 클라이언트 ZIP — 사용자 PC용 (client/app)
       - (2) 서버 ZIP — 서버 PC용 (server/db-mcp)
       - 각각 독립 테스트 가능하게
5. [ ] 로컬 PC: ZIP 풀고 data/, models/ 추가 → DB MCP 서버 실행 테스트
6. [ ] 로컬 PC: WinForm 앱 ↔ 서버 통합 테스트 (Gauss로 DB 조회)
7. [ ] (위 테스트 결과 보고) C# ↔ 서버 연결 수정 — 실제 동작 확인 후 결정

### 다음 버전 (챗봇 정규화)
8. [ ] 쿼리 재작성 LLM 추가 (Gauss)
9. [ ] Gauss/GPT 답변 분기:
       - Gauss → 서버에서 답변 작성 (지금 구조)
       - GPT → 서버는 검색결과만 반환 → winform GPT가 답변 작성
10. [ ] meg_chatbot의 검증된 검색 로직을 필요한 것만 골라서 가져오기

### 나중에 다듬기 (급하지 않음)
- [ ] GPT 로그인 창 깜빡임 제거 (첫 ProbeAsync 전 페이지 로딩 대기)
- [ ] LLM 선택 → 홈 화면에 뒤로가기 버튼
- [ ] 설정 페이지: GPT 로그아웃 / 재로그인 버튼
- [ ] 실제 Gauss/GPT 로고 이미지 교체 (현재 임시 직접그림)
- [ ] UI 파일 분리 리팩토링 (UiKit/MainForm을 기능별로)

### 2차 배포 (1000명)
- [ ] 1차 라우터 추가 (RouterClient에 로직 보존됨)
  - 임시채팅 격리 버그 해결 필요: ResetTemporaryChatAsync가 기존 세션 못 지움
    → 2번째 질문부터 응답 못읽음 + 이전 맥락 유지(격리 안됨)
- [ ] DB 내부 라우터 (도메인 판단 LLM)
- [ ] Rolling Summary 히스토리, Hybrid Retriever, Gauss 스트리밍(SSE)
- [ ] Gauss rate limit 대응, DB MCP 멀티워커
