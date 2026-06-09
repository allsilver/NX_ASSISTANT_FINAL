// mcp/DbMcpClient.cs
// DB MCP 서버(server/db-mcp/server.py)에 HTTP 요청하는 클라이언트

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NxAssistant.Mcp;

public record DbMcpResult(
    string Question,
    string RewrittenQuery,
    string Domain,
    int    Case,
    string Answer
);

public record RouteResult(string Intent);

public class DbMcpClient
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Uri        _baseUri;

    public DbMcpClient()
    {
        var url   = Environment.GetEnvironmentVariable("NX_ASSISTANT_DB_MCP_URL")
                    ?? "http://127.0.0.1:8766";
        var token = Environment.GetEnvironmentVariable("DB_MCP_TOKEN") ?? "";

        _baseUri = new Uri(url.TrimEnd('/') + "/");
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>1차 라우팅 — intent 분류만</summary>
    public async Task<RouteResult> RouteAsync(
        string question,
        string historyText,
        CancellationToken ct = default)
    {
        var payload = new { question, history = historyText };
        var result  = await PostAsync<JsonElement>("/mech/route", payload, ct);
        var intent  = result.TryGetProperty("intent", out var v) ? v.GetString() ?? "chat" : "chat";
        return new RouteResult(intent);
    }

    /// <summary>전체 RAG 파이프라인 — 라우팅+검색+답변</summary>
    public async Task<DbMcpResult> AskAsync(
        string                              question,
        string                              rewrittenQuery,
        string                              domain,
        int                                 caseNum,
        List<Dictionary<string, string>>    history,
        string                              synonymHint = "",
        CancellationToken                   ct          = default)
    {
        var payload = new
        {
            question,
            rewritten_query = rewrittenQuery,
            domain,
            @case       = caseNum,
            history,
            synonym_hint = synonymHint,
        };

        var result = await PostAsync<JsonElement>("/mech/ask", payload, ct);

        return new DbMcpResult(
            Question:       GetStr(result, "question"),
            RewrittenQuery: GetStr(result, "rewritten_query"),
            Domain:         GetStr(result, "domain"),
            Case:           result.TryGetProperty("case", out var c) ? c.GetInt32() : 1,
            Answer:         GetStr(result, "answer")
        );
    }

    /// <summary>서버 상태 확인</summary>
    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(new Uri(_baseUri, "health"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> PostAsync<T>(string path, object payload, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(payload, _jsonOpts);
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, path.TrimStart('/')))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"DB MCP 오류 {(int)resp.StatusCode}: {text[..Math.Min(200, text.Length)]}");

        return JsonSerializer.Deserialize<T>(text, _jsonOpts)!;
    }

    private static string GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}
