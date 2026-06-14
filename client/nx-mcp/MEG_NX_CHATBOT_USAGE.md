# MEG DB + NX MCP Chatbot Usage

이 문서는 다른 Codex/Cline 채팅방을 “MEG DB를 조회하고 NX를 제어하는 설계 assistant”처럼 쓰기 위한 운영 가이드입니다.

## 목표

예시 목표 문장:

```text
살두께 기준 참고해서 힌지 하우징 기본 단면 하나 그려줘
```

이 요청은 한 번에 NX를 그리는 문제가 아니라 아래 순서로 처리합니다.

1. MEG DB에서 설계 기준 검색
2. 검색 근거에서 필요한 기준값/치수 추출
3. 생성 전 설계 plan 작성
4. NX MCP의 범용 tool 또는 recipe tool 호출
5. 생성 결과와 MEG 근거를 사용자에게 보고

## 현재 구현 상태

현재는 두 종류의 tool이 있습니다.

범용 NX primitive:

```text
nx_remoting_status
nx_remoting_create_line_curve
nx_remoting_create_circle_curve
nx_remoting_create_reference_cross
nx_remoting_create_rectangle_curves
nx_remoting_create_basic_sketch
nx_remoting_create_box_body
nx_remoting_create_extruded_rectangle
nx_remoting_list_bodies
nx_remoting_list_features
nx_remoting_analyze_bodies
nx_remoting_validate_body_dimensions
nx_remoting_color_thinnest_wall_face
```

MEG recipe:

```text
nx_remoting_create_hinge_housing_section
```

중요한 방향성:

- hinge housing tool은 최종 구조가 아니라 첫 recipe 예제입니다.
- 앞으로 screw boss, rib, FPCB floor 같은 recipe는 범용 primitive tool 조합으로 만들어야 합니다.
- 범용 tool이 충분해져야 “특정 부품 전용 챗봇”이 아니라 “설계 assistant”로 확장됩니다.

## Suggested Cline / Chat Instruction

다른 채팅방 또는 Cline rules에는 아래 지시문을 넣습니다. 더 짧은 버전은 `MEG_NX_QUICK_PROMPT.md`를 사용하면 됩니다.

```text
You are a MEG mechanical design assistant with access to two local capabilities:

1. MEG DB REST API:
   POST http://127.0.0.1:8766/meg/ask

2. NX local MCP tools.

Always start NX work with nx_remoting_status and confirm work_part_name.

When the user asks to create NX geometry based on MEG standards:
- Do not guess the standard values.
- First query the MEG DB with domain="MEG_STANDARD", include_prompt=false, compact=true.
- Extract standard values and evidence.
- Convert the values into a short design plan.
- Prefer generic NX tools such as line/circle/reference/rectangle/sketch/box.
- Use recipe tools such as nx_remoting_create_hinge_housing_section only when the request clearly matches that recipe.
- After creating a 3D body, call nx_remoting_analyze_bodies and report the measured exact bbox/XYZ size, volume, and area against the intended dimensions.
- If expected dimensions are known, call nx_remoting_validate_body_dimensions and report PASS/FAIL with per-axis deltas.
- For wall-thickness visual review, call nx_remoting_color_thinnest_wall_face on the target body and report the candidate thickness and colored faces. Large production parts can take 1-2 minutes.

If the MEG DB result is ambiguous, ask one short clarification before creating geometry.
If NX is not reachable, tell the user to load NxMcpSessionServer.dll in NX with Ctrl+U.
Never execute arbitrary NXOpen code. Use only the provided NX MCP tools or verified helper scripts.

For first-run Python permission issues, immediately use:
F:\python313\python.exe
```

## MEG REST Query Example

PowerShell example:

```powershell
$body = @{
  question = "Spring 부 살두께 중앙 Screw 체결부 주변 살두께 CTC FPCB 바닥부 살두께 Hinge Housing Gap 두께"
  domain = "MEG_STANDARD"
  max_results = 7
  include_prompt = $false
  compact = $true
} | ConvertTo-Json -Depth 5

curl.exe -X POST "http://127.0.0.1:8766/meg/ask" `
  -H "Content-Type: application/json; charset=utf-8" `
  --data-binary $body
```

검증된 기준값 예시:

```text
Spring 부 살두께: 0.38mm 이상
중앙 Screw 체결부 주변 살두께: 0.5mm 이상
CTC FPCB 바닥부 살두께: 0.4mm 이상
```

## Natural Language Test Examples

다른 채팅방에서는 아래처럼 자연어로 요청해 검증합니다. 설계자는 터미널 명령어를 입력하지 않습니다.

```text
현재 NX 연결 상태와 work part 이름 확인해줘.
```

기대 동작: `nx_remoting_status`를 호출하고 제어 중인 NX 세션과 work part를 보고합니다.

