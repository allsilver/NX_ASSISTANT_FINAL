// ui/MainForm.cs
// 화면 전환 관리 + 4개 View (AI선택 → 홈/분야 → 도메인 → 채팅)
// 1차 배포: 라우터 없음. 사용자가 모드/도메인 직접 선택.

using NxAssistant.Providers;

namespace NxAssistant.UI;

public sealed class MainForm : Form
{
    private readonly Panel _host;          // 화면 교체 영역
    private readonly LlmSession _session;  // 앱 전역 LLM 세션
    private string _domain = "";           // 선택된 DB 도메인 키
    private string _domainName = "";       // 도메인 표시명

    public MainForm()
    {
        Text          = "Gauss AI";
        StartPosition = FormStartPosition.CenterScreen;
        Size          = new Size(620, 1000);
        MinimumSize   = new Size(460, 720);
        BackColor     = Palette.Bg;
        Font          = new Font("Malgun Gothic", 9F);

        _session = new LlmSession();
        FormClosed += (_, _) => _session.Dispose();

        _host = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
        Controls.Add(_host);

        ShowAiSelect();   // 시작: AI 선택
    }

    private void Swap(Control view)
    {
        var old = _host.Controls.Count > 0 ? _host.Controls[0] : null;
        view.Dock = DockStyle.Fill;
        _host.Controls.Add(view);
        view.BringToFront();
        if (old != null) { _host.Controls.Remove(old); old.Dispose(); }
    }

    // ── 화면 0: AI 선택 ──────────────────────────────────────
    private void ShowAiSelect()
    {
        var view = new AiSelectView(async llm =>
        {
            try
            {
                await _session.SetLlmAsync(llm);   // GPT면 로그인창 표시
                ShowFieldSelect();
            }
            catch (Exception ex)
            {
                MessageBox.Show("LLM 준비 중 오류: " + ex.Message, "오류");
            }
        });
        Swap(view);
    }

    // ── 화면 1: 분야 선택 (홈) ───────────────────────────────
    private void ShowFieldSelect()
    {
        var view = new FieldSelectView(
            onDbQuery:   ShowDomainSelect,
            onNxControl: () => MessageBox.Show("NX 제어는 준비 중입니다.", "안내"),
            onAuto:      () => MessageBox.Show("자동화 기능은 준비 중입니다.", "안내"));
        Swap(view);
    }

    // ── 화면 2: DB 도메인 선택 ───────────────────────────────
    private void ShowDomainSelect()
    {
        var view = new DomainSelectView(
            onBack: ShowFieldSelect,
            onHome: ShowFieldSelect,
            onPick: (key, name) => { _domain = key; _domainName = name; ShowChat(); });
        Swap(view);
    }

    // ── 화면 3: 채팅 ─────────────────────────────────────────
    private void ShowChat()
    {
        _session.SetDomain(_domain);   // Gauss DB조회 시 /mech/ask 에 도메인 전달
        var view = new ChatView(_domainName, _session,
            onBack: ShowDomainSelect,
            onHome: ShowFieldSelect);
        Swap(view);
    }
}

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
        var leftBlock = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        var logoIcon = new BrandLogo { Kind = logo, Size = new Size(60, 60), Anchor = AnchorStyles.None };
        var nameLabel = new Label { Text = name, AutoSize = true, Font = new Font("Malgun Gothic", 13F, FontStyle.Bold), ForeColor = Palette.Text, Margin = new Padding(0, 8, 0, 0), Anchor = AnchorStyles.None };
        var logoHolder = new Panel { Size = new Size(80, 60), BackColor = Color.Transparent };
        logoIcon.Location = new Point((80-60)/2, 0);
        logoHolder.Controls.Add(logoIcon);
        leftBlock.Controls.Add(logoHolder); leftBlock.Controls.Add(nameLabel);
        leftWrap.Controls.Add(leftBlock);
        leftWrap.Resize += (_, _) =>
        {
            nameLabel.Margin = new Padding(Math.Max(0,(leftBlock.Width - nameLabel.Width)/2), 8, 0, 0);
            leftBlock.Location = new Point((leftWrap.Width - leftBlock.Width)/2, (leftWrap.Height - leftBlock.Height)/2);
        };

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

// ════════════════════════════════════════════════════════════
// 화면 1: 분야 선택
// ════════════════════════════════════════════════════════════
internal sealed class FieldSelectView : Panel
{
    public FieldSelectView(Action onDbQuery, Action onNxControl, Action onAuto)
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
        Controls.Add(new TopBar("\U0001F6E1  AI 설계 어시스턴트"));
    }
}

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

// ════════════════════════════════════════════════════════════
// 화면 3: 채팅
// ════════════════════════════════════════════════════════════
internal sealed class ChatView : Panel
{
    private FlowLayoutPanel _messages = null!;
    private TextBox         _input = null!;
    private Panel           _composerHost = null!;
    private PillButton      _llmToggle = null!;
    private readonly LlmSession _session;
    private readonly string _domainName;
    private bool _busy;

