// ui/ChatView.cs
// 화면 3: 채팅 (MainForm 에서 분리)
// 스트리밍 = 마크다운 스냅샷을 받아, "완성된 줄"만 서식 입혀 하나씩 페이드인 출력.
//   (토큰 단위 X. 문단/줄 단위로 서식 포함 점진 출력)

using NxAssistant.Providers;
using NxAssistant.Mcp;
using System.IO;

namespace NxAssistant.UI;

internal sealed class ChatView : Panel
{
    private FlowLayoutPanel _messages = null!;
    private TextBox         _input = null!;
    private Panel           _composerHost = null!;
    private WheelFilter?    _wheelFilter;
    private Panel?          _bottomSpacer;   // 목록 맨 아래 스크롤 가능한 여백 (FlowLayoutPanel은 bottom padding이 스크롤에 안 잡힘)
    private PillButton      _llmToggle = null!;
    private readonly IChatSession _session;
    private readonly string _domainName;
    private bool _busy;
    private System.Windows.Forms.Timer? _dotsTimer;   // 진행 멘트 점(…) 애니메이션

    private readonly Label _measure = new Label { AutoSize = true, Font = new Font("Malgun Gothic", 9F) };
    private static readonly Font MsgFont  = new Font("Malgun Gothic", 9F);
    private static readonly Font BoldFont = new Font("Malgun Gothic", 9F, FontStyle.Bold);
    private static readonly Font HeadFont = new Font("Malgun Gothic", 10.5F, FontStyle.Bold);
    private static readonly Font MonoFont = new Font("Consolas", 9F);
    private static readonly Font CaptionFont = new Font("Malgun Gothic", 8F);

