// ui/FieldSelectView.cs
// 화면 1: 분야 선택 (MainForm 에서 분리 — 순수 UI)

namespace NxAssistant.UI;

// ════════════════════════════════════════════════════════════
// 화면 1: 분야 선택
// ════════════════════════════════════════════════════════════
internal sealed class FieldSelectView : Panel
{
    public FieldSelectView(Action onBack, Action onDbQuery, Action onNxControl, Action onAuto)
    {
        BackColor = Palette.Bg;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Palette.Bg, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 40, 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cards = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Palette.Bg };
        cards.Controls.Add(new FieldCard(IconKind.Search, "DB 조회", "설계에 필요한 데이터 검색", onDbQuery));
        cards.Controls.Add(new FieldCard(IconKind.Gear, "NX 제어", "자연어로 NX 기능을 실행", onNxControl));
        cards.Controls.Add(new FieldCard(IconKind.Bolt, "자동화 기능", "반복 작업 자동화", onAuto));

        body.Controls.Add(TitleBlock.Make("어떤 부분을 도와드릴까요?", "사용할 작업 모드를 선택하세요."), 0, 0);
        body.Controls.Add(cards, 0, 1);
        Controls.Add(body);
        Controls.Add(new TopBar("\U0001F6E1  AI 설계 어시스턴트", onBack: onBack));
    }
}
