// Program.cs (UI 프리뷰 전용 진입점)

using NxAssistant.UI;

namespace NxAssistant;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PreviewShell());
    }
}
