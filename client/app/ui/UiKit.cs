// ui/UiKit.cs
// 공통 UI 키트: 색상 팔레트 + 재사용 컨트롤 모음 (1차 배포용, 추후 분리)

using System.Drawing.Drawing2D;

namespace NxAssistant.UI;

internal static class Palette
{
    public static readonly Color Bg          = Color.FromArgb(238, 241, 245);
    public static readonly Color Surface     = Color.FromArgb(255, 255, 255);
    public static readonly Color Border       = Color.FromArgb(200, 208, 220);
    public static readonly Color Accent       = Color.FromArgb(26, 58, 107);
    public static readonly Color AccentSoft   = Color.FromArgb(40, 68, 115);
    public static readonly Color Text         = Color.FromArgb(26, 31, 46);
    public static readonly Color Muted        = Color.FromArgb(90, 100, 120);
    public static readonly Color CardHover    = Color.FromArgb(228, 234, 244);
    public static readonly Color IconBg       = Color.FromArgb(232, 237, 245);
    public static readonly Color IconBgHover  = Color.FromArgb(214, 224, 240);
    public static readonly Color UserBubble   = Color.FromArgb(26, 58, 107);
    public static readonly Color SubPill      = Color.FromArgb(255, 255, 255);
    public static readonly Color SubPillHover = Color.FromArgb(232, 238, 248);
    public static readonly Color GptGreen     = Color.FromArgb(16, 163, 127);
    public static readonly Color CheckColor   = Color.FromArgb(40, 110, 90);
}

internal enum IconKind { Search, Gear, Bolt, Doc, Coin, Atom, Spanner }
internal enum LogoKind { Gauss, Gpt }
internal enum NavIcon  { Back, Home, Gear }

internal class RoundedPanel : Panel
{
    public Color BorderColor { get; set; } = Color.Gray;
    public int   Radius      { get; set; } = 16;
    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Palette.Surface;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Palette.Bg))
            e.Graphics.FillRectangle(bg, ClientRectangle);
        var rect = new Rectangle(0, 0, Width-1, Height-1);
        using var path = Rounded(rect, Radius);
        using (var fill = new SolidBrush(BackColor)) e.Graphics.FillPath(fill, path);
        using (var pen = new Pen(BorderColor, 1.5f)) e.Graphics.DrawPath(pen, path);
    }
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(2, radius*2); var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right-d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); path.AddArc(r.X, r.Bottom-d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class AutoShrinkLabel : Control
{
    public Font BaseFont { get; set; } = new Font("Malgun Gothic", 14F);
    public AutoShrinkLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float size = BaseFont.Size;
        Font font = new Font(BaseFont.FontFamily, size, BaseFont.Style);
        while (size > 7f)
        {
            if (g.MeasureString(Text, font).Width <= Width - 4) break;
            font.Dispose(); size -= 0.5f; font = new Font(BaseFont.FontFamily, size, BaseFont.Style);
        }
        using var brush = new SolidBrush(ForeColor);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(Text, font, brush, ClientRectangle, sf);
        font.Dispose();
    }
}

