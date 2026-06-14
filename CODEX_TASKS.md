# CODEX_TASKS.md — Codex 작업 지시서

> **시작 전 필독**: `COLLABORATION.md` (특히 §0 절대 규칙).
> Codex는 **자기 소유 경로만** 편집한다: `client/app/providers/tooling/**`, `client/nx-launcher/**`.
> 계약 파일(`IToolRouter.cs`, `ToolCall.cs`)은 **읽기 전용**. 시그니처 변경 금지.
> 막히거나 계약 변경이 필요하면 → `HANDOFF_REQUESTS.md` 에 적고 멈춘다.

---

## T1. LLM 도구 선택 레이어 (tooling)

### 목적
자연어 명령을 받아 **어떤 함수를 어떤 인자로 호출할지** LLM으로 판단해 `ToolCall` 로 반환한다.
지금의 키워드 매칭(`NxControlSession.MapAction`)을 대체할 "두뇌". **실행은 하지 않는다** (실행은 Claude 세션 담당).

### 산출물 (모두 `client/app/providers/tooling/` 안에)
- `ToolCatalog.cs` — 모드별 툴 정의(이름·설명·파라미터). LLM 프롬프트 생성에 사용.
- `LlmToolRouter.cs` — `IToolRouter` 구현체.
- `ToolRoutingPrompt.cs` — 프롬프트 템플릿 생성 (분리해도 되고 LlmToolRouter 안에 둬도 됨).
- (테스트) `tooling/tests/` 또는 별도 — JSON 파싱 단위 테스트.

### 계약 (읽기 전용 — 이미 정의됨)
```csharp
// providers/IToolRouter.cs
Task<ToolCall> RouteAsync(string mode, string command, CancellationToken ct = default);

// providers/ToolCall.cs
record ToolCall(string ToolName, IReadOnlyDictionary<string,string> Args,
                double Confidence = 1.0, string? Clarification = null);
```

### LLM 호출 방법 (중요 — Gauss/GPT를 직접 부르지 말 것)
`LlmToolRouter` 생성자는 **LLM 호출 델리게이트를 주입받는다.** Codex는 이 델리게이트만 호출한다.
```csharp
public LlmToolRouter(
    Func<string /*prompt*/, CancellationToken, Task<string /*completion*/>> complete)
```
- Claude가 이 델리게이트(Gauss 또는 전용 엔드포인트 연결)를 구성해서 넘긴다.
- Codex는 `complete(prompt, ct)` 로 LLM 응답(문자열)을 받아 **JSON 파싱만** 한다.
- → Codex는 `GptProvider`/`GaussProvider`/`mcp/` 를 import 하지 않는다.

### 툴 카탈로그 (이름·인자 — 이대로 고정. Claude 실행 매핑과 일치해야 함)
**mode = "nx"**
| ToolName | 인자 | 설명 |
|---|---|---|
| `nx_sketch` | (없음) | 스케치 생성 |
| `nx_box` | `w`,`h`,`d` (mm) | 박스 바디 생성 |
| `nx_extrude` | `distance` (mm) | 선택 선/면 돌출 |
| `nx_curves` | (없음) | 커브 생성 |
| `nx_hinge_section` | (없음) | 힌지 하우징 단면 |

**mode = "automation"**
| ToolName | 인자 | 설명 |
|---|---|---|
| `send_mail` | `to`,`body` | Knox 메일 발송 (to=영문 ID 예 daeun.seo) |
| `quick_delivery` | `company`,`person`,`item`,`qty` | 퀵 신청 폼 작성 |

> 새 툴 추가가 필요하면 **임의로 만들지 말고** `HANDOFF_REQUESTS.md` 에 제안. 이름은 Claude가 확정.

### RouteAsync 동작
1. `mode` 에 맞는 카탈로그를 골라, 프롬프트를 만든다:
   - 시스템 지시: "너는 도구 선택기다. 아래 도구 목록에서 사용자 명령에 맞는 **하나**를 골라, 인자를 채워 **JSON만** 출력하라. 마크다운/설명 금지. 맞는 도구가 없으면 tool_name 을 빈 문자열로. 인자가 부족하면 clarification 에 한국어로 되물을 말."
   - 도구 목록(이름·설명·인자 스키마)
   - 사용자 명령
   - 출력 형식 명시(아래 JSON)
