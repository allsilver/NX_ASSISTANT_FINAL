// router/RouterClient.cs
// 전체 대화 흐름 조율
// 1차 라우터 → 2차 intent LLM → MCP 호출 → 답변 LLM

using NxAssistant.History;
using NxAssistant.Mcp;
using NxAssistant.Providers;

namespace NxAssistant.Router;

public record ChatResult(string Answer, string Intent, string Domain);

public class RouterClient
{
    private readonly DbMcpClient  _dbMcp;
    private readonly NxMcpClient  _nxMcp;
    private readonly HistoryManager _history;

    public RouterClient(DbMcpClient dbMcp, NxMcpClient nxMcp, HistoryManager history)
    {
        _dbMcp   = dbMcp;
        _nxMcp   = nxMcp;
        _history = history;
    }

    public async Task<ChatResult> HandleAsync(
        string          userMessage,
        ILlmProvider    provider,
        CancellationToken ct = default)
    {
        // ── 1차 라우팅 ────────────────────────────────────────────
        var routeResult = await _dbMcp.RouteAsync(
            userMessage,
            _history.ForRouter(),
            ct);

        var intent = routeResult.Intent;

        // ── intent별 처리 ─────────────────────────────────────────
        return intent switch
        {
            "db_search"  => await HandleDbSearchAsync(userMessage, provider, ct),
            "nx_control" => await HandleNxControlAsync(userMessage, provider, ct),
            "chat"       => await HandleChatAsync(userMessage, provider, ct),
            _            => await HandleChatAsync(userMessage, provider, ct),
        };
    }

    // ── DB 검색 흐름 ──────────────────────────────────────────────
    private async Task<ChatResult> HandleDbSearchAsync(
        string         question,
        ILlmProvider   provider,
        CancellationToken ct)
    {
        // DB MCP 서버에 라우팅+RAG+답변 한번에 요청
        // (2차 DB intent LLM은 서버 내부에서 처리)
        var result = await _dbMcp.AskAsync(
            question:       question,
            rewrittenQuery: question,     // 서버에서 재작성
            domain:         "",           // 서버에서 자동 선택
            caseNum:        1,            // 서버에서 자동 판단
            history:        _history.ForAnswer(),
            ct:             ct);

        return new ChatResult(result.Answer, "db_search", result.Domain);
    }

    // ── NX 제어 흐름 ──────────────────────────────────────────────
    private async Task<ChatResult> HandleNxControlAsync(
        string         question,
        ILlmProvider   provider,
        CancellationToken ct)
    {
        // Phase 1: NX 상태만 확인하고 GPT/Gauss에게 넘김
        var nxAvailable = await _nxMcp.IsAvailableAsync(ct);
        var nxStatus    = nxAvailable
            ? await _nxMcp.GetStatusAsync(ct)
            : "NX MCP 서버가 실행되지 않았습니다.";

        var prompt = $"[NX 제어 모드]\nNX 상태: {nxStatus}\n\n사용자 질문: {question}\n\n" +
                     "NX 제어 관련 안내를 한국어로 답변하세요. " +
                     "실제 NX 작업을 수행하려면 사용자 확인이 필요하다고 명시하세요.";

        var answer = await provider.ChatAsync(prompt, ct);
        return new ChatResult(answer, "nx_control", "");
    }

    // ── 잡담/일반 대화 흐름 ───────────────────────────────────────
    private async Task<ChatResult> HandleChatAsync(
        string         question,
        ILlmProvider   provider,
        CancellationToken ct)
    {
        var historyText = _history.ForIntent();
        var prompt = string.IsNullOrEmpty(historyText)
            ? question
            : $"[이전 대화]\n{historyText}\n\n질문: {question}";

        var answer = await provider.ChatAsync(prompt, ct);
        return new ChatResult(answer, "chat", "");
    }
}
