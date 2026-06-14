# Knox Portal Browser Automation Summary

Date: 2026-05-22

## 결론

사내 Knox Portal 화면을 자동화 브라우저로 조작하는 것이 가능함을 확인했다.

확인된 범위:

- 자동화용 Edge/Chrome 프로필을 별도로 실행할 수 있음.
- 실행된 브라우저에 CDP로 다시 붙어서 제어할 수 있음.
- 로그인된 Knox Portal 세션에서 DOM을 읽을 수 있음.
- 포털 메뉴의 `메일` 버튼을 클릭할 수 있음.
- `새 메일 쓰기` 화면으로 진입할 수 있음.
- 메일 작성 화면의 수신자 영역, 제목 입력칸, iframe 내부 본문 에디터를 식별할 수 있음.
- 실제 메일 작성창에서 받는 사람을 `서다은 <daeun.seo@samsung.com>`으로 설정하고 본문을 `hi`로 바꿀 수 있음.
- 발신 버튼은 누르지 않았다.

최종 증거 스크린샷:

- `cdp-mail-draft-prepared.png`

## 현재 준비된 작성창 상태

- URL: `http://kor1.samsung.net/formapp/?initModule=mail`
- 제목: 비어 있음
- 받는 사람: `서다은/스마트폰기구개발1그룹(MX)/삼성전자 <daeun.seo@samsung.com>`
- 본문: `hi`
- 발신 버튼: 누르지 않음

## 브라우저 제어 방식

최종적으로 성공한 방식은 다음과 같다.

1. 별도 Edge 프로필을 remote debugging port와 함께 실행한다.
2. 사용자가 해당 브라우저에서 정상 로그인한다.
3. Python Playwright가 CDP로 해당 브라우저에 붙는다.
4. DOM locator, iframe locator, 필요 시 좌표 클릭으로 화면을 조작한다.

성공한 CDP endpoint 예:

```text
http://127.0.0.1:9231
```

핵심 포인트는 사용자의 기존 기본 브라우저를 훔쳐 쓰는 것이 아니라, 자동화용 프로필을 따로 만들고 그 프로필에 사용자가 정상 로그인한 뒤 자동화가 이어받는 구조다.

## MCP 스타일로 확장 가능 여부

가능하다.

현재 구현은 아직 정식 MCP 서버라기보다는 MCP에 붙이기 쉬운 로컬 도구 모음이다. 다음 형태로 감싸면 MCP tool처럼 사용할 수 있다.

예상 tool schema:

- `browser_session_start`
  - 자동화용 Edge/Chrome 프로필을 열고 CDP endpoint를 준비한다.
- `browser_snapshot`
  - 현재 로그인된 페이지의 URL, 제목, 버튼, 입력칸, iframe 목록을 반환한다.
- `portal_click_menu`
  - `메일`, `N-ERP`, `GHRP`, `MOSAIC` 같은 포털 메뉴를 클릭한다.
- `mail_open_compose`
  - 메일 작성 화면을 연다.
- `mail_prepare_draft`
  - 수신자, 제목, 본문을 입력하되 발신은 하지 않는다.
- `mail_send_prepared_draft`
  - 사용자의 명시적 확인이 있을 때만 발신 버튼을 누른다.
- `workflow_run`
  - 자재발주, 견적처리, 반출, 퀵 신청 같은 업무별 어댑터를 실행한다.

중요한 안전 규칙:

- 로그인, MFA, 패스키는 사용자가 직접 처리한다.
- 발신, 제출, 승인, 신청, 발주처럼 실제 업무 상태를 바꾸는 버튼은 tool 내부에서 confirmation gate를 둔다.
- 자동화는 보안 통제를 우회하지 않고 사용자가 정상 로그인한 화면을 조작한다.

## 백그라운드 실행 가능성

가능하지만 조건이 있다.

가능한 경우:

- 자동화용 별도 브라우저 프로필을 사용한다.
- Playwright/CDP locator 기반 조작을 사용한다.
- 사용자가 자동화 브라우저 창이나 같은 탭을 건드리지 않는다.
- 사용자는 다른 브라우저 창, 다른 앱, 문서 작업 등은 계속할 수 있다.

주의할 점:

- 자동화가 같은 탭을 조작하는 동안 사용자가 그 탭을 클릭하거나 입력하면 충돌할 수 있다.
- 좌표 클릭을 쓰는 단계는 화면 배율과 레이아웃에 영향을 받으므로 DOM locator보다 취약하다.
- SSO, 패스키, Knox helper, 보안 프로그램이 필요한 구간은 visible browser가 필요할 가능성이 높다.
- headless 모드는 사내 인증/보안 흐름에서 막힐 수 있으므로 우선순위가 낮다.

권장 운영 형태:

```text
사용자 기본 브라우저: 사용자가 평소 업무에 사용
자동화 브라우저: 별도 Edge 프로필 + CDP port + 로그인 세션 유지
```

이렇게 분리하면 자동화는 백그라운드 작업자처럼 돌릴 수 있고, 사용자는 기본 브라우저나 다른 앱을 계속 사용할 수 있다.

## 이번 세션에서 추가된 주요 파일

- `knox_mail_automation/browser_probe.py`
  - 로컬 페이지와 사내 URL에서 브라우저 제어 가능성을 점검한다.
- `knox_mail_automation/cdp_attach_probe.py`
  - 이미 열린 CDP 브라우저에 붙어서 제어 가능성을 점검한다.
- `knox_mail_automation/cdp_dom_snapshot.py`
  - 현재 페이지의 버튼, 입력칸, 링크, contenteditable 요소 요약을 출력한다.
- `knox_mail_automation/cdp_frames_snapshot.py`
  - iframe 목록과 iframe 내부 편집 가능 요소를 출력한다.
- `knox_mail_automation/cdp_click_control.py`
  - role/text/selector 기반으로 특정 컨트롤을 클릭한다.
- `knox_mail_automation/cdp_click_capture.py`
  - 좌표 클릭 후 상태와 스크린샷을 저장한다.
- `knox_mail_automation/cdp_fill_capture.py`
  - 특정 selector에 값을 입력하고 스크린샷을 저장한다.
- `knox_mail_automation/cdp_prepare_mail_draft.py`
  - 현재 메일 작성창에서 받는 사람과 본문을 준비하되 발신하지 않는다.

기존 pseudo-MCP 구조:

- `knox_mail_automation/server.py`
- `knox_mail_automation/runtime.py`
- `knox_mail_automation/parser.py`

## 실제로 확인한 순서

1. 로컬 HTML 폼에서 `daeun.seo`, `hi` 입력 및 버튼 클릭 성공.
2. Knox Portal 접속 시 자동화 전용 프로필에는 세션이 없어 `사용자 세션이 만료되었습니다` 확인.
3. 별도 Edge 프로필을 CDP port `9231`로 실행하는 데 성공.
4. Edge CDP 세션에 붙어 로컬 폼 제어 성공.
5. Knox 로그인 페이지에서 `#id`에 `daeun.seo` 입력 성공.
6. 사용자가 직접 로그인 완료.
7. 로그인된 포털 DOM 스냅샷 성공.
8. 포털의 `메일` 버튼 클릭 성공.
9. `새 메일 쓰기` 버튼 클릭 성공.
10. 메일 작성창 DOM 및 iframe 스냅샷 성공.
11. 본문 iframe의 `body#cafe-note-contents[contenteditable=true]` 확인.
12. 받는 사람을 수신으로 바꾸고 본문을 `hi`로 설정 성공.

## 다음 단계

1. 현재 스크립트를 정식 MCP stdio server로 감싼다.
2. tool별로 confirmation policy를 둔다.
3. 메일 말고 실제 목표 업무 사이트를 하나 고른다.
4. 해당 사이트에서 다음을 순서대로 수집한다.
   - 접속 URL
   - 메뉴 진입 경로
   - 입력 필드 목록
   - 최종 제출 전 검토 화면
   - 제출 버튼과 confirmation modal
5. 업무별 adapter를 만든다.

추천 첫 업무 adapter:

```text
quick_request_prepare
```

이유:

- 보통 입력 폼 구조가 비교적 단순하다.
- 최종 신청 버튼 전까지 prepare-only 자동화를 검증하기 좋다.
- 자재발주/견적처리보다 업무 위험도가 낮은 편이다.

## 검증

마지막 검증:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests
.\.venv\Scripts\python.exe -m compileall knox_mail_automation
```

결과:

```text
6 tests OK
compileall OK
```
