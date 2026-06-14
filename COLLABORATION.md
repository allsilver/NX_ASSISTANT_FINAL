# COLLABORATION.md — Claude × Codex 협업 규칙

> 이 저장소(NX_ASSISTANT_FINAL)는 **Claude(Cowork)** 와 **Codex** 가 함께 작업한다.
> 두 AI는 **계약(인터페이스)에서 만나고, 서로의 영역엔 들어가지 않는다.**
> 작업 시작 전 이 문서를 반드시 읽는다.

---

## 0. 절대 규칙 (특히 Codex)

1. **각자 자기 소유 경로의 파일만 편집한다.** 남의 파일은 **읽기만**.
2. **공유 계약(인터페이스/DTO) 시그니처는 Claude만 정의·변경한다.** Codex는 **구현만**.
3. **남의 파일 변경이 필요하면 직접 고치지 말고** `HANDOFF_REQUESTS.md` 에 요청만 적는다.
4. **Codex는 먼저 설계하지 않는다.** 항상 Claude가 만든 스펙(`CODEX_TASKS.md`)·계약을 받고 시작한다.
5. **"겸사겸사 리팩터" 금지.** 배정된 task 범위만 건드린다.
6. 커밋 메시지 접두: `[claude]` / `[codex]`. 1 task = 1 commit 지향.
7. 파괴적 git 작업(force push, 공유 히스토리 rebase) 금지.
8. MD 파일은 Claude가 유지한다. Codex는 `CODEX_TASKS.md` 의 "진행 로그" 섹션과 `HANDOFF_REQUESTS.md` 에만 추가한다.

---

## 1. 소유권 표

### 코드
| 경로 | 소유 |
|---|---|
| `client/app/ui/**` | 🟦 Claude |
| `client/app/providers/DbQuerySession.cs · NxControlSession.cs · AutomationSession.cs` | 🟦 Claude |
| `client/app/providers/GptProvider.cs · GaussProvider.cs · WorkerForm.cs` | 🟦 Claude |
| `client/app/providers/IChatSession.cs · IToolRouter.cs · ToolCall.cs` (계약) | 🟦 Claude **정의** |
| `client/app/mcp/** · AppConfig.cs · config/** · nx-mcp/**` | 🟦 Claude |
| `client/app/providers/tooling/**` (신규) | 🟪 **Codex** |
| `client/nx-launcher/**` | 🟪 **Codex** |
| `client/nx-customization/** · server/**` | 🟦 Claude (당분간 손대지 않음) |

### 문서
| 파일 | 소유 / 역할 |
|---|---|
| `CLAUDE.md · PROGRESS.md · DEV_ENVIRONMENT.md` | 🟦 Claude 유지 · 둘 다 읽기 |
| `COLLABORATION.md` | 🟦 Claude (이 문서) |
| `CODEX_TASKS.md` | Claude 작성 / Codex 실행·진행로그 |
| `CLAUDE_TASKS.md` | 🟦 Claude Cowork |
| `HANDOFF_REQUESTS.md` | 양쪽 요청 채널 |

---

## 2. 시임 (둘이 닿는 지점)

```
🟦 Claude 세션  ──calls──▶  IToolRouter  ◀──implements──  🟪 Codex (tooling/)
                              (계약)
        세션이 ToolCall 실행 ◀──returns ToolCall──  라우터
```

- **Codex**: 자연어 → `ToolCall{ToolName, Args}` 결정만 (`IToolRouter.RouteAsync`).
- **Claude**: 반환된 `ToolCall` 을 실제 실행(NX 브리지 / 자동화 툴)으로 매핑·배선.
- Codex는 세션·UI·mcp 를 **건드리지 않는다.** 계약 두 파일(`IToolRouter.cs`, `ToolCall.cs`)만 읽는다.

---

## 3. 새 기능 파이프라인 (고정 순서)

1. **기획·설계 (Claude)** — 요구 분석 → 아키텍처 → 인터페이스 정의 → `CODEX_TASKS.md` 스펙 작성
2. **계약 동결 (Claude)** — 인터페이스·DTO 스켈레톤 + 통합 스텁 커밋
3. **사용자 컨펌**
4. **구현 (Codex)** — 동결된 계약에 맞춰 자기 영역에서만 구현 (수용 기준대로)
5. **통합·배선 (Claude)** — Codex 산출물을 세션/UI에 연결
6. **검증·테스트 (Claude Cowork)** — 실환경 테스트·디버깅. 수정 요청은 `HANDOFF_REQUESTS.md`
7. **문서 갱신 (Claude)** — PROGRESS/CLAUDE 갱신

**역할 유형**: 판단 필요(설계·통합·디버깅·테스트·문서·경계 결정) = Claude / 스펙 명확한 자율 구현 = Codex.

---

## 4. 충돌 방지

- 파일 소유가 **겹치지 않으므로** 같은 브랜치에서 작업해도 충돌이 안 난다.
- 계약은 항상 **Claude가 먼저 동결** → Codex는 고정점에 구현.
- 막히거나 계약 변경이 필요하면 **멈추고** `HANDOFF_REQUESTS.md`.
