// PreviewShell.cs (UI 프리뷰 전용)
// MainForm 의 화면 전환을 그대로 흉내내되, 서버 대신 mock 데이터/세션을 사용.
// 전 페이지(AI선택 → 분야 → 도메인 → DB세부선택 → 채팅)가 실제처럼 연결됨.
//
// ※ 화면 위젯(View) 자체는 본 앱과 같은 파일을 링크해 쓴다.
//   여기(셸)만 프리뷰 전용이며, 본 앱 MainForm 과 흐름을 맞춰 둔다.

using NxAssistant.Config;
using NxAssistant.Mcp;
using NxAssistant.Providers;

namespace NxAssistant.UI;

internal sealed class PreviewShell : Form
{
    private readonly Panel _host;
    private readonly MockLlmSession _session = new();
    private string _domain = "";
    private string _domainName = "";
    private string[] _dbKeys = Array.Empty<string>();

    public PreviewShell()
    {
        Text          = "Gauss AI — UI 프리뷰 (mock)";
        StartPosition = FormStartPosition.CenterScreen;
        Size          = new Size(620, 1000);
        MinimumSize   = new Size(460, 720);
        BackColor     = Palette.Bg;
        Font          = new Font("Malgun Gothic", 9F);

        _host = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
        Controls.Add(_host);

        ShowAiSelect();
    }

    private void Swap(Control view)
    {
        var old = _host.Controls.Count > 0 ? _host.Controls[0] : null;
        view.Dock = DockStyle.Fill;
        _host.Controls.Add(view);
        view.BringToFront();
        if (old != null) { _host.Controls.Remove(old); old.Dispose(); }
    }

    // 화면 0: AI 선택
    private void ShowAiSelect()
    {
        var view = new AiSelectView(async llm =>
        {
            await _session.SetLlmAsync(llm);   // mock: 이름만 변경 (로그인 없음)
            ShowFieldSelect();
        });
        Swap(view);
    }

    // 화면 1: 분야 선택
    private void ShowFieldSelect()
    {
        var view = new FieldSelectView(
            onDbQuery:   ShowDomainSelect,
            onNxControl: () => MessageBox.Show("NX 제어는 준비 중입니다. (프리뷰)", "안내"),
            onAuto:      () => MessageBox.Show("자동화 기능은 준비 중입니다. (프리뷰)", "안내"));
        Swap(view);
    }

    // 화면 2: DB 도메인 선택
    private void ShowDomainSelect()
    {
        var view = new DomainSelectView(
            onBack: ShowFieldSelect,
            onHome: ShowFieldSelect,
            onPick: (key, name) => { _domain = key; _domainName = name; ShowDbKeySelect(); });
        Swap(view);
    }

    // 화면 2.5: DB 세부 선택 (mock 옵션)
    private void ShowDbKeySelect()
    {
        var options = MockOptions(_domain);

        // 옵션 0~1개면 선택 페이지 건너뛰고 바로 채팅 (실제 흐름과 동일)
        if (options.Count <= 1)
        {
            _dbKeys = options.Select(o => o.Key).ToArray();
            ShowChat();
            return;
        }

        var saved = DbKeySelectionStore.Load(_domain);
        HashSet<string> preChecked;
        if (saved is { Length: > 0 })
            preChecked = new HashSet<string>(saved.Where(k => options.Any(o => o.Key == k)));
        else
            preChecked = new HashSet<string>(options.Where(o => o.Default).Select(o => o.Key));
        if (preChecked.Count == 0)
            preChecked = new HashSet<string>(options.Select(o => o.Key));

        var (title, subtitle) = DbKeyPrompts.For(_domain);

        var view = new DbKeySelectView(title, subtitle, options, preChecked,
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

    // 화면 3: 채팅 (mock 세션)
    private void ShowChat()
    {
        var view = new ChatView(_domainName, _session,
            onBack: ShowDomainSelect,
            onHome: ShowFieldSelect,
            onSettings: ShowSettings);
        Swap(view);
    }

    // 화면: 설정 (프리뷰)
    private void ShowSettings()
    {
        var view = new SettingsView(
            onBack: ShowChat,
            onHome: ShowFieldSelect,
            onResetInterests: ShowDbKeySelect,
            onReloginAi:      ShowAiSelect);
        Swap(view);
    }

    // 서버 /mech/dbkeys 응답을 흉내낸 mock. 경계 케이스 확인용.
    private static List<DbKeyOption> MockOptions(string domain) => domain switch
    {
        "MECH_STANDARD" => new()
        {
            new DbKeyOption("mobile",      "모바일",     "모바일 기구 설계 표준", false),
            new DbKeyOption("foldable",    "폴더블",     "폴더블 힌지·전개 구조", false),
            new DbKeyOption("water_proof", "방수 공통",  "방수/방진 공통 기준",   true),
            new DbKeyOption("wearable",    "웨어러블",   "웨어러블 소형 기구",    false),
        },
        "MECHA_DFM" => new()
        {
            new DbKeyOption("cam_design",   "CAM 설계", "CAM 가공 고려",  false),
            new DbKeyOption("jig_design",   "지그 설계", "지그 제작 고려", false),
            new DbKeyOption("metal_design", "메탈 설계", "판금/메탈 고려", false),
            new DbKeyOption("mold_design",  "금형 설계", "사출 금형 고려", true),
        },
        // CMF_* 는 단일 → 선택 페이지 건너뜀
        "CMF_DFC"   => new() { new DbKeyOption("CMF_DFC",   "DFC", "Design For Cost", true) },
        "CMF_ISSUE" => new() { new DbKeyOption("CMF_ISSUE", "CMF", "CMF 문제/이력",    true) },
        _ => new(),
    };
}
