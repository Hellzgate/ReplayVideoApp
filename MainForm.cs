using System;
using System.Drawing;
using System.IO;
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
        private TextBox TxtReplay, TxtOut, TxtPics, TxtLog;
        private DropDownBox CmbFps, CmbLayout;
        private CheckBox ChkApi, ChkTranslate, ChkImgApi;
        private Button BtnPickReplay, BtnPickOut, BtnPickPics, BtnRender, BtnCancel, BtnOpenOut, BtnClearLog;
        private ProgressBarEx PrgBar;
        private Label LblStatus, LblTitle;

        private bool _busy;
        private bool _wired;
        private string _lastOut;

        // ── 설정 ──────────────────────────────────────────────
        private sealed class Settings
        {
            public string PicsDir { get; set; }
            public string LastReplay { get; set; }
            public int Fps { get; set; }
            public string Layout { get; set; }
            public bool Api { get; set; }
            public bool Translate { get; set; }
            public bool ImageApiFirst { get; set; }
        }

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));  // 2 입력 3행
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
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));

            TxtReplay = Ui.Input("리플레이 파일 (.yrpX) — 끌어놓기 가능");
            TxtReplay.Dock = DockStyle.Fill;
            TxtReplay.AllowDrop = true;
            BtnPickReplay = Ui.Button("찾아보기…");
            BtnPickReplay.Dock = DockStyle.Fill;
            BtnPickReplay.Click += OnPickReplay;

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

            AddRow(grid, 0, "리플레이", TxtReplay, BtnPickReplay);
            AddRow(grid, 1, "저장 파일", TxtOut, BtnPickOut);
            AddRow(grid, 2, "카드 이미지", TxtPics, BtnPickPics);
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
            BtnOpenOut.Width = 104;
            BtnOpenOut.Height = 32;
            BtnOpenOut.Enabled = false;
            BtnOpenOut.Click += OnOpenOut;
            actions.Controls.Add(BtnRender);
            actions.Controls.Add(BtnCancel);
            actions.Controls.Add(BtnOpenOut);

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
            BtnClearLog = Ui.Button("로그 지우기", false, true);
            BtnClearLog.Dock = DockStyle.Right;
            BtnClearLog.Width = 92;
            BtnClearLog.Click += OnClearLog;
            var logTitle = Ui.Text("로그", Ui.Sub, Ui.FontSection);
            logTitle.Dock = DockStyle.Fill;
            logHead.Controls.Add(logTitle);
            logHead.Controls.Add(BtnClearLog);
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

            LoadSettings();
            Log("렌더를 시작하면 진행 상황이 여기에 표시됩니다.");
            Log("(카드 이미지는 pics 폴더 → 없으면 인터넷에서 받습니다)");

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

        // ── 검증용 ────────────────────────────────────────────
        /// <summary>--screenshot --busy : 렌더 중 상태를 그려본다(실제 렌더는 하지 않는다).</summary>
        public void DemoBusy()
        {
            TxtReplay.Text = @"D:\edopro\replay\2026-11-03 21-52-43.yrpX";
            TxtOut.Text = @"D:\ReplayVideo\out\2026-11-03 21-52-43.mp4";
            TxtPics.Text = @"D:\edopro\pics";
            PrgBar.Value = 42;
            if (PrgBar is ProgressBarEx pex) pex.Caption = "128 / 300";
            Status("프레임 128 / 300 · 체인 3 · 카드 이미지 내려받는 중", false);
            BtnCancel.Enabled = true;
            BtnRender.Enabled = false;
            DemoLog();
        }

        /// <summary>--screenshot --log : 로그창에 예시 로그를 채운다.</summary>
        public void DemoLog()
        {
            Log("=== 렌더 시작 ===");
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
            sb.Append(" fps=" + (CmbFps.Items.Length == 4 ? "OK" : "FAIL"));
            sb.Append(" layout=" + (CmbLayout.Items.Length == 2 ? "OK" : "FAIL"));
            sb.Append(" opts=" + (ChkApi != null && ChkTranslate != null && ChkImgApi != null ? "OK" : "FAIL"));
            sb.Append(" render=" + (BtnRender != null && BtnRender.Enabled ? "OK" : "FAIL"));
            sb.Append(" cancel=" + (BtnCancel != null ? "OK" : "FAIL"));
            sb.Append(" progress=" + (PrgBar != null ? "OK" : "FAIL"));
            sb.Append(" log=" + (TxtLog != null ? "OK" : "FAIL"));
            sb.Append(" wired=" + (_wired ? "OK" : "FAIL"));
            sb.Append(" settings=" + (SaveLoadRoundTrip() ? "OK" : "FAIL"));
            sb.Append(" sections=" + (_sectionCount >= 3 ? "OK" : "FAIL"));
            sb.Append(" clear=" + (BtnClearLog != null ? "OK" : "FAIL"));
            sb.Append(" dpi=" + (AutoScaleMode == AutoScaleMode.Dpi ? "OK" : "FAIL"));
            sb.Append(" root=" + (rootLayoutOk() ? "OK" : "FAIL"));
            return sb.ToString();
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
                    s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? s;
            }
            catch { }

            string pics = s.PicsDir;
            if (string.IsNullOrEmpty(pics))
            {
                if (Directory.Exists(@"D:\edopro\pics")) pics = @"D:\edopro\pics";
                else if (Directory.Exists(@"C:\ProjectIgnis\pics")) pics = @"C:\ProjectIgnis\pics";
            }
            TxtPics.Text = pics ?? "";
            int fpsIdx = Math.Max(0, Array.IndexOf(CmbFps.Items, s.Fps.ToString()));
            CmbFps.SelectedIndex = fpsIdx;
            int layIdx = Math.Max(0, Array.IndexOf(CmbLayout.Items, s.Layout ?? ""));
            CmbLayout.SelectedIndex = layIdx;
            ChkApi.Checked = s.Api;
            ChkTranslate.Checked = s.Translate;
            ChkImgApi.Checked = s.ImageApiFirst;
            if (!string.IsNullOrEmpty(s.LastReplay) && File.Exists(s.LastReplay))
                TxtReplay.Text = s.LastReplay;
        }

        private void SaveSettings()
        {
            try
            {
                var s = new Settings
                {
                    PicsDir = TxtPics.Text.Trim(),
                    LastReplay = TxtReplay.Text.Trim(),
                    Fps = SelectedFps(),
                    Layout = CmbLayout.SelectedText,
                    Api = ChkApi.Checked,
                    Translate = ChkTranslate.Checked,
                    ImageApiFirst = ChkImgApi.Checked
                };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
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
                d.Title = "리플레이 선택 (.yrpX)";
                d.Filter = "리플레이 (*.yrpX;*.yrp)|*.yrpX;*.yrp|모든 파일 (*.*)|*.*";
                if (Directory.Exists(@"D:\edopro\replay")) d.InitialDirectory = @"D:\edopro\replay";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SetReplay(d.FileName);
            }
        }

        /// <summary>기본 저장 경로 — C: 용량 부족 → D:\ReplayVideo\out (없으면 E:/F:, 그다음 원본 폴더).</summary>
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
            TxtReplay.Text = path;
            if (string.IsNullOrWhiteSpace(TxtOut.Text))
                TxtOut.Text = DefaultOutPath(path);
            Log("리플레이: " + path);
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
            SetReplay(files[0]);
        }

        // ── 렌더 ──────────────────────────────────────────────
        private async void OnRender(object sender, EventArgs e)
        {
            if (_busy) return;

            string replay = TxtReplay.Text.Trim();
            string outPath = TxtOut.Text.Trim();
            if (!File.Exists(replay)) { Status("리플레이 파일이 없습니다", true); return; }
            if (outPath.Length == 0)
            {
                outPath = DefaultOutPath(replay);
                TxtOut.Text = outPath;
            }
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))); }
            catch { Status("저장 폴더를 만들 수 없습니다", true); return; }

            string pics = TxtPics.Text.Trim();
            if (pics.Length > 0 && !Directory.Exists(pics))
            {
                Log("⚠ 카드 이미지 폴더가 없습니다 → 무시하고 인터넷 이미지를 씁니다: " + pics);
                pics = "";
            }

            var req = new ReplayVideo.Program.RenderRequest
            {
                ReplayPath = replay,
                OutPath = outPath,
                PicsDir = pics,
                CdbPath = @"D:\edopro\expansions\cards.cdb",
                Fps = SelectedFps(),
                Portrait = (CmbLayout.SelectedIndex == 1),
                Api = ChkApi.Checked,
                Translate = ChkTranslate.Checked,
                ImageApiFirst = ChkImgApi.Checked
            };

            SetBusy(true);
            PrgBar.Value = 0;
            Log("");
            Log("=== 렌더 시작 ===");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await Task.Run(delegate
                {
                    ReplayVideo.Program.Render(req, delegate (int cur, int total, string text)
                    {
                        BeginInvokeSafe(delegate
                        {
                            if (total > 0)
                            {
                                int v = (int)Math.Min(100, (long)cur * 100 / total);
                                if (v >= 0 && v <= 100) PrgBar.Value = v;
                                if (PrgBar is ProgressBarEx pex) pex.Caption = cur + " / " + total;
                            }
                            if (!string.IsNullOrEmpty(text)) LblStatus.Text = text;
                        });
                    });
                });

                sw.Stop();
                _lastOut = outPath;
                BtnOpenOut.Enabled = true;
                var fi = new FileInfo(outPath);
                Status(string.Format("완료 · {0:N1}MB · {1:N0}ms", fi.Exists ? fi.Length / 1048576.0 : 0, sw.ElapsedMilliseconds), false);
                PrgBar.Value = 100;
                Log(string.Format("=== 완료: {0} ({1:N0}ms) ===", outPath, sw.ElapsedMilliseconds));
            }
            catch (OperationCanceledException)
            {
                Status("취소됨", false);
                Log("=== 취소됨 ===");
            }
            catch (Exception ex)
            {
                Status("오류: " + ex.Message, true);
                Log("오류: " + ex);
            }
            finally
            {
                SetBusy(false);
                SaveSettings();
            }
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
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            BtnRender.Enabled = !busy;
            BtnCancel.Enabled = busy;
            BtnPickReplay.Enabled = !busy;
            BtnPickOut.Enabled = !busy;
            BtnPickPics.Enabled = !busy;
            CmbFps.Enabled = !busy;
            CmbLayout.Enabled = !busy;
            ChkApi.Enabled = !busy;
            ChkTranslate.Enabled = !busy;
            ChkImgApi.Enabled = !busy;
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
