# NX MCP Context Snapshot

Last updated: 2026-05-22

이 문서는 MEG DB 검색 + NX 제어 챗봇 POC의 현재 맥락을 이어받기 위한 작업 기록입니다. 다른 채팅방이나 다음 작업 세션에서 이 파일을 먼저 보면 현재 구조, 검증된 결과, 남은 이슈를 빠르게 파악할 수 있습니다.

## Project Goal

기구팀 설계자가 자연어로 질문하면 다음 흐름을 자동화하는 설계 assistant를 만드는 것이 목표입니다.

1. MEG/기구팀 DB에서 설계 기준과 근거를 검색한다.
2. 검색된 기준을 설계 요구사항으로 해석한다.
3. NX에서 sketch, curve, solid, feature를 생성하거나 열린 부품을 분석한다.
4. 생성/분석 결과를 기준값과 비교해서 설계자가 확인하기 쉬운 형태로 보고한다.

대표 목표 문장:

```text
살두께 기준 참고해서 힌지 하우징 기본 단면 하나 그려줘.
```

## Current Architecture

- `nx_mcp_server.py`
  - Cline/Codex 같은 MCP client가 호출하는 local STDIO MCP server입니다.
  - 현재 NX remoting client는 기본적으로 `remoting_bridge/bin/NxMcpSessionClientSection.exe`를 우선 사용하도록 되어 있습니다.
  - 이유: 기존 `NxMcpSessionClient.exe`가 PID 33116 프로세스에 잡혀 있어 덮어쓰기 빌드가 실패한 상태였기 때문입니다.

- `remoting_bridge/bin/NxMcpSessionServer.dll`
  - NX 안에서 `Ctrl+U`로 로드하는 DLL입니다.
  - NX session을 `127.0.0.1:8792`로 노출합니다.
  - 여러 NX가 열려 있어도 이 DLL을 로드한 NX 세션 하나만 제어 대상입니다.

- `remoting_bridge/src/NxMcpSessionClient.cs`
  - 외부 프로세스에서 NX remoting server로 명령을 보내는 C# client source입니다.
  - status, body list, primitive 생성, 분석, section slice 진입점 등을 담당합니다.

- `remoting_bridge/src/NxMcpSectionInProcess.cs`
  - section slice / 단면 살두께 분석을 NX 프로세스 내부에서 실행하는 DLL source입니다.
  - 무거운 NX API 반복 호출을 외부 remoting으로 하지 않고 `Session.Execute`로 NX 내부에서 처리하기 위해 추가했습니다.

- `remoting_bridge/bin/NxMcpSectionInProcess.dll`
  - 위 source를 빌드한 NX 내부 실행용 DLL입니다.

## NX Tool Progress

완료된 POC:

- NX remoting status 확인
- line, circle, reference cross, rectangle curve 생성
- basic sketch 생성
- box body 생성
- rectangular profile extrude
- body list / feature list 조회
- body bbox, exact bbox, volume, area, centroid 분석
- body dimension validate PASS/FAIL
- hinge housing section recipe POC
- face-pair 기반 최소 살두께 후보 색칠 POC
- 특정 단면 기반 section image + section wall thickness POC

## Wall Thickness Analysis History

처음에는 전체 3D face pair에서 최소 거리를 찾는 방식으로 접근했습니다.

문제:

- 실제 설계자가 말하는 살두께가 아니라 edge, blend, facet 근처의 매우 작은 거리 후보가 선택될 수 있었습니다.
- `UNPARAMETERIZED_FEATURE(8)` 단일 body만 보고 있었기 때문에, work part에 여러 body가 있는 실제 구조를 놓쳤습니다.
- 사용자가 지적한 실제 최소 부위는 중앙홀 부 `0.289 mm`였지만, 이전 로직은 다른 위치의 `0.041518 mm`를 선택했습니다.

현재 방향:

- 전역 3D 최단거리보다, 설계자가 지정한 단면에서 보이는 2D 벽두께를 먼저 계산합니다.
- 기준 단면 예시:

```text
plane point: x=0, y=58, z=0
plane normal: Y axis
```

## Verified Section Result

요청:

```text
x=0, y=58, z=0 위치에서 Y축 normal 단면을 자르고, 열린 모든 body 기준으로 가장 얇은 부위를 찾아라.
```