```text
NX에 있는 body 목록을 확인하고, 가장 최근에 만든 3D body의 크기와 체적을 읽어줘.
```

기대 동작: `nx_remoting_list_bodies`와 `nx_remoting_analyze_bodies`를 호출하고 body 이름, 정확 bbox, 체적, 표면적을 요약합니다.

```text
가장 최근에 만든 직사각형 extrude body가 30 x 20 x 8mm로 만들어졌는지 검증해줘. 허용오차는 0.01mm로 봐줘.
```

기대 동작: body를 선택한 뒤 `nx_remoting_validate_body_dimensions`를 호출하고 `PASS/FAIL`, 측정값, 축별 오차를 보고합니다.

```text
일부러 검증 실패가 나는지 확인하고 싶어. 같은 body를 31 x 20 x 8mm 기준으로 검증해줘.
```

기대 동작: `FAIL`을 반환하고 X축 오차가 약 1mm임을 설명합니다.

```text
50,0,0 위치를 중심으로 30 x 20mm 사각 단면을 8mm 두께로 extrude해서 3D solid를 만들고, 만든 뒤 실제 크기와 체적을 검증해줘.
```

기대 동작: `nx_remoting_create_extruded_rectangle`로 body를 만들고, `nx_remoting_analyze_bodies`와 `nx_remoting_validate_body_dimensions`로 생성 결과를 검증합니다.

```text
현재 열린 부품에서 body 목록을 확인하고, 가장 최근 solid body의 최소 살두께 후보 face를 색칠해줘.
```

기대 동작: `nx_remoting_list_bodies`로 body를 고른 뒤 `nx_remoting_color_thinnest_wall_face`를 호출합니다. 결과에는 최소 두께 후보값, face pair, 색칠 성공 여부가 포함되어야 합니다.

```text
UNPARAMETERIZED_FEATURE(8) body의 살두께가 가장 얇은 후보 face를 찾아서 빨간색으로 표시하고, 두께 값을 알려줘.
```

기대 동작: 지정된 body를 대상으로 최소 살두께 후보 face pair를 찾고 색칠합니다. 실제 큰 부품에서는 1분 이상 걸릴 수 있습니다.

```text
살두께 기준 참고해서 힌지 하우징 기본 단면 하나 그려줘.
```

기대 동작: MEG DB에서 관련 살두께 기준을 먼저 검색하고, 기준값/근거를 정리한 뒤 가능한 NX tool로 생성합니다. 3D body를 만든 경우 생성 후 분석과 PASS/FAIL 검증까지 수행합니다.

## NX Helper Examples

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo

F:\python313\python.exe .\remoting_client_via_mcp.py status
F:\python313\python.exe .\remoting_client_via_mcp.py line "Generic Line" 0 0 30 10
F:\python313\python.exe .\remoting_client_via_mcp.py circle "Generic Circle" 20 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py cross "Generic Cross" 40
F:\python313\python.exe .\remoting_client_via_mcp.py box "Generic Box" 0 0 0 30 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py extrude-rectangle "Generic Extrude Rectangle" 50 0 0 30 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py bodies
F:\python313\python.exe .\remoting_client_via_mcp.py features 10
F:\python313\python.exe .\remoting_client_via_mcp.py analyze
F:\python313\python.exe .\remoting_client_via_mcp.py validate "MCP READY CHECK EXTRUDE_144441_BODY" 30 20 8 0.01
F:\python313\python.exe .\remoting_client_via_mcp.py hinge-section "MEG Hinge Housing Section" 80 12 0.38 0.50 0.40
```

MEG 검색부터 NX 생성까지 한 번에 확인할 때:

```powershell
F:\python313\python.exe .\meg_nx_hinge_section_flow.py
```

## Multi-NX Rule

NX를 여러 개 열어 참고용으로 쓰는 것은 가능합니다. 단, MCP는 `NxMcpSessionServer.dll`을 로드한 NX 세션 하나만 제어합니다.

항상 생성 전에 `nx_remoting_status`의 `work_part_name`을 확인합니다. 여러 NX 세션에 동시에 `NxMcpSessionServer.dll`을 로드하지 않습니다.

## Development Roadmap

상세 로드맵은 `NX_DESIGN_ASSISTANT_ROADMAP.md`를 봅니다.

가장 가까운 개발 순서:

1. 범용 2D/3D primitive 안정화
2. closed profile + extrude 구현
   - 1차 완료: 사각 sketch/profile extrude
   - 다음: 임의 polyline/circle profile extrude
3. 생성된 body bbox/크기/면/엣지/체적/표면적 분석 안정화
4. 생성 치수 PASS/FAIL 리포트 구현
5. hole / fillet / chamfer 구현
6. MEG DB 결과를 structured requirement로 변환
7. DB 기준 기반 3D hinge housing flow 구현
