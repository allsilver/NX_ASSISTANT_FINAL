// MockLlmSession.cs (UI 프리뷰 전용)
// ILlmSession 을 구현하되 서버/WebView2 없이 가짜 답변만 돌려줌.
// ChatView 가 이걸 주입받아 실제처럼 동작(답변만 mock).

using System.Runtime.CompilerServices;
using System.IO;
using NxAssistant.Providers;
using NxAssistant.Mcp;

namespace NxAssistant.UI;

internal sealed class MockLlmSession : ILlmSession
{
    public string Current { get; private set; } = "Gauss";
    public bool   IsGpt   => Current == "GPT";

    public Task<bool> IsGptReadyAsync() => Task.FromResult(true);

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);   // 답변 생성 지연 흉내 (스피너 확인용)
        return $"[mock · {Current}] \"{prompt}\" 에 대한 예시 답변입니다.\n" +
               "UI 프리뷰라 실제 서버/LLM 연결 없이 표시됩니다.\n" +
               "여러 줄·줄바꿈·말풍선 레이아웃을 확인하기 위한 더미 텍스트입니다.";
    }

    // 가짜 스트리밍: 진행 멘트 → 토큰 단위 출력 → 완료. (서버/GPT 없이 UI 검증용)
    public async IAsyncEnumerable<ChatEvent> AskStreamAsync(string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return ChatEvent.Status("질문을 이해하는 중");
        await Task.Delay(700, ct);

        yield return ChatEvent.Status(IsGpt ? "관련 자료를 검색하는 중" : "설계수순서 DB를 조회하는 중");
        await Task.Delay(900, ct);

        yield return ChatEvent.Status("답변을 작성하는 중");
        await Task.Delay(500, ct);

        var mdLines = new[]
        {
            "### [FH-012] 폴더블 힌지 표준",
            "- 힌지 반복 내구는 **20만 회 이상**을 만족해야 한다.",
            "- 토크 편차는 **±10%** 이내로 한다.",
            "",
            "간단한 코드 예시는 아래와 같습니다.",
            "",
            "```",
            "for i in range(5):",
            "    print(f\"Hello NX! {i}\")",
            "```",
            "",
            "그 외 세부 기준은 `자료에 없음`입니다."
        };
        var acc = new System.Text.StringBuilder();
        foreach (var ln in mdLines)
        {
            ct.ThrowIfCancellationRequested();
            acc.Append(ln).Append('\n');
            yield return ChatEvent.Token(acc.ToString());   // 누적 마크다운 스냅샷
            await Task.Delay(180, ct);
        }

        // 검색 이미지 렌더 확인용 (프리뷰 — 서버 없이 가짜 PNG 2장)
        yield return ChatEvent.ImageList(new[]
        {
            MakeMockImage("foldable Damper Front Damper Front 설계 Flip.png", 100),
            MakeMockImage("foldable Dust Cover Dust Cover 설계가이드.png",     62),
        });
        yield return ChatEvent.Done();
    }

    private static RagImage MakeMockImage(string name, int pct)
    {
        using var bmp = new System.Drawing.Bitmap(720, 380);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.FromArgb(245, 247, 250));
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(180, 190, 205), 2);
            g.DrawRectangle(pen, 8, 8, 703, 363);
            using var f  = new System.Drawing.Font("Malgun Gothic", 18F, System.Drawing.FontStyle.Bold);
            using var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(90, 100, 120));
            var sf = new System.Drawing.StringFormat
            {
                Alignment     = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString("MOCK 표준 이미지 (프리뷰)", f, br, new System.Drawing.RectangleF(0, 0, 720, 380), sf);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return new RagImage(name, pct, ms.ToArray());
    }

    private static IEnumerable<string> Tokenize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            sb.Append(ch);
            if (ch is ' ' or '\n') { yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public Task SetLlmAsync(string llm)
    {
        Current = llm;   // 실제 로그인/전환 없이 이름만 변경
        return Task.CompletedTask;
    }
}
