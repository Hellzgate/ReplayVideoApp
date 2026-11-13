using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayVideoApp
{
    /// <summary>
    /// 2026-11-07: 화면 토큰(색/간격/글꼴)과 공용 컨트롤 생성기.
    ///
    /// 왜: 이 앱은 컨트롤을 `SetBounds` 로 절대 배치해 왔기 때문에 DPI·리사이즈에서 어긋나고,
    /// 색/여백이 코드 곳곳에 흩어져 있었다(에디터의 테마 토큰과 같은 방식으로 모은다).
    /// </summary>
    internal static class Ui
    {
        // ── 색 토큰 ────────────────────────────────────────────
        public static readonly Color Bg = Color.FromArgb(30, 31, 34);        // 창
        public static readonly Color Panel = Color.FromArgb(38, 40, 44);     // 카드/구역
        public static readonly Color Box = Color.FromArgb(45, 47, 52);       // 입력
        public static readonly Color BoxHi = Color.FromArgb(52, 55, 61);     // 버튼
        public static readonly Color Border = Color.FromArgb(70, 74, 82);    // 테두리
        public static readonly Color Fg = Color.FromArgb(232, 234, 238);     // 본문
        public static readonly Color Sub = Color.FromArgb(158, 164, 174);    // 보조
        public static readonly Color Hint = Color.FromArgb(120, 126, 136);   // 힌트
        public static readonly Color Accent = Color.FromArgb(86, 156, 214);  // 강조(파랑)
        public static readonly Color AccentHi = Color.FromArgb(108, 178, 236);
        public static readonly Color Ok = Color.FromArgb(120, 200, 140);
        public static readonly Color Err = Color.FromArgb(240, 140, 140);
        public static readonly Color MonoTrack = Color.FromArgb(24, 25, 28); // 로그 배경

        // ── 글꼴/간격 ──────────────────────────────────────────
        public static readonly Font FontBase = new Font("Malgun Gothic", 9f);
        public static readonly Font FontTitle = new Font("Malgun Gothic", 12.5f, FontStyle.Bold);
        public static readonly Font FontSection = new Font("Malgun Gothic", 9f, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9f);
        public const int Gap = 8;
        public const int RowH = 26;

        // ── 공용 컨트롤 ────────────────────────────────────────
        public static Label Text(string text, Color? color = null, Font font = null)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.ForeColor = color ?? Fg;
            l.Font = font ?? FontBase;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Note(string text)
        {
            var l = Text(text, Hint, new Font("Malgun Gothic", 8.25f));
            return l;
        }

        public static TextBox Input(string placeholder = null)
        {
            var t = new TextBox();
            t.BackColor = Box;
            t.ForeColor = Fg;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = FontBase;
            t.Margin = new Padding(0, 4, Gap, 4);
            if (!string.IsNullOrEmpty(placeholder)) t.PlaceholderText = placeholder;
            return t;
        }

        public static Button Button(string text, bool primary = false, bool small = false)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.BackColor = primary ? Accent : BoxHi;
            b.ForeColor = primary ? Color.White : Fg;
            b.Font = new Font("Malgun Gothic", primary ? 9.75f : 9f, primary ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Height = small ? 24 : 28;
            b.Margin = new Padding(0, 4, Gap, 4);
            // FlatButton 은 hover 색을 따로 준다(기본 회색으로 깜빡이는 것 방지)
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHi : Color.FromArgb(62, 66, 73);
            b.FlatAppearance.MouseDownBackColor = primary ? Accent : Color.FromArgb(52, 55, 61);
            return b;
        }

        public static CheckBox Check(string text, bool @checked = true)
        {
            var c = new CheckBox();
            c.Text = text;
            c.ForeColor = Fg;
            c.BackColor = Panel;                                  // Transparent 면 Flat 체크박스가 흰 칸으로 그려진다
            c.Font = FontBase;
            c.Checked = @checked;
            c.AutoSize = true;
            c.FlatStyle = FlatStyle.Flat;                        // 시스템 흰 사각형 대신 팔레트 색
            c.FlatAppearance.BorderColor = Sub;
            c.FlatAppearance.CheckedBackColor = Accent;
            c.FlatAppearance.MouseOverBackColor = BoxHi;
            c.Margin = new Padding(0, 6, 14, 4);
            c.Cursor = Cursors.Hand;
            return c;
        }

        /// <summary>
        /// 어두운 드롭다운. WinForms ComboBox(DropDownList)는 BackColor 를 무시하고
        /// **흰 필드**로 그려진다(테마 비활성화 없이는 불가) → 단추 + 컨텍스트 메뉴로 대체했다.
        /// </summary>
        public static DropDownBox DropDown(params string[] items)
        {
            var d = new DropDownBox(items);
            d.Height = RowH;
            d.Margin = new Padding(0, 6, 18, 4);
            return d;
        }

        /// <summary>구역 제목 띠 — 왼쪽 강조 막대 + 옅은 배경 (에디터의 SectionHead 와 같은 역할).</summary>
        public static Panel SectionHeader(string title)
        {
            var p = new Panel();
            p.BackColor = Panel;
            p.Height = 26;
            p.Dock = DockStyle.Fill;
            p.Margin = new Padding(0, Gap + 2, 0, 4);

            var bar = new Panel();
            bar.BackColor = Accent;
            bar.Width = 3;
            bar.Dock = DockStyle.Left;
            p.Controls.Add(bar);

            var l = Text("  " + title, Sub, FontSection);
            l.Dock = DockStyle.Fill;
            p.Controls.Add(l);
            l.BringToFront();
            return p;
        }
    }

    /// <summary>
    /// 단추 + 컨텍스트 메뉴로 만든 어두운 드롭다운 (ComboBox 의 흰 필드 문제 회피).
    /// 선택 항목은 체크 표시로 보여준다.
    /// </summary>
    internal sealed class DropDownBox : Button
    {
        private readonly string[] _items;
        private int _index;
        private readonly ContextMenuStrip _menu;

        public DropDownBox(string[] items)
        {
            _items = items ?? new string[0];
            Text = "";
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = Ui.Border;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 66, 73);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(52, 55, 61);
            BackColor = Ui.Box;
            ForeColor = Ui.Fg;
            Font = Ui.FontBase;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(8, 0, 0, 0);
            UseVisualStyleBackColor = false;

            _menu = new ContextMenuStrip();
            _menu.BackColor = Ui.Panel;
            _menu.ForeColor = Ui.Fg;
            _menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            _menu.ShowImageMargin = false;
            for (int i = 0; i < _items.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(_items[i]);
                it.Click += delegate { SelectedIndex = idx; };
                _menu.Items.Add(it);
            }
            Click += delegate { _menu.Show(this, new Point(0, Height)); };
            SelectedIndex = 0;
        }

        public string[] Items { get { return _items; } }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (_items.Length == 0) return;
                int v = Math.Max(0, Math.Min(_items.Length - 1, value));
                _index = v;
                Text = _items[v] + "  ▾";
                for (int i = 0; i < _menu.Items.Count; i++)
                    ((ToolStripMenuItem)_menu.Items[i]).Checked = (i == v);
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string SelectedText { get { return _items.Length == 0 ? "" : _items[_index]; } }

        public event EventHandler SelectedIndexChanged;

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Ui.BoxHi; } }
            public override Color MenuItemBorder { get { return Ui.Accent; } }
            public override Color MenuBorder { get { return Ui.Border; } }
            public override Color ToolStripDropDownBackground { get { return Ui.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Ui.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Ui.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Ui.Panel; } }
            public override Color CheckBackground { get { return Ui.Panel; } }
            public override Color CheckSelectedBackground { get { return Ui.BoxHi; } }
        }
    }

    /// <summary>
    /// WinForms ProgressBar 는 색을 바꿀 수 없다(항상 초록 + 밝은 테두리) → 직접 그린다.
    /// 진행률 0~100 과 상태 문구를 함께 표시한다.
    /// </summary>
    internal sealed class ProgressBarEx : Control
    {
        private int _value;
        private string _caption = "";

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Height = 22;
            // ⚠ 커스텀 Control 은 SupportsTransparentBackColor 없이 Color.Transparent 를 못 쓴다
            //    (ArgumentException: 컨트롤이 투명 배경색을 지원하지 않습니다)
            BackColor = Ui.Bg;
            ForeColor = Ui.Fg;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
            }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 3, Width - 1, Height - 7);
            using (var back = new SolidBrush(Ui.Box))
                FillRounded(g, back, r, 5);
            if (_value > 0)
            {
                int w = Math.Max(6, (int)(r.Width * (_value / 100.0)));
                using (var fill = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(w, 1), r.Height),
                                                          Ui.Accent, Ui.AccentHi, LinearGradientMode.Horizontal))
                    FillRounded(g, fill, new Rectangle(r.X, r.Y, w, r.Height), 5);
            }
            using (var pen = new Pen(Ui.Border))
                DrawRounded(g, pen, r, 5);
            if (!string.IsNullOrEmpty(_caption))
                TextRenderer.DrawText(g, _caption, Ui.FontBase,
                    new Rectangle(8, r.Y, r.Width - 16, r.Height), Ui.Fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
        {
            using (var path = Round(r, radius)) g.FillPath(b, path);
        }

        private static void DrawRounded(Graphics g, Pen p, Rectangle r, int radius)
        {
            using (var path = Round(r, radius)) g.DrawPath(p, path);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
