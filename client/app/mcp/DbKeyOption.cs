// mcp/DbKeyOption.cs
// 도메인 안 db_key 1개의 메타(카드 표시용). 서버 /mech/dbkeys 응답 항목과 1:1.
// DbMcpClient 와 DbKeySelectView 가 공유하며, 네트워크 의존이 없어 UI 프리뷰에서도 링크해 쓴다.

namespace NxAssistant.Mcp;

public record DbKeyOption(
    string Key,
    string DisplayName,
    string Description,
    bool   Default
);
