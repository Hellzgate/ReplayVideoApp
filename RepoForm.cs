// 카드 DB 레포 관리 창 — EDOPro 설정에서 가져오기 / 추가 / 삭제 / 지금 갱신.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayVideoApp
{
    public sealed class RepoForm : Form
    {
        private readonly string _edoDir;
        private readonly List<CdbRepo> _repos;
        private ListView _lv;
        private TextBox _txtUrl, _txtLog;
        private Button _btnImport, _btnAdd, _btnDel, _btnSync, _btnClose;
        private bool _busy;

        public List<CdbRepo> Repos { get { return _repos; } }

        public RepoForm(string edoDir, IEnumerable<CdbRepo> repos)
        {
            _edoDir = edoDir ?? "";
            _repos = (repos ?? new List<CdbRepo>()).Select(r => r.Clone()).ToList();

            Text = "카드 DB 레포 — EDOPro 처럼 받아서 쓰기";
            ClientSize = new Size(920, 640);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Fg;
            Font = Ui.FontBase;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Ui.Bg, Padding = new Padding(12, 8, 12, 10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            Controls.Add(root);

            var head = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            head.Controls.Add(new Label
            {
                Text = "카드 DB 레포",
                Dock = DockStyle.Top,
                Height = 24,
                Font = Ui.FontTitle,
                ForeColor = Ui.Fg
            });
            head.Controls.Add(new Label
            {
                Text = "EDOPro 의 configs.json · user_configs.json 과 같은 repos[] 목록입니다 · 받은 파일은 " + CdbRepoStore.CacheRoot + " 에 저장됩니다",
                Dock = DockStyle.Bottom,
                Height = 26,
                Font = new Font("Malgun Gothic", 8.25f),
                ForeColor = Ui.Hint
            });
            root.Controls.Add(head, 0, 0);

            var addRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Ui.Bg };
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            _txtUrl = Ui.Input("레포 주소 (https://github.com/owner/repo)");
            _txtUrl.Dock = DockStyle.Fill;
            _btnAdd = Ui.Button("추가");
            _btnAdd.Dock = DockStyle.Fill;
            _btnAdd.Click += OnAdd;
            _btnImport = Ui.Button("EDOPro 설정에서 가져오기");
            _btnImport.Dock = DockStyle.Fill;
            _btnImport.Click += OnImport;
            addRow.Controls.Add(_txtUrl, 0, 0);
            addRow.Controls.Add(_btnAdd, 1, 0);
            addRow.Controls.Add(_btnImport, 2, 0);
            root.Controls.Add(addRow, 0, 1);

            _lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                BackColor = Ui.Panel,
                ForeColor = Ui.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Ui.FontBase
            };
            _lv.Columns.Add("사용", 48);
            _lv.Columns.Add("이름", 170);
            _lv.Columns.Add("레포", 330);
            _lv.Columns.Add("갱신", 56);
            _lv.Columns.Add("cdb", 60);
            _lv.Columns.Add("상태", 220);
            _lv.ItemChecked += OnItemChecked;
            root.Controls.Add(_lv, 0, 2);

            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Ui.Bg, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            _btnDel = Ui.Button("선택 삭제");
            _btnDel.Click += OnDelete;
            _btnSync = Ui.Button("지금 갱신", true);
            _btnSync.Width = 120;
            _btnSync.Click += OnSync;
            _btnClose = Ui.Button("닫기");
            _btnClose.Click += delegate { Close(); };
            btns.Controls.Add(_btnSync);
            btns.Controls.Add(_btnDel);
            btns.Controls.Add(_btnClose);
            root.Controls.Add(btns, 0, 3);

            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Ui.MonoTrack,
                ForeColor = Color.FromArgb(205, 214, 224),
                Font = Ui.FontMono,
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false
            };
            root.Controls.Add(_txtLog, 0, 4);

            RefreshList();
            Log("EDOPro 카드DB 폴더: " + (_edoDir.Length > 0 ? _edoDir : "(없음 — 레포를 받아 쓰세요)"));
            Log("캐시: " + CdbRepoStore.CacheRoot);
            Log("사용 체크 = 카드 정보에 포함 · 갱신 체크 = [지금 갱신] 대상");
        }

        private void Log(string line)
        {
            if (_txtLog.IsDisposed) return;
            if (_txtLog.InvokeRequired) { try { BeginInvoke((Action)(() => Log(line))); } catch { } return; }
            _txtLog.AppendText(line + Environment.NewLine);
            try { _txtLog.SelectionStart = _txtLog.TextLength; _txtLog.ScrollToCaret(); } catch { }
        }

        private void RefreshList()
        {
            _lv.BeginUpdate();
            _lv.Items.Clear();
            foreach (var r in _repos)
            {
                var it = new ListViewItem("")
                {
                    Checked = r.Enabled,
                    Tag = r
                };
                it.SubItems.Add(r.Name);
                it.SubItems.Add(r.Url);
                it.SubItems.Add(r.Update ? "O" : "-");
                it.SubItems.Add(CdbRepoStore.CountCdb(CdbRepoStore.LocalDirOf(r)).ToString());
                it.SubItems.Add(r.Status);
                _lv.Items.Add(it);
            }
            _lv.EndUpdate();
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            var r = e.Item.Tag as CdbRepo;
            if (r == null) return;
            r.Enabled = e.Item.Checked;
            e.Item.SubItems[0].Text = "";
        }

        private void OnAdd(object sender, EventArgs e)
        {
            string url = (_txtUrl.Text ?? "").Trim();
            if (url.Length == 0) { Log("⚠ 레포 주소를 입력하세요"); return; }
            if (!url.StartsWith("http")) url = "https://github.com/" + url.Trim('/');
            if (_repos.Any(r => string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                Log("⚠ 이미 있는 레포: " + url);
                return;
            }
            var r = new CdbRepo { Url = url };
            r.Name = r.Slug;
            _repos.Add(r);
            _txtUrl.Text = "";
            RefreshList();
            Log("+ 추가: " + url);
        }

        private void OnImport(object sender, EventArgs e)
        {
            var list = CdbRepoStore.FromEdoPro(_edoDir);
            if (list.Count == 0)
            {
                list = CdbRepoStore.Defaults();
                Log("EDOPro 설정을 찾지 못했습니다 → 기본 목록(이 PC 기준)을 넣습니다");
            }
            int added = 0;
            foreach (var r in list)
            {
                if (_repos.Any(x => string.Equals(x.Url, r.Url, StringComparison.OrdinalIgnoreCase))) continue;
                _repos.Add(r);
                added++;
            }
            RefreshList();
            Log(string.Format("EDOPro 설정에서 {0}개 가져옴(새로 {1}개)", list.Count, added));
        }

        private void OnDelete(object sender, EventArgs e)
        {
            if (_lv.SelectedItems.Count == 0) { Log("⚠ 삭제할 레포를 선택하세요"); return; }
            var r = _lv.SelectedItems[0].Tag as CdbRepo;
            if (r == null) return;
            _repos.Remove(r);
            RefreshList();
            Log("- 삭제: " + r.Url + " (내려받은 파일은 남아 있습니다)");
        }

        private void OnSync(object sender, EventArgs e)
        {
            if (_busy) return;
            var targets = _repos.Where(r => r.Update || CdbRepoStore.CountCdb(CdbRepoStore.LocalDirOf(r)) == 0).ToList();
            if (targets.Count == 0) { Log("갱신할 레포가 없습니다(갱신 체크 없음)"); return; }

            _busy = true;
            _btnSync.Enabled = false;
            _btnImport.Enabled = false;
            _btnAdd.Enabled = false;
            _btnDel.Enabled = false;
            Log("");
            Log(string.Format("=== 레포 {0}개 받는 중 ===", targets.Count));

            Task.Run(delegate
            {
                int ok = 0, fail = 0;
                foreach (var r in targets)
                {
                    var it = r;
                    Log("[ " + it.Name + " ] " + it.Url);
                    it.Status = "받는 중…";
                    RefreshRow(it);
                    bool done = CdbRepoStore.Sync(it, Log);
                    if (done) ok++; else fail++;
                    RefreshRow(it);
                    Log("  → " + it.Status);
                }
                Log(string.Format("=== 끝: 성공 {0} · 실패 {1} ===", ok, fail));
                try { BeginInvoke((Action)(() => { _busy = false; _btnSync.Enabled = true; _btnImport.Enabled = true; _btnAdd.Enabled = true; _btnDel.Enabled = true; })); } catch { }
            });
        }

        private void RefreshRow(CdbRepo r)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => RefreshRow(r))); return; }
                foreach (ListViewItem it in _lv.Items)
                {
                    if (!ReferenceEquals(it.Tag, r)) continue;
                    it.SubItems[4].Text = CdbRepoStore.CountCdb(CdbRepoStore.LocalDirOf(r)).ToString();
                    it.SubItems[5].Text = r.Status;
                    break;
                }
            }
            catch { }
        }

        /// <summary>검증용: 창을 띄우지 않고 PNG 로 저장.</summary>
        public void SaveShot(string path)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-4000, -4000);
            Show();
            Application.DoEvents();
            Refresh();
            Application.DoEvents();
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Hide();
        }

        /// <summary>검증용: 목록·가져오기 로직만 확인.</summary>
        public string SelfCheck()
        {
            var sb = new StringBuilder();
            sb.Append("list=" + (_lv != null && _lv.Columns.Count == 6 ? "OK" : "FAIL"));
            sb.Append(" repos=" + _repos.Count);
            try
            {
                var def = CdbRepoStore.Defaults();
                sb.Append(" defaults=" + def.Count);
                sb.Append(" slug=" + (def[0].Slug == "Team-AllYGOPro_edopro-korean" ? "OK" : "FAIL"));
                sb.Append(" cache=" + (CdbRepoStore.CacheRoot.IndexOf("ReplayVideo", StringComparison.OrdinalIgnoreCase) >= 0 ? "OK" : "FAIL"));
                var loaded = CdbRepoStore.FromEdoPro(_edoDir);
                sb.Append(" edo=" + loaded.Count);
            }
            catch (Exception ex) { sb.Append(" err=" + ex.Message); }
            return sb.ToString();
        }
    }
}
