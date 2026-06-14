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
        if (!await ProbeReadyAsync())   // 전송 직전 재확인: 세션 만료를 그냥 통과하지 않도록
            throw new InvalidOperationException("GPT 로그인 세션이 만료되었거나 준비되지 않았습니다. 설정 → 외부 AI 재로그인 후 다시 시도하세요.");

        if (NxAssistant.AppConfig.ShowWorker)   // [디버그] 워커 창 표시 (평소엔 화면 밖)
            _userWorker.ShowForLogin();
        else
            _userWorker.ParkOffscreen();

        return await _userWorker.ChatAsync(prompt);
    }

    // 스트리밍: 워커가 내보내는 누적 스냅샷을 그대로 흘려보냄.
    public async IAsyncEnumerable<string> ChatStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!await ProbeReadyAsync())   // 전송 직전 재확인: 세션 만료를 그냥 통과하지 않도록
            throw new InvalidOperationException("GPT 로그인 세션이 만료되었거나 준비되지 않았습니다. 설정 → 외부 AI 재로그인 후 다시 시도하세요.");

        if (NxAssistant.AppConfig.ShowWorker)
            _userWorker.ShowForLogin();
        else
            _userWorker.ParkOffscreen();

        await foreach (var snapshot in _userWorker.ChatStreamAsync(prompt, ct))
            yield return snapshot;
    }

    // 마지막(새) 답변의 서식(markdown) — 스트리밍 완료 후 호출
    public Task<string> LastMarkdownAsync() => _userWorker.LastAnswerMarkdownAsync();

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
