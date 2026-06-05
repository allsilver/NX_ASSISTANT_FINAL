// providers/GaussProvider.cs
// Gauss API REST 호출 프로바이더
// DB MCP 서버의 /meg/ask를 통해 RAG+LLM 결과를 받아옴

using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NxAssistant.Providers;

public class GaussProvider : ILlmProvider
{
    public string Name    => "Gauss";
    public bool   IsReady => true;   // REST API는 항상 준비됨

    private readonly HttpClient _http;
    private readonly string     _dbMcpUrl;
    private readonly string     _token;

    public GaussProvider()
    {
        _dbMcpUrl = Environment.GetEnvironmentVariable("NX_ASSISTANT_DB_MCP_URL")
                    ?? "http://127.0.0.1:8766";
        _token    = Environment.GetEnvironmentVariable("DB_MCP_TOKEN") ?? "";

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        if (!string.IsNullOrEmpty(_token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
    }

    public Task<bool> PrepareAsync() => Task.FromResult(true);

    // 라우팅/분류용 격리 호출.
    // Gauss는 서버 경유라 별도 격리가 불필요(매 호출이 독립적).
    // 지금은 ChatAsync와 동일하게 처리. (추후 서버에 분류 전용 엔드포인트 추가 가능)
    public Task<string> AskIsolatedAsync(string prompt, CancellationToken ct = default)
        => ChatAsync(prompt, ct);

    /// <summary>
    /// DB MCP 서버에 이미 라우팅/RAG가 완료된 프롬프트를 전달.
    /// prompt는 서버에서 구성된 최종 답변 요청.
    /// </summary>
    public async Task<string> ChatAsync(string prompt, CancellationToken ct = default)
    {
        // 직접 Gauss API 호출 (DB MCP를 거치지 않고 순수 LLM 호출)
        // 용도: NX 제어, 브라우저 자동화, 잡담 등 DB 검색이 필요 없는 경우
        var payload = new { prompt };
        var body    = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(_dbMcpUrl), "/gauss/chat"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gauss API 오류 {(int)resp.StatusCode}: {text[..Math.Min(200, text.Length)]}");

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("answer", out var answer)
            ? answer.GetString() ?? ""
            : text;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Phase 1: 비스트리밍으로 전체 응답 받아서 한번에 yield
        // Phase 2: SSE 스트리밍 구현 예정
        var result = await ChatAsync(prompt, ct);
        yield return result;
    }
}
