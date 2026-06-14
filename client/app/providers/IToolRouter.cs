// providers/IToolRouter.cs
// [공유 계약 — Claude 소유 / Codex 읽기 전용]
// 자연어 명령 → ToolCall 변환 계약. Codex가 providers/tooling/ 에 LLM 기반으로 구현한다.
// ⚠️ 이 시그니처는 Claude만 변경. Codex는 구현만. 변경 필요 시 HANDOFF_REQUESTS.md 에 요청.

namespace NxAssistant.Providers;

public interface IToolRouter
{
    /// <param name="mode">사용할 툴 카탈로그: "nx" 또는 "automation"</param>
    /// <param name="command">사용자 자연어 명령</param>
    /// <returns>선택된 ToolCall. 매칭 없으면 ToolCall.None()</returns>
    Task<ToolCall> RouteAsync(string mode, string command, CancellationToken ct = default);
}
