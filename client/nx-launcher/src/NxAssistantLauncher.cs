// nx-launcher/src/NxAssistantLauncher.cs
// NX HEROS 버튼 → NxAssistant.exe 실행
//
// exe 경로 우선순위:
//   1. 환경변수 NX_ASSISTANT_EXE
//   2. DLL 옆 launcher.json 의 NxAssistantExe 키
//   3. DLL 옆 NxAssistant.exe (publish 배포 시)
//
// launcher.json 예시:
//   { "NxAssistantExe": "C:\\path\\to\\NxAssistant.exe" }
//
// 설치 위치: nx-customization/application/NxAssistantLauncher.dll
//           nx-customization/application/launcher.json  ← 여기 경로 채워넣기

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NXOpen;

public class NxAssistantLauncher
{
    private const string AssistantExeName = "NxAssistant.exe";
    private const string AssistantTitle   = "NX Assistant";

    private static string _launcherDir = "";
    private static string _logPath     = "";
    private static bool   _inited;
    private static bool   _menuRegistered;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    // NX 스타트업 DLL 진입점
    public static int Startup()
    {
        Init();
        RegisterMenu();
        return 0;
    }

    // NX 애플리케이션 DLL 진입점
    public static int ApplicationInit()  { Init(); return 0; }
    public static int ApplicationEnter() { OpenAssistantFromNx("application button"); return 0; }
    public static int ApplicationExit()  { return 0; }

    public static NXOpen.MenuBar.MenuBarManager.CallbackStatus OpenAssistantCallback(
        NXOpen.MenuBar.MenuButtonEvent e)
    {
        OpenAssistantFromNx("menu action");
        return NXOpen.MenuBar.MenuBarManager.CallbackStatus.Continue;
    }

    public static int GetUnloadOption(string arg) =>
        Convert.ToInt32(Session.LibraryUnloadOption.Immediately);

    public static void UnloadLibrary(string arg) { }

    // ── 핵심: 어시스턴트 창 열기 ─────────────────────────────────
    private static void OpenAssistantFromNx(string reason)
    {
        Init();
        Log($"어시스턴트 요청: {reason}");

        if (BringExistingWindow()) { Log("기존 창 포커스"); return; }

        var exePath = ResolveExePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            var cfgPath = Path.Combine(_launcherDir, "launcher.json");
            Log($"실행 파일 없음: {exePath}");
            ShowMessage(
                $"NX Assistant 실행 파일을 찾을 수 없습니다.\n\n" +
                $"launcher.json 의 NxAssistantExe 에 NxAssistant.exe 전체 경로를 입력해 주세요.\n\n" +
                $"launcher.json 위치:\n{cfgPath}");
            return;
        }

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
        Process.Start(psi);
        Log($"Started: {exePath}");
    }

    private static bool BringExistingWindow()
    {
        var hwnd = FindWindow(null, AssistantTitle);
        if (hwnd == IntPtr.Zero) return false;
        ShowWindow(hwnd, 9);  // SW_RESTORE
        SetForegroundWindow(hwnd);
        return true;
    }

    // 우선순위: 환경변수 → launcher.json → DLL 옆 NxAssistant.exe
    private static string ResolveExePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NX_ASSISTANT_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        var cfgPath = Path.Combine(_launcherDir, "launcher.json");
        if (File.Exists(cfgPath))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cfgPath));
                if (doc != null && doc.TryGetValue("NxAssistantExe", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var p = v.GetString();
                    if (!string.IsNullOrWhiteSpace(p)) return p!;
                }
            }
            catch (Exception e) { Log($"launcher.json 읽기 실패: {e.Message}"); }
        }

        return Path.Combine(_launcherDir, AssistantExeName);
    }

    private static void Init()
    {
        if (_inited) return;
        _inited = true;
        _launcherDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NX_Assistant", "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "nx-launcher.log");
        Log($"NxAssistantLauncher initialized. DLL dir: {_launcherDir}");
    }

    private static void RegisterMenu()
    {
        if (_menuRegistered) return;
        try
        {
            var ui = UI.GetUI();
            ui.MenuBarManager.RegisterApplication(
                "NX_ASSISTANT_APP",
                new NXOpen.MenuBar.MenuBarManager.InitializeMenuApplication(ApplicationInit),
                new NXOpen.MenuBar.MenuBarManager.EnterMenuApplication(ApplicationEnter),
                new NXOpen.MenuBar.MenuBarManager.ExitMenuApplication(ApplicationExit),
                true, true, true);
            ui.MenuBarManager.AddMenuAction(
                "NX_ASSISTANT_OPEN_ACTION",
                new NXOpen.MenuBar.MenuBarManager.ActionCallback(OpenAssistantCallback));
            _menuRegistered = true;
            Log("NX 메뉴 등록 완료");
        }
        catch (Exception ex) { Log($"NX 메뉴 등록 실패: {ex.Message}"); }
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); }
        catch { }
    }

    private static void ShowMessage(string msg)
    {
        try { UI.GetUI().NXMessageBox.Show("NX Assistant", NXMessageBox.DialogType.Warning, msg); }
        catch { }
    }
}
