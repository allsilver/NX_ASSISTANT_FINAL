# NX Local MCP Demo

This is a dependency-free local MCP server prototype for Siemens NX workflows.

It is designed for this shape:

```text
Cline / Codex MCP client
  -> local STDIO MCP server on the user's own PC
  -> NX localhost remoting bridge loaded inside Siemens NX
  -> Siemens NX on the same PC
```

Do not expose this server on the company network. NX control should stay local
to the user's workstation unless IT/security approves another architecture.

## What This Demo Does

- Exposes a local STDIO MCP server.
- Provides an optional live NX bridge through NX .NET Remoting.
- Keeps a short-lived file bridge fallback through `nx_bridge_journal.py`.
- Lists NX-related tools to Cline/Codex.
- Generates safe NXOpen Python journal templates.
- Reads generated body geometry through the live bridge for exact bbox and mass-property verification.
- Optionally builds or runs an NX journal command if you configure one.
- Defaults to dry-run behavior for execution.

## What This Demo Does Not Do Yet

- It does not execute arbitrary code from the LLM.
- It does not know your exact NX installation command line.
- The live bridge is a proof of concept. It supports only allowlisted commands.

For a first proof of concept, use the generated journal in NX manually:

1. Ask Cline to call `nx_create_journal` with `template="report_work_part"`.
2. Open NX.
3. Run the generated `.py` journal from NX's journal playback/menu.
4. Confirm that the NX Listing Window shows work-part information.

For a simple sketch POC, generate `template="create_basic_sketch"`, open or
create a blank part in NX, and run the generated journal manually in NX.

## Live NX Bridge Flow

For a chat-like workflow, use the persistent NX Remoting bridge first. This is
much better than manually running a journal for every command.

## Persistent NX Remoting Bridge

Build:

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
powershell -ExecutionPolicy Bypass -File .\remoting_bridge\build.ps1
```

Load once per NX session:

1. Open NX and create/open a part.
2. Press `Ctrl+U` to open the Execute NX Open dialog.
3. Select:

```text
C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo\remoting_bridge\bin\NxMcpSessionServer.dll
```

4. From Codex/Cline, call `nx_remoting_status`.
5. Then call `nx_remoting_create_basic_sketch`.

Codex-only helper through the MCP server:

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python remoting_client_via_mcp.py status
python remoting_client_via_mcp.py curves "Live Remoting Rectangle Curves" 60 40
python remoting_client_via_mcp.py sketch "Live Remoting Rectangle" 60 40
python remoting_client_via_mcp.py line "Generic Line" 0 0 30 10
python remoting_client_via_mcp.py circle "Generic Circle" 20 20 8
python remoting_client_via_mcp.py cross "Generic Cross" 40
python remoting_client_via_mcp.py box "Generic Box" 0 0 0 30 20 8
python remoting_client_via_mcp.py extrude-rectangle "Generic Extrude Rectangle" 50 0 0 30 20 8
python remoting_client_via_mcp.py bodies
python remoting_client_via_mcp.py features 10
python remoting_client_via_mcp.py analyze
python remoting_client_via_mcp.py validate "MCP READY CHECK EXTRUDE_144441_BODY" 30 20 8 0.01
python remoting_client_via_mcp.py thin-wall "UNPARAMETERIZED_FEATURE(8)" 186 0.01 -0.35 160
python remoting_client_via_mcp.py hinge-section "MEG Hinge Housing Section" 80 12 0.38 0.50 0.40
```

If the chat/runtime blocks `python` or spends time on sandbox permission issues,
use the known Python path directly:

```powershell
F:\python313\python.exe remoting_client_via_mcp.py status
F:\python313\python.exe remoting_client_via_mcp.py box "Generic Box" 0 0 0 30 20 8
F:\python313\python.exe remoting_client_via_mcp.py extrude-rectangle "Generic Extrude Rectangle" 50 0 0 30 20 8
F:\python313\python.exe remoting_client_via_mcp.py analyze
F:\python313\python.exe remoting_client_via_mcp.py validate "MCP READY CHECK EXTRUDE_144441_BODY" 30 20 8 0.01
F:\python313\python.exe remoting_client_via_mcp.py thin-wall "UNPARAMETERIZED_FEATURE(8)" 186 0.01 -0.35 160
F:\python313\python.exe remoting_client_via_mcp.py hinge-section "MEG Hinge Housing Section" 80 12 0.38 0.50 0.40
```

