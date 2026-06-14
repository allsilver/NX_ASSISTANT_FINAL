# Security Notes for MCP-Style Browser Automation

Date: 2026-05-26

## Current Direction

Use a visible browser only for the first Knox SSO login and target-site warm-up. After that, copy the browser storage state in memory and run the actual work in a headless browser context.

The storage state must not be written to disk, logged, returned to the LLM, included in screenshots, or included in crash artifacts.

## Why Memory-Only Storage State

- Browser cookies and session tokens can be present in storage state.
- Saving storage state as JSON creates an extra credential artifact outside the normal browser profile.
- Keeping it in process memory only reduces the chance that session data appears in developer logs or support bundles.
- The company SSO server still controls whether the session is valid; this automation does not extend the server-side session lifetime.

## Rules for a Production MCP Tool

- Do not store Knox passwords.
- Do not save `storage_state` JSON files.
- Do not save captured mail send request payloads. If a UI-derived payload is needed, intercept it in memory and immediately discard it after the headless POST completes.
- Do not print cookies, request headers, authorization values, or storage state to logs.
- Do not return storage state or raw network payloads to the LLM.
- Disable Playwright trace, HAR recording, and verbose network logging by default.
- Mask sensitive values in exceptions before writing logs.
- Keep all automation local to the user's PC unless a reviewed server-side design is approved.
- Use a per-user automation browser profile, for example `%LOCALAPPDATA%\NXAssistant\AutomationEdgeProfile`.
- Allow only approved internal domains and approved task tools in the MCP server.
- Return only high-level results to the LLM, such as success status, request number, page title, and screenshot file path.

## Deployment Questions to Review Later

- Whether Samsung security policy allows copying browser cookies into a headless worker process, even when memory-only.
- Whether crash dump collection needs additional exclusion rules.
- Whether automation screenshots may include confidential business data and need retention limits.
- Whether each MCP tool needs a confirmation step before final submit/save/send actions.
- Whether task audit logs should include approver, timestamp, target site, and action summary without sensitive payloads.
- Whether a hidden headed browser may be used only as a local payload-capture helper when a site blocks headless UI events, with the actual business POST still executed by the headless worker.
