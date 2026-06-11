// providers/ILlmSession.cs
// 채팅 화면(ChatView)이 의존하는 LLM 세션의 최소 인터페이스.
// 실제 앱은 LlmSession 이 구현하고, UI 프리뷰는 MockLlmSession 이 구현한다.
// → ChatView 가 서버/WebView2 의존(LlmSession 구현체)을 직접 알지 않게 분리.

namespace NxAssistant.Providers;

public enum ChatEventKind { Status, Token, Done, Markdown }

/// <summary>스트리밍 채팅 이벤트. Status=진행 멘트, Token=부분 답변(이어붙임), Markdown=완료 후 서식본, Done=완료.</summary>
public readonly record struct ChatEvent(ChatEventKind Kind, string Text)
{
    public static ChatEvent Status(string text)   => new(ChatEventKind.Status, text);
    public static ChatEvent Token(string text)    => new(ChatEventKind.Token, text);
    public static ChatEvent Markdown(string text) => new(ChatEventKind.Markdown, text);
    public static ChatEvent Done()                => new(ChatEventKind.Done, "");
}

public interface ILlmSession
{
    /// <summary>현재 선택된 LLM 이름 ("Gauss" / "GPT")</summary>
    string Current { get; }

    /// <summary>현재 LLM 이 GPT 인지</summary>
    bool IsGpt { get; }

    /// <summary>GPT 로그인 완료 여부</summary>
    Task<bool> IsGptReadyAsync();

    /// <summary>현재 LLM 으로 질의 → 답변(한 번에)</summary>
    Task<string> AskAsync(string prompt, CancellationToken ct = default);

    /// <summary>현재 LLM 으로 질의 → 스트리밍 이벤트(진행 멘트 → 부분 답변 → 완료)</summary>
    IAsyncEnumerable<ChatEvent> AskStreamAsync(string question, CancellationToken ct = default);

    /// <summary>LLM 전환</summary>
    Task SetLlmAsync(string llm);
}
