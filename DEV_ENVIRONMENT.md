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
- 용도: DB MCP 서버 실행, RAG 테스트, **WinForm 빌드·실행, 통합 테스트**, 파인튜닝
- pip / Gauss API / 모델 다운로드 다 됨
- meg_chatbot 작업 때 data/, models/ 폴더 이미 구축돼 있음
- **.NET 8 + NuGet 사용 가능** (인터넷 됨) → WinForm 빌드 가능. VDI보다 사양·환경이 좋음
- **WebView2: NuGet 패키지로 복원** (VDI 처럼 로컬 dll 환경변수 불필요)
- 결론: 서버와 클라(WinForm)를 한 PC에서 다 띄울 수 있어, **client↔server 통합 테스트는 로컬 PC에서 진행**

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

## 5. WebView2 (환경별 자동 분기)

csproj(`NxAssistant.csproj`)가 `WEBVIEW2_CORE_DLL` 경로에 dll 이 **실제로 존재하는지**(`Exists`)로 자동 분기한다:
- **dll 파일 있음 = VDI**: NuGet 막혀서 로컬 설치 dll 을 참조 (아래 환경변수 사용)
- **dll 파일 없음 = 로컬 PC**: NuGet 패키지(`Microsoft.Web.WebView2`)로 복원
  (환경변수가 비었거나, 환경변수만 있고 그 경로에 파일이 없어도 자동으로 NuGet)

VDI 용 환경변수 (User 영구):

| 환경변수 | 값 (예시) |
|---|---|
| WEBVIEW2_CORE_DLL | ...\NX_Assistant_codex\...\Microsoft.Web.WebView2.Core.dll |
| WEBVIEW2_WINFORMS_DLL | ...\Microsoft.Web.WebView2.WinForms.dll |
| WEBVIEW2_LOADER_DLL | C:\Program Files\Microsoft Office\root\Office16\WebView2Loader.dll |

로컬 PC 빌드(환경변수 불필요):
```powershell
cd client\app
dotnet restore   # WebView2 NuGet 복원
dotnet build
.\bin\Debug\net8.0-windows\NxAssistant.exe
```

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

---

## 11. 로컬 전용 데이터 — VDI→로컬 동기화 시 보존 (덮어쓰기 금지)

VDI 는 코드만 git 으로 관리한다. 아래는 **로컬 PC 에만 존재·수정**되며 git/ZIP 에 없다.
VDI→로컬 통째 덮어쓰기 시 이 항목들이 사라지지 않도록 반드시 보존할 것.
(설정값을 로컬에서 고쳐도 VDI 가 모르므로, 같은 수정을 VDI 코드/문서에도 반영해야 다음 ZIP 에 반영됨)

| 경로 | 내용 | 분실 시 |
|---|---|---|
| `server/config/settings.json` | 실제 Gauss 키 + db_mcp_token | example 에서 복사 후 키 재입력 (번거로움) |
| `server/data/` | ChromaDB, db_registry_*.json, parsed_result | 재구축 또는 meg_chatbot 에서 재배치 |
| `server/models/` | bge-reranker-v2-m3 | 재다운로드 (느림) |
| `server/.venv/` | Python 가상환경 | 재생성 가능 (pip install 다시) |
| `client/app/bin/`, `obj/` | 빌드 산출물 | 재빌드 |

**권장 동기화 방식:**
- (X) 기존 NX_ASSISTANT_FINAL 폴더를 통째 삭제 후 ZIP 압축해제 → 위 항목 전부 소실
- (O) 코드 파일만 덮어쓰기(merge) → ZIP 에 없는 위 항목은 그대로 보존
- 또는 ZIP 풀기 전 위 폴더들을 백업 → 푼 뒤 복원

**주의:** `settings.json`, `db_registry_*.json` 의 default 플래그처럼 로컬에서만 고친 값은,
같은 내용을 VDI 의 코드/문서(또는 settings.example.json)에도 반영해두지 않으면 다음 동기화 때 놓치기 쉽다.