2. `complete(prompt, ct)` 호출.
3. 응답을 JSON 파싱 → `ToolCall`. 마크다운 펜스(```), 앞뒤 잡텍스트 있으면 **첫 `{` ~ 마지막 `}`** 만 추출해서 파싱.
4. 파싱 실패/빈 결과 → `ToolCall.None()`.

### LLM 응답 JSON 형식 (Codex가 프롬프트로 강제)
```json
{ "tool_name": "nx_extrude", "args": { "distance": "0.5" }, "confidence": 0.95, "clarification": null }
```
- `tool_name` 빈 문자열 → 매칭 없음.
- `clarification` 채워짐 → 실행 대신 사용자에게 되물음.
- 모든 args 값은 **문자열로** (숫자도 "0.5" 처럼).

### 수용 기준 (Acceptance)
- 모킹된 `complete`(고정 JSON 반환)로 단위 테스트 통과:
  - "선택한 선에 0.5mm extrude" → `nx_extrude{distance:"0.5"}`
  - "서다은에게 hi 메일 보내줘" → `send_mail{to:"daeun.seo", body:"hi"}` *(또는 to 추출은 LLM 몫; 모킹 JSON 기준 파싱이 정확하면 OK)*
  - 맞는 도구 없음 JSON → `ToolCall.None()`
  - 마크다운 펜스로 감싼 JSON도 정상 파싱
- `tooling/` 밖 파일을 **import/수정하지 않음** (계약 2파일 제외).
- 실제 Gauss/GPT 호출 코드 없음 (델리게이트만 사용).

### 금지
- 세션/UI/mcp/Provider 편집·참조 금지.
- `IToolRouter`/`ToolCall` 시그니처 변경 금지.
- 실행(브리지·자동화 프로세스 실행) 구현 금지 — 그건 Claude.

### 진행 로그 (Codex가 여기에만 추가)
- (작업하며 날짜·완료 항목 기록)

---

## T2. NX 런처 정식화

### 목적
현재 NX 버튼은 **코덱스 스파이크 런처**가 환경변수 `NX_ASSISTANT_WEBVIEW2_EXE` 로 우리 exe를 띄우는 **임시 연결**이다.
이 의존을 제거하고, **우리 전용 런처**(`client/nx-launcher/`)가 결정적 경로로 `NxAssistant.exe` 를 실행하게 한다.

### 산출물 (`client/nx-launcher/` 안에)
- 런처 소스(C#) — NX가 호출 시 `NxAssistant.exe` 를 찾아 실행.
- `build.ps1` 또는 빌드 지침.
- `README.md` — 빌드·배치 방법.

### 요구 동작
1. 런처는 **자기 위치 기준**으로 `NxAssistant.exe` 를 찾는다 (환경변수 의존 금지).
   - 탐색 순서 예: ① 런처와 같은 폴더 ② `..\app\bin\...\NxAssistant.exe` ③ 설정파일에 적힌 경로.
   - 절대경로 하드코딩·환경변수 의존 금지 (배포 안전).
2. 이미 실행 중이면 새로 띄우지 말고 **기존 창을 앞으로** (창 제목 "NX Assistant" 로 탐색 — 현재 동작 유지).
3. 실패 시 로그를 `<launcherDir>\logs\nx-launcher.log` 에 남긴다.

### 참고 (읽기 전용 — 기존 스파이크 런처)
- 기존 런처 로직: `apps/nx-launcher/src/NxAssistantLauncher.cs` (codex 예전 작업, BringExistingWindow 패턴 포함). 동작 방식 참고만.

### 수용 기준
- 환경변수 `NX_ASSISTANT_WEBVIEW2_EXE` **없이** 런처가 `NxAssistant.exe` 를 실행.
- 이미 실행 중이면 중복 실행 안 함(기존 창 포커스).
- `nx-launcher/` 밖 파일 편집 없음.

### 통합 (Codex가 하지 않음 — Claude 몫)
- NX 버튼이 이 런처를 가리키도록 `nx-customization/` 갱신은 **Claude**가 한다.
  Codex는 런처 산출물 + 배치 방법(README)만 제공.

### 금지
- `nx-customization/` 편집 금지(요청만).
- 환경변수 기반 경로 재도입 금지.

### 진행 로그 (Codex가 여기에만 추가)
- (기록)
