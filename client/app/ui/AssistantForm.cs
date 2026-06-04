// ui/AssistantForm.cs
// 메인 채팅 UI — 사용자가 보는 창
// Gauss/GPT 선택, MCP 모드 선택, 채팅 표시

using NxAssistant.History;
using NxAssistant.Mcp;
using NxAssistant.Providers;
using NxAssistant.Router;

namespace NxAssistant.UI;

public sealed class AssistantForm : Form
{
    // ── 핵심 컴포넌트 ──────────────────────────────────────────────
    private readonly WorkerForm     _worker;
    private readonly HistoryManager _history  = new();
    private readonly DbMcpClient    _dbMcp    = new();
    private readonly NxMcpClient    _nxMcp    = new();
    private RouterClient?           _router;
    private ILlmProvider?           _provider;

    // ── UI 컴포넌트 ────────────────────────────────────────────────
    private readonly FlowLayoutPanel _messages       = new();
    private readonly TextBox         _input          = new();
    private readonly Button          _sendButton     = new();
    private readonly Button          _gaussButton    = new();
    private readonly Button          _gptButton      = new();
    private readonly Button          _dbMcpButton    = new();
    private readonly Button          _nxMcpButton    = new();
    private readonly Button          _clearButton    = new();
    private readonly Label           _statusLabel    = new();
    private readonly System.Windows.Forms.Timer _gptReadyTimer = new();

    private bool _gptReady;
    private bool _probingGpt;
    private bool _sending;

    // ── 색상 정의 ──────────────────────────────────────────────────
    private static readonly Color TopBarBg    = Color.FromArgb(14, 73, 63);
    private static readonly Color McpBarBg    = Color.FromArgb(239, 246, 244);
    private static readonly Color ActiveColor = Color.FromArgb(0, 122, 108);
    private static readonly Color ChatBg      = Color.FromArgb(247, 250, 249);

    public AssistantForm(WorkerForm worker)
    {
        _worker = worker;
        Text          = "NX Assistant";
        Icon          = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize   = new Size(760, 560);
        Size          = new Size(900, 680);

        Controls.Add(BuildComposer());
        Controls.Add(BuildMessages());
        Controls.Add(BuildMcpBar());
        Controls.Add(BuildTopBar());

        _messages.SizeChanged += (_, _) => RelayoutMessages();

        _gptReadyTimer.Interval = 1500;
        _gptReadyTimer.Tick    += async (_, _) => await ProbeGptReadyAsync();

        AddMessage("Assistant", "Gauss 또는 GPT를 선택하세요. GPT는 로그인이 필요합니다.");
    }