For the full MEG-search-to-NX flow, prefer the wrapper below instead of
hand-writing JSON-RPC:

```powershell
F:\python313\python.exe meg_nx_hinge_section_flow.py
```

Readiness check:

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python verify_remoting_ready.py
python verify_remoting_ready.py --curves
python verify_remoting_ready.py --sketch
python verify_remoting_ready.py --analyze
python verify_remoting_ready.py --hinge-section
```

`verify_remoting_ready.py` should first report that port `8792` is open. If it
is closed, load `NxMcpSessionServer.dll` in NX with `Ctrl+U` and retry.

The MCP server sets the minimum NX client environment before launching the
Remoting client. If NX is installed somewhere else, set `NX_MCP_NX_BASE_DIR`.

```powershell
$env:NX_MCP_NX_BASE_DIR = "C:\SCAD\NX2406"
```

The client still requires `NxMcpSessionServer.dll` to be loaded inside NX first.


Unload when needed in NX:

```text
File -> Utilities -> Unload Shared Images
```

The remoting bridge uses localhost port `8792`. It binds to `127.0.0.1`, so it
is local to this PC.

You may keep multiple NX windows open for reference. MCP control is connected
only to the NX session where `NxMcpSessionServer.dll` was loaded. Before a
creation command, check `nx_remoting_status` and confirm `work_part_name`.
Do not load `NxMcpSessionServer.dll` into multiple NX sessions at the same time.

Known cleanup note: earlier experiments may leave `NxMcpSessionClientV2.exe` in
`remoting_bridge\bin`. It is not used by the current MCP server and can be
deleted after Windows releases any old process handles.

## Short-Lived File Bridge

If remoting is unavailable in a restricted environment, use the short-lived file
bridge:

```text
Codex/Cline
  -> nx_mcp_server.py through MCP stdio
  -> writes workspace/nx_bridge_command.json
  -> user runs nx_bridge_journal.py in NX
  -> NXOpen command executes and writes workspace/nx_bridge_response.json
```

Setup:

1. Open NX and create or open a part.
2. Run this journal in NX via `Tools -> Journal -> Play` once:

```text
C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo\nx_bridge_journal.py
```

3. In Codex/Cline, call `nx_bridge_status` or `nx_bridge_create_basic_sketch`.
   This writes a command file.
4. Run `nx_bridge_journal.py` in NX again to process that command.
5. In Codex/Cline, call `nx_bridge_read_response`.

Codex-only helper commands after the bridge journal is running:

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python bridge_client_via_mcp.py status
python bridge_client_via_mcp.py sketch "Live Bridge Rectangle" 60 40
python bridge_client_via_mcp.py read
```

The bridge is short-lived and does not open a server socket. It requires a local
token saved at:

```text
C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo\workspace\nx_bridge_token.txt
```

## Files

- `nx_mcp_server.py`: local STDIO MCP server.
- `smoke_test.py`: protocol smoke test.
- `verify_remoting_ready.py`: local readiness check for the NX Remoting bridge.
- `MEG_NX_CHATBOT_USAGE.md`: workflow guide for combining MEG DB search with NX MCP creation.
- `MEG_NX_QUICK_PROMPT.md`: concise prompt/rules to paste into another chat.
- `NX_MCP_CONTEXT.md`: current implementation context, verified section-thickness result, known issues, and next steps.
- `NX_DESIGN_ASSISTANT_ROADMAP.md`: roadmap from hinge POC to generic DB-driven NX design assistant.
- `meg_nx_hinge_section_flow.py`: one-command MEG DB search to NX hinge-section creation flow.
- `cline_mcp_settings.example.json`: example Cline MCP settings entry.
- `workspace/generated_journals/`: generated NXOpen Python journals.