internal sealed class VectorIcon : Control
{
    public Color    Fill       { get; set; } = Palette.IconBg;
    public Color    GlyphColor { get; set; } = Palette.Accent;
    public IconKind Kind       { get; set; } = IconKind.Search;
    public VectorIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height); if (d > 56) d = 56;
        int cx = Width/2, cy = Height/2;
        var circle = new Rectangle(cx-d/2, cy-d/2, d, d);
        using (var b = new SolidBrush(Fill)) g.FillEllipse(b, circle);
        float s = d * 0.5f;
        var box = new RectangleF(cx-s/2, cy-s/2, s, s);
        using var pen = new Pen(GlyphColor, Math.Max(2f, d*0.05f)){ StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round };
        using var fill = new SolidBrush(GlyphColor);
        switch (Kind)
        {
            case IconKind.Search:  DrawSearch(g, box, pen); break;
            case IconKind.Gear:    DrawGear(g, box, fill); break;
            case IconKind.Bolt:    DrawBolt(g, box, fill); break;
            case IconKind.Doc:     DrawDoc(g, box, pen); break;
            case IconKind.Coin:    DrawCoin(g, box, pen); break;
            case IconKind.Atom:    DrawAtom(g, box, pen, fill); break;
            case IconKind.Spanner: DrawSpanner(g, box, fill); break;
        }
    }
    private static void DrawSearch(Graphics g, RectangleF b, Pen pen)
    {
        float r = b.Width * 0.62f; var lens = new RectangleF(b.X, b.Y, r, r);
        g.DrawEllipse(pen, lens);
        g.DrawLine(pen, lens.Right - r*0.18f, lens.Bottom - r*0.18f, b.Right, b.Bottom);
    }
    private static void DrawGear(Graphics g, RectangleF b, SolidBrush fill)
    {
        float cx = b.X+b.Width/2, cy = b.Y+b.Height/2;
        float outer = b.Width*0.5f, inner = b.Width*0.34f, hole = b.Width*0.16f;
        for (int i=0;i<8;i++){ var st=g.Save(); g.TranslateTransform(cx,cy); g.RotateTransform(45f*i);
            float tw=b.Width*0.16f, th=b.Width*0.18f; g.FillRectangle(fill, -tw/2, -outer, tw, th); g.Restore(st); }
        g.FillEllipse(fill, cx-inner, cy-inner, inner*2, inner*2);
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(white, cx-hole, cy-hole, hole*2, hole*2);
    }
    private static void DrawBolt(Graphics g, RectangleF b, SolidBrush fill)
    {
        float x=b.X, y=b.Y, w=b.Width, h=b.Height;
        g.FillPolygon(fill, new[]{ new PointF(x+w*0.55f,y), new PointF(x+w*0.18f,y+h*0.58f),
            new PointF(x+w*0.45f,y+h*0.58f), new PointF(x+w*0.40f,y+h),
            new PointF(x+w*0.82f,y+h*0.40f), new PointF(x+w*0.52f,y+h*0.40f) });
    }
    private static void DrawDoc(Graphics g, RectangleF b, Pen pen)
    {
        float w=b.Width*0.66f, h=b.Height*0.82f, x=b.X+(b.Width-w)/2, y=b.Y+(b.Height-h)/2;
        g.DrawRectangle(pen, x, y, w, h);
        for (int i=1;i<=3;i++){ float ly=y+h*(0.22f*i); g.DrawLine(pen, x+w*0.18f, ly, x+w*0.82f, ly); }
    }
    private static void DrawCoin(Graphics g, RectangleF b, Pen pen)
    {
        g.DrawEllipse(pen, b.X, b.Y, b.Width, b.Height);
        float cx=b.X+b.Width/2;
        g.DrawLine(pen, cx, b.Y+b.Height*0.28f, cx, b.Y+b.Height*0.72f);
        g.DrawLine(pen, cx-b.Width*0.16f, b.Y+b.Height*0.42f, cx+b.Width*0.16f, b.Y+b.Height*0.42f);
        g.DrawLine(pen, cx-b.Width*0.16f, b.Y+b.Height*0.56f, cx+b.Width*0.16f, b.Y+b.Height*0.56f);
    }
    private static void DrawAtom(Graphics g, RectangleF b, Pen pen, SolidBrush fill)
    {
        float cx=b.X+b.Width/2, cy=b.Y+b.Height/2, rx=b.Width*0.5f, ry=b.Height*0.22f;
        for (int i=0;i<3;i++){ var st=g.Save(); g.TranslateTransform(cx,cy); g.RotateTransform(60f*i);
            g.DrawEllipse(pen, -rx, -ry, rx*2, ry*2); g.Restore(st); }
        float nr=b.Width*0.10f; g.FillEllipse(fill, cx-nr, cy-nr, nr*2, nr*2);
    }
    private static void DrawSpanner(Graphics g, RectangleF b, SolidBrush fill)
    {
        var st=g.Save(); g.TranslateTransform(b.X+b.Width/2, b.Y+b.Height/2); g.RotateTransform(40f);
        float w=b.Width;
        using var hpen = new Pen(fill.Color, w*0.16f){ StartCap=LineCap.Round, EndCap=LineCap.Round };
        g.DrawLine(hpen, 0, w*0.05f, 0, w*0.42f);
        float hw=w*0.30f, hh=w*0.26f; g.FillRectangle(fill, -hw/2, -w*0.42f, hw, hh);
        using var bg = new SolidBrush(Palette.Surface);
        float jaw=hw*0.34f; g.FillRectangle(bg, -jaw/2, -w*0.46f, jaw, hh*0.7f);
        g.Restore(st);
    }
}

internal sealed class BrandLogo : Control
{
    public LogoKind Kind { get; set; }
    public BrandLogo()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height); if (d > 60) d = 60;
        int cx = Width/2, cy = Height/2;
        var circle = new Rectangle(cx-d/2, cy-d/2, d, d);
        if (Kind == LogoKind.Gauss)
        {
            using var fill = new SolidBrush(Palette.Accent); g.FillEllipse(fill, circle);
            using var font = new Font("Segoe UI", d*0.42f, FontStyle.Bold);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("G", font, Brushes.White, circle, sf);
        }
        else
        {
            using var fill = new SolidBrush(Palette.GptGreen); g.FillEllipse(fill, circle);
            using var pen = new Pen(Color.White, Math.Max(2f, d*0.06f)){ StartCap=LineCap.Round, EndCap=LineCap.Round };
            float r = d*0.26f;
            for (int i=0;i<6;i++){ double a=Math.PI/3*i; g.DrawLine(pen, cx, cy, cx+(float)(r*Math.Cos(a)), cy+(float)(r*Math.Sin(a))); }
        }
    }
}

