// ui/MainForm.cs
// 화면 전환 관리 (AI선택 → 홈/분야 → 도메인 → 관심분야(db_key) → 채팅, + 설정)
// 1차 배포: 라우터 없음. 사용자가 모드/도메인 직접 선택.

using System.Linq;
using NxAssistant.Config;
using NxAssistant.Mcp;
using NxAssistant.Providers;

namespace NxAssistant.UI;

public sealed class MainForm : Form
{
    private readonly Panel _host;          // 화면 교체 영역
    private readonly LlmSession _session;  // 앱 전역 LLM 세션
    private readonly DbMcpClient _dbMcp = new();           // dbkeys 조회용
    private readonly bool _isVdi = (Environment.GetEnvironmentVariable("NX_ASSISTANT_MODE") ?? "")
                                   .Trim().Equals("vdi", StringComparison.OrdinalIgnoreCase);  // VDI(개발) = 서버 체크 예외
    private string   _domain     = "";     // 선택된 DB 도메인 키
    private string   _domainName = "";     // 도메인 표시명
    private string[] _dbKeys     = Array.Empty<string>();  // 선택된 db_key

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

        StartUp();   // 시작: (배포 환경) 서버 연결 확인 → AI 선택 / (VDI) 바로 AI 선택
    }

    // ── 시작: 서버 연결 확인 ─────────────────────────────────
    //  배포 환경에서 DB 서버 미연결이면 경고 화면 + 진입 차단(서버 없이 모르고 사용하는 위험 방지).
    //  VDI(개발)은 서버 없이 빌드·동작 확인하므로 예외.
    private async void StartUp()
    {
        if (_isVdi) { ShowAiSelect(); return; }

        bool ok = await _dbMcp.HealthCheckAsync();
        if (ok) ShowAiSelect();
        else    ShowServerError();
    }

    // 서버 연결 실패 화면 (다시 시도 가능). 이 화면에선 다른 기능으로 진행 불가.
    private void ShowServerError()
    {
        var holder = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = Palette.Bg };
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        holder.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var box = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None, BackColor = Palette.Bg };
        Label C(string t, float s, FontStyle st, Color c, int bottom) => new()
        {
            Text = t, AutoSize = true, Anchor = AnchorStyles.None,
            Font = new Font("Malgun Gothic", s, st), ForeColor = c,
            TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, bottom),
        };
        box.Controls.Add(C("서버에 연결할 수 없습니다", 14F, FontStyle.Bold, Palette.Text, 10));
        box.Controls.Add(C("DB 서버에 연결되지 않았습니다.", 9.5F, FontStyle.Regular, Palette.Muted, 2));
        box.Controls.Add(C("네트워크·서버 상태를 확인한 뒤 다시 시도해 주세요.", 9.5F, FontStyle.Regular, Palette.Muted, 18));
        var retry = new PrimaryButton("다시 시도") { Anchor = AnchorStyles.None, Width = 160, Height = 44, Margin = new Padding(0) };
        retry.Click += (_, _) => StartUp();
        box.Controls.Add(retry);

        holder.Controls.Add(box, 0, 0);
        Swap(holder);
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
            onBack:      ShowAiSelect,
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
            onPick: (key, name) => { _domain = key; _domainName = name; ShowDbKeySelect(); });
        Swap(view);
    }

    // ── 화면 2.5: 관심분야(db_key) 복수선택 ──────────────────
    //  어느 도메인이 복수 DB인지는 하드코딩하지 않고 서버 /mech/dbkeys 결과로 판단(옵션 2개 이상 → 페이지).
    //  force=false : 일반 진입(도메인 선택 직후). 저장된 선택이 있으면 페이지를 건너뛰고 그대로 사용(= 1회만 표시).
    //  force=true  : 설정 > 관심분야 재설정. 저장값이 있어도 페이지를 다시 띄움.
    private async void ShowDbKeySelect(bool force = false)
    {
        // 이미 선택해 저장돼 있으면(재설정이 아닌 한) 건너뜀 → 저장된 DB 그대로 유지(= 1회만 표시)
        var saved = DbKeySelectionStore.Load(_domain);
        if (!force && saved is { Length: > 0 })
        {
            _dbKeys = saved;
            ShowChat();
            return;
        }

        List<DbKeyOption> options;
        try
        {
            options = await _dbMcp.GetDbKeysAsync(_domain);
        }
        catch (Exception ex)
        {
            NxAssistant.Program.Log($"dbkeys 조회 실패 [{_domain}]: {ex.Message}");
            _dbKeys = saved is { Length: > 0 } ? saved : Array.Empty<string>();
            ShowChat();
            return;
        }

        // 옵션 0~1개 → 복수 선택 페이지 불필요 (서버가 단일 DB 로 판단)
        if (options.Count <= 1)
        {
            _dbKeys = options.Select(o => o.Key).ToArray();
            DbKeySelectionStore.Save(_domain, _dbKeys);
            if (force) MessageBox.Show("이 분야는 세부 DB 선택 항목이 없습니다.", "안내");
            ShowChat();
            return;
        }

        // 미리 체크: 저장된 선택 > 서버 default > 전체
        HashSet<string> pre = (saved is { Length: > 0 })
            ? new HashSet<string>(saved.Where(k => options.Any(o => o.Key == k)))
            : new HashSet<string>(options.Where(o => o.Default).Select(o => o.Key));
        if (pre.Count == 0) pre = new HashSet<string>(options.Select(o => o.Key));

        var (title, subtitle) = DbKeyPrompts.For(_domain);
        var view = new DbKeySelectView(title, subtitle, options, pre,
            onBack: ShowDomainSelect,
            onHome: ShowFieldSelect,
            onConfirm: keys =>
            {
                _dbKeys = keys;
                DbKeySelectionStore.Save(_domain, keys);
                ShowChat();
            });
        Swap(view);
    }

    // ── 화면 3: 채팅 ─────────────────────────────────────────
    private void ShowChat()
    {
        _session.SetDomain(_domain);    // /mech/ask 에 도메인 전달
        _session.SetDbKeys(_dbKeys);    // 선택한 db_keys 전달 (비어있으면 서버 전체 검색)
        _session.DomainName = _domainName;   // 상태 멘트용 표시명 (예: "설계수순서 DB를 조회하는 중")
        var view = new ChatView(_domainName, _session,
            onBack: ShowDomainSelect,
            onHome: ShowFieldSelect,
            onSettings: ShowSettings);
        Swap(view);
    }

    // ── 화면: 설정 ───────────────────────────────────────────
    private void ShowSettings()
    {
        var view = new SettingsView(
            onBack: ShowChat,
            onHome: ShowFieldSelect,
            onResetInterests: () => ShowDbKeySelect(force: true),   // 관심분야 재설정 (저장값 있어도 다시 표시)
            onReloginAi:      ShowAiSelect);                          // 외부 AI 재로그인 (AI 선택/로그인 재진입)
        Swap(view);
    }
}
