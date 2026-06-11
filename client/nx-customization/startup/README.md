# NX Startup Customization

NX loads files in this folder when the project root is registered through `UGII_CUSTOM_DIRECTORY_FILE`.

Current startup assets:

- `nx_assistant_button.men`: adds a visible `NX Assistant` menu near the standard Window/HEROES area.
- `NxMcpSessionServer.dll`: copied from `mcp\servers\nx-local\remoting_bridge\bin` for automatic NX remoting bridge startup testing. If NX does not auto-run .NET startup DLLs in this environment, keep using the explicit `Ctrl+U` fallback path documented in `mcp\servers\nx-local\README.md`.

Current application assets live in `..\application`:

- `nx_assistant.men`: MenuBarManager-compatible fallback menu definition.
- `NxAssistantLauncher.dll`: copied there by the build/install scripts and loaded by NX through the application button.

The stable two-day POC uses the HEROS ribbon button and a standalone Assistant window. The experimental `NxAssistantResourceBar.dll` is disabled by default under `..\disabled` because the external host embedding approach caused focus and repaint issues inside NX.

The future official left Resource Bar implementation should use a native in-process C++ `ufsta` bridge with `UI.ResourceBarManager`. See `apps/nx-resourcebar-bridge`.
