// providers/NxControlSession.cs
// NX 제어 모드 세션. 자연어 명령을 키워드로 매핑해 NX 브리지(verify_remoting_ready.py)를 실행한다.
//   브리지: client/nx-mcp/ (repo 내장, 빌드 시 exe 옆 nx-mcp\ 로 복사). 설정: AppConfig.NxBridgeDir(기본 "nx-mcp") / NxBridgePython
//   전제: NX 실행 + 브리지 DLL 로드(Ctrl+U, 127.0.0.1:8792 대기).
//   ※ 키워드 매핑은 1차 구현. 추후 LLM tool-calling 으로 정규화 예정.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxAssistant;

namespace NxAssistant.Providers;

public sealed class NxControlSession : IChatSession
{
    public string Current => "NX 제어";
    public bool   IsGpt   => false;
    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);
    public Task SetLlmAsync(string llm) => Task.CompletedTask;

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        var (flag, label) = MapAction(prompt);
        var (ok, output) = await RunBridgeAsync(flag, ct);
        return ok ? $"✅ NX에 {label}을(를) 생성했습니다." : $"NX 제어 실패:\n{output}";
    }

    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(
        string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (flag, label) = MapAction(question);

        yield return ChatEvent.Status("명령을 해석하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status("NX 세션에 연결하는 중");
        await Task.Delay(550, ct);
        yield return ChatEvent.Status($"{label} 명령을 NX로 전송하는 중");

        var (ok, output) = await RunBridgeAsync(flag, ct);
        if (ok)
            yield return ChatEvent.Token($"✅ **NX에 {label}을(를) 실행했습니다.**\n\nNX 작업 화면에서 결과를 확인하세요.");
        else
            yield return ChatEvent.Token(
                "⚠️ NX 제어에 실패했습니다.\n\n" +
                "NX가 실행 중이고 브리지가 로드돼 있는지, 설정의 `NxBridgeDir` 가 올바른지 확인해 주세요.\n\n" +
                "```\n" + (string.IsNullOrWhiteSpace(output) ? "(출력 없음)" : output.Trim()) + "\n```");
        yield return ChatEvent.Done();
    }

    // 자연어 → 브리지 명령 키워드 매핑 (1차)
    private static (string flag, string label) MapAction(string q)
    {
        var s = (q ?? "").ToLowerInvariant();
        if (s.Contains("힌지") || s.Contains("단면") || s.Contains("hinge") || s.Contains("살두께"))
            return ("--hinge-section", "힌지 하우징 단면");
        if (s.Contains("박스") || s.Contains("box") || s.Contains("육면"))
            return ("--box", "박스 바디");
        if (s.Contains("커브") || s.Contains("곡선") || s.Contains("curve"))
            return ("--curves", "커브");
        if (s.Contains("extrude") || s.Contains("익스트루드") || s.Contains("돌출"))
            return ("--extrude", "Extrude");
        // 기본: 스케치 ("스케치 그려줘" 포함)
        return ("--sketch", "스케치");
    }

    private static async Task<(bool ok, string output)> RunBridgeAsync(string flag, CancellationToken ct)
    {
        var dir = AppConfig.NxBridgeDir;
        if (string.IsNullOrWhiteSpace(dir))
            return (false, "NxBridgeDir 설정이 비어 있습니다. (appsettings.json 또는 NX_BRIDGE_DIR)");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = AppConfig.NxBridgePython,
                Arguments              = $"verify_remoting_ready.py {flag}",
                WorkingDirectory       = dir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
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
