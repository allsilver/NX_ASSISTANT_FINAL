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

    /// <summary>GPT용: 서버가 검색+컨텍스트+프롬프트 조립까지 한 "완성 프롬프트"를 반환. (/mech/ask, for_gpt=true)</summary>
    public async Task<string> GetGptPromptAsync(string question, string domain, string[] dbKeys, CancellationToken ct = default)
    {
        // [임시 검증용] NX_ASSISTANT_FAKE_DBPROMPT=1 이면 서버 호출 없이 예시 프롬프트 반환.
        //  목적: DB MCP 서버가 없는 VDI 에서 GPT 분기(질문→프롬프트→GPT 답변)를 검증.
        //  실서버 테스트/배포 시엔 이 환경변수를 끌 것. (자세한 배경: DEV_ENVIRONMENT.md 2장)
        var fake = Environment.GetEnvironmentVariable("NX_ASSISTANT_FAKE_DBPROMPT");
        if (!string.IsNullOrEmpty(fake) && fake.Trim() is "1" or "true" or "TRUE")
        {
            NxAssistant.Program.Log("[DbMcp] FAKE_DBPROMPT 사용 — 서버 미호출, 예시 프롬프트 반환");
            return FakeGptPrompt(question);
        }

        var payload = new Dictionary<string, object?>
        {
            ["question"] = question,
            ["domain"]   = domain,
            ["case"]     = 1,
            ["history"]  = Array.Empty<object>(),
            ["for_gpt"]  = true,
        };
        if (dbKeys is { Length: > 0 })
            payload["db_keys"] = dbKeys;

        var body = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "mech/ask"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"DB MCP ask(gpt) 오류 {(int)resp.StatusCode}: {text[..Math.Min(200, text.Length)]}");

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
    }

    // [임시 검증용] 서버가 만들 RAG 프롬프트를 흉내낸 예시. 실제 검색 결과 아님(가짜 컨텍스트).
    private static string FakeGptPrompt(string question) =>
$@"[테스트용 임시 컨텍스트 — DB MCP 서버 미연결 상태의 예시 데이터입니다]
아래는 검색된 기구 설계 표준 예시입니다.

- [MS-001] 모바일 외장 설계: 부품 간 최소 유격은 0.15mm 이상 확보한다.
- [WP-007] 방수 설계 가이드: IP68 달성을 위해 실링 가스켓 압축률은 20~30%로 한다.
- [FH-012] 폴더블 힌지 표준: 힌지 반복 내구는 20만 회 이상을 만족해야 한다.

위 자료에만 근거해 아래 질문에 한국어로 답하세요.
자료에 없는 내용은 추측하지 말고 ""자료에 없음""이라고 답하세요.
(이 답변은 임시 예시 데이터 기반임을 한 줄로 먼저 밝혀 주세요.)

[질문]
{question}";

    /// <summary>도메인의 선택 가능한 db_key 목록 (카드용). GET /mech/dbkeys?domain=X</summary>
    public async Task<List<DbKeyOption>> GetDbKeysAsync(string domain, CancellationToken ct = default)
    {
        var uri = new Uri(_baseUri, "mech/dbkeys?domain=" + Uri.EscapeDataString(domain));
        using var resp = await _http.GetAsync(uri, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"DB MCP dbkeys 오류 {(int)resp.StatusCode}: {text[..Math.Min(200, text.Length)]}");

        var list = new List<DbKeyOption>();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("db_options", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in arr.EnumerateArray())
            {
                var key = GetStr(it, "key");
                if (string.IsNullOrEmpty(key)) continue;
                list.Add(new DbKeyOption(
                    Key:         key,
                    DisplayName: it.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? key : key,
                    Description: it.TryGetProperty("description", out var d)   ? d.GetString()  ?? "" : "",
                    Default:     it.TryGetProperty("default", out var df) && df.ValueKind == JsonValueKind.True));
            }
        }
        return list;
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