    public ChatView(string domainName, IChatSession session, Action onBack, Action onHome, Action onSettings, string? greeting = null)
    {
        _domainName = string.IsNullOrEmpty(domainName) ? "채팅" : domainName;
        _session = session;
        BackColor = Palette.Bg;

        Controls.Add(BuildChatArea());
        Controls.Add(BuildComposer());
        Controls.Add(BuildTopBar(onBack, onHome, onSettings));

        AddAiMessage(greeting ?? $"안녕하세요! {_domainName} 어시스턴트입니다.\n설계 관련 궁금한 점을 질문해 주세요.");
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

        if (_session.IsGpt)
        {
            bool ready = await _session.IsGptReadyAsync();
            if (!ready) { AddAiMessage("GPT 로그인이 필요합니다. 열린 로그인 창에서 로그인한 뒤 다시 질문해 주세요."); return; }
        }

        _busy = true;
        var aiRow = AddAiMessage("");
        string curStatus = "질문을 이해하는 중", pendStatus = curStatus;
        var statusAt = DateTime.UtcNow;
        const int MinStatusMs = 550; int dots = 0;

        _dotsTimer?.Stop(); _dotsTimer?.Dispose();
        _dotsTimer = new System.Windows.Forms.Timer { Interval = 280 };
        _dotsTimer.Tick += (_, _) =>
        {
            if (pendStatus != curStatus && (DateTime.UtcNow - statusAt).TotalMilliseconds >= MinStatusMs)
            { curStatus = pendStatus; statusAt = DateTime.UtcNow; dots = 0; }
            dots = (dots + 1) % 4;
            UpdateAiMessage(aiRow, curStatus + new string('.', dots));
        };
        UpdateAiMessage(aiRow, curStatus);
        _dotsTimer.Start();

        bool   hasAnswer = false;
        string lastMd = "";
        IReadOnlyList<RagImage>? lastImages = null;

        try
        {
            await foreach (var ev in _session.AskStreamAsync(text))
            {
                switch (ev.Kind)
                {
                    case ChatEventKind.Status:
                        pendStatus = ev.Text;   // 생성 동안 멘트만 유지 (부분 렌더 안 함 → 안정적)
                        break;
                    case ChatEventKind.Token:
                        hasAnswer = true;
                        lastMd = ev.Text;       // 최종 마크다운 보관 (완료 후 한 번에 서식 렌더)
                        break;
                    case ChatEventKind.Images:
                        lastImages = ev.Pics;   // 검색된 표준 이미지 (답변 아래 표시)
                        break;
                    case ChatEventKind.Done:
                        break;
                }
            }

            _dotsTimer?.Stop();
            if (!hasAnswer || string.IsNullOrWhiteSpace(lastMd))
            {
                UpdateAiMessage(aiRow, "(빈 응답)");
            }
            else
            {
                var rtb = BeginAiRichText(aiRow);
                await RenderMarkdownAnimated(rtb, lastMd);   // 문단 단위 서식 페이드인
                if (lastImages is { Count: > 0 })
                    AddImages(lastImages);                   // 답변 ↔ 액션바 사이에 이미지
                AddActionBar(aiRow, rtb.Text);
            }
        }
        catch (Exception ex)
        {
            _dotsTimer?.Stop();
            UpdateAiMessage(aiRow, "오류가 발생했습니다: " + ex.Message);
        }
        finally
        {
            _dotsTimer?.Stop(); _dotsTimer?.Dispose(); _dotsTimer = null;
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
                    try { await _session.SetLlmAsync(captured); _llmToggle.Text = $"{_session.Current}  \u25BE"; }
                    catch (Exception ex) { MessageBox.Show("LLM 전환 오류: " + ex.Message, "오류"); }
                }
            };
            menu.Items.Add(item);
        }
        menu.Show(_llmToggle, new Point(0, _llmToggle.Height));
        await Task.CompletedTask;
    }

    // ── 정적 AI 메시지(안내/상태/에러): 라벨 ─────────────────────────
    private Panel AddAiMessage(string text)
    {
        var row = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 18), BackColor = Color.Transparent };
        var label = new Label { Text = text, AutoSize = true, Font = MsgFont, ForeColor = Palette.Text, BackColor = Color.Transparent, MaximumSize = new Size(10, 0) };
        row.Controls.Add(label); row.Tag = ("ai", label);
        AppendBeforeSpacer(row); LayoutRow(row); ScrollToBottom();
        return row;
    }

    private void UpdateAiMessage(Panel row, string text)
    {
        if (row.Tag is ValueTuple<string, Label> ai && ai.Item1 == "ai")
        { ai.Item2.Text = text; LayoutRow(row); ScrollToBottom(); }
    }

    // ── 스트리밍 답변: 서식 RichTextBox (선택/복사 가능) ─────────────
    private RichTextBox BeginAiRichText(Panel row)
    {
        if (row.Tag is ValueTuple<string, Label> ai && ai.Item1 == "ai")
        { row.Controls.Remove(ai.Item2); ai.Item2.Dispose(); }

        var rtb = new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Palette.Bg,
            Multiline = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.None,
            Font = MsgFont, TabStop = false, HideSelection = true, Location = new Point(0, 0)
        };
        rtb.ContentsResized += (_, e) => { rtb.Height = e.NewRectangle.Height + 2; row.Height = rtb.Height; };
        row.Margin = new Padding(0, 0, 0, 4);   // 답변↔액션바 간격 좁게 (꼬리 여백 줄임)
        row.Controls.Add(rtb);
        row.Tag = ("aimd", rtb);
        rtb.Width = RowWidth();
        return rtb;
    }

    // 완료된 마크다운을 블록(문단/불릿/제목/코드)으로 나눠 하나씩 서식 입혀 페이드인.
    // (부분 스냅샷을 다시 그리지 않으므로 번쩍임/싱크 문제 없음)
    private async Task RenderMarkdownAnimated(RichTextBox rtb, string md)
    {
        bool wrote = false;
        foreach (var block in SplitBlocks(md))
        {
            if (IsBlankBlock(block)) { if (wrote) AppendRun(rtb, "\n", MsgFont); continue; }
            if (wrote) AppendRun(rtb, "\n", MsgFont);   // 블록 구분 (마지막 뒤엔 안 붙음 → 꼬리 여백 없음)
            int start = rtb.TextLength;
            RenderBlock(rtb, block);
            int end = rtb.TextLength;
            FadeRange(rtb, start, end);
            ScrollToBottom();
            wrote = true;
            await Task.Delay(110);
        }
    }

    private static bool IsBlankBlock(string block)
        => !block.StartsWith("```") && string.IsNullOrWhiteSpace(block);

    // 마크다운 → 블록 목록 (코드펜스 블록은 통째로 하나). 꼬리 빈 줄 제거.
    private static List<string> SplitBlocks(string md)
    {
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n', ' ', '\t').Split('\n');
        var blocks = new List<string>();
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Trim() == "```")
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(lines[i]).Append('\n'); i++;
                while (i < lines.Length && lines[i].Trim() != "```") { sb.Append(lines[i]).Append('\n'); i++; }
                if (i < lines.Length) { sb.Append(lines[i]).Append('\n'); i++; }
                blocks.Add(sb.ToString());
            }
            else { blocks.Add(lines[i]); i++; }
        }
        return blocks;
    }

    // 한 블록 서식 렌더 (코드/제목/불릿/번호/볼드/인라인코드). 꼬리 줄바꿈 안 붙임.
    private void RenderBlock(RichTextBox rtb, string block)
    {
        if (block.StartsWith("```"))
        {
            var code = new List<string>();
            foreach (var l in block.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
                if (l.Trim() != "```") code.Add(l);
            while (code.Count > 0 && string.IsNullOrWhiteSpace(code[^1])) code.RemoveAt(code.Count - 1);
            while (code.Count > 0 && string.IsNullOrWhiteSpace(code[0]))   code.RemoveAt(0);
            AppendRun(rtb, string.Join("\n", code), MonoFont);
            return;
        }
        var line = block;
        if (line.StartsWith("### ") || line.StartsWith("## ") || line.StartsWith("# ")) { AppendRun(rtb, line.TrimStart('#').Trim(), HeadFont); return; }
        if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• ")) { AppendRun(rtb, "•  ", MsgFont); AppendInline(rtb, line.Substring(2).Trim()); return; }
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.\s+(.*)$");
        if (m.Success) { AppendRun(rtb, m.Groups[1].Value + ".  ", MsgFont); AppendInline(rtb, m.Groups[2].Value); return; }
        AppendInline(rtb, line);
    }

    // 인라인 **볼드** / `코드`
    private void AppendInline(RichTextBox rtb, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2);
                if (end > 0) { AppendRun(rtb, text.Substring(i + 2, end - (i + 2)), BoldFont); i = end + 2; continue; }
            }
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > 0) { AppendRun(rtb, text.Substring(i + 1, end - (i + 1)), MonoFont); i = end + 1; continue; }
            }
            int next = text.Length;
            int b = text.IndexOf("**", i); if (b >= i) next = Math.Min(next, b);
            int c = text.IndexOf('`', i);  if (c >= i) next = Math.Min(next, c);
            if (next <= i) next = i + 1;
            AppendRun(rtb, text.Substring(i, next - i), MsgFont);
            i = next;
        }
    }

    private static void AppendRun(RichTextBox rtb, string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) return;
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionLength = 0;
        rtb.SelectionFont = font;
        rtb.SelectionColor = Palette.Text;
        rtb.AppendText(text);
    }

    // 방금 append 한 구간을 Bg→Text 로 페이드인
    private void FadeRange(RichTextBox rtb, int start, int end)
    {
        if (end <= start) return;
        SetRangeColor(rtb, start, end, Palette.Bg);
        int i = 0; const int steps = 7;
        var t = new System.Windows.Forms.Timer { Interval = 22 };
        t.Tick += (_, _) =>
        {
            i++;
            float f = Math.Min(1f, i / (float)steps);
            SetRangeColor(rtb, start, end, Blend(Palette.Bg, Palette.Text, f));
            if (i >= steps) { SetRangeColor(rtb, start, end, Palette.Text); t.Stop(); t.Dispose(); }
        };
        t.Start();
    }

    private static void SetRangeColor(RichTextBox rtb, int start, int end, Color c)
    {
        if (start < 0) start = 0;
        if (end > rtb.TextLength) end = rtb.TextLength;
        if (end <= start) return;
        rtb.SelectionStart = start; rtb.SelectionLength = end - start;
        rtb.SelectionColor = c;
        rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0;
    }

    private static Color Blend(Color a, Color b, float f)
        => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f),
            (int)(a.B + (b.B - a.B) * f));

    private static string ToCrlf(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

    // ── 액션바 (복사 / 좋아요 / 싫어요) ──────────────────────────────
    private const string IconCopy    = "\uE8C8";
    private const string IconCheck   = "\uE73E";
    private const string IconLike    = "\uE8E1";
    private const string IconDislike = "\uE8E0";

    private void AddActionBar(Panel aiRow, string fullText)
    {
        var crlf = ToCrlf(fullText);
        var row  = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 36), BackColor = Color.Transparent };
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        var tip  = new ToolTip();

        var copy    = MakeActionButton(IconCopy);    tip.SetToolTip(copy, "복사");
        var like    = MakeActionButton(IconLike);    tip.SetToolTip(like, "좋아요");
        var dislike = MakeActionButton(IconDislike); tip.SetToolTip(dislike, "싫어요");

        copy.Click += (_, _) =>
        {
            try
            {
                if (!string.IsNullOrEmpty(crlf)) Clipboard.SetText(crlf);
                copy.Text = IconCheck; copy.ForeColor = Palette.CheckColor;
                var t = new System.Windows.Forms.Timer { Interval = 1200 };
                t.Tick += (_, _) => { copy.Text = IconCopy; copy.ForeColor = Palette.Muted; t.Stop(); t.Dispose(); };
                t.Start();
            }
            catch { }
        };
        bool liked = false, disliked = false;
        like.Click += (_, _) => { liked = !liked; disliked = false; like.ForeColor = liked ? Palette.Accent : Palette.Muted; dislike.ForeColor = Palette.Muted; };
        dislike.Click += (_, _) => { disliked = !disliked; liked = false; dislike.ForeColor = disliked ? Palette.Accent : Palette.Muted; like.ForeColor = Palette.Muted; };

        flow.Controls.Add(copy); flow.Controls.Add(like); flow.Controls.Add(dislike);
        row.Controls.Add(flow);
        row.Tag = ("actions", flow);

        AppendBeforeSpacer(row);   // 답변(+이미지) 다음, 맨 끝에 추가

        flow.PerformLayout();
        row.Height = flow.Height + 8;
        flow.Location = new Point(-6, 4);
        LayoutRow(row);

        FadeIn(new Control[] { copy, like, dislike });
        ScrollToBottom();
    }

    private Label MakeActionButton(string glyph)
    {
        var b = new Label
        {
            Text = glyph, AutoSize = true, Font = new Font("Segoe MDL2 Assets", 11F),
            ForeColor = Palette.Muted, BackColor = Color.Transparent,
            Cursor = Cursors.Hand, Padding = new Padding(8, 5, 8, 5), Margin = new Padding(2, 0, 2, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        b.MouseEnter += (_, _) => b.BackColor = Palette.CardHover;
        b.MouseLeave += (_, _) => b.BackColor = Color.Transparent;
        return b;
    }

    private void FadeIn(Control[] ctrls)
    {
        var targets = new Color[ctrls.Length];
        for (int k = 0; k < ctrls.Length; k++) { targets[k] = ctrls[k].ForeColor; ctrls[k].ForeColor = Palette.Bg; }
        int steps = 8, i = 0;
        var t = new System.Windows.Forms.Timer { Interval = 25 };
        t.Tick += (_, _) =>
        {
            i++;
            float f = Math.Min(1f, i / (float)steps);
            for (int k = 0; k < ctrls.Length; k++) ctrls[k].ForeColor = Blend(Palette.Bg, targets[k], f);
            if (i >= steps) { for (int k = 0; k < ctrls.Length; k++) ctrls[k].ForeColor = targets[k]; t.Stop(); t.Dispose(); }
        };
        t.Start();
    }

    // ── 사용자 메시지: 읽기전용 TextBox (드래그 선택/복사) ───────────
    private void AddUserMessage(string text)
    {
        var row = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 18), BackColor = Color.Transparent };
        var bubble = new RoundedPanel { BackColor = Palette.UserBubble, BorderColor = Palette.UserBubble, Radius = 16, AutoSize = false };
        var tb = new TextBox
        {
            Text = ToCrlf(text), ReadOnly = true, Multiline = true, WordWrap = true,
            BorderStyle = BorderStyle.None, BackColor = Palette.UserBubble, ForeColor = Color.White,
            Font = MsgFont, ScrollBars = ScrollBars.None, TabStop = false,
            Cursor = Cursors.IBeam, HideSelection = false
        };
        bubble.Controls.Add(tb); row.Controls.Add(bubble); row.Tag = ("user", bubble, tb);
        AppendBeforeSpacer(row); LayoutRow(row); ScrollToBottom();
    }

    // ── 검색된 표준 이미지 (답변 아래) ──────────────────────────────
    private void AddImages(IReadOnlyList<RagImage> images)
    {
        foreach (var img in images)
        {
            Image bmp;
            try
            {
                using var ms  = new MemoryStream(img.Data);
                using var tmp = Image.FromStream(ms);
                bmp = new Bitmap(tmp);   // 픽셀 복사 → 스트림 닫혀도 안전
            }
            catch { continue; }

            var row = new Panel { AutoSize = false, Margin = new Padding(0, 0, 0, 14), BackColor = Color.Transparent };
            var pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Image = bmp, BackColor = Palette.Surface, Margin = new Padding(0), Cursor = Cursors.Hand };
            var cap = new Label { Text = CaptionText(img), AutoSize = true, Font = CaptionFont, ForeColor = Palette.Muted, BackColor = Color.Transparent };

            var full  = bmp;                     // 클릭 시 확대할 원본 (썸네일과 공유)
            var title = StripExt(img.Name);
            pic.Click += (_, _) => ShowImagePopup(full, title);

            row.Controls.Add(pic); row.Controls.Add(cap);
            row.Tag = ("image", pic, cap, bmp.Size);

            AppendBeforeSpacer(row);
            LayoutRow(row);
        }
        ScrollToBottom();
    }

    private static string CaptionText(RagImage img)
        => img.ScorePct is int p ? $"(관련성 {p}%)  {StripExt(img.Name)}" : StripExt(img.Name);

    // 이미지 클릭 → 큰 팝업으로 확대 (클릭/Esc 닫기)
    private void ShowImagePopup(Image img, string title)
    {
        var screen   = Screen.FromControl(this).WorkingArea;
        int maxW     = (int)(screen.Width * 0.9), maxH = (int)(screen.Height * 0.9);
        double scale = Math.Min(1.0, Math.Min(maxW / (double)img.Width, maxH / (double)img.Height));
        int cw = Math.Max(360, (int)(img.Width  * scale));
        int ch = Math.Max(260, (int)(img.Height * scale));

        var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, ShowInTaskbar = false,
            ClientSize = new Size(cw, ch), BackColor = Color.FromArgb(32, 34, 38),
            KeyPreview = true,
        };
        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = Color.FromArgb(32, 34, 38), Cursor = Cursors.Hand };
        pb.Click  += (_, _) => f.Close();
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        f.Controls.Add(pb);
        f.Show(FindForm());   // 모달리스: 팝업 띄운 채 채팅 사용 가능 + 여러 창 동시 가능
        // img(썸네일과 공유)는 dispose 하지 않음 — Form/PictureBox dispose 시 Image 는 보존됨.
    }

    private static string StripExt(string name)
    {
        var i = name.LastIndexOf('.');
        return i > 0 ? name[..i] : name;
    }

    // ── 레이아웃 ────────────────────────────────────────────────────
    private int RowWidth()
    {
        int w = _messages.ClientSize.Width - _messages.Padding.Horizontal - 8;
        return w < 200 ? 200 : w;
    }

    private Size MeasureWrapped(string text, int maxWidth)
    {
        _measure.MaximumSize = new Size(maxWidth, 0);
        _measure.Text = string.IsNullOrEmpty(text) ? " " : text;
        return _measure.GetPreferredSize(new Size(maxWidth, 0));
    }

    private void LayoutRow(Control row)
    {
        int rowWidth = RowWidth();
        row.Width = rowWidth;

        if (row.Tag is ValueTuple<string, Label> ai && ai.Item1 == "ai")
        {
            var label = ai.Item2; label.MaximumSize = new Size(rowWidth, 0); label.Location = new Point(0, 0);
            row.Height = label.GetPreferredSize(new Size(rowWidth, 0)).Height;
        }
        else if (row.Tag is ValueTuple<string, RichTextBox> md && md.Item1 == "aimd")
        {
            md.Item2.Location = new Point(0, 0);
            md.Item2.Width = rowWidth;   // 높이는 ContentsResized 가 설정
        }
        else if (row.Tag is ValueTuple<string, RoundedPanel, TextBox> u && u.Item1 == "user")
        {
            var (_, bubble, tb) = u;
            int maxW = (int)(rowWidth * 0.78);
            var pref = MeasureWrapped(tb.Text, maxW - 28);
            int textW = pref.Width, textH = pref.Height;
            int bw = textW + 28, bh = textH + 22;
            tb.Location = new Point(14, 10); tb.Size = new Size(textW + 2, textH + 4);
            bubble.Size = new Size(bw, bh);
            bubble.Location = new Point(rowWidth - bw, 0); row.Height = bh;
        }
        else if (row.Tag is ValueTuple<string, PictureBox, Label, Size> im && im.Item1 == "image")
        {
            var (_, pic, cap, natural) = im;
            int natW = natural.Width  <= 0 ? rowWidth : natural.Width;
            int natH = natural.Height <= 0 ? 1        : natural.Height;
            int targetW = Math.Min(rowWidth, natW);
            int targetH = (int)Math.Round(natH * (targetW / (double)natW));
            pic.Location    = new Point(0, 0);
            pic.Size        = new Size(targetW, targetH);
            cap.MaximumSize = new Size(rowWidth - 4, 0);
            cap.Location    = new Point(2, targetH + 4);
            row.Height      = targetH + 4 + cap.Height + 2;
        }
        // "actions" 행은 높이 고정(AddActionBar) — 폭만 위에서 맞춤
    }

    private void RelayoutMessages() { foreach (Control row in _messages.Controls) LayoutRow(row); }

    private void ScrollToBottom()
    {
        EnsureSpacerLast();
        if (_messages.Controls.Count == 0) return;
        _messages.PerformLayout();
        var last = _messages.Controls[_messages.Controls.Count - 1];
        _messages.ScrollControlIntoView(last);
        _messages.AutoScrollPosition = new Point(0, _messages.DisplayRectangle.Height);
    }

    // 새 행은 항상 바닥 스페이서 "앞"에 들어가게 (스페이서는 늘 마지막 유지)
    private void AppendBeforeSpacer(Control row)
    {
        _messages.Controls.Add(row);
        if (_bottomSpacer != null && _messages.Controls.Contains(_bottomSpacer))
            _messages.Controls.SetChildIndex(_bottomSpacer, _messages.Controls.Count - 1);
    }

    // 목록 맨 아래에 항상 ~2줄 스크롤 가능한 여백을 둠 (status 멘트/마지막 답변이 컴포저에 붙지 않게)
    private void EnsureSpacerLast()
    {
        _bottomSpacer ??= new Panel { Height = 40, Margin = new Padding(0), BackColor = Color.Transparent, Tag = "spacer" };
        if (!_messages.Controls.Contains(_bottomSpacer)) _messages.Controls.Add(_bottomSpacer);
        _messages.Controls.SetChildIndex(_bottomSpacer, _messages.Controls.Count - 1);
        _bottomSpacer.Width = RowWidth();
    }

    // ── 마우스 휠: 자식(답변 RichTextBox / 이미지 PictureBox) 위에서도 목록이 스크롤되게 ──
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _wheelFilter ??= new WheelFilter(this);
        Application.RemoveMessageFilter(_wheelFilter);   // 중복 방지
        Application.AddMessageFilter(_wheelFilter);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_wheelFilter != null) Application.RemoveMessageFilter(_wheelFilter);
        base.OnHandleDestroyed(e);
    }

    private void ScrollMessagesByWheel(int delta)
    {
        if (_messages == null) return;
        int cur = -_messages.AutoScrollPosition.Y;
        _messages.AutoScrollPosition = new Point(0, Math.Max(0, cur - delta));   // 상한은 패널이 자동 클램프 → 진짜 바닥까지
    }

    // 커서가 메시지 목록 위면, 자식이 휠을 가로채기 전에 목록을 스크롤
    private sealed class WheelFilter : IMessageFilter
    {
        private readonly ChatView _view;
        public WheelFilter(ChatView view) => _view = view;

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL) return false;
            var panel = _view._messages;
            if (panel == null || !panel.IsHandleCreated || !_view.Visible) return false;

            var rect = panel.RectangleToScreen(panel.ClientRectangle);
            if (!rect.Contains(Control.MousePosition)) return false;

            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            _view.ScrollMessagesByWheel(delta);
            return true;   // 자식(RichTextBox/PictureBox)이 못 가로채게 우리가 처리
        }
    }
}
