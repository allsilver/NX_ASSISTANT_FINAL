// providers/NxControlSession.cs
// NX 제어 모드 세션.
//   - 기본(데모): 자연어 입력 → 시나리오에 맞는 NX 명령 수행 멘트를 출력(스크립트). 실제 NX 호출 안 함.
//   - NX_CONTROL_REAL=1: 검증된 NX 브리지 명령(verify_remoting_ready.py)을 실제 실행.
//     (NX_BRIDGE_DIR / NX_BRIDGE_PYTHON 필요)

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NxAssistant.Providers;

public sealed class NxControlSession : ILlmSession
{
    public string Current => "NX 제어";
    public bool   IsGpt   => false;
    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);
    public Task SetLlmAsync(string llm) => Task.CompletedTask;

    private static bool IsReal() => Environment.GetEnvironmentVariable("NX_CONTROL_REAL") == "1";

    // ── 데모 스크립트: 질문 → NX 명령 수행 멘트 ──
    private static string ScriptedReply(string q)
    {
        var s = (q ?? "").ToLowerInvariant();

        if (s.Contains("extrude") || s.Contains("익스트루드"))
            return "선택한 선에 대해 0.5mm 만큼 Extrude를 수행합니다.\n\n✅ NX 명령어 성공적으로 실행되었습니다.";

        if (s.Contains("블렌드") || s.Contains("blend") || s.Contains("베벨") || s.Contains("엣지") || s.Contains("edge"))
            return "선택한 Edge에 대해 0.3mm 베벨(Blend)을 적용합니다.\n\n✅ NX 명령어 성공적으로 실행되었습니다.";

        if (s.Contains("면") && (s.Contains("키워") || s.Contains("offset") || s.Contains("살붙") || s.Contains("오프셋")))
            return "선택한 면을 0.5mm 만큼 offset (살붙이기)하며 키웁니다.\n\n✅ NX 명령어 성공적으로 실행되었습니다.";

        // 기타 입력도 자연스럽게
        return "선택한 객체에 대해 요청하신 NX 명령을 수행합니다.\n\n✅ NX 명령어 성공적으로 실행되었습니다.";
    }

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsReal()) return ScriptedReply(prompt);
        var (flag, label) = MapAction(prompt);
        var (ok, output) = await RunBridgeAsync(flag, ct);
        return ok ? $"✅ NX에 {label}을(를) 생성했습니다." : $"NX 제어 실패:\n{output}";
    }

    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(
        string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsReal())
        {
            // 데모(스크립트) — 기본
            yield return ChatEvent.Status("NX 명령을 해석하는 중");
            await Task.Delay(800, ct);
            yield return ChatEvent.Status("NX 명령을 실행하는 중");
            await Task.Delay(1200, ct);
            yield return ChatEvent.Token(ScriptedReply(question));
            yield return ChatEvent.Done();
            yield break;
        }

        // 실제 브리지 모드
        var (flag, label) = MapAction(question);
        yield return ChatEvent.Status("명령을 해석하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status("NX 세션에 연결하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status($"{label} 생성 명령을 NX로 전송하는 중");

        var (ok, output) = await RunBridgeAsync(flag, ct);
        if (ok)
            yield return ChatEvent.Token($"✅ **NX에 {label}을(를) 생성했습니다.**\n\nNX 작업 화면에서 생성된 형상을 확인하세요.");
        else
            yield return ChatEvent.Token(
                "⚠️ NX 제어에 실패했습니다.\n\n" +
                "NX가 실행 중이고 브리지가 로드돼 있는지, NX_BRIDGE_DIR 설정이 올바른지 확인해 주세요.\n\n" +
                "```\n" + (string.IsNullOrWhiteSpace(output) ? "(출력 없음)" : output.Trim()) + "\n```");
        yield return ChatEvent.Done();
    }

    // ── 실제 브리지 (NX_CONTROL_REAL=1 일 때만) ──
    private static string BridgeDir => Environment.GetEnvironmentVariable("NX_BRIDGE_DIR") ?? "";
    private static string Python    => Environment.GetEnvironmentVariable("NX_BRIDGE_PYTHON") is { Length: > 0 } p ? p : "python";

    private static (string flag, string label) MapAction(string q)
    {
        var s = (q ?? "").ToLowerInvariant();
        if (s.Contains("힌지") || s.Contains("단면") || s.Contains("hinge") || s.Contains("살두께"))
            return ("--hinge-section", "힌지 하우징 단면");
        if (s.Contains("커브") || s.Contains("곡선") || s.Contains("curve"))
            return ("--curves", "커브");
        return ("--sketch", "스케치");
    }

    private static async Task<(bool ok, string output)> RunBridgeAsync(string flag, CancellationToken ct)
    {
        var dir = BridgeDir;
        if (string.IsNullOrWhiteSpace(dir))
            return (false, "NX_BRIDGE_DIR 환경변수가 설정되지 않았습니다.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Python, Arguments = $"verify_remoting_ready.py {flag}",
                WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return (false, "파이썬 프로세스를 시작하지 못했습니다.");
            string so = await p.StandardOutput.ReadToEndAsync(ct);
            string se = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return (p.ExitCode == 0, (so + "\n" + se).Trim());
        }
        catch (Exception e) { return (false, e.Message); }
    }
}
