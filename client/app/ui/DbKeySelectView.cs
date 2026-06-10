// ui/DbKeySelectView.cs
// 화면 2.5: DB 세부 선택 (db_key 복수선택) — 남색/밝은 톤(전역 Palette)
//
// [레이아웃 근본 설계]
//   중앙 영역 = TableLayoutPanel(Dock=Fill, 2열 50/50)
//     · 카드 행: Absolute(고정 높이) → 위에서부터 채움
//     · 카운터 행: Absolute
//     · 마지막 스페이서 행: Percent 100 → 남는 세로공간을 흡수 ⇒ 카드가 항상 "맨 위" 고정
//   ※ AutoScroll 패널 + 도킹 자식 조합(상단 정렬이 불안정)을 쓰지 않는다. 이게 그동안의 근본 원인이었음.
//
//   카드 = 흰 카드 → 선택 시 옅은 하늘색 배경 + 남색 테두리(글자 중앙정렬, 세로 2배)
//   제목/안내 = 중앙정렬 + 줄바꿈. "설정 - 관심분야 재설정"은 칩(블록).
//   "N개 선택 됨" = 카드 아래 우측(회색).

using NxAssistant.Mcp;

namespace NxAssistant.UI;

// 도메인별 관심분야 페이지 문구 (MainForm / PreviewShell 공용)
//   전 도메인 공통 고정 문구. 카드 내용만 서버 db json 으로 자동으로 달라진다(제목/소제목은 불변).
internal static class DbKeyPrompts
{
    public static (string title, string subtitle) For(string domain)
        => ("어떤 항목에 관심이 있나요?", "선택한 범위로 검색합니다. (복수선택 가능)");
}

internal sealed class DbKeySelectView : Panel
{
    private const int CardHeight = 192;   // 카드 세로 (기존 96 의 2배)
    private const int CardMargin = 7;
    private const int CounterRowH = 38;   // 카운터 행(밑 잘림 방지 위해 넉넉히)

    private readonly List<DbKeyCard> _cards = new();
    private readonly Label _counter = new()
    {
        AutoSize = true, Font = new Font("Malgun Gothic", 8.5F),
        ForeColor = Color.FromArgb(150, 150, 150), BackColor = Palette.Bg,
        Anchor = AnchorStyles.Right, Margin = new Padding(CardMargin, 6, CardMargin, 6),
    };

    public DbKeySelectView(
        string             title,
        string             subtitle,
        List<DbKeyOption>  options,
        ISet<string>       preChecked,
        Action             onBack,
        Action             onHome,
        Action<string[]>   onConfirm)
    {
        BackColor = Palette.Bg;

        // ── 하단: 선택 완료 ──────────────────────────────────
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Palette.Bg, Padding = new Padding(20, 12, 20, 16) };
        var confirm = new PrimaryButton("선택 완료") { Dock = DockStyle.Fill };
        confirm.Click += (_, _) =>
        {
            var keys = _cards.Where(c => c.Checked).Select(c => c.Key).ToArray();
            if (keys.Length == 0) { MessageBox.Show("하나 이상 선택해 주세요.", "안내"); return; }
            onConfirm(keys);
        };
        footer.Controls.Add(confirm);

        // ── 가운데: 카드 2열 그리드 (상단 고정 + 중앙정렬) ───
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Palette.Bg, ColumnCount = 2,
            Padding = new Padding(13, 6, 13, 6),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        foreach (var opt in options)
        {
            var card = new DbKeyCard(opt.Key, opt.DisplayName, opt.Description, preChecked.Contains(opt.Key))
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,   // 가로 채움(반응형) + 세로 중앙
                Height = CardHeight,
                Margin = new Padding(CardMargin),
            };
            card.Toggled += UpdateCounter;
            _cards.Add(card);
        }

        int cardRows = (_cards.Count + 1) / 2;
        for (int r = 0; r < cardRows; r++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, CardHeight + CardMargin * 2));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, CounterRowH));   // 카운터 행
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));            // 스페이서 ⇒ 카드 상단 고정
        grid.RowCount = cardRows + 2;

        for (int i = 0; i < _cards.Count; i++)
            grid.Controls.Add(_cards[i], i % 2, i / 2);
        grid.Controls.Add(_counter, 0, cardRows);
        grid.SetColumnSpan(_counter, 2);   // 두 열 가로질러 우측 정렬

        // ── 상단: 제목 + 안내 ────────────────────────────────
        var titleHost = MakeTitle(title, subtitle);

        // 도킹: grid(Fill) → footer(Bottom) → titleHost(Top) → TopBar(Top, 최상단)
        Controls.Add(grid);
        Controls.Add(footer);
        Controls.Add(titleHost);
        Controls.Add(new TopBar("관심 분야", onBack, onHome));

        UpdateCounter();
    }

    private void UpdateCounter() => _counter.Text = $"{_cards.Count(c => c.Checked)}개 선택 됨";

    // 제목/안내: AutoSize 한 줄 라벨을 줄 단위로 쌓음 (부풀지도, 잘리지도 않음). 가운데 정렬.
    private static Panel MakeTitle(string title, string subtitle)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, BackColor = Palette.Bg, Padding = new Padding(20, 64, 20, 10),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 도메인 선택 페이지와 제목 높이 맞춤(상단 ~68px)

        static Label Center(string t, float size, FontStyle style, Color color, int top) => new()
        {
            Text = t, AutoSize = true, Anchor = AnchorStyles.None,
            Font = new Font("Malgun Gothic", size, style), ForeColor = color,
            TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, top, 0, 0),
        };

        var heading = Center(title, 14F, FontStyle.Bold, Palette.Text, 0);
        var sub1    = Center(subtitle, 8F, FontStyle.Regular, Palette.Muted, 6);

        // 셋째 줄: "관심 분야는 [설정 - 관심분야 재설정]" (한 줄, 가운데)
        var line3 = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Palette.Bg, Margin = new Padding(0, 6, 0, 0),
        };
        line3.Controls.Add(new Label { Text = "관심 분야는", AutoSize = true, Font = new Font("Malgun Gothic", 8F), ForeColor = Palette.Muted, Margin = new Padding(0, 5, 0, 0) });
        line3.Controls.Add(new Chip("설정 - 관심분야 재설정"));

        // 넷째 줄: 나머지 문장 (수동 줄나눔 → 자동 줄바꿈 불필요)
        var sub2 = Center("메뉴에서 변경할 수 있어요", 8F, FontStyle.Regular, Palette.Muted, 4);

        table.Controls.Add(heading, 0, 0);
        table.Controls.Add(sub1, 0, 1);
        table.Controls.Add(line3, 0, 2);
        table.Controls.Add(sub2, 0, 3);
        return table;
    }
}