검증 결과:

- Work part: `MB-013166895/000`
- 요청 당시 work part solid body count: `4`
- y=58 단면과 실제 교차한 body count: `2`
- 교차 body:
  - `UNPARAMETERIZED_FEATURE(7)`
  - `UNPARAMETERIZED_FEATURE(8)`
- 선택된 최소 단면 살두께: `0.289 mm`
- Point A: `[19.831078, 58, -2.028]`
- Point B: `[19.831092, 58, -1.739]`
- raw 최소값 `0.030581 mm`는 같은 edge/facet 근처 노이즈로 보고 제외했습니다.

결과 파일:

```text
workspace/section_images/section_slice_response_20260521_170823_719.json
workspace/section_images/nx_inprocess_section_y_58_20260521_170834.svg
```

## Important Implementation Notes

- `NxMcpSectionInProcess.cs`는 `targetBodyName`이 비어 있거나 `ALL`이면 모든 solid body를 분석하도록 수정했습니다.
- 모든 solid body 중 section plane bounding box와 교차하는 body만 실제 facet/section 분석 대상으로 필터링합니다.
- 단면 추출은 현재 facet 기반 근사입니다.
  - facet surface tolerance:
    - single body: `0.06 mm`
    - multiple bodies: `0.12 mm`
  - multiple body 모드에서는 안정성을 위해 facet을 약간 거칠게 잡았습니다.
- 단면 벽두께 후보는 midpoint가 어느 body 내부에라도 있으면 union 내부 후보로 인정합니다.
- 결과 JSON에는 최소 두께 양 끝점의 `body_index`, `body_journal_id`, `xyz_mm`, `section_uv_mm`가 포함됩니다.

## Current Known Issues

- `NxMcpSessionClient.exe`가 PID 33116 프로세스에 잡혀 있어 메인 exe 덮어쓰기 빌드가 실패했습니다.
  - 그래서 `nx_mcp_server.py`는 최신 테스트 빌드인 `NxMcpSessionClientSection.exe`를 우선 사용하도록 변경되어 있습니다.
  - NX 재시작 후 잠금이 풀리면 main exe를 다시 빌드해서 파일명을 정리하는 것이 좋습니다.

- 전체 body section 분석은 결과 파일을 정상 생성했지만, 외부 remoting client process가 반환되지 않고 timeout되는 경우가 있었습니다.
  - 검증된 결과 JSON/SVG는 생성됐습니다.
  - 다음 단계에서는 `section_slice` 호출 안정화가 필요합니다.
  - 특히 `Session.Execute` 호출 이후 응답 반환 방식, NX main thread 점유, remoting channel timeout을 더 정리해야 합니다.

- 현재 section wall thickness는 POC 근사입니다.
  - 최종 설계 검증용으로는 NX exact section curve, sketch/drafting dimension, 또는 NX measurement API 기반의 정확 치수화가 필요합니다.

## Recommended Next Steps

1. NX를 재시작하거나 remoting server DLL을 다시 로드해서 stale `NxMcpSessionClient.exe` 잠금을 해제합니다.
2. `NxMcpSessionClient.exe`를 최신 `NxMcpSessionClient.cs`로 다시 빌드합니다.
3. MCP tool `nx_remoting_create_section_slice_report`가 `ALL` body section에서 timeout 없이 JSON을 반환하도록 안정화합니다.
4. section SVG에 body별 색상 또는 legend를 추가합니다.
5. `0.289 mm` 최소 부위를 NX 화면에서도 face/edge/curve 색상으로 표시하는 tool을 추가합니다.
6. facet 근사를 NX exact section curve + exact dimension 기반으로 개선합니다.
7. DB 기준값과 section 측정값을 비교해 PASS/FAIL 보고하는 흐름으로 연결합니다.

## Files To Check First

```text
NX_MCP_CONTEXT.md
MEG_NX_QUICK_PROMPT.md
MEG_NX_CHATBOT_USAGE.md
nx_mcp_server.py
remoting_client_via_mcp.py
remoting_bridge/src/NxMcpSessionClient.cs
remoting_bridge/src/NxMcpSectionInProcess.cs
workspace/section_images/section_slice_response_20260521_170823_719.json
workspace/section_images/nx_inprocess_section_y_58_20260521_170834.svg
```
