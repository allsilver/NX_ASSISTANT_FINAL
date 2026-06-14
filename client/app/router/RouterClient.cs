// router/RouterClient.cs
// 전체 대화 흐름 조율 (팀장 역할)
//
// 흐름:
//   질문 → 1차 라우터(Provider 격리 호출로 분류)
//        → db_search  : DB MCP 서버 검색 (없으면 안내)
//        → nx_control : NX MCP 상태 + Provider 답변
//        → chat       : Provider 답변
//
// [우회 모드] NX_ASSISTANT_MODE=vdi 또는 분류 실패 시 → 바로 chat

using NxAssistant.History;
using NxAssistant.Mcp;
using NxAssistant.Providers;

namespace NxAssistant.Router;

public record ChatResult(string Answer, string Intent, string Domain);

public class RouterClient
{
    private readonly DbMcpClient    _dbMcp;
    private readonly NxMcpClient    _nxMcp;
    private readonly HistoryManager _history;

    private readonly bool _bypassByEnv;

    public RouterClient(DbMcpClient dbMcp, NxMcpClient nxMcp, HistoryManager history)
    {
        _dbMcp   = dbMcp;
        _nxMcp   = nxMcp;
        _history = history;

        _bypassByEnv = NxAssistant.AppConfig.IsVdi;
    }

    public async Task<ChatResult> HandleAsync(
        string            userMessage,
        ILlmProvider      provider,
        CancellationToken ct = default)
    {
        // ── 우회 모드: 1차 라우터 건너뛰고 바로 답변 ──────────────
        if (_bypassByEnv)
        {
            NxAssistant.Program.Log("[Router] 우회 모드(vdi) → 바로 chat");
            return await HandleChatAsync(userMessage, provider, ct);
        }
        NxAssistant.Program.Log("[Router] 라우터 모드 진입");

        // ── 1차 라우터: Provider 격리 호출로 분류 ─────────────────
        string intent;
        try
        {
            intent = await ClassifyAsync(userMessage, provider, ct);
        }
        catch (Exception)
        {
            // 분류 실패 → 안전하게 chat 처리
            return await HandleChatAsync(userMessage, provider, ct);
        }

        // ── intent별 분기 ─────────────────────────────────────────
        return intent switch
        {
            "db_search"  => await HandleDbSearchAsync(userMessage, provider, ct),
            "nx_control" => await HandleNxControlAsync(userMessage, provider, ct),
            _            => await HandleChatAsync(userMessage, provider, ct),
        };
    }

    // ── 1차 라우터 분류 (격리 호출) ───────────────────────────────
    private async Task<string> ClassifyAsync(
        string question, ILlmProvider provider, CancellationToken ct)
    {
        var prompt =
            "너는 질문 분류기다. 아래 질문을 정확히 한 단어로만 분류하라. 다른 말은 절대 하지 마라.\n\n" +
            "분류 기준:\n" +
            "- db_search: 설계 표준, 수치, 규격, 가이드, 치수 등 DB 검색이 필요한 질문\n" +
            "- nx_control: NX CAD 프로그램 조작, 모델링 작업 요청\n" +
            "- browser: 웹사이트, 포털, PLM 등 브라우저 작업\n" +
            "- chat: 그 외 인사, 잡담, 일반 대화\n\n" +
            $"질문: {question}\n\n답(한 단어):";

        var t0 = DateTime.Now;
        NxAssistant.Program.Log($"[Router] 분류 호출 시작: \"{question}\"");

        var raw = await provider.AskIsolatedAsync(prompt, ct);
        var elapsed = (DateTime.Now - t0).TotalSeconds;
        var cleaned = (raw ?? "").Trim().ToLowerInvariant();

        NxAssistant.Program.Log($"[Router] 분류 응답({elapsed:F1}초) raw=\"{raw}\" cleaned=\"{cleaned}\"");

        // 응답에서 카테고리 키워드 추출 (LLM이 문장으로 답해도 잡아냄)
        string result;
        if      (cleaned.Contains("db_search"))  result = "db_search";
        else if (cleaned.Contains("nx_control")) result = "nx_control";
        else if (cleaned.Contains("browser"))    result = "browser";
        else                                     result = "chat";

        NxAssistant.Program.Log($"[Router] 최종 분류: {result}");
        return result;
    }

    // ── DB 검색 흐름 ──────────────────────────────────────────────
    private async Task<ChatResult> HandleDbSearchAsync(
        string question, ILlmProvider provider, CancellationToken ct)
    {
        try
        {
            var result = await _dbMcp.AskAsync(
                question:       question,
                rewrittenQuery: question,
                domain:         "",
                caseNum:        1,
                history:        _history.ForAnswer(),
                ct:             ct);
            return new ChatResult(result.Answer, "db_search", result.Domain);
        }
        catch (Exception)
        {
            // DB 서버 없음 (VDI 등) → 안내 메시지
            return new ChatResult(
                "이 질문은 설계 표준 DB 검색이 필요합니다. " +
                "현재 DB 서버에 연결할 수 없습니다. (개발 중)",
                "db_search", "");
        }
    }

    // ── NX 제어 흐름 ──────────────────────────────────────────────
    private async Task<ChatResult> HandleNxControlAsync(
        string question, ILlmProvider provider, CancellationToken ct)
    {
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
        string question, ILlmProvider provider, CancellationToken ct)
    {
        var historyText = _history.ForIntent();
        var prompt = string.IsNullOrEmpty(historyText)
            ? question
            : $"[이전 대화]\n{historyText}\n\n질문: {question}";

        var answer = await provider.ChatAsync(prompt, ct);
        return new ChatResult(answer, "chat", "");
    }
}