// ui/WorkerForm.cs
// GPT WebView2 워커 창 (사용자에게 보이지 않는 숨겨진 브라우저)
// chatgpt.com을 띄우고 DOM 조작으로 메시지 전송/수신
//
// [역할(WorkerRole)]
//   - User   : 사용자 대화용. 일반 채팅. 히스토리 누적.
//   - Router : 1차 라우터 전용. 임시 채팅(?temporary-chat=true). 격리 단발 호출.
//
// 두 워커는 같은 WebView2 프로필을 공유 → 로그인 1회로 둘 다 로그인됨

using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxAssistant.UI;

public enum WorkerRole { User, Router }

public sealed class WorkerForm : Form
{
    private readonly WebView2   webView = new() { Dock = DockStyle.Fill };
    private readonly WorkerRole _role;
    private bool _initialized;
    public  bool IsGptReady { get; private set; }

    private string StartUrl => _role == WorkerRole.Router
        ? "https://chatgpt.com/?temporary-chat=true"
        : "https://chatgpt.com/";

    public WorkerForm(WorkerRole role = WorkerRole.User)
    {
        _role         = role;
        Text          = role == WorkerRole.Router
            ? "NX Assistant Router"
            : "NX Assistant GPT Login";
        Icon          = AppIcon.Load();
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size          = new Size(980, 760);
        Controls.Add(webView);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // 두 워커가 같은 프로필 폴더를 공유 → 로그인 세션 공유
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NX_Assistant", "WebView2WorkerProfile");

        var options = new CoreWebView2EnvironmentOptions
        {
            AllowSingleSignOnUsingOSPrimaryAccount = true,
            // 화면 밖(ParkOffscreen) 상태에서도 렌더링/타이머가 멈추지 않게 함.
            // 이게 없으면 숨겨진 워커가 GPT 응답을 못 읽고 멈출 수 있음.
            AdditionalBrowserArguments =
                "--disable-background-timer-throttling " +
                "--disable-backgrounding-occluded-windows " +
                "--disable-renderer-backgrounding"
        };
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder:          userDataFolder,
            options:                 options);

