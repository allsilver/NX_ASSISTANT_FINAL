// providers/GaussProvider.cs
// Gauss API REST 호출 프로바이더
// DB MCP 서버의 /mech/ask를 통해 RAG+LLM 결과를 받아옴

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

    /// <summary>현재 DB 도메인 키. /mech/ask 요청에 실림. (LlmSession.SetDomain 으로 설정)</summary>
    public string Domain  { get; set; } = "";

    /// <summary>검색할 db_key 목록. 비어있으면 서버가 도메인 전체 검색. (LlmSession.SetDbKeys 로 설정)</summary>
    public string[] DbKeys { get; set; } = Array.Empty<string>();

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
    /// DB MCP 서버 /mech/ask 호출 (RAG 검색 + Gauss 답변 완성).
    /// 검색 범위는 현재 Domain. (db_keys 미지정 → 서버가 도메인 전체 검색)
    /// </summary>
    public async Task<string> ChatAsync(string prompt, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["question"] = prompt,
            ["domain"]   = Domain,
            ["case"]     = 1,
            ["history"]  = Array.Empty<object>(),
        };
        if (DbKeys is { Length: > 0 })
            payload["db_keys"] = DbKeys;
        var body = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(_dbMcpUrl), "/mech/ask"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DB MCP 오류 {(int)resp.StatusCode}: {text[..Math.Min(300, text.Length)]}");

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
