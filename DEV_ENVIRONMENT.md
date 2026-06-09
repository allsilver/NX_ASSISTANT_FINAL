# 개발 환경 정리 (다우니 / NX Assistant)

> 작업하며 파악한 개발 환경·제약·운영 방식 총정리.

---

## 1. 두 개의 환경: VDI vs 로컬 PC

### VDI (개발/편집 환경)
- 용도: C# 코드 작성·편집·빌드, GPT 테스트, git 관리
- 경로: `C:\Users\daeun.seo\Documents\GitHub\NX_ASSISTANT_FINAL`
- 사양: RAM 8GB (가용 약 2.4GB) — 빌드 시 메모리 부족 잦음
- OS: Windows
- 설치됨: .NET 8 (dotnet 8.0.421), Python 3.14.4, git 2.53, WebView2 Runtime

### 로컬 PC (서버 실행/테스트 환경)
- 용도: DB MCP 서버 실행, RAG 테스트, 통합 테스트, 파인튜닝
- pip / Gauss API / 모델 다운로드 다 됨
- meg_chatbot 작업 때 data/, models/ 폴더 이미 구축돼 있음

---

## 2. 환경별 가능/불가능 (핵심)

| 항목 | VDI | 로컬 PC | 비고 |
|---|---|---|---|
| GPT (chatgpt.com) | ✅ 열림 | ✅ | WebView2로 접속 |
| Gauss API (sr-cloud.com) | ❌ 막힘 | ✅ | 사내망 정책 |
| pip install (PyPI) | ❌ 막힘 | ✅ | VDI에서 Python 패키지 설치 불가 |
| NuGet | ❌ 막힘 | ✅ | C# 패키지 → 로컬 dll 참조로 우회 |
| 임베딩/reranker 모델 다운 | ❌ 막힘 | ✅ | |
| GitHub push/pull (터미널) | ❌ 막힘 | - | 보안 정책. GitHub Desktop 사용 |
| GitHub Desktop | ✅ | - | push는 이걸로 |
| dotnet build | ✅ (옵션 필수) | ✅ | 메모리 제한 옵션 없으면 OOM |

### 결론
- **DB MCP 서버(Python RAG)는 VDI에서 못 띄움** (pip + Gauss + 모델 다 막힘).
  → 로컬 PC에서만 가능.
- **VDI = C# 작업 + GPT 테스트 + 코드 편집/빌드/커밋**
- **로컬 PC = 서버 실행 + RAG/통합 테스트**

---

## 3. 운영 방식 (작업 분담)

```
VDI에서:  Claude와 코드 작업 → 빌드 → GPT 테스트 → git 커밋
   ↓ (ZIP으로 옮김)
로컬 PC:  data/, models/ 추가 → DB MCP 서버 실행 → 통합 테스트
   ↓ (디테일 수정 사항 정리)
다시 VDI: 수정 작업
```

---

## 4. VDI 빌드 (메모리 제한 - 필수)

일반 빌드는 OutOfMemory로 실패. 아래 옵션 필수:
```powershell
cd client/app
dotnet build -p:UseSharedCompilation=false -m:1 --disable-build-servers
.\bin\Debug\net8.0-windows\NxAssistant.exe
```

### OutOfMemory 발생 시 정리
```powershell
dotnet build-server shutdown
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
# 떠있는 테스트 exe들도 종료 후 재시도
```

---

## 5. WebView2 (NuGet 우회)

VDI에서 NuGet이 막혀서, WebView2를 로컬 설치된 dll로 참조.
csproj가 아래 환경변수로 dll 경로를 읽음:

| 환경변수 | 값 (예시) |
|---|---|
| WEBVIEW2_CORE_DLL | ...\NX_Assistant_codex\...\Microsoft.Web.WebView2.Core.dll |
| WEBVIEW2_WINFORMS_DLL | ...\Microsoft.Web.WebView2.WinForms.dll |
| WEBVIEW2_LOADER_DLL | C:\Program Files\Microsoft Office\root\Office16\WebView2Loader.dll |

---

## 6. 기타 환경변수 (VDI, User 영구)

| 변수 | 값 | 용도 |
|---|---|---|
| NX_ASSISTANT_MODE | vdi | 우회 모드 (라우터 건너뛰고 GPT 직행). 세션만 끄려면 `$env:NX_ASSISTANT_MODE=""` |
| NX_ASSISTANT_DB_MCP_URL | http://127.0.0.1:8766 | DB 서버 주소 |
| DB_MCP_TOKEN | (settings.json) | DB 서버 인증 토큰 |

---

## 7. 로그

- 위치: `%LOCALAPPDATA%\NX_Assistant\logs\nx-assistant.log`
- 한글 안 깨지게 읽기: `Get-Content ... -Encoding UTF8`
- Program.cs가 UTF-8로 기록함

---

## 8. Git

- 저장소: allsilver/NX_ASSISTANT_FINAL (public)
- push: **GitHub Desktop 사용** (터미널 push는 보안 차단)
- 터미널에서 `git diff` 시 less 없어서 에러 → `git --no-pager diff` 사용
- 빌드 산출물(obj/bin)은 .gitignore로 제외됨

---

## 9. Claude 작업 통로 (중요)

- **Claude는 VDI/로컬 파일을 직접 못 읽고 못 고침.**
- 프로젝트에 깃헙 연결됨 → Claude가 커밋된 코드는 읽을 수 있음 (project_knowledge_search).
  - 단, 미커밋 로컬 변경 / data·models 등 깃헙 미포함 파일은 못 봄 → Get-Content로 보여줘야 함.
- Claude 수정본은 파일로 전달 → 사용자가 VDI/로컬에 직접 반영.
- ZIP은 Claude가 직접 못 만듦 → PowerShell 스크립트로 제공 → 사용자 실행.

---

## 10. 관련 레포 (참고)

| 레포 | 용도 |
|---|---|
| allsilver/NX_ASSISTANT_FINAL | 현재 작업 레포 |
| allsilver/MEG_ChatBot_claude | RAG 챗봇 원본 (Django UI, 검증된 RAG 로직) |
| allsilver/NX_Assistant_codex | 기존 WebView2 구현 (WebView2 dll 출처) |
