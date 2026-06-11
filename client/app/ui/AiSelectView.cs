// ui/AiSelectView.cs
// 화면 0: AI 선택 (MainForm 에서 분리 — 순수 UI, 프로바이더 의존 없음)

namespace NxAssistant.UI;

// ════════════════════════════════════════════════════════════
// 화면 0: AI 선택
// ════════════════════════════════════════════════════════════
internal sealed class AiSelectView : Panel
{
    public AiSelectView(Action<string> onPick)
    {
        BackColor = Palette.Bg;

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Palette.Bg,
            ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 44, 0, 0),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 제목
        var titleBox = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Palette.Bg, Padding = new Padding(16, 0, 16, 40) };
        var sub2 = MakeCenter("최초 1회 설정 후, 채팅창에서 변경 가능합니다.", 9F, false, Palette.Muted);
        var g2 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Palette.Bg };
        var sub1 = MakeCenter("사용할 AI를 선택하세요", 11F, true, Palette.Muted);
        var g1 = new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Palette.Bg };
        var head = MakeCenter("환영합니다!", 16F, true, Palette.Text);
        var g0 = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Palette.Bg };
        titleBox.Controls.Add(sub2); titleBox.Controls.Add(g2); titleBox.Controls.Add(sub1);
        titleBox.Controls.Add(g1); titleBox.Controls.Add(head); titleBox.Controls.Add(g0);

        // 카드 2개 세로
        var cardCol = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Anchor = AnchorStyles.None, BackColor = Palette.Bg,
        };
        cardCol.Controls.Add(MakeAiCard(LogoKind.Gauss, "Gauss",
            new[] { "gauss:o4-instruct 모델", "사내 서버를 이용합니다." }, () => onPick("Gauss")));
        cardCol.Controls.Add(MakeAiCard(LogoKind.Gpt, "GPT",
            new[] { "GPT-5.5 모델", "개인 계정 토큰을 사용하며, 최초 1회 로그인이 필요합니다." }, () => onPick("GPT")));

        body.Controls.Add(titleBox, 0, 0);
        body.Controls.Add(cardCol,  0, 1);

        Controls.Add(body);
        Controls.Add(new TopBar("\U0001F6E1  AI 설계 어시스턴트"));
    }

    private static Label MakeCenter(string text, float size, bool bold, Color color)
    {
        var font = new Font("Malgun Gothic", size, bold ? FontStyle.Bold : FontStyle.Regular);
        int h = TextRenderer.MeasureText("가", font).Height + 6;
        return new Label { Text = text, AutoSize = false, Dock = DockStyle.Top, Height = h, Font = font, ForeColor = color, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0) };
    }

    private Control MakeAiCard(LogoKind logo, string name, string[] points, Action onClick)
    {
        var card = new RoundedPanel
        {
            Size = new Size(548, 210), Margin = new Padding(0, 0, 0, 22),
            BackColor = Palette.Surface, BorderColor = Palette.Border,
            Radius = 18, Cursor = Cursors.Hand, Padding = new Padding(28, 0, 28, 0),
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var leftWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        // 로고 PNG에 글자(Gauss/ChatGPT)가 포함돼 있어 별도 텍스트 라벨은 두지 않음.
        var logoIcon = new BrandLogo { Kind = logo, Dock = DockStyle.Fill, Margin = new Padding(0) };
        leftWrap.Controls.Add(logoIcon);

        var rightWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var checks = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        foreach (var p in points) checks.Controls.Add(new CheckItem(p) { Width = 320 });
        rightWrap.Controls.Add(checks);
        rightWrap.Resize += (_, _) => checks.Location = new Point(8, Math.Max(0, (rightWrap.Height - checks.Height)/2));

        grid.Controls.Add(leftWrap, 0, 0);
        grid.Controls.Add(rightWrap, 1, 0);
        card.Controls.Add(grid);

        void Enter(object? s, EventArgs e) { card.BackColor = Palette.CardHover; card.BorderColor = Palette.Accent; card.Invalidate(); }
        void Leave(object? s, EventArgs e) { card.BackColor = Palette.Surface; card.BorderColor = Palette.Border; card.Invalidate(); }
        void Hook(Control c) { c.MouseEnter += Enter; c.MouseLeave += Leave; c.Click += (_, _) => onClick(); c.Cursor = Cursors.Hand; foreach (Control ch in c.Controls) Hook(ch); }
        Hook(card);
        return card;
    }
}
