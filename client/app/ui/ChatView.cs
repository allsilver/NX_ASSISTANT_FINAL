// ui/ChatView.cs
// 화면 3: 채팅 (MainForm 에서 분리)
// LlmSession 구상클래스 대신 ILlmSession 인터페이스에 의존 → UI 프리뷰에서 Mock 주입 가능

using NxAssistant.Providers;

namespace NxAssistant.UI;

// ════════════════════════════════════════════════════════════
// 화면 3: 채팅
// ════════════════════════════════════════════════════════════
internal sealed class ChatView : Panel
{
    private FlowLayoutPanel _messages = null!;
    private TextBox         _input = null!;
    private Panel           _composerHost = null!;
    private PillButton      _llmToggle = null!;
    private readonly ILlmSession _session;
    private readonly string _domainName;
    private bool _busy;

    public ChatView(string domainName, ILlmSession session, Action onBack, Action onHome, Action onSettings)
    {
        _domainName = string.IsNullOrEmpty(domainName) ? "채팅" : domainName;
        _session = session;
        BackColor = Palette.Bg;

        Controls.Add(BuildChatArea());
        Controls.Add(BuildComposer());
        Controls.Add(BuildTopBar(onBack, onHome, onSettings));

        AddAiMessage($"안녕하세요! {_domainName} 어시스턴트입니다.\n설계 관련 궁금한 점을 질문해 주세요.");
    }

    private Control BuildTopBar(Action onBack, Action onHome, Action onSettings)
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
        var settings = new NavButton(NavIcon.Gear) { Dock = DockStyle.Right, Width = 48 }; settings.Click += (_, _) => onSettings();
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
