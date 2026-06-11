// ui/WorkerForm.cs
// GPT WebView2 워커 창 (사용자에게 보이지 않는 숨겨진 브라우저)
// chatgpt.com을 띄우고 DOM 조작으로 메시지 전송/수신
//
// [역할(WorkerRole)]
//   - User   : 사용자 대화용. 일반 채팅. 히스토리 누적.
//   - Router : 1차 라우터 전용. 임시 채팅(?temporary-chat=true). 격리 단발 호출.
//
// 두 워커는 같은 WebView2 프로필을 공유 → 로그인 1회로 둘 다 로그인됨

using System.Runtime.CompilerServices;
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

        // [핵심] 페이지에 "항상 보이는 상태"라고 주입.
        // ChatGPT는 React 앱이라, 창이 화면 밖이면 document.hidden=true 로 보고 토큰마다의
        // DOM 갱신을 미뤘다가 끝에 한 번에 flush 함 → 폴링해도 최종만 읽힘(스트리밍 실패).
        // visibilityState/hidden 을 강제하고 visibilitychange/blur 를 삼켜서, 화면 밖에서도
        // 토큰 단위로 DOM 이 갱신되게 한다. (페이지 로드 전에 등록되어야 하므로 Navigate 직전)
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
"""
(() => { try {
  Object.defineProperty(document, 'visibilityState',       { configurable: true, get: () => 'visible' });
  Object.defineProperty(document, 'hidden',                { configurable: true, get: () => false });
  Object.defineProperty(document, 'webkitVisibilityState', { configurable: true, get: () => 'visible' });
  Object.defineProperty(document, 'webkitHidden',          { configurable: true, get: () => false });
  document.hasFocus = () => true;
  const block = (e) => e.stopImmediatePropagation();
  document.addEventListener('visibilitychange', block, true);
  window.addEventListener('visibilitychange', block, true);
  window.addEventListener('blur', block, true);
} catch (e) {} })();
""");

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
        NxAssistant.Program.Log($"[Worker] 전송 시도 (프롬프트 {message.Length}자)");
        if (!await SendMessageAsync(message))
        {
            NxAssistant.Program.Log("[Worker] 전송 실패: composer/send 버튼 미발견");
            throw new InvalidOperationException("ChatGPT composer에 메시지를 보낼 수 없습니다.");
        }
        NxAssistant.Program.Log("[Worker] 전송 완료 → 응답 폴링 시작 (최대 90초, 1.2초 간격)");

        var deadline    = DateTime.UtcNow.AddSeconds(90);
        var last        = "";
        var stableCount = 0;
        var polls       = 0;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1200);
            polls++;
            var current = await LatestAssistantTextAsync();
            if (IsTransient(current)) continue;
            if (!string.IsNullOrWhiteSpace(current) && current != before)
            {
                if (current == last) stableCount++;
                else { last = current; stableCount = 0; NxAssistant.Program.Log($"[Worker] 응답 갱신 중 ({last.Length}자, {polls}회)"); }
                if (stableCount >= 2) { NxAssistant.Program.Log($"[Worker] 응답 확정 ({current.Length}자, 폴링 {polls}회)"); return current; }
            }
        }
        NxAssistant.Program.Log($"[Worker] 90초 타임아웃 (폴링 {polls}회, last={last.Length}자, before와 동일?={last == before})");
        if (!string.IsNullOrWhiteSpace(last)) return last;
        throw new TimeoutException("ChatGPT 응답 타임아웃");
    }

    // 스트리밍: ChatGPT 답변이 자라는 동안 "현재까지의 누적 텍스트"를 스냅샷으로 계속 내보냄.
    // (per-token은 아니고 폴링 스냅샷 → 점진 출력 효과. 누적이라 소비측은 라벨을 그 값으로 교체)
    public async IAsyncEnumerable<string> ChatStreamAsync(string message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await InitializeAsync();
        await MarkSeenAsync();                                // 기존 답변 전부 '읽음' 표시
        NxAssistant.Program.Log($"[Worker] (stream) 전송 시도 ({message.Length}자)");
        if (!await SendMessageAsync(message))
        {
            NxAssistant.Program.Log("[Worker] (stream) 전송 실패: composer/send 미발견");
            throw new InvalidOperationException("ChatGPT 입력창에 질문을 전달하지 못했습니다. 다시 시도해 주세요.");
        }
        NxAssistant.Program.Log("[Worker] (stream) 전송 완료 → 완료 감지 폴링(0.5초)");

        var firstDeadline = DateTime.UtcNow.AddSeconds(35);
        var hardDeadline  = DateTime.UtcNow.AddSeconds(150);
        var lastText = "";
        var stable   = 0;
        var polls    = 0;
        var started  = false;

        // 완료 감지는 평문(가벼움)으로, 완료되면 최종 "마크다운"을 한 번만 yield (부분 렌더 안 함 → 안정적)
        while (DateTime.UtcNow < hardDeadline)
        {
            await Task.Delay(500, ct);
            polls++;
            var (has, text) = await ReadNewAnswerAsync();

            if (!started)
            {
                if (has) { started = true; NxAssistant.Program.Log("[Worker] (stream) 새 응답 감지"); }
                else if (DateTime.UtcNow > firstDeadline)
                {
                    NxAssistant.Program.Log($"[Worker] (stream) 35초 내 응답 시작 안 됨 (폴링 {polls}회)");
                    throw new TimeoutException("ChatGPT가 응답을 시작하지 않았습니다. 잠시 후 다시 질문해 주세요.");
                }
                else continue;
            }

            if (IsTransient(text) || string.IsNullOrWhiteSpace(text)) continue;
            if (text != lastText) { lastText = text; stable = 0; }
            else if (++stable >= 3)   // 1.5초간 변화 없음 → 완료
            {
                var md = await LastAnswerMarkdownAsync();
                NxAssistant.Program.Log($"[Worker] (stream) 응답 확정 (md {md.Length}자, 폴링 {polls}회)");
                yield return string.IsNullOrWhiteSpace(md) ? lastText : md;
                yield break;
            }
        }
        NxAssistant.Program.Log($"[Worker] (stream) 하드 타임아웃 (last={lastText.Length}자)");
        if (!string.IsNullOrWhiteSpace(lastText))
        {
            var md = await LastAnswerMarkdownAsync();
            yield return string.IsNullOrWhiteSpace(md) ? lastText : md;
            yield break;
        }
        throw new TimeoutException("ChatGPT 응답 시간이 초과되었습니다. 다시 시도해 주세요.");
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

    // 보내기 직전: 기존 assistant 메시지를 전부 '읽음(seen)' 표시 → 이후 표시 없는 노드 = 새 답변
    private async Task MarkSeenAsync()
    {
        await webView.CoreWebView2.ExecuteScriptAsync(
            "document.querySelectorAll('[data-message-author-role=\"assistant\"]').forEach(n=>n.setAttribute('data-nx-seen','1'));");
    }

    // 표시 없는(=새) 마지막 assistant 노드의 텍스트. 가상화/동일텍스트에도 안전.
    private async Task<(bool has, string text)> ReadNewAnswerAsync()
    {
        var raw = await webView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const ns = document.querySelectorAll('[data-message-author-role="assistant"]:not([data-nx-seen="1"])');
  if (!ns.length) return JSON.stringify({ has:false, text:'' });
  const n = ns[ns.length-1].cloneNode(true);
  n.querySelectorAll('button,svg,textarea,input,[contenteditable="true"],[role="button"]').forEach(el=>el.remove());
  return JSON.stringify({ has:true, text:(n.innerText||n.textContent||'').trim() });
})()
""");
        try
        {
            var inner = JsonSerializer.Deserialize<string>(raw) ?? "{}";
            using var doc = JsonDocument.Parse(inner);
            return (doc.RootElement.GetProperty("has").GetBoolean(),
                    doc.RootElement.GetProperty("text").GetString() ?? "");
        }
        catch { return (false, ""); }
    }

    // 새(표시 없는) 답변 노드를 마크다운으로 추출 (볼드/불릿/번호/제목/코드 보존)
    public async Task<string> LastAnswerMarkdownAsync()
    {
        var raw = await webView.CoreWebView2.ExecuteScriptAsync("""
