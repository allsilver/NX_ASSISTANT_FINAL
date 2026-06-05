# NX Assistant 개발 진행상황

> 최종 업데이트: 2026-06-05
> 이 파일은 개발 컨텍스트 유지용입니다.

## 프로젝트 개요
Siemens NX 안에서 동작하는 AI 어시스턴트 (WinForms + WebView2).
- LLM 2종: GPT(WebView2, 개인계정) / Gauss(서버 경유 REST)
- DB MCP 서버(중앙 PC)로 RAG 기반 설계표준 검색

## 환경 제약 (중요)
| 항목 | VDI | 로컬 PC |
|---|---|---|
| GPT (chatgpt.com) | ✅ 열림 | 다음주부터 |
| Gauss API (sr-cloud) | ❌ 막힘 | ✅ 사용가능 |
| pip / PyPI | ❌ 막힘 | ✅ |
| NuGet | ❌ 막힘 | ✅ |
| github push/pull | 불안정 | - |
| dotnet build | ✅ (메모리 제한 옵션 필수) | ✅ |
| RAM | 8GB (가용 2.4GB) | - |

## 빌드 명령 (VDI 필수 옵션)
```
cd client/app
dotnet build -p:UseSharedCompilation=false -m:1 --disable-build-servers
.\bin\Debug\net8.0-windows\NxAssistant.exe
```

## 환경변수 (VDI, User 영구)
- NX_ASSISTANT_MODE = vdi  (우회 모드: 라우터 건너뛰고 GPT 직행)
- WEBVIEW2_CORE_DLL = ...\NX_Assistant_codex\...\Microsoft.Web.WebView2.Core.dll
- WEBVIEW2_WINFORMS_DLL = ...\Microsoft.Web.WebView2.WinForms.dll
- WEBVIEW2_LOADER_DLL = C:\Program Files\Microsoft Office\root\Office16\WebView2Loader.dll

## 완료된 작업
1. ✅ 프로젝트 폴더 구조 재정립 (server/ client/ 분리)
2. ✅ VDI 개발환경 구축 (.NET 8, git PATH, 메모리제한 빌드)
3. ✅ WebView2 로컬 dll 참조 (NuGet 우회) + Loader.dll 자동복사
4. ✅ csproj 경로를 환경변수로 분리 (VDI/로컬 호환)
5. ✅ GPT WinForm 채팅 동작 (로그인 감지 #prompt-textarea)
6. ✅ 라우터 우회 모드 (NX_ASSISTANT_MODE=vdi)
7. ✅ 워커 2개 구조 (User=일반채팅 / Router=임시채팅 격리)
8. ✅ 1차 라우터 분류 검증 (db_search / chat 정확히 분류됨)
9. ✅ throttling 해결 (화면밖 워커도 GPT 응답 읽기 - 검증중)

## 핵심 발견사항
- **throttling 문제**: WebView2를 화면 밖(ParkOffscreen)에 두면 렌더링이
  멈춰서 GPT 응답을 못 읽음. AdditionalBrowserArguments에
  --disable-background-timer-throttling 등 3개 플래그로 해결.
- **임시채팅 URL**: https://chatgpt.com/?temporary-chat=true 로 navigate하면
  격리된 단발 호출 가능. 로그인 세션은 일반채팅과 공유됨.
- **LLM 호출 속도**: 분류 1회에 9~11초. 라우터+답변 시 20초+ 가능.
  → 1차 배포에서 라우터 빼는 이유 중 하나.

## 다음 작업 (TODO)
1. [진행예정] 라우터 제거 + 사용자 모드 선택 UI
   - 모드: DB조회 / NX제어 / 브라우저자동화 / 일반채팅
   - UI 대폭 수정 (기존 MEG_ChatBot UI 최대한 가져오기) - 별도 논의
2. [대기] UI 전면 개편 (스크롤/레이아웃 문제 해결)
3. [대기] pip 문제 해결 → DB MCP 서버 + RAG
4. [대기] MEG → MECH 네이밍 일괄 변경
5. [대기] github commit/push (망 열릴 때)

## 2차 배포 개선사항
- 1차 라우터 추가 (현재 RouterClient에 로직 보존, 호출만 안 함)
- DB 내부 라우터 (도메인 판단 LLM - GPT or DB서버 종속, 추후 논의)
- Rolling Summary 히스토리
- Hybrid Retriever (vector + BM25)
- Gauss rate limit 대응 (분당 10~50회) - 캐싱, GPT 분산
- DB MCP 서버 멀티워커 (gunicorn)

## 알려진 이슈
- UI 자동 스크롤 안 됨 + 메시지 잘림 (FlowLayoutPanel 레이아웃 계산 문제)
  → UI 전면 개편 시 해결 예정
