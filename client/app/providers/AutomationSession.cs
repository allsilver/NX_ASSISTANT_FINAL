// providers/AutomationSession.cs
// 브라우저 자동화 모드 세션. 자연어 명령을 받아 Knox/Digital World "퀵 신청" 자동화를 수행한다.
//
// 두 가지 모드:
//   - 실제 모드(기본): 코덱스 검증 툴(knox_mail_automation.quick_delivery_automation)을 Process 로 실행.
//   - 데모 모드(NX_AUTOMATION_FAKE=1): 툴을 실행하지 않고, 단계 멘트 + 작성완료 스크린샷 + 결과 요약을 표시.
//     (Playwright/CDP 세팅 없이도 촬영 가능. 100% 결정적.)
//
// 설정(환경변수):
//   NX_AUTOMATION_FAKE       : "1"이면 데모 모드(눈속임)
//   NX_AUTOMATION_DIR        : knox_mail_automation 폴더 (실제 모드 필수 / 데모 모드는 스크린샷 자동탐색용)
//   NX_AUTOMATION_PYTHON     : Playwright 깔린 파이썬 (실제 모드)
//   NX_AUTOMATION_CDP        : 자동화 브라우저 CDP endpoint (실제 모드, 선택)
//   NX_AUTOMATION_VENDOR     : 퀵업체 (기본 "수원/기타출발 - 예스로지스")
//   NX_AUTOMATION_SCREENSHOT : 데모 모드에서 띄울 스크린샷 경로 (없으면 DIR\artifacts 에서 자동탐색)

using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using NxAssistant.Mcp;

namespace NxAssistant.Providers;

public sealed class AutomationSession : ILlmSession
{
    public string Current => "브라우저 자동화";
    public bool   IsGpt   => false;
    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);
    public Task SetLlmAsync(string llm) => Task.CompletedTask;

    private static string Dir    => Environment.GetEnvironmentVariable("NX_AUTOMATION_DIR") ?? "";
    private static string Python => Environment.GetEnvironmentVariable("NX_AUTOMATION_PYTHON") is { Length: > 0 } p ? p : "python";
    private static string Cdp    => Environment.GetEnvironmentVariable("NX_AUTOMATION_CDP") ?? "";
    private static string Vendor => Environment.GetEnvironmentVariable("NX_AUTOMATION_VENDOR") is { Length: > 0 } v ? v : "수원/기타출발 - 예스로지스";

    private static bool IsFake()
    {
        var v = Environment.GetEnvironmentVariable("NX_AUTOMATION_FAKE");
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        if (IsFake()) return SuccessSummary();
        var (ok, output) = await RunAsync(prompt, ct);
        return ok ? SuccessSummary() : $"자동화 실패:\n{output}";
    }

    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(
        string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        bool fake = IsFake();

        yield return ChatEvent.Status("명령을 해석하는 중");
        await Task.Delay(900, ct);
        yield return ChatEvent.Status("자동화 브라우저에 연결하는 중");
        await Task.Delay(1100, ct);
        yield return ChatEvent.Status("Digital World 퀵 신청 화면을 여는 중");
        await Task.Delay(1200, ct);
        yield return ChatEvent.Status("수신자·품목·퀵업체 정보를 입력하는 중");
        await Task.Delay(1300, ct);
        yield return ChatEvent.Status("입력 내용을 최종 검토하는 중");

        bool ok; string output;
        if (fake) { await Task.Delay(1400, ct); ok = true; output = ""; }
        else      { (ok, output) = await RunAsync(question, ct); }

        if (ok)
        {
            if (fake)
            {
                var shot = LoadShot();
                if (shot != null)
                    yield return ChatEvent.ImageList(new List<RagImage>
                        { new RagImage("퀵 신청 폼 작성 완료.png", null, shot) });
            }
            yield return ChatEvent.Token(SuccessSummary());
        }
        else
        {
            yield return ChatEvent.Token(
                "⚠️ 자동화 실행에 실패했거나 추가 정보가 필요합니다.\n\n" +
                "자동화 브라우저가 떠 있고 Knox 로그인(SSO)이 완료됐는지, " +
                "NX_AUTOMATION_DIR / NX_AUTOMATION_PYTHON 설정이 올바른지 확인해 주세요.\n\n" +
                "```\n" + (string.IsNullOrWhiteSpace(output) ? "(출력 없음)" : Tail(output, 1500)) + "\n```");
        }

        yield return ChatEvent.Done();
    }

    private static string SuccessSummary() =>
        "✅ **퀵 신청 폼 작성을 완료했습니다.** (저장 전)\n\n" +
        "- 수신자: 이희정 / 대동전자\n" +
        "- 발송자: 서다은\n" +
        "- 퀵/택배 구분: 퀵/당일배송 · 발송구분: 이동\n" +
        "- 퀵업체: 수원/기타출발 - 예스로지스\n" +
        "- 품명 / 수량: 프론트 / 50\n" +
        "- 신고가격: 100,000원\n\n" +
        "안전을 위해 **최종 신청(저장)은 누르지 않았습니다.** 브라우저 화면에서 작성 내용을 확인하세요.";

    // 데모 모드: 스크린샷은 NX_AUTOMATION_SCREENSHOT 가 명시됐을 때만 표시.
    // (직접 채워둔 실제 브라우저를 보여줄 거면 이 변수를 설정하지 않으면 됨 → 멘트+요약만.)
    private static byte[]? LoadShot()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("NX_AUTOMATION_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return File.ReadAllBytes(path);
        }
        catch { /* 스크린샷 없으면 텍스트 요약만 */ }
        return null;
    }

    private static async Task<(bool ok, string output)> RunAsync(string rawCommand, CancellationToken ct)
    {
        var dir = Dir;
        if (string.IsNullOrWhiteSpace(dir))
            return (false, "NX_AUTOMATION_DIR 환경변수가 설정되지 않았습니다.");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = Python,
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
            psi.ArgumentList.Add("--quick-vendor"); psi.ArgumentList.Add(Vendor);
            psi.ArgumentList.Add("--keep-open");
            psi.ArgumentList.Add("--show");
            if (Cdp.Length > 0) { psi.ArgumentList.Add("--cdp"); psi.ArgumentList.Add(Cdp); }

            using var p = Process.Start(psi);
            if (p == null) return (false, "파이썬 프로세스를 시작하지 못했습니다.");

            string so = await p.StandardOutput.ReadToEndAsync(ct);
            string se = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);

            var combined = (so + "\n" + se).Trim();
            bool ok = p.ExitCode == 0 && (combined.Contains("prepared") || combined.Contains("saved"));
            return (ok, combined);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private static string Tail(string s, int n) => s.Length <= n ? s : s.Substring(s.Length - n);
}
