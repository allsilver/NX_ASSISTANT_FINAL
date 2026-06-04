// nx-launcher/src/NxAssistantLauncher.cs
// NX HEROS 버튼 → NxAssistant.exe 실행
// 경로: client/app/publish/win-x64/ 를 기준으로 탐색

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NXOpen;

public class NxAssistantLauncher
{
    private const string AssistantExeName  = "NxAssistant.exe";
    private const string AssistantTitle    = "NX Assistant";
    // 프로젝트 루트 기준 상대 경로
    private const string RelativeExePath   = @"client\app\publish\win-x64\NxAssistant.exe";

    private static string _projectRoot = "";
    private static string _logPath     = "";
    private static bool   _menuRegistered;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    public static void Main(string[] args) => OpenAssistantFromNx("manual");

    public static int Startup()
    {
        InitRuntime(false);
        RegisterMenu();
        return 0;
    }

    public static int ApplicationInit()  { InitRuntime(false); return 0; }
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
        InitRuntime(true);
        Log($"어시스턴트 요청: {reason}");

        // 이미 열려있으면 앞으로 가져오기
        if (BringExistingWindow()) { Log("기존 창 포커스"); return; }

        // exe 실행
        var exePath = ResolveExePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Log($"실행 파일 없음: {exePath}");
            TryWriteListing($"NX Assistant 실행 파일을 찾을 수 없습니다.\n경로: {exePath}");
            return;
        }

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
        psi.EnvironmentVariables["NX_ASSISTANT_HOME"]        = _projectRoot;
        psi.EnvironmentVariables["NX_ASSISTANT_DB_MCP_URL"]  =
            Environment.GetEnvironmentVariable("NX_ASSISTANT_DB_MCP_URL") ?? "http://127.0.0.1:8766";

        Process.Start(psi);
        Log($"어시스턴트 시작: {exePath}");
        TryWriteListing("NX Design Assistant가 열렸습니다.");
    }

    private static bool BringExistingWindow()
    {
        var hwnd = FindWindow(null, AssistantTitle);
        if (hwnd == IntPtr.Zero) return false;
        ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
        return true;
    }

    private static string ResolveExePath()
    {
        // 1. 환경변수 우선
        var fromEnv = Environment.GetEnvironmentVariable("NX_ASSISTANT_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        // 2. 프로젝트 루트 기준 상대 경로
        return Path.Combine(_projectRoot, RelativeExePath);
    }

    private static void InitRuntime(bool writeListing)
    {
        _projectRoot = ResolveProjectRoot();
        Directory.CreateDirectory(Path.Combine(_projectRoot, "logs"));
        _logPath = Path.Combine(_projectRoot, "logs", "nx-launcher.log");
        if (writeListing) TryWriteListing("NX Design Assistant 시작 중...");
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

    private static string ResolveProjectRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NX_ASSISTANT_HOME");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv);
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        // client/nx-launcher/src → 프로젝트 루트 (3단계 위)
        return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", ".."));
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); }
        catch { }
    }

    private static void TryWriteListing(string msg)
    {
        try { UI.GetUI().NXMessageBox.Show("NX Assistant", NXMessageBox.DialogType.Information, msg); }
        catch { }
    }
}