internal sealed class CheckItem : Control
{
    private readonly string _text;
    public CheckItem(string text)
    {
        _text = text; AutoSize = false; Width = 320;
        Margin = new Padding(0, 5, 0, 5);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = MeasureHeight();
    }
    private int MeasureHeight()
    {
        using var font = new Font("Malgun Gothic", 9F);
        using var g = CreateGraphics();
        var sz = g.MeasureString(_text, font, (Width>60?Width:320) - 24);
        return (int)Math.Ceiling(sz.Height) + 6;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Malgun Gothic", 9F);
        int textLeft = 22;
        using var pen = new Pen(Palette.CheckColor, 2f){ StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round };
        float cy = 5;
        g.DrawLines(pen, new[]{ new PointF(2, cy+4), new PointF(7, cy+9), new PointF(15, cy) });
        var rect = new RectangleF(textLeft, 0, Width-textLeft, Height);
        using var brush = new SolidBrush(Palette.Muted);
        g.DrawString(_text, font, brush, rect);
    }
}

internal sealed class NavButton : Control
{
    private readonly NavIcon _icon; private bool _hover;
    public NavButton(NavIcon icon)
    {
        _icon = icon; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Palette.Accent)) g.FillRectangle(bg, ClientRectangle);
        int box=32, x=(Width-box)/2, y=(Height-box)/2;
        var rect = new Rectangle(x, y, box, box);
        using (var path = RoundedPanel.Rounded(rect, 8))
        using (var fill = new SolidBrush(_hover ? Color.FromArgb(55,85,135) : Palette.AccentSoft))
            g.FillPath(fill, path);
        using var pen = new Pen(Color.White, 2f){ StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round };
        using var w = new SolidBrush(Color.White);
        float cx=x+box/2f, cy=y+box/2f, r=box*0.26f;
        switch (_icon)
        {
            case NavIcon.Back:
                g.DrawLine(pen, cx+r, cy, cx-r, cy);
                g.DrawLine(pen, cx-r, cy, cx-r*0.2f, cy-r*0.7f);
                g.DrawLine(pen, cx-r, cy, cx-r*0.2f, cy+r*0.7f);
                break;
            case NavIcon.Home:
                var roof = new[]{ new PointF(cx, cy-r*1.1f), new PointF(cx-r*1.2f, cy), new PointF(cx+r*1.2f, cy) };
                g.FillPolygon(w, roof);
                g.FillRectangle(w, cx-r*0.8f, cy, r*1.6f, r*1.1f);
                break;
            case NavIcon.Gear:
                float gr = r*1.35f;
                for (int i=0;i<8;i++){ var st=g.Save(); g.TranslateTransform(cx,cy); g.RotateTransform(45f*i);
                    g.FillRectangle(w, -gr*0.20f, -gr*1.15f, gr*0.40f, gr*0.5f); g.Restore(st); }
                g.FillEllipse(w, cx-gr*0.72f, cy-gr*0.72f, gr*1.44f, gr*1.44f);
                using (var hole = new SolidBrush(_hover ? Color.FromArgb(55,85,135) : Palette.AccentSoft))
                    g.FillEllipse(hole, cx-gr*0.34f, cy-gr*0.34f, gr*0.68f, gr*0.68f);
                break;
        }
    }
}