    // ── 상단 바 (Gauss/GPT 선택) ───────────────────────────────────
    private Control BuildTopBar()
    {
        var top = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 92,
            Padding     = new Padding(16, 14, 16, 10),
            ColumnCount = 4,
            BackColor   = TopBarBg,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        StyleProviderButton(_gaussButton, "Gauss");
        StyleProviderButton(_gptButton,   "GPT");
        _gaussButton.Click += (_, _) => SelectGauss();
        _gptButton.Click   += async (_, _) => await SelectGptAsync();

        _statusLabel.Dock      = DockStyle.Fill;
        _statusLabel.ForeColor = Color.White;
        _statusLabel.Font      = new Font("Segoe UI", 10);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text      = "모델을 선택하세요.";

        var showGptBtn = new Button { Text = "GPT 창 보기", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        showGptBtn.Click += async (_, _) => await ShowGptLoginAsync();

        top.Controls.Add(_gaussButton, 0, 0);
        top.Controls.Add(_gptButton,   1, 0);
        top.Controls.Add(_statusLabel, 2, 0);
        top.Controls.Add(showGptBtn,   3, 0);
        return top;
    }

    // ── MCP 모드 바 ────────────────────────────────────────────────
    private Control BuildMcpBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 72,
            Padding     = new Padding(16, 10, 16, 10),
            ColumnCount = 4,
            BackColor   = McpBarBg,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));

        StyleMcpButton(_dbMcpButton, "DB 검색",  true);
        StyleMcpButton(_nxMcpButton, "NX 제어",  false);
        StyleMcpButton(_clearButton, "초기화",    false);

        _clearButton.Click += (_, _) => ClearHistory();

        var hint = new Label
        {
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font      = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(44, 73, 68),
            Text      = "라우터가 질문 유형을 자동 분석합니다.",
        };

        bar.Controls.Add(_dbMcpButton, 0, 0);
        bar.Controls.Add(_nxMcpButton, 1, 0);
        bar.Controls.Add(_clearButton, 2, 0);
        bar.Controls.Add(hint,         3, 0);
        return bar;
    }

    private Control BuildMessages()
    {
        _messages.Dock          = DockStyle.Fill;
        _messages.AutoScroll    = true;
        _messages.FlowDirection = FlowDirection.TopDown;
        _messages.WrapContents  = false;
        _messages.Padding       = new Padding(18);
        _messages.BackColor     = ChatBg;
        return _messages;
    }

    private Control BuildComposer()
    {
        var composer = new TableLayoutPanel
        {
            Dock        = DockStyle.Bottom,
            Height      = 78,
            Padding     = new Padding(16, 10, 16, 12),
            ColumnCount = 2,
            BackColor   = Color.White,
        };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _input.Dock             = DockStyle.Fill;
        _input.Font             = new Font("Segoe UI", 13);
        _input.PlaceholderText  = "설계 질문을 입력하세요...";
        _input.KeyDown         += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendAsync(); } };

        _sendButton.Text   = "전송";
        _sendButton.Dock   = DockStyle.Fill;
        _sendButton.Font   = new Font("Segoe UI", 12, FontStyle.Bold);
        _sendButton.Click += async (_, _) => await SendAsync();

        composer.Controls.Add(_input,      0, 0);
        composer.Controls.Add(_sendButton, 1, 0);
        return composer;
    }

    // ── 프로바이더 선택 ────────────────────────────────────────────
    private void SelectGauss()
    {
        _provider = new GaussProvider();
        _router   = new RouterClient(_dbMcp, _nxMcp, _history);
        _gptReadyTimer.Stop();

        HighlightProviderButton(_gaussButton, _gptButton);
        _statusLabel.Text = "Gauss 선택됨. 질문을 입력하세요.";
        AddMessage("Assistant", "Gauss가 선택되었습니다. 설계 질문을 입력하세요.");
    }

    private async Task SelectGptAsync()
    {
        _gptReady = false;
        _provider = new GptProvider(_worker);
        _router   = new RouterClient(_dbMcp, _nxMcp, _history);

        HighlightProviderButton(_gptButton, _gaussButton);
        AddMessage("Assistant", "GPT 선택됨. ChatGPT 창에서 로그인 후 사용하세요.");
        await ShowGptLoginAsync();
    }

    private async Task ShowGptLoginAsync()
    {
        _statusLabel.Text = "GPT 로그인 창 열기...";
        await _worker.InitializeAsync();
        _worker.ShowForLogin();
        _gptReadyTimer.Start();
        await ProbeGptReadyAsync();
    }

    private async Task ProbeGptReadyAsync()
    {
        if (_probingGpt) return;
        _probingGpt = true;
        try
        {
            var probe = await _worker.ProbeAsync();
            if (probe.HasComposer)
            {
                _gptReadyTimer.Stop();
                _gptReady         = true;
                _worker.ParkOffscreen();
                _statusLabel.Text = "GPT 연결됨. 질문을 입력하세요.";
                AddMessage("Assistant", "GPT가 연결되었습니다. 질문을 입력하세요.");
            }
            else
            {
                _statusLabel.Text = "GPT 로그인 대기 중... ChatGPT 창에서 로그인하세요.";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"GPT 연결 확인 실패: {ex.Message}";
        }
        finally
        {
            _probingGpt = false;
        }
    }

    // ── 메시지 전송 ────────────────────────────────────────────────
    private async Task SendAsync()
    {
        if (_sending) return;
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _input.Clear();
        AddMessage("You", text);

        if (_provider == null || _router == null)
        {
            AddMessage("Assistant", "먼저 Gauss 또는 GPT를 선택하세요.");
            return;
        }

        if (_provider is GptProvider && !_gptReady)
        {
            AddMessage("Assistant", "GPT 로그인이 필요합니다. 로그인 창을 확인하세요.");
            await ShowGptLoginAsync();
            return;
        }

        _sending            = true;
        _sendButton.Text    = "대기 중...";
        _sendButton.Enabled = false;
        _statusLabel.Text   = "응답 생성 중...";

        try
        {
            var result = await _router.HandleAsync(text, _provider);

            // 히스토리에 추가
            _history.Add("user",      text);
            _history.Add("assistant", result.Answer);

            AddMessage("Assistant", result.Answer);

            _statusLabel.Text = result.Intent == "db_search"
                ? $"DB 검색 완료 ({result.Domain})"
                : "응답 완료";
        }
        catch (Exception ex)
        {
            _gptReady = false;
            AddMessage("Assistant", $"오류: {ex.Message}");
            _statusLabel.Text = "오류 발생. 재연결이 필요할 수 있습니다.";
            if (_provider is GptProvider) _gptReadyTimer.Start();
        }
        finally
        {
            _sending            = false;
            _sendButton.Text    = "전송";
            _sendButton.Enabled = true;
            _input.Focus();
        }
    }

    // ── 히스토리 초기화 ────────────────────────────────────────────
    private async void ClearHistory()
    {
        _history.Clear();
        _messages.Controls.Clear();

        // GPT Worker도 새 채팅으로 동기화
        if (_provider is GptProvider && _gptReady)
        {
            _statusLabel.Text = "새 채팅 시작 중...";
            try
            {
                await _worker.StartNewChatAsync();
                _gptReady         = _worker.IsGptReady;
                _statusLabel.Text = "새 채팅 시작됨.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"새 채팅 실패: {ex.Message}";
            }
        }

        AddMessage("Assistant", "대화가 초기화되었습니다.");
    }

    // ── 메시지 렌더링 ──────────────────────────────────────────────
    public void AddMessage(string role, string content)
    {
        var isUser = role.Equals("You", StringComparison.OrdinalIgnoreCase);
        var row    = new Panel
        {
            Width     = MessageRowWidth(),
            Height    = 10,
            AutoSize  = false,
            Margin    = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent,
        };

        var label = new Label
        {
            AutoSize  = false,
            Padding   = new Padding(14, 10, 14, 12),
            Font      = new Font("Segoe UI", 10.5f),
            BackColor = isUser ? Color.FromArgb(224, 238, 255) : Color.White,
            ForeColor = Color.Black,
            Text      = role.ToUpperInvariant() + Environment.NewLine + content,
        };

        row.Tag = new ChatBubbleLayout(isUser, label);
        row.Controls.Add(label);
        _messages.Controls.Add(row);
        LayoutMessageRow(row);
        ScrollToBottom(row);
    }

    private int MessageRowWidth()
    {
        var scrollbar = SystemInformation.VerticalScrollBarWidth + 8;
        return Math.Max(320, _messages.ClientSize.Width - _messages.Padding.Horizontal - scrollbar);
    }

    private void RelayoutMessages()
    {
        foreach (Control row in _messages.Controls) LayoutMessageRow(row);
        if (_messages.Controls.Count > 0)
            ScrollToBottom(_messages.Controls[_messages.Controls.Count - 1]);
    }

    private void LayoutMessageRow(Control row)
    {
        if (row.Tag is not ChatBubbleLayout bubble) return;
        var rowWidth      = MessageRowWidth();
        var maxWidth      = (int)(rowWidth * (bubble.IsUser ? 0.62 : 0.82));
        var preferred     = bubble.Label.GetPreferredSize(new Size(maxWidth, 0));
        var bubbleWidth   = Math.Min(maxWidth, Math.Max(220, preferred.Width));
        var bubbleHeight  = preferred.Height;

        row.Width               = rowWidth;
        row.Height              = bubbleHeight + 2;
        bubble.Label.MaximumSize = new Size(maxWidth, 0);
        bubble.Label.Size       = new Size(bubbleWidth, bubbleHeight);
        bubble.Label.Location   = bubble.IsUser
            ? new Point(Math.Max(0, rowWidth - bubbleWidth - 4), 0)
            : new Point(4, 0);
    }

    private async void ScrollToBottom(Control newest)
    {
        if (!IsHandleCreated || !_messages.IsHandleCreated) { Shown += (_, _) => ScrollToBottom(newest); return; }
        try
        {
            for (var i = 0; i < 5; i++)
            {
                if (IsDisposed || _messages.IsDisposed || newest.IsDisposed) return;
                _messages.PerformLayout();
                _messages.ScrollControlIntoView(newest);
                _messages.AutoScrollPosition = new Point(0, _messages.DisplayRectangle.Height);
                await Task.Delay(i == 0 ? 1 : 45);
            }
        }
        catch { }
    }

    // ── 버튼 스타일 헬퍼 ──────────────────────────────────────────
    private static void StyleProviderButton(Button btn, string text)
    {
        btn.Text      = text;
        btn.Dock      = DockStyle.Fill;
        btn.Font      = new Font("Segoe UI", 13, FontStyle.Bold);
        btn.BackColor = SystemColors.Control;
        btn.ForeColor = SystemColors.ControlText;
    }

    private static void HighlightProviderButton(Button active, Button inactive)
    {
        active.BackColor   = ActiveColor;
        active.ForeColor   = Color.White;
        inactive.BackColor = SystemColors.Control;
        inactive.ForeColor = SystemColors.ControlText;
    }

    private static void StyleMcpButton(Button btn, string text, bool active)
    {
        btn.Text      = text;
        btn.Dock      = DockStyle.Fill;
        btn.Font      = new Font("Segoe UI", 11, FontStyle.Bold);
        btn.Margin    = new Padding(0, 0, 10, 0);
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = active ? ActiveColor : Color.White;
        btn.ForeColor = active ? Color.White : Color.FromArgb(18, 44, 40);
        btn.FlatAppearance.BorderColor = active ? ActiveColor : Color.FromArgb(181, 203, 198);
        btn.FlatAppearance.BorderSize  = 1;
    }

    private sealed record ChatBubbleLayout(bool IsUser, Label Label);
}