// "설정 - 관심분야 재설정" 같은 메뉴 경로를 보여주는 칩(블록). (볼드 없음)
internal sealed class Chip : RoundedPanel
{
    public Chip(string text)
    {
        Radius = 6; BackColor = Color.FromArgb(233, 236, 241); BorderColor = Palette.Border;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8, 3, 8, 3); Margin = new Padding(5, 1, 5, 0);
        Controls.Add(new Label
        {
            Text = text, AutoSize = true, Margin = new Padding(0),
            Font = new Font("Malgun Gothic", 8F), ForeColor = Palette.Text,
        });
    }
}

// db_key 카드 1개 — 체크 없이 선택 시 색 변경(흰→옅은 하늘+남색 테두리). 글자 중앙정렬.
internal sealed class DbKeyCard : RoundedPanel
{
    public string Key { get; }
    public event Action? Toggled;

    private bool _checked;
    public bool Checked { get => _checked; set { _checked = value; ApplyVisual(); } }

    private readonly Label _name;
    private readonly Label? _desc;

    public DbKeyCard(string key, string displayName, string description, bool isChecked)
    {
        Key = key;
        Radius  = 16;
        Cursor  = Cursors.Hand;
        Padding = new Padding(12, 10, 12, 10);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        bool hasDesc = !string.IsNullOrWhiteSpace(description);
        _name = new Label
        {
            Text = displayName, AutoSize = false, Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", 12.5F, FontStyle.Bold),
            TextAlign = hasDesc ? ContentAlignment.BottomCenter : ContentAlignment.MiddleCenter,
        };

        if (hasDesc)
        {
            grid.RowCount = 2;
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            _desc = new Label
            {
                Text = description, AutoSize = false, Dock = DockStyle.Fill,
                Font = new Font("Malgun Gothic", 8.5F), TextAlign = ContentAlignment.TopCenter,
            };
            grid.Controls.Add(_name, 0, 0);
            grid.Controls.Add(_desc, 0, 1);
        }
        else
        {
            grid.RowCount = 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.Controls.Add(_name, 0, 0);
        }
        Controls.Add(grid);

        _checked = isChecked;
        ApplyVisual();

        Hook(this, (_, _) => { _checked = !_checked; ApplyVisual(); Toggled?.Invoke(); });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Palette.Bg))
            g.FillRectangle(bg, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.Rounded(rect, Radius);
        using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
        using (var edge = new Pen(BackColor, 1.4f)) g.DrawPath(edge, path);
        using (var pen = new Pen(BorderColor, 1.4f)) g.DrawPath(pen, path);
    }

    private void ApplyVisual()
    {
        if (_checked) { BackColor = Palette.CardHover; BorderColor = Palette.Accent; }
        else          { BackColor = Palette.Surface;   BorderColor = Palette.Border; }
        _name.ForeColor = Palette.Text;
        if (_desc != null) _desc.ForeColor = Palette.Muted;
        Invalidate();
    }

    private static void Hook(Control root, EventHandler click)
    {
        void H(Control c)
        {
            c.Click  += click;
            c.Cursor  = Cursors.Hand;
            foreach (Control ch in c.Controls) H(ch);
        }
        H(root);
    }
}

// 강조(남색) 채움 버튼
internal sealed class PrimaryButton : Control
{
    private bool _hover;
    public PrimaryButton(string text)
    {
        Text = text; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Palette.Bg)) g.FillRectangle(bg, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.Rounded(rect, 14);
        using (var fill = new SolidBrush(_hover ? Color.FromArgb(40, 80, 140) : Palette.Accent))
            g.FillPath(fill, path);
        using var font = new Font("Malgun Gothic", 10.5F, FontStyle.Bold);
        TextRenderer.DrawText(g, Text, font, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