internal sealed class PillButton : Control
{
    private bool _hover;
    public PillButton(string text)
    {
        Text = text; Cursor = Cursors.Hand; Height = 34;
        Margin = new Padding(6, 0, 6, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        using var f = new Font("Malgun Gothic", 9F);
        Width = TextRenderer.MeasureText(text, f).Width + 30;
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        using var f = new Font("Malgun Gothic", 9F);
        Width = TextRenderer.MeasureText(Text, f).Width + 30; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Palette.Bg)) g.FillRectangle(bg, ClientRectangle);
        var rect = new Rectangle(0, 1, Width-1, Height-3);
        using var path = RoundedPanel.Rounded(rect, rect.Height/2);
        using (var fill = new SolidBrush(_hover ? Palette.SubPillHover : Palette.SubPill)) g.FillPath(fill, path);
        using (var pen = new Pen(Palette.Border, 1f)) g.DrawPath(pen, path);
        using var font = new Font("Malgun Gothic", 9F);
        TextRenderer.DrawText(g, Text, font, rect, Palette.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class SendButton : Control
{
    private bool _hover;
    public SendButton()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int d=36, x=(Width-d)/2, y=(Height-d)/2;
        using (var fill = new SolidBrush(_hover ? Color.FromArgb(40,80,140) : Palette.Accent))
            g.FillEllipse(fill, x, y, d, d);
        using var pen = new Pen(Color.White, 2.4f){ StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round };
        float cx=x+d/2f, cy=y+d/2f, r=d*0.22f;
        g.DrawLine(pen, cx, cy+r, cx, cy-r);
        g.DrawLine(pen, cx, cy-r, cx-r*0.7f, cy-r*0.2f);
        g.DrawLine(pen, cx, cy-r, cx+r*0.7f, cy-r*0.2f);
    }
}

internal sealed class TopBar : Panel
{
    public TopBar(string title, Action? onBack = null, Action? onHome = null)
    {
        Dock = DockStyle.Top; Height = 50; BackColor = Palette.Accent;
        var titleLabel = new Label
        {
            Text = title, ForeColor = Color.White,
            Font = new Font("Malgun Gothic", 10.5F, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding((onBack != null || onHome != null) ? 8 : 22, 0, 0, 0),
        };
        Controls.Add(titleLabel);
        if (onHome != null)
        {
            var home = new NavButton(NavIcon.Home) { Dock = DockStyle.Left, Width = 46 };
            home.Click += (_, _) => onHome(); Controls.Add(home); home.BringToFront();
        }
        if (onBack != null)
        {
            var back = new NavButton(NavIcon.Back) { Dock = DockStyle.Left, Width = 46 };
            back.Click += (_, _) => onBack(); Controls.Add(back); back.BringToFront();
        }
        titleLabel.BringToFront();
    }
}

internal static class TitleBlock
{
    public static Control Make(string heading, string sub)
    {
        var box = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Palette.Bg, Padding = new Padding(16, 0, 16, 0) };
        var bottomGap = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Palette.Bg };
        var subLabel = new AutoShrinkLabel { Text = sub, BaseFont = new Font("Malgun Gothic", 9F), ForeColor = Palette.Muted, Dock = DockStyle.Top, Height = 22 };
        var midGap = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Palette.Bg };
        var headingLabel = new AutoShrinkLabel { Text = heading, BaseFont = new Font("Malgun Gothic", 14F, FontStyle.Bold), ForeColor = Palette.Text, Dock = DockStyle.Top, Height = 38 };
        var topGap = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Palette.Bg };
        box.Controls.Add(bottomGap); box.Controls.Add(subLabel); box.Controls.Add(midGap);
        box.Controls.Add(headingLabel); box.Controls.Add(topGap);
        return box;
    }
}

internal sealed class FieldCard : RoundedPanel
{
    public FieldCard(IconKind kind, string name, string desc, Action onClick)
    {
        Size = new Size(480, 130); Margin = new Padding(0, 0, 0, 18);
        BackColor = Palette.Surface; BorderColor = Palette.Border;
        Radius = 16; Cursor = Cursors.Hand; Padding = new Padding(22, 0, 22, 0);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new VectorIcon { Kind = kind, Dock = DockStyle.Fill, Fill = Palette.IconBg, GlyphColor = Palette.Accent };
        var textHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Padding = new Padding(16, 0, 0, 0) };
        textHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        textHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var nameLabel = new Label { Text = name, AutoSize = true, Font = new Font("Malgun Gothic", 12.5F, FontStyle.Bold), ForeColor = Palette.Text, Margin = new Padding(0, 0, 0, 6) };
        var descLabel = new Label { Text = desc, AutoSize = true, Font = new Font("Malgun Gothic", 8.5F), ForeColor = Palette.Muted, Margin = new Padding(0), MaximumSize = new Size(360, 0) };
        textHost.Controls.Add(new Label { Text = "", AutoSize = false, Margin = new Padding(0) }, 0, 0);
        textHost.Controls.Add(nameLabel, 0, 1);
        textHost.Controls.Add(descLabel, 0, 2);
        textHost.Controls.Add(new Label { Text = "", AutoSize = false, Margin = new Padding(0) }, 0, 3);
        grid.Controls.Add(icon, 0, 0);
        grid.Controls.Add(textHost, 1, 0);
        Controls.Add(grid);
        void Enter(object? s, EventArgs e) { BackColor = Palette.CardHover; BorderColor = Palette.Accent; icon.Fill = Palette.IconBgHover; Invalidate(); icon.Invalidate(); }
        void Leave(object? s, EventArgs e) { BackColor = Palette.Surface; BorderColor = Palette.Border; icon.Fill = Palette.IconBg; Invalidate(); icon.Invalidate(); }
        Hook(this, Enter, Leave, (_, _) => onClick());
    }
    private static void Hook(Control root, EventHandler enter, EventHandler leave, EventHandler click)
    {
        void H(Control c) { c.MouseEnter += enter; c.MouseLeave += leave; c.Click += click; c.Cursor = Cursors.Hand; foreach (Control ch in c.Controls) H(ch); }
        H(root);
    }
}
