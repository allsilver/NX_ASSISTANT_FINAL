// providers/GptProvider.cs
// GPT WebView2 Worker 프로바이더
// WorkerForm을 ILlmProvider 인터페이스로 래핑

using System.Runtime.CompilerServices;
using NxAssistant.UI;

namespace NxAssistant.Providers;

public class GptProvider : ILlmProvider
{
    public string Name    => "GPT";
    public bool   IsReady => _workerForm?.IsGptReady ?? false;

    private readonly WorkerForm _workerForm;

    public GptProvider(WorkerForm workerForm)
    {
        _workerForm = workerForm;
    }

    public async Task<bool> PrepareAsync()
    {
        await _workerForm.InitializeAsync();
        _workerForm.ShowForLogin();
        return true;
    }

    public async Task<string> ChatAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("GPT가 준비되지 않았습니다. 로그인 창에서 로그인하세요.");

        _workerForm.ParkOffscreen();
        return await _workerForm.ChatAsync(prompt);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // GPT WebView2는 스트리밍 불가 (DOM 폴링 방식)
        // 전체 응답 완료 후 한번에 yield
        var result = await ChatAsync(prompt, ct);
        yield return result;
    }
}
