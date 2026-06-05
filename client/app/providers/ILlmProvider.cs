// providers/ILlmProvider.cs
// LLM 프로바이더 인터페이스
// Gauss와 GPT가 이 인터페이스를 구현하므로 AssistantForm은 코드 변경 없음

namespace NxAssistant.Providers;

public interface ILlmProvider
{
    string Name { get; }
    bool   IsReady { get; }

    /// <summary>비스트리밍 응답 (사용자 대화에 누적)</summary>
    Task<string> ChatAsync(string prompt, CancellationToken ct = default);

    /// <summary>격리된 단발 호출 (라우팅/분류용, 사용자 대화에 안 남김)</summary>
    Task<string> AskIsolatedAsync(string prompt, CancellationToken ct = default);

    /// <summary>스트리밍 응답 (토큰 단위 yield)</summary>
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);

    /// <summary>프로바이더 준비 (로그인, 연결 등)</summary>
    Task<bool> PrepareAsync();
}
