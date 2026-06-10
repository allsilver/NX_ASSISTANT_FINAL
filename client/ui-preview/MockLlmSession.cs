// MockLlmSession.cs (UI 프리뷰 전용)
// ILlmSession 을 구현하되 서버/WebView2 없이 가짜 답변만 돌려줌.
// ChatView 가 이걸 주입받아 실제처럼 동작(답변만 mock).

using NxAssistant.Providers;

namespace NxAssistant.UI;

internal sealed class MockLlmSession : ILlmSession
{
    public string Current { get; private set; } = "Gauss";
    public bool   IsGpt   => Current == "GPT";

    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);   // 답변 생성 지연 흉내 (스피너 확인용)
        return $"[mock · {Current}] \"{prompt}\" 에 대한 예시 답변입니다.\n" +
               "UI 프리뷰라 실제 서버/LLM 연결 없이 표시됩니다.\n" +
               "여러 줄·줄바꿈·말풍선 레이아웃을 확인하기 위한 더미 텍스트입니다.";
    }

    public Task SetLlmAsync(string llm)
    {
        Current = llm;   // 실제 로그인/전환 없이 이름만 변경
        return Task.CompletedTask;
    }
}
