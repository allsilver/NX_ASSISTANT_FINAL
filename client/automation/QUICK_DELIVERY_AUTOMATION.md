# Quick Delivery Automation

Date: 2026-05-26

## Current Status

Implemented a first MCP-style quick delivery automation tool for Digital World.

Primary files:

- `knox_mail_automation/quick_delivery_automation.py`
- `knox_mail_automation/server.py`
- `QUICK_DELIVERY_AUTOMATION_LOG.md`

The tool can parse:

```text
대동전자 이희정님에게 프론트 50개 발송할거야. 퀵 신청해줘
```

It maps that to:

- Receiver company: `대동전자`
- Receiver name: `이희정`
- Item: `프론트`
- Quantity: `50`
- Delivery kind: `퀵/당일배송`
- Send kind: `이동`
- Declared amount: `100000`

## Safety Contract

Default behavior is prepare-only.

- It does not click `저장` unless `--allow-save` is passed.
- Through the MCP-style server, save is allowed only when both `dryRun=false` and `allowSave=true`.
- Missing required business data returns `status=needs_input`.
- Current known required questions:
  - `반입사유`
  - `퀵업체`
  - ambiguous or missing sender/receiver address-book entries

## Verified Prepare-Only Run

Test command:

```powershell
.venv\Scripts\python.exe -m knox_mail_automation.quick_delivery_automation `
  --raw-command "대동전자 이희정님에게 프론트 50개 발송할거야. 퀵 신청해줘" `
  --allow-inferred-reason `
  --quick-vendor "수원/기타출발 - 예스로지스" `
  --keep-open `
  --show
```

Result:

- `status=prepared`
- `saveAttempted=false`
- Screenshot: `artifacts/quick-delivery-final-20260526-193827.png`
- Result JSON: `artifacts/quick-delivery-result-20260526-193828.json`

Verified filled values:

- `퀵/택배 구분`: `퀵/당일배송`
- `퀵업체`: `수원/기타출발 - 예스로지스`
- `발송구분`: `이동`
- `운임구분`: `당사부담`
- `신고가격`: `100,000`
- `반입사유`: `프론트 50개 이동 및 설계 검증용`
- Sender: `서다은`, R5 B타워 9층 B-8 복도
- Receiver: `이희정`, 대동전자
- Item: `프론트`
- Quantity: `50`

## MCP-Style Tool

Added tool name:

```text
prepare_quick_delivery_from_chat
```

Example arguments:

```json
{
  "text": "대동전자 이희정님에게 프론트 50개 발송할거야. 퀵 신청해줘",
  "reason": "프론트 50개 이동 및 설계 검증용",
  "quickVendor": "수원/기타출발 - 예스로지스",
  "dryRun": true,
  "allowSave": false,
  "cdp": "http://127.0.0.1:9242",
  "show": true,
  "keepOpen": true
}
```

Example `needs_input` behavior:

- If `reason` is missing, returns `code=missing_reason` and a suggested reason.
- If `quickVendor` is missing, returns `code=missing_quick_vendor` and actual site options.

## Practical Deployment Shape

Recommended production shape for the 기구팀:

1. NX CAD exposes an AI button.
2. User logs into their LLM account from the NX-side chat UI.
3. The LLM sees MCP tools, including `prepare_quick_delivery_from_chat`.
4. The local MCP server runs on the user PC.
5. The browser tool launches or reuses an automation-only Edge profile.
6. On first Knox/Digital World request each day, the user performs SSO login once in a visible automation window.
7. After login warm-up, routine tasks run in headless mode when possible.
8. If required input is missing or ambiguous, the tool returns `needs_input` to the chat instead of guessing.
9. If visual confirmation is required, the same automation context is shown to the user. Do not fill headless and duplicate into a second visible page.

## Why An Automation Profile Still Matters

An automation profile is a separate Edge user-data folder for automation.

It stores cookies/session state for that automation browser only. This can coexist with company security policy because:

- Session validity is still enforced by Knox/SSO servers.
- If the server expires the session, the local cookie cannot bypass that.
- The profile does not need to store passwords.
- Deployment can delete or rotate the profile on logout, shutdown, or policy interval if required.

For clean UX:

- First login can be headed/visible.
- After login, the same profile can be reused by headless browser jobs if the site allows it.
- If headless is blocked, use a hidden/minimized headed automation window as fallback.

## Known Lessons From This Tool

- Address-book search is not reliable enough as the primary primitive. Row matching plus radio selection is more stable.
- Control order matters. Selecting `퀵/당일배송` after receiver address selection cleared the receiver address. The fixed flow selects transport controls first, then address-book entries.
- Choosing `퀵/당일배송` dynamically reveals `quickKind`, so the tool must detect and handle dynamic required fields.
- Console/log output must be UTF-8 on Windows because Digital World page text can contain characters CP949 cannot encode.

## Remaining Work

- Test actual `저장` once with explicit user approval and `--allow-save`.
- Decide team policy for default `퀵업체` or keep asking every time.
- Add more natural-language parsing patterns for other phrasing.
- Add address ambiguity UX: present candidate rows cleanly and resume after user selection.
- Package the MCP server with a per-user automation profile bootstrapper.
- Add a login warm-up command that opens visible Edge only when the session is expired.