    public ChatView(string domainName, LlmSession session, Action onBack, Action onHome)
    {
        _domainName = string.IsNullOrEmpty(domainName) ? "채팅" : domainName;
        _session = session;
        BackColor = Palette.Bg;

        Controls.Add(BuildChatArea());
        Controls.Add(BuildComposer());
        Controls.Add(BuildTopBar(onBack, onHome));

        AddAiMessage($"안녕하세요! {_domainName} 어시스턴트입니다.\n설계 관련 궁금한 점을 질문해 주세요.");
    }

    private Control BuildTopBar(Action onBack, Action onHome)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Palette.Accent };

        var row2 = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Palette.Bg };
        var row2Center = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Palette.Bg };
        var clear = new PillButton("대화 초기화"); clear.Click += (_, _) => ClearChat();
        var synonym = new PillButton("동의어 사전"); synonym.Click += (_, _) => MessageBox.Show("동의어 사전 (준비 중)");
        _llmToggle = new PillButton($"{_session.Current}  \u25BE"); _llmToggle.Click += (_, _) => ShowLlmMenu();
        row2Center.Controls.Add(clear); row2Center.Controls.Add(synonym); row2Center.Controls.Add(_llmToggle);
        row2.Controls.Add(row2Center);
        row2.Resize += (_, _) => row2Center.Location = new Point((row2.Width - row2Center.Width)/2, (row2.Height - row2Center.Height)/2);

        var row1 = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Palette.Accent };
        var back = new NavButton(NavIcon.Back) { Dock = DockStyle.Left, Width = 44 }; back.Click += (_, _) => onBack();
        var home = new NavButton(NavIcon.Home) { Dock = DockStyle.Left, Width = 44 }; home.Click += (_, _) => onHome();
        var settings = new NavButton(NavIcon.Gear) { Dock = DockStyle.Right, Width = 48 }; settings.Click += (_, _) => MessageBox.Show("설정 (준비 중)");
        var title = new Label { Text = _domainName, ForeColor = Color.White, Font = new Font("Malgun Gothic", 10.5F, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
        row1.Controls.Add(title); row1.Controls.Add(settings); row1.Controls.Add(home); row1.Controls.Add(back);
        title.BringToFront();

        bar.Controls.Add(row2); bar.Controls.Add(row1);
        return bar;
    }

    private Control BuildChatArea()
    {
        _messages = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 18, 20, 18), BackColor = Palette.Bg };
        _messages.SizeChanged += (_, _) => RelayoutMessages();
        return _messages;
    }

    private Control BuildComposer()
    {
        _composerHost = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = Palette.Bg, Padding = new Padding(18, 10, 18, 14) };
        var box = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Palette.Surface, BorderColor = Palette.Border, Radius = 20, Padding = new Padding(6, 4, 6, 4) };
        var send = new SendButton { Dock = DockStyle.Right, Width = 48 }; send.Click += (_, _) => Send();
        _input = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Malgun Gothic", 9.5F), BackColor = Palette.Surface, PlaceholderText = "질문을 입력하세요.", Multiline = true, WordWrap = true, ScrollBars = ScrollBars.None };
        _input.TextChanged += (_, _) => AdjustComposerHeight();
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); } };
        _input.KeyUp += (_, _) => AdjustComposerHeight();
        _input.Click += (_, _) => _input.Focus();
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 8, 10), BackColor = Palette.Surface };
        pad.Controls.Add(_input);
        box.Controls.Add(pad); box.Controls.Add(send);
        _composerHost.Controls.Add(box);
        return _composerHost;
    }

    private void AdjustComposerHeight()
    {
        int endLine = _input.GetLineFromCharIndex(_input.TextLength);
        int lineCount = Math.Max(1, endLine + 1);
        using var g = _input.CreateGraphics();
        int lineH = (int)Math.Ceiling(g.MeasureString("가", _input.Font).Height); if (lineH < 16) lineH = 16;
        int padV = 28, hostPad = 24;
        int target = lineCount * lineH + padV + hostPad;
        int maxH = ClientSize.Height / 2;
        bool overflow = target > maxH; if (overflow) target = maxH;
        int minH = lineH + padV + hostPad; if (target < minH) target = minH;
        var desired = overflow ? ScrollBars.Vertical : ScrollBars.None;
        if (_input.ScrollBars != desired) _input.ScrollBars = desired;
        if (_composerHost.Height != target) { _composerHost.Height = target; _composerHost.PerformLayout(); }
        _input.ScrollToCaret();
    }

    private async void Send()
    {
        if (_busy) return;
        var text = _input.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _input.Clear(); AdjustComposerHeight();
        AddUserMessage(text);

        // GPT면 로그인 완료됐는지 확인
        if (_session.IsGpt)
        {
            bool ready = await _session.IsGptReadyAsync();
            if (!ready)
            {
                AddAiMessage("GPT 로그인이 필요합니다. 열린 로그인 창에서 로그인한 뒤 다시 질문해 주세요.");
                return;
            }
        }

        _busy = true;
        var thinking = AddAiMessage("답변을 생성하고 있습니다…");
        try
        {
            // WebView2(GPT)는 UI 스레드에서 호출해야 하므로 Task.Run 쓰지 않음.
            // ChatAsync 내부가 await로 UI를 양보하므로 창은 멈추지 않음.
            var answer = await _session.AskAsync(text);
            UpdateAiMessage(thinking, answer);
        }
        catch (Exception ex)
        {
            UpdateAiMessage(thinking, "오류가 발생했습니다: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ClearChat()
    {
        _messages.Controls.Clear();
        AddAiMessage("대화가 초기화되었습니다.\n새로운 질문을 입력해 주세요.");
    }

    private async void ShowLlmMenu()
    {
        var menu = new ContextMenuStrip { Font = new Font("Malgun Gothic", 9F), ShowImageMargin = false };
        foreach (var opt in new[] { "Gauss", "GPT" })
        {
            var item = new ToolStripMenuItem(opt) { Checked = (opt == _session.Current) };
            string captured = opt;
            item.Click += async (_, _) =>
            {
                if (_session.Current != captured)
                {
                    try
                    {
                        await _session.SetLlmAsync(captured);
                        _llmToggle.Text = $"{_session.Current}  \u25BE";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("LLM 전환 오류: " + ex.Message, "오류");
                    }
                }
            };
            menu.Items.Add(item);
        }
        menu.Show(_llmToggle, new Point(0, _llmToggle.Height));
        await Task.CompletedTask;
    }

    private Panel AddAiMessage(string text)
    {
        var row = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 18), BackColor = Color.Transparent };
        var label = new Label { Text = text, AutoSize = true, Font = new Font("Malgun Gothic", 9F), ForeColor = Palette.Text, BackColor = Color.Transparent, MaximumSize = new Size(10, 0) };
        row.Controls.Add(label); row.Tag = ("ai", label);
        _messages.Controls.Add(row); LayoutRow(row); ScrollToBottom();
        return row;
    }

    private void UpdateAiMessage(Panel row, string text)
    {
        if (row.Tag is ValueTuple<string, Label> ai && ai.Item1 == "ai")
        {
            ai.Item2.Text = text;
            LayoutRow(row);
            ScrollToBottom();
        }
    }

    private void AddUserMessage(string text)
    {
        var row = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 18), BackColor = Color.Transparent };
        var bubble = new RoundedPanel { BackColor = Palette.UserBubble, BorderColor = Palette.UserBubble, Radius = 16, AutoSize = false };
        var label = new Label { Text = text, AutoSize = true, Font = new Font("Malgun Gothic", 9F), ForeColor = Color.White, BackColor = Color.Transparent, MaximumSize = new Size(340, 0) };
        bubble.Controls.Add(label); row.Controls.Add(bubble); row.Tag = ("user", bubble, label);
        _messages.Controls.Add(row); LayoutRow(row); ScrollToBottom();
    }

    private void LayoutRow(Control row)
    {
        int rowWidth = _messages.ClientSize.Width - _messages.Padding.Horizontal - 8;
        if (rowWidth < 200) rowWidth = 200;
        row.Width = rowWidth;
        if (row.Tag is ValueTuple<string, Label> ai && ai.Item1 == "ai")
        {
            var label = ai.Item2; label.MaximumSize = new Size(rowWidth, 0); label.Location = new Point(0, 0);
            var pref = label.GetPreferredSize(new Size(rowWidth, 0)); row.Height = pref.Height;
        }
        else if (row.Tag is ValueTuple<string, RoundedPanel, Label> u && u.Item1 == "user")
        {
            var (_, bubble, label) = u;
            int maxW = (int)(rowWidth * 0.78);
            label.MaximumSize = new Size(maxW - 28, 0);
            var pref = label.GetPreferredSize(new Size(maxW - 28, 0));
            int bw = pref.Width + 28, bh = pref.Height + 20;
            bubble.Size = new Size(bw, bh); label.Location = new Point(14, 10);
            bubble.Location = new Point(rowWidth - bw, 0); row.Height = bh;
        }
    }

    private void RelayoutMessages() { foreach (Control row in _messages.Controls) LayoutRow(row); }

    private void ScrollToBottom()
    {
        if (_messages.Controls.Count == 0) return;
        _messages.PerformLayout();
        var last = _messages.Controls[_messages.Controls.Count - 1];
        _messages.ScrollControlIntoView(last);
        _messages.AutoScrollPosition = new Point(0, _messages.DisplayRectangle.Height);
    }
}
