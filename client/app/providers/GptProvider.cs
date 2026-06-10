// providers/GptProvider.cs
// GPT WebView2 Worker 프로바이더
// - ChatAsync : 사용자 질의용 워커 (일반 채팅, 히스토리 누적)
//
// [1차 배포] 라우터 미사용 → userWorker 1개만 사용.
//   AskIsolatedAsync(격리 호출)는 라우터 재도입 시 복원 예정.

using System.Runtime.CompilerServices;
using NxAssistant.UI;

namespace NxAssistant.Providers;

public class GptProvider : ILlmProvider
{
    public string Name    => "GPT";
    public bool   IsReady => _userWorker?.IsGptReady ?? false;

    private readonly WorkerForm _userWorker;     // 사용자 질의용 (일반 채팅)

    public GptProvider(WorkerForm userWorker)
    {
        _userWorker = userWorker;
    }

    // 워커 초기화만 (창 띄우지 않음)
    public Task InitOnlyAsync() => _userWorker.InitializeAsync();

    // 로그인 창 표시
    public void ShowLogin() => _userWorker.ShowForLogin();

    // 워커를 화면 밖으로 숨김
    public void HideWorker() => _userWorker.ParkOffscreen();

    // 로그인 상태 확인 (composer 보이면 준비 완료)
    public async Task<bool> ProbeReadyAsync()
    {
        var probe = await _userWorker.ProbeAsync();
        return probe.HasComposer;
    }

    // (기존 호환) 초기화 + 로그인창
    public async Task<bool> PrepareAsync()
    {
        await _userWorker.InitializeAsync();
        _userWorker.ShowForLogin();
        return true;
    }

    // 사용자 질의 → 일반 채팅 워커로 답변
    public async Task<string> ChatAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("GPT가 준비되지 않았습니다. 로그인 창에서 로그인하세요.");

        // [디버그] NX_ASSISTANT_SHOW_WORKER=1 이면 워커 창을 띄워 동작을 직접 관찰 (평소엔 화면 밖).
        var show = Environment.GetEnvironmentVariable("NX_ASSISTANT_SHOW_WORKER");
        if (!string.IsNullOrEmpty(show) && show.Trim() is "1" or "true" or "TRUE")
            _userWorker.ShowForLogin();
        else
            _userWorker.ParkOffscreen();

        return await _userWorker.ChatAsync(prompt);
    }

    // [1차 배포 미사용] 라우터 격리 호출. 라우터 재도입 시 routerWorker로 복원.
    public Task<string> AskIsolatedAsync(string prompt, CancellationToken ct = default)
        => throw new NotSupportedException("AskIsolatedAsync는 1차 배포에서 사용하지 않습니다 (라우터 미사용).");

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await ChatAsync(prompt, ct);
        yield return result;
    }
}
