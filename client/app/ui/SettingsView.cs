// ui/SettingsView.cs
// 화면: 설정 — 현재 항목: "관심분야 재설정", "외부 AI 재로그인"
//   각 항목은 클릭 가능한 행(제목 + 설명 + ›). 동작은 호출자가 콜백으로 주입.

namespace NxAssistant.UI;

internal sealed class SettingsView : Panel
{
    public SettingsView(Action onBack, Action onHome, Action onResetInterests, Action onReloginAi)
    {
        BackColor = Palette.Bg;

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = false, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(20, 14, 20, 14), BackColor = Palette.Bg,
        };
        list.Controls.Add(new SettingsRow("관심분야 재설정", "검색할 세부 DB(표준 분야)를 다시 선택합니다.", onResetInterests));
        list.Controls.Add(new SettingsRow("외부 AI 재로그인", "외부 AI(GPT) 계정에 다시 로그인합니다.", onReloginAi));

        var titleHost = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Palette.Bg };
        titleHost.Controls.Add(TitleBlock.Make("설정", "원하는 항목을 선택하세요."));

        Controls.Add(list);
        Controls.Add(titleHost);
        Controls.Add(new TopBar("설정", onBack, onHome));
    }
}

// 설정 항목 한 줄 (제목 + 설명 + 오른쪽 ›). 클릭 시 콜백.
internal sealed class SettingsRow : RoundedPanel
{
    private bool _hover;

    public SettingsRow(string title, string desc, Action onClick)
    {
        Radius   = 14;
        Cursor   = Cursors.Hand;
        Padding  = new Padding(16, 12, 14, 12);
        Margin   = new Padding(0, 0, 0, 12);
        Width    = 520;                       // 부모 FlowLayoutPanel 폭에 맞춰 아래서 늘림
        Height   = 70;
        BackColor = Palette.Surface;
        BorderColor = Palette.Border;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));

        var textCol = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
        textCol.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Malgun Gothic", 11.5F, FontStyle.Bold), ForeColor = Palette.Text, Margin = new Padding(0, 0, 0, 3) });
        textCol.Controls.Add(new Label { Text = desc,  AutoSize = true, Font = new Font("Malgun Gothic", 8.5F), ForeColor = Palette.Muted, Margin = new Padding(0) });

        var chevron = new Label { Text = "›", AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 15F), ForeColor = Palette.Muted, TextAlign = ContentAlignment.MiddleCenter };

        grid.Controls.Add(textCol, 0, 0);
        grid.Controls.Add(chevron, 1, 0);
        Controls.Add(grid);

        void Hook(Control c)
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => onClick();
            c.MouseEnter += (_, _) => { _hover = true;  Apply(); };
            c.MouseLeave += (_, _) => { _hover = false; Apply(); };
            foreach (Control ch in c.Controls) Hook(ch);
        }
        Hook(this);
        Apply();
    }

    // 부모 FlowLayoutPanel 폭에 맞춰 가로로 늘림
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent != null) { FitWidth(); Parent.SizeChanged += (_, _) => FitWidth(); }
    }
    private void FitWidth()
    {
        if (Parent is null) return;
        int w = Parent.ClientSize.Width - Parent.Padding.Horizontal - Margin.Horizontal;
        if (w > 0) Width = w;
    }

    private void Apply()
    {
        BackColor   = _hover ? Palette.CardHover : Palette.Surface;
        BorderColor = _hover ? Palette.Accent    : Palette.Border;
        Invalidate();
    }
}
