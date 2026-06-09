// providers/LlmSession.cs
// 앱 전역 LLM 세션 관리.
// - 선택된 LLM(Gauss/GPT)에 맞는 Provider 보유
// - GPT는 WorkerForm(로그인) 생성/공유 → 도메인 바꿔도 로그인 1회
// - 도메인 바꿔도 LLM 설정 유지 (앱 전역 1개)

using NxAssistant.UI;

namespace NxAssistant.Providers;

public sealed class LlmSession : IDisposable
{
    public string       Current  { get; private set; } = "Gauss";
    public ILlmProvider Provider { get; private set; }

    private WorkerForm? _gptWorker;       // GPT 로그인/채팅 워커 (공유)
    private GptProvider? _gptProvider;
    private GaussProvider? _gaussProvider;

    public LlmSession(string initial = "Gauss")
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

    /// <summary>현재 Provider로 질의.</summary>
    public Task<string> AskAsync(string prompt, CancellationToken ct = default)
        => Provider.ChatAsync(prompt, ct);

    /// <summary>DB 도메인 키 설정. Gauss DB조회 시 /mech/ask 에 실린다.</summary>
    public void SetDomain(string domain)
    {
        _gaussProvider ??= new GaussProvider();
        _gaussProvider.Domain = domain ?? "";
    }

    public void Dispose()
    {
        _gptWorker?.Dispose();
    }
}
