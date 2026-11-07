using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayVideoApp
{
    /// <summary>
    /// 리플레이(.yrpX) → 보드 영상(MP4) GUI.
    /// 렌더 엔진은 CLI 프로젝트(ReplayVideo)의 Program.Render 를 그대로 호출한다(동작 동일).
    /// </summary>
    public sealed class MainForm : Form
    {

        // ── 컨트롤 ────────────────────────────────────────────
        private TextBox TxtReplay, TxtOut, TxtPics, TxtCdb, TxtLog;
        private DropDownBox CmbFps, CmbLayout;
        private CheckBox ChkApi, ChkTranslate, ChkImgApi;
        private Button BtnPickReplay, BtnPickOut, BtnPickPics, BtnPickCdb, BtnRepos, BtnRender, BtnCancel, BtnOpenOut, BtnPlay, BtnClearLog, BtnCopyLog, BtnRecent;
        private ProgressBarEx PrgBar;
        private Label LblStatus, LblTitle;

        private bool _busy;
        private bool _wired;
        private string _lastOut;
        private readonly List<string> _queue = new List<string>();      // 일괄 변환 대기
        private readonly List<string> _recent = new List<string>();     // 최근 파일(최대 10)
        private List<CdbRepo> _repos = new List<CdbRepo>();             // 카드 DB 레포(EDOPro 방식)
        private DateTime _runStart;
        private int _batchIndex, _batchTotal;

        /// <summary>검증용(--uicheck/--screenshot)에서 사용자 설정을 덮어쓰지 않게 한다.</summary>
        public bool NoSaveOnClose;

        // ── 설정 ──────────────────────────────────────────────
        private sealed class Settings
        {
            public string PicsDir { get; set; }
            public string CdbDir { get; set; }
            public string OutDir { get; set; }
            public string LastReplay { get; set; }
            public int Fps { get; set; }
            public string Layout { get; set; }
            public bool Api { get; set; }
            public bool Translate { get; set; }
            public bool ImageApiFirst { get; set; }
            public List<string> Recent { get; set; }
            public List<CdbRepo> Repos { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true       // CdbRepo 는 public 필드로 저장한다
        };

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ReplayVideoApp", "settings.json");
            }
        }

        public MainForm()
        {
            Text = "ReplayVideoApp — 리플레이 영상 만들기";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 664);
            MinimumSize = new Size(820, 580);
            BackColor = Ui.Bg;
            ForeColor = Ui.Fg;
            Font = Ui.FontBase;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            AllowDrop = true;

            // 2026-11-07: SetBounds 절대배치 → TableLayoutPanel(리사이즈·DPI 대응) 로 교체.
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Ui.Bg;
            root.Padding = new Padding(14, 10, 14, 12);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowCount = 10;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));   // 0 제목
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 1 입력 헤더
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));  // 2 입력 4행
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 3 옵션 헤더
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 4 옵션
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 5 실행 헤더
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 6 실행(버튼+진행률)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 7 상태
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // 8 로그 헤더
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 9 로그
            Controls.Add(root);

            // ── 제목 ──────────────────────────────────────────
            var head = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Margin = new Padding(0) };
            LblTitle = Ui.Text("리플레이(.yrpX) → 보드 영상(MP4)", Ui.Fg, Ui.FontTitle);
            LblTitle.Dock = DockStyle.Top;
            LblTitle.Height = 30;
            var sub = Ui.Note("리플레이 파일을 창에 끌어놓아도 됩니다 · 결과는 " + ReplayVideo.Program.BaseDir() + "\\out 에 저장됩니다");
            sub.Dock = DockStyle.Top;
            sub.Height = 20;
            head.Controls.Add(sub);
            head.Controls.Add(LblTitle);
            root.Controls.Add(head, 0, 0);

            // ── 입력 ──────────────────────────────────────────
            AddSection(root, 1, "입력");

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Ui.Bg,
                Margin = new Padding(0, 2, 0, 2)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            TxtReplay = Ui.Input("리플레이 파일 (.yrpX) — 끌어놓기 가능");
            TxtReplay.Dock = DockStyle.Fill;
            TxtReplay.AllowDrop = true;
            BtnPickReplay = Ui.Button("찾아보기…");
            BtnPickReplay.Dock = DockStyle.Fill;
            BtnPickReplay.Margin = new Padding(0, 2, 4, 2);
            BtnPickReplay.Click += OnPickReplay;
            BtnRecent = Ui.Button("최근 ▾");
            BtnRecent.Width = 64;
            BtnRecent.Margin = new Padding(0, 2, 0, 2);
            BtnRecent.Click += OnShowRecent;

            var replayBtns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Ui.Bg,
                Margin = new Padding(0)
            };
            replayBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            replayBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            replayBtns.Controls.Add(BtnPickReplay, 0, 0);
            replayBtns.Controls.Add(BtnRecent, 1, 0);

            TxtOut = Ui.Input("저장할 MP4 경로");
            TxtOut.Dock = DockStyle.Fill;
            BtnPickOut = Ui.Button("저장 위치…");
            BtnPickOut.Dock = DockStyle.Fill;
            BtnPickOut.Click += OnPickOut;

            TxtPics = Ui.Input("카드 이미지 폴더 (비우면 인터넷에서 받음)");
            TxtPics.Dock = DockStyle.Fill;
            BtnPickPics = Ui.Button("폴더…");
            BtnPickPics.Dock = DockStyle.Fill;
            BtnPickPics.Click += OnPickPics;

            TxtCdb = Ui.Input("EDOPro 폴더 (카드 이름·효과 텍스트 cdb)");
            TxtCdb.Dock = DockStyle.Fill;
            BtnPickCdb = Ui.Button("폴더…");
            BtnPickCdb.Dock = DockStyle.Fill;
            BtnPickCdb.Margin = new Padding(0, 2, 4, 2);
            BtnPickCdb.Click += OnPickCdb;
            BtnRepos = Ui.Button("레포…");
            BtnRepos.Dock = DockStyle.Fill;
            BtnRepos.Margin = new Padding(0, 2, 0, 2);
            BtnRepos.Click += OnOpenRepos;
            var cdbBtns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Ui.Bg,
                Margin = new Padding(0)
            };
            cdbBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            cdbBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            cdbBtns.Controls.Add(BtnPickCdb, 0, 0);
            cdbBtns.Controls.Add(BtnRepos, 1, 0);

            AddRow(grid, 0, "리플레이", TxtReplay, replayBtns);
            AddRow(grid, 1, "저장 파일", TxtOut, BtnPickOut);
            AddRow(grid, 2, "카드 이미지", TxtPics, BtnPickPics);
            AddRow(grid, 3, "카드DB", TxtCdb, cdbBtns);
            root.Controls.Add(grid, 0, 2);

            // ── 옵션 ──────────────────────────────────────────
            AddSection(root, 3, "옵션");

            var opts = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Ui.Bg,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(0, 2, 0, 0)
            };
            var fpsLabel = Ui.Text("fps", Ui.Sub);
            fpsLabel.Width = 26;
            fpsLabel.Margin = new Padding(0, 8, 4, 4);
            CmbFps = Ui.DropDown("8", "12", "15", "24");
            CmbFps.Width = 78;
            CmbFps.Margin = new Padding(0, 6, 18, 4);

            var layLabel = Ui.Text("화면", Ui.Sub);
            layLabel.Width = 34;
            layLabel.Margin = new Padding(0, 8, 4, 4);
            CmbLayout = Ui.DropDown("가로 (1400x820)", "세로 (1080x1920)");
            CmbLayout.Width = 178;
            CmbLayout.Margin = new Padding(0, 6, 18, 4);

            ChkApi = Ui.Check("카드 정보 API");
            ChkTranslate = Ui.Check("효과 한글 번역");
            ChkImgApi = Ui.Check("API 이미지 우선", false);

            opts.Controls.Add(fpsLabel);
            opts.Controls.Add(CmbFps);
            opts.Controls.Add(layLabel);
            opts.Controls.Add(CmbLayout);
            opts.Controls.Add(ChkApi);
            opts.Controls.Add(ChkTranslate);
            opts.Controls.Add(ChkImgApi);
            root.Controls.Add(opts, 0, 4);

            // ── 실행 ──────────────────────────────────────────
            AddSection(root, 5, "실행");

            var run = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Ui.Bg,
                Margin = new Padding(0, 2, 0, 2)
            };
            run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Ui.Bg,
                Margin = new Padding(0)
            };
            BtnRender = Ui.Button("렌더 시작", true);
            BtnRender.Width = 132;
            BtnRender.Height = 32;
            BtnRender.Click += OnRender;
            BtnCancel = Ui.Button("취소");
            BtnCancel.Width = 84;
            BtnCancel.Height = 32;
            BtnCancel.Enabled = false;
            BtnCancel.Click += OnCancel;
            BtnOpenOut = Ui.Button("폴더 열기");
            BtnOpenOut.Width = 96;
            BtnOpenOut.Height = 32;
            BtnOpenOut.Enabled = false;
            BtnOpenOut.Click += OnOpenOut;
            BtnPlay = Ui.Button("영상 재생");
            BtnPlay.Width = 96;
            BtnPlay.Height = 32;
            BtnPlay.Enabled = false;
            BtnPlay.Click += OnPlay;
            actions.Controls.Add(BtnRender);
            actions.Controls.Add(BtnCancel);
            actions.Controls.Add(BtnOpenOut);
            actions.Controls.Add(BtnPlay);

            PrgBar = new ProgressBarEx();
            PrgBar.Dock = DockStyle.Fill;
            PrgBar.Margin = new Padding(10, 8, 0, 6);
            PrgBar.Height = 24;

            run.Controls.Add(actions, 0, 0);
            run.Controls.Add(PrgBar, 1, 0);
            root.Controls.Add(run, 0, 6);

            LblStatus = Ui.Text("리플레이를 선택하세요 (창에 끌어놓아도 됩니다)", Ui.Sub);
            LblStatus.Dock = DockStyle.Fill;
            LblStatus.Margin = new Padding(0, 2, 0, 2);
            root.Controls.Add(LblStatus, 0, 7);

            // ── 로그 ──────────────────────────────────────────
            var logHead = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Margin = new Padding(0, 4, 0, 0) };
            var logBtns = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Ui.Bg,
                Margin = new Padding(0)
            };
            BtnCopyLog = Ui.Button("복사", false, true);
            BtnCopyLog.Width = 62;
            BtnCopyLog.Click += OnCopyLog;
            BtnClearLog = Ui.Button("지우기", false, true);
            BtnClearLog.Width = 72;
            BtnClearLog.Click += OnClearLog;
            logBtns.Controls.Add(BtnCopyLog);
            logBtns.Controls.Add(BtnClearLog);
            var logTitle = Ui.Text("로그", Ui.Sub, Ui.FontSection);
            logTitle.Dock = DockStyle.Fill;
            logHead.Controls.Add(logTitle);
            logHead.Controls.Add(logBtns);
            root.Controls.Add(logHead, 0, 8);

            TxtLog = new TextBox();
            TxtLog.Multiline = true;
            TxtLog.ReadOnly = true;
            TxtLog.ScrollBars = ScrollBars.Vertical;
            TxtLog.Dock = DockStyle.Fill;
            TxtLog.BackColor = Ui.MonoTrack;
            TxtLog.ForeColor = Color.FromArgb(205, 214, 224);
            TxtLog.BorderStyle = BorderStyle.FixedSingle;
            TxtLog.Font = Ui.FontMono;
            TxtLog.WordWrap = false;
            TxtLog.Margin = new Padding(0);
            root.Controls.Add(TxtLog, 0, 9);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            TxtReplay.DragEnter += OnDragEnter;
            TxtReplay.DragDrop += OnDragDrop;

            KeyPreview = true;                       // F5 렌더 / Esc 취소 / Ctrl+O 열기
            KeyDown += OnKeyDown;
            FormClosing += OnFormClosing;
            AllowDrop = true;

            LoadSettings();
            Log("렌더를 시작하면 진행 상황이 여기에 표시됩니다.");
            Log("(카드 이미지: pics 폴더 → 없으면 인터넷 / 카드 이름: EDOPro cdb)");
            Log("단축키: F5 렌더 · Esc 취소 · Ctrl+O 리플레이 열기 · Ctrl+E 결과 폴더");
            CheckFfmpeg();

            // 이후 CLI 로그는 UI 로그창으로
            Console.SetOut(new TextBoxWriter(this));
            _wired = true;
        }

        // ── 어두운 제목 표시줄 (Win10 1809+/Win11) ────────────
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));   // 구 빌드
            }
            catch { }
        }

        // ── 레이아웃 보조 ─────────────────────────────────────
        private int _sectionCount;

        private void AddSection(TableLayoutPanel root, int row, string title)
        {
            _sectionCount++;
            root.Controls.Add(Ui.SectionHeader(title), 0, row);
        }

        private static void AddRow(TableLayoutPanel grid, int row, string label, Control input, Control button)
        {
            var l = Ui.Text(label, Ui.Sub);
            l.Dock = DockStyle.Fill;
            l.Margin = new Padding(0, 4, 6, 4);
            input.Dock = DockStyle.Fill;
            button.Dock = DockStyle.Fill;
            grid.Controls.Add(l, 0, row);
            grid.Controls.Add(input, 1, row);
            grid.Controls.Add(button, 2, row);
        }

        private void OnClearLog(object sender, EventArgs e)
        {
            if (TxtLog != null && !TxtLog.IsDisposed) TxtLog.Clear();
        }

        private void OnCopyLog(object sender, EventArgs e)
        {
            try
            {
                if (TxtLog != null && TxtLog.TextLength > 0) Clipboard.SetText(TxtLog.Text);
                Status("로그를 클립보드에 복사했습니다", false);
            }
            catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.F5) { e.Handled = true; if (!_busy) OnRender(this, EventArgs.Empty); return; }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; if (_busy) OnCancel(this, EventArgs.Empty); return; }
                if (e.Control && e.KeyCode == Keys.O) { e.Handled = true; if (!_busy) OnPickReplay(this, EventArgs.Empty); return; }
                if (e.Control && e.KeyCode == Keys.E) { e.Handled = true; OnOpenOut(this, EventArgs.Empty); return; }
                if (e.Control && e.KeyCode == Keys.L) { e.Handled = true; OnClearLog(this, EventArgs.Empty); return; }
            }
            catch { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                var r = MessageBox.Show(this, "렌더가 진행 중입니다.\n취소하고 종료할까요?", "ReplayVideoApp",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                try { ReplayVideo.Program.CancelRequested = true; } catch { }
            }
            if (!NoSaveOnClose) SaveSettings();
        }

        /// <summary>ffmpeg 가 있는지 미리 알려준다(없으면 MP4 대신 PNG 만 생성된다).</summary>
        private void CheckFfmpeg()
        {
            try
            {
                string ff = FindFfmpegForCheck();
                Log(ff != null ? "ffmpeg: " + ff : "⚠ ffmpeg 없음 — MP4 대신 PNG 시퀀스만 만들어집니다(PATH 또는 chocolatey 설치)");
            }
            catch { }
        }

        private static string FindFfmpegForCheck()
        {
            string[] cands =
            {
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
            };
            foreach (string p in cands) { try { if (File.Exists(p)) return p; } catch { } }
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try { if (File.Exists(Path.Combine(dir.Trim(), "ffmpeg.exe"))) return Path.Combine(dir.Trim(), "ffmpeg.exe"); }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        // ── 최근 파일 ─────────────────────────────────────────
        private void PushRecent(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                _recent.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
                _recent.Insert(0, path);
                while (_recent.Count > 10) _recent.RemoveAt(_recent.Count - 1);
            }
            catch { }
        }

        private void OnShowRecent(object sender, EventArgs e)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Ui.Panel;
            menu.ForeColor = Ui.Fg;
            menu.ShowImageMargin = false;
            if (_recent.Count == 0)
            {
                var none = new ToolStripMenuItem("(최근 파일 없음)") { Enabled = false };
                menu.Items.Add(none);
            }
            else
            {
                foreach (string p in _recent)
                {
                    string path = p;
                    var it = new ToolStripMenuItem(Path.GetFileName(path)) { ToolTipText = path };
                    it.Click += delegate { SetReplay(path); };
                    menu.Items.Add(it);
                }
                menu.Items.Add(new ToolStripSeparator());
                var clr = new ToolStripMenuItem("목록 지우기");
                clr.Click += delegate { _recent.Clear(); SaveSettings(); };
                menu.Items.Add(clr);
            }
            try { menu.Show(BtnRecent, new Point(0, BtnRecent.Height)); } catch { }
        }

        // ── 검증용 ────────────────────────────────────────────
        /// <summary>--screenshot --busy : 렌더 중 상태를 그려본다(실제 렌더는 하지 않는다).</summary>
        public void DemoBusy()
        {
            TxtReplay.Text = @"D:\edopro\replay\2026-11-03 21-52-43.yrpX";
            TxtOut.Text = @"D:\ReplayVideo\out\2026-11-03 21-52-43.mp4";
            TxtPics.Text = @"D:\edopro\pics";
            TxtCdb.Text = @"D:\edopro";
            _batchTotal = 3;
            _batchIndex = 0;
            _runStart = DateTime.UtcNow.AddSeconds(-38);
            PrgBar.Value = 42;
            if (PrgBar is ProgressBarEx pex) pex.Caption = "[1/3] 128 / 300 (42%) · 3.4fps · 남은 0:50";
            Status("[1/3] 프레임 128 / 300 · 체인 3 · 카드 이미지 내려받는 중", false);
            BtnCancel.Enabled = true;
            BtnRender.Enabled = false;
            DemoLog();
        }

        /// <summary>--screenshot --log : 로그창에 예시 로그를 채운다.</summary>
        public void DemoLog()
        {
            Log("ffmpeg: C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe");
            Log("");
            Log("=== 렌더 시작 === 파일 3개 · fps 12 · 가로 (1400x820) · 카드DB D:\\edopro");
            Log("=== [1/3] 2026-11-03 21-52-43.yrpX ===");
            Log("리플레이: D:\\edopro\\replay\\2026-11-03 21-52-43.yrpX  (5,550 패킷 / 7턴)");
            Log("카드 정보: cdb 171개 · API 12건 · 번역 캐시 48건");
            Log("[120/300] 체인 3 · 108,000 프레임 중 43,200 (40%)");
            Log("경고: 로컬 pics 에 없는 카드 6장 → 인터넷 이미지 사용");
        }

        public int ControlCount() { return Controls.Count; }

        public string CheckControls()
        {
            var sb = new StringBuilder();
            sb.Append("replay=" + (TxtReplay != null ? "OK" : "FAIL"));
            sb.Append(" out=" + (TxtOut != null ? "OK" : "FAIL"));
            sb.Append(" pics=" + (TxtPics != null ? "OK" : "FAIL"));
            sb.Append(" cdb=" + (TxtCdb != null && BtnPickCdb != null ? "OK" : "FAIL"));
            sb.Append(" repos=" + (BtnRepos != null && _repos.Count > 0 ? "OK" : "FAIL"));
            sb.Append(" repocache=" + (CdbRepoStore.CacheRoot.IndexOf("ReplayVideo", StringComparison.OrdinalIgnoreCase) >= 0 ? "OK" : "FAIL"));
            sb.Append(" repoload=" + (CdbRepoStore.FromEdoPro(GuessEdoProDir()).Count > 0 ? "OK" : "skip"));
            sb.Append(" recent=" + (BtnRecent != null ? "OK" : "FAIL"));
            sb.Append(" fps=" + (CmbFps.Items.Length == 4 ? "OK" : "FAIL"));
            sb.Append(" layout=" + (CmbLayout.Items.Length == 2 ? "OK" : "FAIL"));
            sb.Append(" opts=" + (ChkApi != null && ChkTranslate != null && ChkImgApi != null ? "OK" : "FAIL"));
            sb.Append(" render=" + (BtnRender != null && BtnRender.Enabled ? "OK" : "FAIL"));
            sb.Append(" cancel=" + (BtnCancel != null ? "OK" : "FAIL"));
            sb.Append(" play=" + (BtnPlay != null && !BtnPlay.Enabled ? "OK" : "FAIL"));
            sb.Append(" progress=" + (PrgBar != null ? "OK" : "FAIL"));
            sb.Append(" log=" + (TxtLog != null ? "OK" : "FAIL"));
            sb.Append(" copylog=" + (BtnCopyLog != null ? "OK" : "FAIL"));
            sb.Append(" wired=" + (_wired ? "OK" : "FAIL"));
            sb.Append(" settings=" + (SaveLoadRoundTrip() ? "OK" : "FAIL"));
            sb.Append(" batch=" + (BatchCheck() ? "OK" : "FAIL"));
            sb.Append(" ffmpeg=" + (FindFfmpegForCheck() != null ? "yes" : "no"));
            sb.Append(" sections=" + (_sectionCount >= 3 ? "OK" : "FAIL"));
            sb.Append(" clear=" + (BtnClearLog != null ? "OK" : "FAIL"));
            sb.Append(" dpi=" + (AutoScaleMode == AutoScaleMode.Dpi ? "OK" : "FAIL"));
            sb.Append(" root=" + (rootLayoutOk() ? "OK" : "FAIL"));
            return sb.ToString();
        }

        /// <summary>일괄 대기열·최근 목록이 동작하는지(파일 없이 로직만).</summary>
        private bool BatchCheck()
        {
            try
            {
                _queue.Clear();
                _queue.Add("a.yrpX");
                _queue.Add("b.yrpX");
                bool ok = _queue.Count == 2;
                _queue.Clear();
                _recent.Clear();
                PushRecent("x.yrpX");
                PushRecent("y.yrpX");
                PushRecent("x.yrpX");
                ok = ok && _recent.Count == 2 && _recent[0] == "x.yrpX";
                _recent.Clear();
                return ok;
            }
            catch { return false; }
        }

        /// <summary>레이아웃이 TableLayoutPanel 기반인지(절대배치 회귀 방지).</summary>
        private bool rootLayoutOk()
        {
            return Controls.Count > 0 && Controls[0] is TableLayoutPanel;
        }

        private bool SaveLoadRoundTrip()
        {
            try
            {
                var s = new Settings { PicsDir = "x", Fps = 12, Layout = "portrait", Api = true, Translate = false, ImageApiFirst = true };
                string json = JsonSerializer.Serialize(s);
                var t = JsonSerializer.Deserialize<Settings>(json);
                return t != null && t.Fps == 12 && t.Layout == "portrait" && !t.Translate;
            }
            catch { return false; }
        }

        // ── 설정 ──────────────────────────────────────────────
        private void LoadSettings()
        {
            var s = new Settings { Fps = 12, Layout = "가로 (1400x820)", Api = true, Translate = true };
            try
            {
                if (File.Exists(SettingsPath))
                    s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), JsonOpts) ?? s;
            }
            catch { }

            // 카드 이미지 폴더
            string pics = s.PicsDir;
            if (string.IsNullOrEmpty(pics) || !Directory.Exists(pics))
            {
                pics = "";
                string[] cand = { @"D:\edopro\pics", @"C:\ProjectIgnis\pics", @"C:\EDOPro\pics" };
                foreach (string p in cand) { try { if (Directory.Exists(p)) { pics = p; break; } } catch { } }
            }
            TxtPics.Text = pics ?? "";

            // EDOPro(cdb) 폴더 — 하드코딩 대신 자동 탐색
            string cdb = s.CdbDir;
            if (string.IsNullOrEmpty(cdb) || !Directory.Exists(cdb)) cdb = GuessEdoProDir();
            TxtCdb.Text = cdb ?? "";

            // 카드 DB 레포 — 설정에 있으면 그대로, 없으면 EDOPro 설정에서, 그것도 없으면 기본 목록
            _repos.Clear();
            if (s.Repos != null && s.Repos.Count > 0)
                _repos.AddRange(s.Repos.Select(r => r.Clone()));
            else
            {
                _repos.AddRange(CdbRepoStore.FromEdoPro(cdb));
                if (_repos.Count == 0) _repos.AddRange(CdbRepoStore.Defaults());
            }

            int fpsIdx = Math.Max(0, Array.IndexOf(CmbFps.Items, s.Fps.ToString()));
            CmbFps.SelectedIndex = fpsIdx;
            int layIdx = Math.Max(0, Array.IndexOf(CmbLayout.Items, s.Layout ?? ""));
            CmbLayout.SelectedIndex = layIdx;
            ChkApi.Checked = s.Api;
            ChkTranslate.Checked = s.Translate;
            ChkImgApi.Checked = s.ImageApiFirst;

            _recent.Clear();
            if (s.Recent != null)
                foreach (string r in s.Recent)
                    if (!string.IsNullOrEmpty(r) && File.Exists(r) && _recent.Count < 10) _recent.Add(r);

            if (!string.IsNullOrEmpty(s.LastReplay) && File.Exists(s.LastReplay))
            {
                TxtReplay.Text = s.LastReplay;
                if (string.IsNullOrWhiteSpace(TxtOut.Text)) TxtOut.Text = DefaultOutPath(s.LastReplay);
            }
            else if (_recent.Count > 0)
            {
                TxtReplay.Text = _recent[0];
                if (string.IsNullOrWhiteSpace(TxtOut.Text)) TxtOut.Text = DefaultOutPath(_recent[0]);
            }
        }

        /// <summary>EDOPro 폴더 자동 탐색 — cdb 가 있는 곳.</summary>
        internal static string GuessEdoProDir()
        {
            string[] cand = { @"D:\edopro", @"C:\ProjectIgnis", @"C:\EDOPro" };
            foreach (string dir in cand)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    if (File.Exists(Path.Combine(dir, "expansions", "cards.cdb"))) return dir;
                    if (Directory.GetFiles(dir, "*.cdb", SearchOption.AllDirectories).Length > 0) return dir;
                }
                catch { }
            }
            return "";
        }

        private void SaveSettings()
        {
            try
            {
                var s = new Settings
                {
                    PicsDir = TxtPics.Text.Trim(),
                    CdbDir = TxtCdb.Text.Trim(),
                    OutDir = SafeDirOf(TxtOut.Text),
                    LastReplay = TxtReplay.Text.Trim(),
                    Fps = SelectedFps(),
                    Layout = CmbLayout.SelectedText,
                    Api = ChkApi.Checked,
                    Translate = ChkTranslate.Checked,
                    ImageApiFirst = ChkImgApi.Checked,
                    Recent = new List<string>(_recent),
                    Repos = _repos.Select(r => r.Clone()).ToList()
                };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts), Encoding.UTF8);
            }
            catch { }
        }

        private static string SafeDirOf(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "";
                string d = Path.GetDirectoryName(path);
                return d ?? "";
            }
            catch { return ""; }
        }

        private int SelectedFps()
        {
            int v = 12;
            try { v = int.Parse(CmbFps.SelectedText); } catch { }
            return v;
        }

        // ── 파일 선택 ─────────────────────────────────────────
        private void OnPickReplay(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "리플레이 선택 (.yrpX) — 여러 개 선택하면 순서대로 렌더합니다";
                d.Filter = "리플레이 (*.yrpX;*.yrp)|*.yrpX;*.yrp|모든 파일 (*.*)|*.*";
                d.Multiselect = true;
                string last = SafeDirOf(TxtReplay.Text.Trim());
                if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) d.InitialDirectory = last;
                else if (Directory.Exists(@"D:\edopro\replay")) d.InitialDirectory = @"D:\edopro\replay";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                AddFiles(d.FileNames);
            }
        }

        /// <summary>선택한 파일들 — 첫 번째를 현재 파일로, 나머지는 일괄 대기열로.</summary>
        private void AddFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;
            _queue.Clear();
            for (int i = 1; i < files.Length; i++)
                if (File.Exists(files[i])) _queue.Add(files[i]);
            SetReplay(files[0]);
            if (_queue.Count > 0)
            {
                Log(string.Format("일괄 변환 대기 {0}개 — [렌더 시작] 을 누르면 순서대로 처리합니다", _queue.Count));
                Status(string.Format("대기 {0}개 · 현재 {1}", _queue.Count, Path.GetFileName(files[0])), false);
            }
        }

        private void OnPickCdb(object sender, EventArgs e)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "EDOPro 폴더 (expansions\\cards.cdb 등 cdb 가 있는 곳)";
                if (Directory.Exists(TxtCdb.Text)) d.SelectedPath = TxtCdb.Text;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                TxtCdb.Text = d.SelectedPath;
                if (string.IsNullOrWhiteSpace(TxtPics.Text))
                {
                    string pics = Path.Combine(d.SelectedPath, "pics");
                    if (Directory.Exists(pics)) TxtPics.Text = pics;
                }
                Log("카드DB 폴더: " + d.SelectedPath);
                SaveSettings();
            }
        }

        /// <summary>카드 DB 레포 관리(EDOPro 방식: 레포 목록 + 받아서 쓰기).</summary>
        private void OnOpenRepos(object sender, EventArgs e)
        {
            string edo = TxtCdb.Text.Trim();
            using (var f = new RepoForm(edo, _repos))
            {
                f.ShowDialog(this);
                _repos = f.Repos.Select(r => r.Clone()).ToList();
            }
            SaveSettings();
            int cdb = CdbRepoStore.TotalCdb(_repos);
            int use = _repos.Count(r => r.Enabled);
            Log(string.Format("카드 DB 레포: 사용 {0}개 · 받은 cdb {1}개 (캐시 {2})", use, cdb, CdbRepoStore.CacheRoot));
            if (use > 0 && cdb == 0) Log("⚠ 아직 받은 레포가 없습니다 → [레포…] 에서 [지금 갱신] 을 누르세요");
        }

        /// <summary>기본 저장 경로 — 이전에 쓴 폴더 → D:\ReplayVideo\out → 원본 폴더.</summary>
        private static string DefaultOutPath(string replayPath)
        {
            try
            {
                string dir = Path.Combine(ReplayVideo.Program.BaseDir(), "out");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, Path.GetFileNameWithoutExtension(replayPath) + ".mp4");
            }
            catch
            {
                return Path.ChangeExtension(replayPath, ".mp4");
            }
        }

        private void SetReplay(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Trim();
            TxtReplay.Text = path;
            // 저장 경로는 "이전 저장 폴더"가 있으면 그 폴더를 쓴다
            string outDir = SafeDirOf(TxtOut.Text);
            if (!string.IsNullOrEmpty(outDir) && Directory.Exists(outDir))
                TxtOut.Text = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".mp4");
            else if (string.IsNullOrWhiteSpace(TxtOut.Text))
                TxtOut.Text = DefaultOutPath(path);

            PushRecent(path);
            SaveSettings();
            string size = "";
            try { size = string.Format("  ({0:N0} KB)", new FileInfo(path).Length / 1024.0); } catch { }
            Log("리플레이: " + path + size);
        }

        private void OnPickOut(object sender, EventArgs e)
        {
            using (var d = new SaveFileDialog())
            {
                d.Title = "저장할 MP4";
                d.Filter = "MP4 (*.mp4)|*.mp4";
                d.FileName = Path.GetFileName(TxtOut.Text.Length > 0 ? TxtOut.Text : "replay.mp4");
                string dir = Path.GetDirectoryName(TxtOut.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                TxtOut.Text = d.FileName;
            }
        }

        private void OnPickPics(object sender, EventArgs e)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "카드 이미지 폴더 (pics) — 없으면 비워두세요(인터넷에서 받습니다)";
                if (Directory.Exists(TxtPics.Text)) d.SelectedPath = TxtPics.Text;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                TxtPics.Text = d.SelectedPath;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            AddFiles(files);
        }

        // ── 렌더 ──────────────────────────────────────────────
        private async void OnRender(object sender, EventArgs e)
        {
            if (_busy) return;

            string first = TxtReplay.Text.Trim();
            if (!File.Exists(first)) { Status("리플레이 파일이 없습니다", true); return; }

            var files = new List<string> { first };
            foreach (string q in _queue)
                if (File.Exists(q) && !files.Contains(q)) files.Add(q);

            string outPath0 = TxtOut.Text.Trim();
            if (outPath0.Length == 0) { outPath0 = DefaultOutPath(first); TxtOut.Text = outPath0; }

            string pics = TxtPics.Text.Trim();
            if (pics.Length > 0 && !Directory.Exists(pics))
            {
                Log("⚠ 카드 이미지 폴더가 없습니다 → 무시하고 인터넷 이미지를 씁니다: " + pics);
                pics = "";
            }
            string cdbDir = TxtCdb.Text.Trim();
            if (cdbDir.Length > 0 && !Directory.Exists(cdbDir))
            {
                Log("⚠ 카드DB 폴더가 없습니다 → 무시합니다: " + cdbDir);
                cdbDir = "";
            }
            string cdbPath = cdbDir.Length > 0
                ? Path.Combine(cdbDir, "expansions", "cards.cdb")
                : @"D:\edopro\expansions\cards.cdb";
            var cdbDirs = CdbRepoStore.ResolveDirs(_repos, cdbDir);

            SetBusy(true);
            _batchTotal = files.Count;
            int ok = 0, failed = 0;
            bool canceled = false;
            var swAll = System.Diagnostics.Stopwatch.StartNew();

            Log("");
            Log(string.Format("=== 렌더 시작 === 파일 {0}개 · fps {1} · {2} · 카드DB {3} · 레포 cdb {4}개",
                files.Count, SelectedFps(), (CmbLayout.SelectedIndex == 1 ? "세로 (1080x1920)" : "가로 (1400x820)"),
                cdbDir.Length > 0 ? cdbDir : "(없음)", CdbRepoStore.TotalCdb(_repos)));

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    _batchIndex = i;
                    string replay = files[i];
                    string outPath = (i == 0 && outPath0.Length > 0) ? outPath0 : DefaultOutPath(replay);
                    try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))); }
                    catch { Status("저장 폴더를 만들 수 없습니다: " + outPath, true); failed++; continue; }

                    Log("");
                    Log(string.Format("=== [{0}/{1}] {2} ===", i + 1, files.Count, Path.GetFileName(replay)));
                    PrgBar.Value = 0;
                    if (PrgBar is ProgressBarEx p0) p0.Caption = "";
                    Status(string.Format("[{0}/{1}] 준비 중…", i + 1, files.Count), false);

                    var req = new ReplayVideo.Program.RenderRequest
                    {
                        ReplayPath = replay,
                        OutPath = outPath,
                        PicsDir = pics,
                        CdbPath = cdbPath,
                        CdbDirs = cdbDirs,
                        Fps = SelectedFps(),
                        Portrait = (CmbLayout.SelectedIndex == 1),
                        Api = ChkApi.Checked,
                        Translate = ChkTranslate.Checked,
                        ImageApiFirst = ChkImgApi.Checked
                    };

                    _runStart = DateTime.UtcNow;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await Task.Run(delegate
                        {
                            ReplayVideo.Program.Render(req, delegate (int cur, int total, string text)
                            {
                                BeginInvokeSafe(delegate { UpdateProgress(cur, total, text); });
                            });
                        });
                        sw.Stop();
                        var fi = new FileInfo(outPath);
                        if (fi.Exists && fi.Length > 1024)
                        {
                            ok++;
                            _lastOut = outPath;
                            BtnOpenOut.Enabled = true;
                            BtnPlay.Enabled = true;
                            PrgBar.Value = 100;
                            Status(string.Format("완료 [{0}/{1}] · {2:N1}MB · {3}",
                                i + 1, files.Count, fi.Length / 1048576.0, Mmss(sw.Elapsed.TotalSeconds)), false);
                            Log(string.Format("=== 완료: {0}  ({1:N1}MB · {2}) ===", outPath, fi.Length / 1048576.0, Mmss(sw.Elapsed.TotalSeconds)));
                        }
                        else
                        {
                            failed++;
                            Status("MP4 가 만들어지지 않았습니다 — ffmpeg 설치를 확인하세요", true);
                            Log("⚠ 결과 파일이 없습니다 → ffmpeg 미설치이거나 인코딩 실패(PNG 프레임만 생성됨).");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        canceled = true;
                        Status("취소됨", false);
                        Log("=== 취소됨 ===");
                        break;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Status("오류: " + ex.Message, true);
                        Log("오류: " + ex);
                    }
                }
            }
            finally
            {
                ReplayVideo.Program.CancelRequested = false;
                swAll.Stop();
                if (files.Count > 1 || failed > 0)
                {
                    Log("");
                    Log(string.Format("=== 전체 결과 === 성공 {0} · 실패 {1}{2} · 총 {3}",
                        ok, failed, canceled ? " · 취소" : "", Mmss(swAll.Elapsed.TotalSeconds)));
                }
                SetBusy(false);
                SaveSettings();
            }
        }

        /// <summary>진행률 갱신 — 퍼센트 · 프레임 · 속도 · 남은 시간.</summary>
        private void UpdateProgress(int cur, int total, string text)
        {
            if (total > 0)
            {
                int v = (int)Math.Min(100, (long)cur * 100 / total);
                PrgBar.Value = v;
                double sec = (DateTime.UtcNow - _runStart).TotalSeconds;
                string extra = "";
                if (cur > 0 && cur < total && sec > 0.5)
                {
                    double speed = cur / sec;
                    if (speed > 0.01) extra = string.Format(" · {0:F1}fps · 남은 {1}", speed, Mmss((total - cur) / speed));
                }
                string batch = _batchTotal > 1 ? string.Format("[{0}/{1}] ", _batchIndex + 1, _batchTotal) : "";
                if (PrgBar is ProgressBarEx pex)
                    pex.Caption = string.Format("{0}{1:N0}/{2:N0} ({3}%){4}", batch, cur, total, v, extra);
            }
            if (!string.IsNullOrEmpty(text))
                LblStatus.Text = (_batchTotal > 1 ? string.Format("[{0}/{1}] ", _batchIndex + 1, _batchTotal) : "") + text;
        }

        private static string Mmss(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            int s = (int)Math.Round(sec);
            return string.Format("{0}:{1:00}", s / 60, s % 60);
        }

        private void OnCancel(object sender, EventArgs e)
        {
            ReplayVideo.Program.CancelRequested = true;
            Status("취소 요청…", false);
        }

        private void OnOpenOut(object sender, EventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(_lastOut ?? "");
                if (string.IsNullOrEmpty(dir)) dir = SafeDirOf(TxtOut.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                else Status("폴더가 없습니다", true);
            }
            catch { }
        }

        /// <summary>완성된 MP4 를 기본 플레이어로 연다.</summary>
        private void OnPlay(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastOut) && File.Exists(_lastOut))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _lastOut,
                        UseShellExecute = true
                    });
                else Status("재생할 결과 파일이 없습니다", true);
            }
            catch (Exception ex) { Status("재생 실패: " + ex.Message, true); }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            BtnRender.Enabled = !busy;
            BtnCancel.Enabled = busy;
            BtnPickReplay.Enabled = !busy;
            BtnPickOut.Enabled = !busy;
            BtnPickPics.Enabled = !busy;
            BtnPickCdb.Enabled = !busy;
            BtnRecent.Enabled = !busy;
            CmbFps.Enabled = !busy;
            CmbLayout.Enabled = !busy;
            ChkApi.Enabled = !busy;
            ChkTranslate.Enabled = !busy;
            ChkImgApi.Enabled = !busy;
            TxtCdb.BackColor = busy ? Ui.Panel : Ui.Box;
        }

        private void Status(string text, bool warn)
        {
            LblStatus.Text = text;
            LblStatus.ForeColor = warn ? Ui.Err : Ui.Sub;
        }

        public void AppendLog(string line)
        {
            // Console 리다이렉트로 들어오는 CLI 로그
            try { Log(line); } catch { }
        }

        private void Log(string line)
        {
            if (TxtLog == null || TxtLog.IsDisposed) return;
            if (TxtLog.InvokeRequired)
            {
                BeginInvokeSafe(delegate { Log(line); });
                return;
            }
            TxtLog.AppendText(line + Environment.NewLine);
            // 너무 길어지면 앞부분을 잘라 UI 가 느려지지 않게 한다
            try
            {
                if (TxtLog.TextLength > 400000)
                {
                    string t = TxtLog.Text;
                    TxtLog.Text = "…(앞부분 생략)…" + Environment.NewLine + t.Substring(Math.Min(t.Length, 120000));
                }
                TxtLog.SelectionStart = TxtLog.TextLength;
                TxtLog.ScrollToCaret();
            }
            catch { }
        }

        private void BeginInvokeSafe(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(a);
            }
            catch { }
        }

        /// <summary>Console.WriteLine 을 UI 로그창으로 보낸다.</summary>
        private sealed class TextBoxWriter : TextWriter
        {
            private readonly MainForm _f;
            public TextBoxWriter(MainForm f) { _f = f; }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override void Write(string value) { if (!string.IsNullOrEmpty(value)) _f.AppendLog(value); }
            public override void WriteLine(string value) { _f.AppendLog(value); }
        }
    }
}