(() => {
  const ns = document.querySelectorAll('[data-message-author-role="assistant"]:not([data-nx-seen="1"])');
  if (!ns.length) return JSON.stringify({ has:false, md:'' });
  const root = ns[ns.length-1];
  function inline(node){
    let s='';
    node.childNodes.forEach(c=>{
      if(c.nodeType===3){ s+=c.textContent; return; }
      if(c.nodeType!==1) return;
      const t=c.tagName.toLowerCase();
      if(t==='strong'||t==='b') s+='**'+inline(c).trim()+'**';
      else if(t==='em'||t==='i') s+='*'+inline(c).trim()+'*';
      else if(t==='code') s+='`'+c.textContent+'`';
      else if(t==='br') s+='\n';
      else s+=inline(c);
    });
    return s;
  }
  function block(node){
    let out='';
    node.childNodes.forEach(c=>{
      if(c.nodeType===3){ const tx=c.textContent.replace(/\s+/g,' ').trim(); if(tx) out+=tx+'\n'; return; }
      if(c.nodeType!==1) return;
      const t=c.tagName.toLowerCase();
      if(['button','svg','textarea','input'].includes(t)) return;
      if(/^h[1-6]$/.test(t)) out+='\n### '+inline(c).trim()+'\n';
      else if(t==='p') out+=inline(c).trim()+'\n';
      else if(t==='ul'){ c.querySelectorAll(':scope > li').forEach(li=>{ out+='- '+inline(li).trim()+'\n'; }); out+='\n'; }
      else if(t==='ol'){ let i=1; c.querySelectorAll(':scope > li').forEach(li=>{ out+=(i++)+'. '+inline(li).trim()+'\n'; }); out+='\n'; }
      else if(t==='pre'){ const code=c.querySelector('code'); const tx=((code?code.innerText:c.innerText)||'').replace(/\s+$/,''); out+='\n```\n'+tx+'\n```\n'; }
      else if(t==='li') out+='- '+inline(c).trim()+'\n';
      else out+=block(c);
    });
    return out;
  }
  let md = block(root).replace(/\n{3,}/g,'\n\n').trim();
  return JSON.stringify({ has:true, md:md });
})()
""");
        try
        {
            var inner = JsonSerializer.Deserialize<string>(raw) ?? "{}";
            using var doc = JsonDocument.Parse(inner);
            if (!doc.RootElement.GetProperty("has").GetBoolean()) return "";
            return doc.RootElement.GetProperty("md").GetString() ?? "";
        }
        catch { return ""; }
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