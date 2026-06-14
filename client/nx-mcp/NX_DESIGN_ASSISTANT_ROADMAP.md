# NX Design Assistant Roadmap

목표는 “힌지 하우징 전용 생성기”가 아니라, MEG DB에서 설계 기준을 조회하고 그 수치를 이용해 NX 형상을 단계적으로 생성/검증하는 설계 어시스턴트입니다.

## 핵심 구조

사용자 질문은 바로 NX 명령으로 바뀌지 않습니다. 아래 4단계를 거칩니다.

1. DB 조회: MEG DB에서 관련 기준, 수치, 출처를 찾습니다.
2. 설계 계획: 검색 결과를 치수 요구사항과 형상 생성 계획으로 바꿉니다.
3. NX 실행: 범용 NX tool을 조합해 sketch, curve, solid, feature를 생성합니다.
4. 검증 보고: 생성 치수와 DB 기준을 비교하고 근거/주의사항을 사용자에게 돌려줍니다.

힌지 하우징 tool은 2번과 3번을 한 번에 묶은 첫 recipe 예제입니다. 앞으로는 recipe를 늘리는 것보다, recipe들이 공통으로 사용할 수 있는 범용 tool을 먼저 키웁니다.

## Layer 1. Session / Safety

완료:

- `nx_remoting_status`: 현재 연결된 NX, work part, display part 확인
- localhost `127.0.0.1:8792` 고정 연결
- 여러 NX가 열렸을 때 “서버 DLL을 로드한 NX 세션만 제어”한다는 운영 규칙 정리
- Codex/Cline 첫 실행 지연 우회: `F:\python313\python.exe` 명시 실행 권장

다음:

- 현재 선택 객체 조회
- work part / units / absolute coordinate system 조회
- 최근 생성 feature 목록 조회
- undo mark 단위 rollback 도구

## Layer 2. Universal 2D Tools

완료:

- `nx_remoting_create_line_curve`
- `nx_remoting_create_circle_curve`
- `nx_remoting_create_reference_cross`
- `nx_remoting_create_rectangle_curves`
- `nx_remoting_create_basic_sketch`

다음:

- polyline closed profile 생성
- slot, rounded-rectangle, offset curve 생성
- sketch constraint / dimension 생성
- named profile 저장과 조회

## Layer 3. Universal 3D Tools

완료:

- `nx_remoting_create_box_body`: origin + XYZ dimensions로 직육면체 solid 생성
- `nx_remoting_create_extruded_rectangle`: sketch/profile 기반 사각 단면을 +Z 방향으로 extrude해 solid 생성
- `nx_remoting_list_bodies`: 현재 work part의 solid/sheet body 목록 조회
- `nx_remoting_list_features`: 최근 feature 목록 조회
- `nx_remoting_analyze_bodies`: 생성된 body의 edge bbox, NX 정확 bbox, XYZ 크기, 면 수, 엣지 수, 표면적, 체적, 중심 조회
- `nx_remoting_validate_body_dimensions`: 기대 XYZ 치수와 NX 정확 bbox 측정값을 비교해 PASS/FAIL 반환
- `nx_remoting_color_thinnest_wall_face`: body 내부 face pair의 최소 거리 후보를 찾아 해당 face pair 색상 표시

다음 우선순위:

- 최소 살두께 분석 속도 개선: face spatial index / local sampling
- general closed profile extrude
- cylinder body
- through hole / blind hole
- fillet / chamfer
- shell / thicken
- boolean unite / subtract

이 단계가 되면 “DB에서 살두께 기준을 찾고, 그 기준 이상의 wall/body를 3D로 만든 뒤, hole/fillet까지 적용”하는 흐름이 가능해집니다.

STEP export는 외부 검토/전달용으로 나중에 붙입니다. 설계 assistant의 즉시 검증은 NX 세션에서 실시간으로 body 정보를 읽는 방식이 우선입니다.

## Layer 4. DB-Driven Planning

완료:

- `meg_nx_hinge_section_flow.py`: MEG DB에서 힌지 하우징 살두께 기준을 찾고 NX section을 생성하는 end-to-end POC
- `source_note`, `evidence`, `input_standards`를 NX 생성 결과에 포함

다음:

- 검색 결과를 `Requirement` JSON으로 표준화
- 질문을 `part_type`, `feature_type`, `required_dimensions`, `unknowns`로 분해
- DB 기준 충돌/부족 시 “질문 필요” 상태 반환
- 생성 전 plan preview 출력

## Layer 5. Recipes

현재 recipe:

- hinge housing basic section

다음 recipe 후보:

- wall thickness check body
- screw boss
- rib
- FPCB floor / clearance
- gasket groove
- hinge housing 3D base

중요한 원칙:

- recipe는 최종 사용자 경험을 빠르게 만들기 위한 상위 명령입니다.
- 실제 NX 생성은 반드시 Layer 2/3의 범용 tool 조합으로 내려가야 합니다.
- 이렇게 해야 특정 부품 하나에 갇히지 않고, 다른 기구 부품으로 확장할 수 있습니다.

## Immediate Development Plan

1. 범용 2D/3D primitive tool 안정화
2. closed profile + extrude 구현
   - 1차 완료: rectangular sketch profile extrude
   - 다음: arbitrary polyline/circle profile extrude
3. 생성된 body 분석/검증 구현
   - 1차 완료: bbox/XYZ 크기/면 수/엣지 수 조회
   - 2차 완료: NX 내부 API 기반 정확 bbox, volume, area, centroid 조회
   - 3차 완료: 생성 의도값과 측정값 비교 PASS/FAIL 리포트
   - 4차 완료: 최소 살두께 후보 face pair 탐색 및 색상 표시 POC
   - 다음: 두께 검증 리포트 정밀화, 큰 부품 성능 최적화
4. hole / fillet / chamfer 구현
5. MEG DB 결과를 structured requirement로 변환
6. “살두께 기준 참고해서 힌지 하우징 기본 3D 형상 그려줘” flow 구현
7. 생성 결과를 DB 기준과 비교해 pass/fail report 반환

## Current Test Commands

```powershell
F:\python313\python.exe .\remoting_client_via_mcp.py status
F:\python313\python.exe .\remoting_client_via_mcp.py line "Generic Tool Line POC" 0 0 30 10
F:\python313\python.exe .\remoting_client_via_mcp.py circle "Generic Tool Circle POC" 20 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py cross "Generic Tool Cross POC" 40
F:\python313\python.exe .\remoting_client_via_mcp.py box "Generic Tool Box POC" 0 0 0 30 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py extrude-rectangle "Generic Extrude Rectangle POC" 50 0 0 30 20 8
F:\python313\python.exe .\remoting_client_via_mcp.py bodies
F:\python313\python.exe .\remoting_client_via_mcp.py features 10
F:\python313\python.exe .\remoting_client_via_mcp.py analyze
F:\python313\python.exe .\remoting_client_via_mcp.py validate "MCP READY CHECK EXTRUDE_144441_BODY" 30 20 8 0.01
F:\python313\python.exe .\remoting_client_via_mcp.py thin-wall "UNPARAMETERIZED_FEATURE(8)" 186 0.01 -0.35 160
```
