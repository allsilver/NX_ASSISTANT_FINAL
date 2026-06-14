// providers/DbQuerySession.cs
// 앱 전역 LLM 세션 관리.
// - 선택된 LLM(Gauss/GPT)에 맞는 Provider 보유
// - GPT는 WorkerForm(로그인) 생성/공유 → 도메인 바꿔도 로그인 1회
// - 도메인 바꿔도 LLM 설정 유지 (앱 전역 1개)

using System.Runtime.CompilerServices;
using NxAssistant.Mcp;
using NxAssistant.UI;

namespace NxAssistant.Providers;

public sealed class DbQuerySession : IChatSession, IDisposable
{
    public string       Current  { get; private set; } = "Gauss";
    public ILlmProvider Provider { get; private set; }

    private WorkerForm? _gptWorker;       // GPT 로그인/채팅 워커 (공유)
    private GptProvider? _gptProvider;
    private GaussProvider? _gaussProvider;

    private readonly DbMcpClient _dbMcp = new();          // GPT 답변용 프롬프트를 서버에서 받아옴
    private string   _domain = "";                        // 현재 도메인 (GPT 프롬프트 요청에 사용)
    private string[] _dbKeys = Array.Empty<string>();     // 현재 db_keys (GPT 프롬프트 요청에 사용)

    public DbQuerySession(string initial = "Gauss")
    {
        _gaussProvider = new GaussProvider();
        Provider = _gaussProvider;
        // 초기 LLM 설정 (GPT면 SetLlmAsync로 별도 준비 필요)
        Current = initial;
        if (initial == "Gauss")
            Provider = _gaussProvider;
    }

    public bool IsGpt => Current == "GPT";

    /// <summary>LLM 전환. GPT면 로그인 상태 확인 후 필요 시에만 로그인창 표시.</summary>
    public async Task SetLlmAsync(string llm)
    {
        Current = llm;
        if (llm == "Gauss")
        {
            _gaussProvider ??= new GaussProvider();
            Provider = _gaussProvider;
            return;
        }

        // GPT: 워커가 없으면 생성
        if (_gptWorker == null)
        {
            _gptWorker   = new WorkerForm(WorkerRole.User);
            _gptProvider = new GptProvider(_gptWorker);
        }
        Provider = _gptProvider!;

        // 1) 창 안 띄우고 초기화만
        await _gptProvider!.InitOnlyAsync();

        // 2) 이미 로그인돼 있으면(쿠키 남음) 창 안 띄우고 끝
        if (await _gptProvider.ProbeReadyAsync())
        {
            _gptProvider.HideWorker();
            return;
        }

        // 3) 로그인 필요 → 창 띄우고, 백그라운드로 완료 감지 → 완료 시 자동 숨김
        _gptProvider.ShowLogin();
        _ = WaitLoginThenHideAsync();
    }

    // 로그인 완료를 감지하면 워커를 자동으로 숨김
    private async Task WaitLoginThenHideAsync()
    {
        if (_gptProvider == null) return;
        var deadline = DateTime.UtcNow.AddMinutes(5);   // 최대 5분 대기
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            try
            {
                if (await _gptProvider.ProbeReadyAsync())
                {
                    _gptProvider.HideWorker();
                    return;
                }
            }
            catch { /* 페이지 전환 중 일시 오류 무시 */ }
        }
    }

    /// <summary>GPT 로그인 완료 여부 확인.</summary>
    public async Task<bool> IsGptReadyAsync()
    {
        if (_gptProvider == null) return false;
        return await _gptProvider.ProbeReadyAsync();
    }

    /// <summary>
    /// 현재 Provider로 질의.
    ///  - Gauss: 서버 /mech/ask 가 검색+답변까지 (질문 원문 전달).
    ///  - GPT  : 서버에서 검색+프롬프트 조립(for_gpt)만 받아, 그 프롬프트로 GPT 가 답변 생성.
    /// </summary>
    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        if (IsGpt)
        {
            var (composed, _) = await _dbMcp.GetGptPromptAsync(prompt, _domain, _dbKeys, ct);
            if (string.IsNullOrWhiteSpace(composed)) composed = prompt;   // 안전장치(서버 빈 응답 시 원문)
            return await Provider.ChatAsync(composed, ct);
        }
        return await Provider.ChatAsync(prompt, ct);
    }

    /// <summary>상태 멘트에 쓸 도메인 표시명 (예: "설계수순서"). MainForm 에서 주입.</summary>
    public string DomainName { get; set; } = "";

    /// <summary>현재 LLM 으로 질의 → 스트리밍 이벤트(진행 멘트 → 부분 답변 → 완료).</summary>
    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (IsGpt && _gptProvider != null)
        {
            // 1) 검색·프롬프트 조립(서버) — 이 await 동안 "조회 중"이 떠 있음(실제 검색 시간만큼)
            yield return ChatEvent.Status(SearchStatusText());
            var (prompt, images) = await _dbMcp.GetGptPromptAsync(question, _domain, _dbKeys, ct);
            if (string.IsNullOrWhiteSpace(prompt)) prompt = question;

            // 2) GPT 답변 생성 — 워커가 마크다운 스냅샷을 누적으로 yield → Token 으로 전달
            yield return ChatEvent.Status("답변을 작성하는 중");
            await foreach (var snapshot in _gptProvider.ChatStreamAsync(prompt, ct))
                yield return ChatEvent.Token(snapshot);   // snapshot = 현재까지의 마크다운

            // 3) 검색된 표준 이미지 (있으면 답변 아래 표시)
            if (images is { Count: > 0 })
                yield return ChatEvent.ImageList(images);
            yield return ChatEvent.Done();
        }
        else
        {
            // Gauss(1차): 한 번에. 답변이 markdown 이면 ChatView 가 서식 렌더. (SSE 스트리밍은 다음 단계)
            yield return ChatEvent.Status(SearchStatusText());
            var answer = await AskAsync(question, ct);
            yield return ChatEvent.Token(answer);

            var gimages = _gaussProvider?.LastImages;
            if (gimages is { Count: > 0 })
                yield return ChatEvent.ImageList(gimages);
            yield return ChatEvent.Done();
        }
    }

    private string SearchStatusText()
        => string.IsNullOrEmpty(DomainName) ? "관련 자료를 검색하는 중" : $"{DomainName} DB를 조회하는 중";

    /// <summary>DB 도메인 키 설정. Gauss /mech/ask + GPT 프롬프트 요청에 실린다.</summary>
    public void SetDomain(string domain)
    {
        _domain = domain ?? "";
        _gaussProvider ??= new GaussProvider();
        _gaussProvider.Domain = _domain;
    }

    /// <summary>선택된 db_key 목록 설정. Gauss /mech/ask + GPT 프롬프트 요청에 실린다. (비어있으면 서버 전체 검색)</summary>
    public void SetDbKeys(string[] keys)
    {
        _dbKeys = keys ?? Array.Empty<string>();
        _gaussProvider ??= new GaussProvider();
        _gaussProvider.DbKeys = _dbKeys;
    }

    public void Dispose()
    {
        _gptWorker?.Dispose();
    }
}
