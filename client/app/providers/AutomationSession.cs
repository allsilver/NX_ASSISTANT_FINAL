// providers/AutomationSession.cs
// 브라우저 자동화 모드 세션. 자연어 명령을 코덱스 검증 툴(knox_mail_automation)로 실행한다.
//   툴: client/automation/ (repo 내장, 빌드 시 exe 옆 automation\ 로 복사). 설정: AppConfig.AutomationDir(기본 "automation") / AutomationPython / AutomationCdp / AutomationVendor
//   전제: 자동화 Edge(CDP)에 사용자가 SSO 로그인해둔 상태 + Playwright 런타임.
//   ※ 현재는 퀵 신청(quick_delivery) 1종 배선. 메일 발송 등 다툴 라우팅은 추후(키워드→LLM tool-calling).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxAssistant;

namespace NxAssistant.Providers;

public sealed class AutomationSession : IChatSession
{
    public string Current => "브라우저 자동화";
    public bool   IsGpt   => false;
    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);
    public Task SetLlmAsync(string llm) => Task.CompletedTask;

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        var (ok, output) = await RunAsync(prompt, ct);
        return ok ? "✅ 퀵 신청 폼 작성을 완료했습니다. (저장 전)" : $"자동화 실패:\n{output}";
    }

    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(
        string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return ChatEvent.Status("명령을 해석하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status("자동화 브라우저에 연결하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status("퀵 신청 화면을 여는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status("수신자·품목·퀵업체 정보를 입력하는 중");

        var (ok, output) = await RunAsync(question, ct);
        if (ok)
            yield return ChatEvent.Token(
                "✅ **퀵 신청 폼 작성을 완료했습니다.** (저장 전)\n\n" +
                "브라우저 화면에서 입력 내용을 확인하세요. 안전을 위해 **최종 신청은 누르지 않았습니다.**");
        else
            yield return ChatEvent.Token(
                "⚠️ 자동화 실행에 실패했거나 추가 정보가 필요합니다.\n\n" +
                "자동화 브라우저가 떠 있고 Knox 로그인(SSO)이 완료됐는지, 설정의 `AutomationDir` 가 올바른지 확인해 주세요.\n\n" +
                "```\n" + (string.IsNullOrWhiteSpace(output) ? "(출력 없음)" : Tail(output, 1500)) + "\n```");
        yield return ChatEvent.Done();
    }

    private static async Task<(bool ok, string output)> RunAsync(string rawCommand, CancellationToken ct)
    {
        var dir = AppConfig.AutomationDir;
        if (string.IsNullOrWhiteSpace(dir))
            return (false, "AutomationDir 설정이 비어 있습니다. (appsettings.json 또는 NX_AUTOMATION_DIR)");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = AppConfig.AutomationPython,
                WorkingDirectory       = dir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("knox_mail_automation.quick_delivery_automation");
            psi.ArgumentList.Add("--raw-command");  psi.ArgumentList.Add(rawCommand ?? "");
            psi.ArgumentList.Add("--allow-inferred-reason");
            psi.ArgumentList.Add("--quick-vendor"); psi.ArgumentList.Add(AppConfig.AutomationVendor);
            psi.ArgumentList.Add("--keep-open");
            psi.ArgumentList.Add("--show");
            var cdp = AppConfig.AutomationCdp;
            if (cdp.Length > 0) { psi.ArgumentList.Add("--cdp"); psi.ArgumentList.Add(cdp); }
            // 작성만(prepare-only): --allow-save 는 넣지 않음.

            using var p = Process.Start(psi);
            if (p == null) return (false, "파이썬 프로세스를 시작하지 못했습니다.");
            string so = await p.StandardOutput.ReadToEndAsync(ct);
            string se = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var combined = (so + "\n" + se).Trim();
            bool ok = p.ExitCode == 0 && (combined.Contains("prepared") || combined.Contains("saved"));
            return (ok, combined);
        }
        catch (Exception e) { return (false, e.Message); }
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s.Substring(s.Length - n);
}
