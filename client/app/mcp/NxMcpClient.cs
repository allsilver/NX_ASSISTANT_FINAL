// mcp/NxMcpClient.cs
// NX MCP 서버(client/nx-mcp/nx_mcp_server.py)에 요청하는 클라이언트
// NX는 각자 PC에서 실행되므로 localhost 고정

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NxAssistant.Mcp;

public class NxMcpClient
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Uri        _baseUri;

    public NxMcpClient()
    {
        _baseUri = new Uri($"http://127.0.0.1:{NxAssistant.AppConfig.NxMcpPort}/");
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
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

    public async Task<string> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(new Uri(_baseUri, "status"), ct);
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e)
        {
            return $"NX MCP 연결 실패: {e.Message}";
        }
    }

    // Phase 1: status만 구현. 추후 NX 제어 기능 추가 예정
}
