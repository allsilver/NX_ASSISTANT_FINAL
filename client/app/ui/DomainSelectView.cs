// ui/DomainSelectView.cs
// 화면 2: DB 도메인 선택 (MainForm 에서 분리 — 순수 UI)

namespace NxAssistant.UI;

// ════════════════════════════════════════════════════════════
// 화면 2: DB 도메인 선택
// ════════════════════════════════════════════════════════════
internal sealed class DomainSelectView : Panel
{
    public DomainSelectView(Action onBack, Action onHome, Action<string, string> onPick)
    {
        BackColor = Palette.Bg;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Palette.Bg, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 40, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cards = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Palette.Bg };
        cards.Controls.Add(new FieldCard(IconKind.Doc,     "설계수순서", "기구 설계 표준 체크리스트", () => onPick("MECH_STANDARD", "설계수순서")));
        cards.Controls.Add(new FieldCard(IconKind.Coin,    "DFC", "재료비 절감안", () => onPick("CMF_DFC", "DFC")));
        cards.Controls.Add(new FieldCard(IconKind.Atom,    "CMF", "소재·공정 정보 및 CMF 사양", () => onPick("CMF_ISSUE", "CMF")));
        cards.Controls.Add(new FieldCard(IconKind.Spanner, "DFM", "제조(메탈·금형·지그) 고려 설계", () => onPick("MECHA_DFM", "DFM")));

        body.Controls.Add(TitleBlock.Make("어떤 분야를 검색하시겠습니까?", "검색할 설계 분야를 선택하면 채팅 화면으로 이동합니다."), 0, 0);
        body.Controls.Add(cards, 0, 1);
        Controls.Add(body);
        Controls.Add(new TopBar("DB 조회", onBack, onHome));
    }
}
