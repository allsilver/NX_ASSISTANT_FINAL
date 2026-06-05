// Program.cs
// 진입점 — 최소한만 유지

using NxAssistant.UI;

namespace NxAssistant;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.ThreadException += (_, e) => Log("ThreadException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UnhandledException: " + e.ExceptionObject);

        try
        {
            Log("NX Assistant 시작");
            ApplicationConfiguration.Initialize();
            using var worker = new WorkerForm();
            Application.Run(new AssistantForm(worker));
            Log("정상 종료");
        }
        catch (Exception ex)
        {
            Log("시작 오류: " + ex);
            MessageBox.Show(ex.ToString(), "NX Assistant 시작 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NX_Assistant", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "nx-assistant.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}

internal static class AppIcon
{
    public static Icon? Load()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }
}
