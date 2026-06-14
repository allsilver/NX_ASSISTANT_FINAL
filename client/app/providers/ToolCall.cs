// providers/ToolCall.cs
// [공유 계약 — Claude 소유 / Codex 읽기 전용]
// LLM 도구 선택 결과 DTO. 라우터(Codex 구현)가 자연어를 이 구조로 변환하고,
// 세션(Claude)이 ToolName 을 실제 실행(NX 브리지 / 자동화 툴)으로 매핑한다.
// ⚠️ 이 시그니처는 Claude만 변경. 변경 필요 시 HANDOFF_REQUESTS.md 에 요청.

namespace NxAssistant.Providers;

public sealed record ToolCall(
    string ToolName,                            // 카탈로그 함수명 (예: "nx_extrude"). 매칭 없으면 ""
    IReadOnlyDictionary<string, string> Args,   // 인자 맵 (예: { "distance": "0.5" })
    double Confidence = 1.0,                     // LLM 확신도 0~1 (낮으면 세션이 되물을 수 있음)
    string? Clarification = null                 // 인자 부족 등으로 사용자에게 되물을 말 (있으면 실행 대신 질문)
)
{
    public bool Matched => !string.IsNullOrWhiteSpace(ToolName);

    public static ToolCall None(string? clarification = null) =>
        new("", new Dictionary<string, string>(), 0, clarification);
}
