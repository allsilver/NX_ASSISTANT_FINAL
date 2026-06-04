// history/HistoryManager.cs
// 대화 히스토리 관리
// Phase 1: 최근 N턴 자르기
// Phase 2 (예정): Rolling Summary (10턴 초과 시 오래된 턴 자동 요약 압축)

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NxAssistant.History;

public record ChatMessage(string Role, string Content);

public class HistoryManager
{
    private readonly List<ChatMessage> _history = new();

    // LLM 단계별 전달 턴 수
    public int RouterTurns  { get; set; } = 1;   // 1차 라우터
    public int IntentTurns  { get; set; } = 2;   // 2차 DB/NX LLM
    public int AnswerTurns  { get; set; } = 3;   // 답변 LLM

    public IReadOnlyList<ChatMessage> All => _history;

    public void Add(string role, string content)
    {
        _history.Add(new ChatMessage(role, content));
    }

    public void Clear()
    {
        _history.Clear();
    }

    /// <summary>LLM 단계별로 필요한 히스토리 텍스트 반환</summary>
    public string GetForLlm(int maxTurns)
    {
        if (_history.Count == 0 || maxTurns <= 0)
            return "";

        // 최근 maxTurns 턴 (1턴 = user + assistant 쌍)
        var recent = _history
            .TakeLast(maxTurns * 2)
            .ToList();

        var sb = new StringBuilder();
        foreach (var msg in recent)
        {
            var role = msg.Role == "user" ? "사용자" : "어시스턴트";
            // 어시스턴트 답변은 첫 줄만 (수치 혼입 방지)
            var content = msg.Role == "assistant"
                ? TruncateToFirstLine(msg.Content)
                : msg.Content;
            sb.AppendLine($"{role}: {content}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>1차 라우터용 히스토리</summary>
    public string ForRouter()  => GetForLlm(RouterTurns);

    /// <summary>2차 intent LLM용 히스토리</summary>
    public string ForIntent()  => GetForLlm(IntentTurns);

    /// <summary>답변 LLM용 히스토리 (List 형태, 서버 API 전달용)</summary>
    public List<Dictionary<string, string>> ForAnswer()
    {
        return _history
            .TakeLast(AnswerTurns * 2)
            .Select(m => new Dictionary<string, string>
            {
                ["role"]    = m.Role,
                ["content"] = m.Content,
            })
            .ToList();
    }

    private static string TruncateToFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var firstLine = text.Split('\n')[0];
        return firstLine.Length > 100
            ? firstLine[..100] + "..."
            : firstLine + "...";
    }

    // ── Phase 2 예정: Rolling Summary ────────────────────────────
    // public string GetWithSummary(int maxTurns) { ... }
    // private string SummarizeOldTurns() { ... }
}
