// ui/WorkerForm.cs
// GPT WebView2 워커 창
// 사용자에게 보이지 않는 숨겨진 브라우저 창
// chatgpt.com을 띄우고 DOM 조작으로 메시지 전송/수신

using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxAssistant.UI;

public sealed class WorkerForm : Form
{
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private bool _initialized;
    public  bool IsGptReady { get; private set; }

    public WorkerForm()
    {
        Text          = "NX Assistant GPT Login";
        Icon          = AppIcon.Load();
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size          = new Size(980, 760);
        Controls.Add(webView);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NX_Assistant", "WebView2WorkerProfile");

        var options = new CoreWebView2EnvironmentOptions
        {
            AllowSingleSignOnUsingOSPrimaryAccount = true
        };
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder:          userDataFolder,
            options:                 options);

        await webView.EnsureCoreWebView2Async(environment);
        webView.CoreWebView2.Navigate("https://chatgpt.com/");
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
  const composer = Array.from(document.querySelectorAll('#prompt-textarea, div[role="textbox"], textarea, [contenteditable="true"]'))
    .filter(el => !el.disabled && visible(el)).pop();
  return { url: location.href, title: document.title, hasComposer: !!composer };
})()
""");
        var result = JsonSerializer.Deserialize<ProbeResult>(
            raw.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ProbeResult();
        IsGptReady = result.HasComposer;
        return result;
    }

    /// <summary>새 채팅 시작 — 앱 히스토리 초기화 시 함께 호출</summary>
    public async Task StartNewChatAsync()
    {
        await InitializeAsync();
        // ChatGPT 새 채팅 버튼 클릭
        await EvalAsync("""
(() => {
  const btn = document.querySelector('a[href="/"], button[aria-label*="New"]');
  if (btn) btn.click();
})()
""");
        await Task.Delay(800);
        IsGptReady = false;
        // 준비 상태 재확인
        var probe = await ProbeAsync();
        IsGptReady = probe.HasComposer;
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

    private async Task<bool> SendMessageAsync(string userMessage)
    {
        var prompt = "You are replying through NX Assistant. Reply directly and do not mention browser automation.\n\n" +
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