## Cline MCP Setup

Cline's MCP docs describe local STDIO servers as a `command` plus `args`
configuration. Add something like this in Cline's MCP settings:

```json
{
  "mcpServers": {
    "nx-local": {
      "command": "python",
      "args": [
        "C:\\Users\\daeun.seo\\Documents\\Codex\\2026-05-19\\mcp\\nx-mcp-demo\\nx_mcp_server.py"
      ],
      "env": {
        "NX_MCP_WORKSPACE": "C:\\Users\\daeun.seo\\Documents\\Codex\\2026-05-19\\mcp\\nx-mcp-demo\\workspace",
        "NX_MCP_ALLOW_EXECUTE": "0"
      },
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

Then restart the MCP server from Cline and check that these tools appear:

- `nx_status`
- `nx_create_journal`
- `nx_list_journals`
- `nx_run_journal`
- `nx_bridge_info`
- `nx_bridge_status`
- `nx_bridge_report_work_part`
- `nx_bridge_create_basic_sketch`
- `nx_bridge_stop`
- `nx_remoting_info`
- `nx_remoting_status`
- `nx_remoting_list_bodies`
- `nx_remoting_list_features`
- `nx_remoting_analyze_bodies`
- `nx_remoting_validate_body_dimensions`
- `nx_remoting_color_thinnest_wall_face`
- `nx_remoting_create_rectangle_curves`
- `nx_remoting_create_basic_sketch`
- `nx_remoting_create_line_curve`
- `nx_remoting_create_circle_curve`
- `nx_remoting_create_reference_cross`
- `nx_remoting_create_box_body`
- `nx_remoting_create_extruded_rectangle`
- `nx_remoting_create_hinge_housing_section`
- `nx_remoting_stop`

## Optional NX Execution

Every company/NX install can differ. This demo therefore does not hard-code a
Siemens NX command. If your NX setup has a known journal runner command, set:

```powershell
$env:NX_MCP_ALLOW_EXECUTE = "1"
$env:NX_MCP_RUN_JOURNAL_COMMAND = "YOUR_NX_COMMAND_WITH_{journal}_PLACEHOLDER"
```

Example shape only:

```text
"C:\Path\To\NX\runner.exe" --journal "{journal}"
```

Keep execution disabled until the command is reviewed with the NX/admin owner.

## Recommended Cline Instruction

```text
For NX CAD automation, use the nx-local MCP tools. Start with nx_remoting_status
and confirm work_part_name. Use generic remoting tools first
(line/circle/reference/box/sketch), and use recipe tools such as
nx_remoting_create_hinge_housing_section only after DB evidence and input
dimensions are clear. After creating a 3D body, call
nx_remoting_analyze_bodies to verify exact bbox/XYZ size, volume, and area
before reporting success. If intended dimensions are known, call
nx_remoting_validate_body_dimensions and report PASS/FAIL with per-axis deltas.
For wall-thickness visual review, call nx_remoting_color_thinnest_wall_face and
report the candidate thickness plus colored face pair. Large production parts
can take 1-2 minutes.
Do not hand-write NXOpen code unless no approved tool can do the task.
```

## Codex-Only Test Flow

If Codex has not registered this server as a first-class MCP server yet, Codex
can still test the server by running a small local MCP client or smoke test.

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python smoke_test.py
```

To generate a sketch journal from Codex, ask Codex to call the local MCP server's
`nx_create_journal` tool with:

```json
{
  "template": "create_basic_sketch",
  "requester": "codex",
  "note": "first sketch POC",
  "parameters": {
    "sketch_name": "MCP Rectangle Sketch",
    "size_mm": "50"
  }
}
```

Then run the generated `.py` file manually inside NX on a blank/open part.

Or use the helper client:

```powershell
cd C:\Users\daeun.seo\Documents\Codex\2026-05-19\mcp\nx-mcp-demo
python create_sketch_journal_via_mcp.py "MCP Rectangle Sketch" 50
```
