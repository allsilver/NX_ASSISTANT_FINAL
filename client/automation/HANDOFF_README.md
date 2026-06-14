# NX Assistant Browser MCP Handoff

This folder is the clean handoff package for integrating the browser automation prototype into the existing NX Assistant project.

Created: 2026-05-27

## What Is Included

- `knox_mail_automation/`
  - Prototype MCP-style browser automation server and tools.
  - Key entry points:
    - `server.py`
    - `quick_delivery_automation.py`
    - `cdp_send_mail.py`
    - `memory_state_workflow.py`
- `tests/`
  - Existing parser/server tests.
- `config/`
  - Selector/config examples.
- `examples/`
  - Pseudo MCP request example.
- Documentation:
  - `README.md`
  - `AUTOMATION_SUMMARY.md`
  - `HEADLESS_REUSE_EXPERIMENT.md`
  - `QUICK_DELIVERY_AUTOMATION.md`
  - `QUICK_DELIVERY_AUTOMATION_LOG.md`
  - `WORK_PROGRESS.md`
  - `SECURITY_NOTES.md`
- `evidence/`
  - Curated proof artifacts for the successful headless quick-delivery save.

## What Was Intentionally Excluded

Do not copy these from the original working folder:

- `.nx-mcp-automation-profile/`
- `.nx-mcp-handoff-profile/`
- `.nx-mcp-mail-capture-profile/`
- `.edge-debug-*`
- `.headless-*`
- `.knox-*profile`
- `.browser-probe-profile`
- `.chrome-debug-*`
- `.venv/`
- `__pycache__/`
- full `artifacts/`
- one-off screenshots from early exploration

Those folders/files may contain local browser state, cookies, SSO session state, cache data, or noisy exploratory outputs.

## Quick Delivery Tool Status

The current most important tool is:

```text
prepare_quick_delivery_from_chat
```

It is exposed in:

```text
knox_mail_automation/server.py
```

Core implementation:

```text
knox_mail_automation/quick_delivery_automation.py
```

Verified flow:

1. Parse Korean command:

   ```text
   대동전자 이희정님에게 프론트 50개 발송할거야. 퀵 신청해줘
   ```

2. Select `퀵/당일배송`.
3. Select quick vendor `수원/기타출발 - 예스로지스`.
4. Select send kind `이동`.
5. Fill declared amount `100000`.
6. Select sender address from address book.
7. Select receiver address from address book.
8. Fill item `프론트`, quantity `50`.
9. Save only when explicitly allowed.

Successful headless save evidence:

- `evidence/quick-delivery-headless-save-result-20260527-093226.json`
- `evidence/quick-delivery-headless-save-final-20260527-093226.png`

## Safety Rules To Preserve

- Never save or send by default.
- Mail sending requires `--allow-send`.
- Quick delivery saving requires `--allow-save`.
- MCP server path additionally requires `dryRun=false` and `allowSave=true`.
- If required fields are missing, return `needs_input`.
- If the page is not the expected form, return a diagnostic result instead of continuing.
- Do not log cookies, storage state, raw request headers, passwords, or SSO tokens.

## Integration Notes

Recommended NX Assistant architecture:

1. NX launches the assistant/chat UI.
2. User logs into their LLM account.
3. The assistant exposes MCP tools, including the browser automation tool.
4. A local MCP server runs on the user PC.
5. The browser automation uses a dedicated automation Edge profile.
6. First Knox/SSO login of the day can be visible.
7. After the session is established, routine tasks can run headless if the site allows it.
8. If headless form entry fails, show a visible login/handoff flow.

## Important Caveat

The proof artifacts contain internal business page screenshots/data. They are useful for the NX Assistant developer, but do not share this handoff package outside the approved internal development context.
