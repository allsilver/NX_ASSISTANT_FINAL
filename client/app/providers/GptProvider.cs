// providers/GptProvider.cs
// GPT WebView2 Worker 프로바이더
// - ChatAsync       : 사용자 대화용 워커 (일반 채팅, 히스토리 누적)
// - AskIsolatedAsync: 라우터용 워커 (임시 채팅, 격리 단발 호출)

using System.Runtime.CompilerServices;
using NxAssistant.UI;

namespace NxAssistant.Providers;

public class GptProvider : ILlmProvider
{
    public string Name    => "GPT";
    public bool   IsReady => _userWorker?.IsGptReady ?? false;

    private readonly WorkerForm _userWorker;     // 사용자 대화용 (일반 채팅)
    private readonly WorkerForm _routerWorker;   // 라우터용 (임시 채팅)

    public GptProvider(WorkerForm userWorker, WorkerForm routerWorker)
    {
        _userWorker   = userWorker;
        _routerWorker = routerWorker;
    }

    public async Task<bool> PrepareAsync()
    {
        await _userWorker.InitializeAsync();
        _userWorker.ShowForLogin();
        return true;
    }

    // 사용자 대화 — 일반 채팅 워커에 누적
    public async Task<string> ChatAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("GPT가 준비되지 않았습니다. 로그인 창에서 로그인하세요.");

        _userWorker.ParkOffscreen();
        return await _userWorker.ChatAsync(prompt);
    }

    // 라우팅/분류 — 임시 채팅 워커로 격리 단발 호출
    public async Task<string> AskIsolatedAsync(string prompt, CancellationToken ct = default)
    {
        await _routerWorker.InitializeAsync();
        _routerWorker.ParkOffscreen();
        return await _routerWorker.AskIsolatedAsync(prompt);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await ChatAsync(prompt, ct);
        yield return result;
    }
}