        await webView.EnsureCoreWebView2Async(environment);
        webView.CoreWebView2.Navigate(StartUrl);
        _initialized = true;
    }

    public void ShowForLogin()
    {
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Location        = new Point(80, 80);
        Size            = new Size(980, 760);
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    public void ParkOffscreen()
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState     = FormWindowState.Normal;
        Location        = new Point(-32000, 80);
        Size            = new Size(460, 260);
    }

    public async Task<ProbeResult> ProbeAsync()
    {
        await InitializeAsync();
        var raw = await EvalAsync("""
(() => {
  function visible(n) {
    const r = n.getBoundingClientRect();
    const style = getComputedStyle(n);
    return r.width > 0 && r.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  }
  const composer = document.querySelector('#prompt-textarea');
  const composerVisible = !!(composer && visible(composer));
  const loggedOut = !!document.querySelector('button[data-testid="login-button"], a[href*="auth/login"]')
                    || /auth\.openai\.com|login|auth0/i.test(location.href);
  return { url: location.href, title: document.title, hasComposer: !!(composerVisible && !loggedOut) };
})()
""");
        var result = JsonSerializer.Deserialize<ProbeResult>(
            raw.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ProbeResult();
        IsGptReady = result.HasComposer;
        return result;
    }

    /// <summary>새 채팅 시작 (User 워커용) — 히스토리 초기화 시 GPT도 새 대화로</summary>
    public async Task StartNewChatAsync()
    {
        await InitializeAsync();
        await EvalAsync("""
(() => {
  const btn = document.querySelector('a[href="/"], button[aria-label*="New"]');
  if (btn) btn.click();
})()
""");
        await Task.Delay(800);
        IsGptReady = false;
        var probe = await ProbeAsync();
        IsGptReady = probe.HasComposer;
    }

    /// <summary>
    /// Router 워커 전용 — 임시 채팅을 새로 시작해서 이전 맥락 제거.
    /// 매 라우팅 호출 전에 호출하면 단발 격리 호출이 됨.
    /// </summary>
    public async Task ResetTemporaryChatAsync()
    {
        await InitializeAsync();
        webView.CoreWebView2.Navigate("https://chatgpt.com/?temporary-chat=true");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            var probe = await ProbeAsync();
            if (probe.HasComposer) return;
        }
    }

    public async Task<string> ChatAsync(string message)
    {
        await InitializeAsync();
        var before = await LatestAssistantTextAsync();
        if (!await SendMessageAsync(message))
            throw new InvalidOperationException("ChatGPT composer에 메시지를 보낼 수 없습니다.");

        var deadline    = DateTime.UtcNow.AddSeconds(90);
        var last        = "";
        var stableCount = 0;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1200);
            var current = await LatestAssistantTextAsync();
            if (IsTransient(current)) continue;
            if (!string.IsNullOrWhiteSpace(current) && current != before)
            {
                if (current == last) stableCount++;
                else { last = current; stableCount = 0; }
                if (stableCount >= 2) return current;
            }
        }
        if (!string.IsNullOrWhiteSpace(last)) return last;
        throw new TimeoutException("ChatGPT 응답 타임아웃");
    }

    /// <summary>
    /// Router 워커 전용 — 임시 채팅을 리셋한 뒤 단발 질문.
    /// 라우터 프롬프트가 사용자 대화/이전 라우팅에 섞이지 않음.
    /// </summary>
    public async Task<string> AskIsolatedAsync(string message)
    {
        var t0 = DateTime.Now;
        NxAssistant.Program.Log("[Worker:Router] 임시채팅 리셋 시작");

        // 화면 밖에 두고 호출 (throttling 비활성화 플래그로 정상 동작)
        ParkOffscreen();

        await ResetTemporaryChatAsync();
        NxAssistant.Program.Log($"[Worker:Router] 리셋 완료({(DateTime.Now - t0).TotalSeconds:F1}초), 질문 전송");
        var ans = await ChatAsync(message);
        NxAssistant.Program.Log($"[Worker:Router] 응답 수신(총 {(DateTime.Now - t0).TotalSeconds:F1}초) ans=\"{ans}\"");
        return ans;
    }

    private async Task<bool> SendMessageAsync(string userMessage)
    {
        // Router 워커는 분류만 하므로 시스템 안내를 다르게
        var prompt = _role == WorkerRole.Router
            ? userMessage
            : "You are replying through NX Assistant. Reply directly and do not mention browser automation.\n\n" +
              "User message:\n" + userMessage;

        var focus = await EvalAsync("""
(() => {
  function visible(n) {
    const r = n.getBoundingClientRect();
    return r.width > 0 && r.height > 0 && getComputedStyle(n).visibility !== 'hidden';
  }
  const el = Array.from(document.querySelectorAll('#prompt-textarea, div[role="textbox"], textarea, [contenteditable="true"]'))
    .filter(el => !el.disabled && visible(el)).pop();
  if (!el) return { ok: false };
  el.focus();
  return { ok: true };
})()
""");
        if (!focus.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return false;

        await CdpAsync("Input.dispatchKeyEvent", new { type = "rawKeyDown", modifiers = 2, windowsVirtualKeyCode = 65, key = "a", code = "KeyA" });
        await CdpAsync("Input.dispatchKeyEvent", new { type = "keyUp",      modifiers = 2, windowsVirtualKeyCode = 65, key = "a", code = "KeyA" });
        await CdpAsync("Input.insertText", new { text = prompt });

        var send = await EvalAsync("""
(() => {
  function visible(n) { const r = n.getBoundingClientRect(); return r.width > 0 && r.height > 0; }
  function enabled(b) { return !b.disabled && b.getAttribute('aria-disabled') !== 'true'; }
  const btn = Array.from(document.querySelectorAll('button')).reverse().find(b =>
    enabled(b) && visible(b) && (
      b.getAttribute('data-testid') === 'send-button' ||
      b.getAttribute('data-testid') === 'composer-submit-button' ||
      /send|submit/i.test((b.getAttribute('aria-label') || '') + ' ' + (b.textContent || ''))
    )
  );
  if (btn) { btn.click(); return { ok: true, send: 'button' }; }
  return { ok: true, send: 'needs_enter' };
})()
""");
        if (!send.TryGetProperty("ok", out var sOk) || !sOk.GetBoolean()) return false;

        if (send.TryGetProperty("send", out var mode) && mode.GetString() == "needs_enter")
        {
            await CdpAsync("Input.dispatchKeyEvent", new { type = "rawKeyDown", windowsVirtualKeyCode = 13, key = "Enter", code = "Enter" });
            await CdpAsync("Input.dispatchKeyEvent", new { type = "keyUp",      windowsVirtualKeyCode = 13, key = "Enter", code = "Enter" });
        }
        return true;
    }

    private async Task<string> LatestAssistantTextAsync()
    {
        var raw = await webView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const nodes = [];
  for (const n of document.querySelectorAll('[data-message-author-role="assistant"]')) {
    const clone = n.cloneNode(true);
    clone.querySelectorAll('button, svg, textarea, input, [contenteditable="true"], [role="button"]').forEach(el => el.remove());
    const t = (clone.innerText || clone.textContent || '').trim();
    if (t && nodes.indexOf(t) < 0) nodes.push(t);
  }
  return nodes.length ? nodes[nodes.length - 1] : '';
})()
""");
        return JsonSerializer.Deserialize<string>(raw) ?? "";
    }

    private async Task<JsonElement> EvalAsync(string expression)
    {
        var raw = await webView.CoreWebView2.ExecuteScriptAsync(expression);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private async Task CdpAsync(string method, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _ = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json);
    }

    private static bool IsTransient(string text)
    {
        var normalized = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return normalized is "" or "thinking" or "thinking...";
    }
}

public sealed class ProbeResult
{
    public string Url         { get; set; } = "";
    public string Title       { get; set; } = "";
    public bool   HasComposer { get; set; }
}