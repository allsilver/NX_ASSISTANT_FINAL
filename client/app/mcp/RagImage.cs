// mcp/RagImage.cs
// RAG 검색에 매핑된 표준 이미지. 서버 /mech/ask 응답의 images[] 항목.
//   Name      : 파일명 (예: "foldable Damper Front Damper Front 설계 Flip.png")
//   ScorePct  : 관련성 % (min-max 정규화, null 가능) — 현재 UI 표시는 보류, 데이터만 보존
//   Data      : PNG 바이트 (서버에서 base64 로 받은 것을 디코드한 결과)

namespace NxAssistant.Mcp;

public sealed record RagImage(string Name, int? ScorePct, byte[] Data);
