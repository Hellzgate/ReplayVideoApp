using System;
using System.IO;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ReplayVideoApp
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            try
            {
                if (Array.IndexOf(args, "--uicheck") >= 0)
                    return UiCheck();

                // 화면을 PNG 로 저장(창을 화면 밖에 띄웠다가 바로 닫는다) — 디자인 확인용
                int shot = Array.IndexOf(args, "--screenshot");
                if (shot >= 0 && args.Length >= shot + 2)
                    return Screenshot(args[shot + 1], args);

                int r = Array.IndexOf(args, "--render");
                if (r >= 0 && args.Length >= r + 3)
                    return HeadlessRender(args, r);

                int repolist = Array.IndexOf(args, "--repolist");
                if (repolist >= 0) return RepoList();

                int reposync = Array.IndexOf(args, "--reposync");
                if (reposync >= 0 && args.Length >= reposync + 2)
                    return RepoSync(args[reposync + 1], Array.IndexOf(args, "--zip") >= 0);

                int reposhot = Array.IndexOf(args, "--reposhot");
                if (reposhot >= 0 && args.Length >= reposhot + 2)
                    return RepoShot(args[reposhot + 1]);

                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayVideoApp");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "startup-error.txt"), ex.ToString(), Encoding.UTF8);
                }
                catch { }
                // --uicheck / --screenshot / --render 는 창 없이 돌아야 한다 → MessageBox 를 띄우면
                // 자동 검증이 그 자리에서 멈춘다(실측). 콘솔 + startup-error.txt 로만 알린다.
                bool headless = Array.IndexOf(args, "--uicheck") >= 0
                                || Array.IndexOf(args, "--screenshot") >= 0
                                || Array.IndexOf(args, "--render") >= 0;
                if (headless)
                {
                    Console.Error.WriteLine("STARTUP FAIL " + ex);
                    return 1;
                }
                MessageBox.Show(ex.Message, "ReplayVideoApp 시작 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>카드 DB 레포 목록/캐시 상태 출력(검증용).</summary>
        private static int RepoList()
        {
            string edo = MainForm.GuessEdoProDir();
            var repos = CdbRepoStore.FromEdoPro(edo);
            string src = "EDOPro 설정";
            if (repos.Count == 0) { repos = CdbRepoStore.Defaults(); src = "기본 목록"; }
            Console.WriteLine("EDOPro 폴더 : " + (edo.Length > 0 ? edo : "(없음)"));
            Console.WriteLine("캐시        : " + CdbRepoStore.CacheRoot);
            Console.WriteLine("레포 출처   : " + src + " (" + repos.Count + "개)");
            Console.WriteLine("내장 cdb    : " + (CdbRepoStore.BundledDir.Length > 0 ? CdbRepoStore.BundledDir : "(없음)"));
            foreach (var r in repos)
            {
                string dir = CdbRepoStore.LocalDirOf(r);
                Console.WriteLine(string.Format("  {0,-24} {1,-58} cdb {2,3}개  {3}",
                    r.Name, r.Url, CdbRepoStore.CountCdb(dir), Directory.Exists(dir) ? "받음" : "없음"));
            }
            Console.WriteLine("합계 cdb    : " + CdbRepoStore.TotalCdb(repos));
            Console.WriteLine("REPOLIST OK");
            return 0;
        }

        /// <summary>레포 하나를 받아본다(검증용): --reposync &lt;url&gt; [--zip]</summary>
        private static int RepoSync(string url, bool forceZip)
        {
            if (!url.StartsWith("http")) url = "https://github.com/" + url.Trim('/');
            var r = new CdbRepo { Url = url };
            r.Name = r.Slug;
            Console.WriteLine("SYNC " + url + (forceZip ? "  (zip 강제)" : ""));
            bool ok = CdbRepoStore.Sync(r, delegate (string s) { Console.WriteLine(s); }, forceZip);
            Console.WriteLine("  결과: " + r.Status);
            Console.WriteLine("  폴더: " + CdbRepoStore.LocalDirOf(r));
            Console.WriteLine(ok ? "REPOSYNC OK" : "REPOSYNC FAIL");
            return ok ? 0 : 1;
        }

        /// <summary>카드 DB 레포 창 스크린샷(레이아웃 검증).</summary>
        private static int RepoShot(string path)
        {
            TextWriter orig = Console.Out;
            string edo = MainForm.GuessEdoProDir();
            var repos = CdbRepoStore.FromEdoPro(edo);
            if (repos.Count == 0) repos = CdbRepoStore.Defaults();
            using (var f = new RepoForm(edo, repos))
            {
                Console.SetOut(orig);
                f.CreateControl();
                string check = f.SelfCheck();
                string full = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                f.SaveShot(full);
                Console.WriteLine("REPOSHOT " + full + "  " + check);
            }
            return 0;
        }

        /// <summary>창 없이 렌더(앱 빌드의 엔진 연결 검증용): --render &lt;replay&gt; &lt;out.mp4&gt; [--portrait] [--fps N] [--pics DIR]</summary>
        private static int HeadlessRender(string[] args, int at)
        {
            string edo = MainForm.GuessEdoProDir();
            if (Array.IndexOf(args, "--no-edo") >= 0) edo = "";     // EDOPro 없이(레포 캐시만) 검증
            var req = new ReplayVideo.Program.RenderRequest
            {
                ReplayPath = args[at + 1],
                OutPath = args[at + 2],
                PicsDir = edo.Length > 0 ? Path.Combine(edo, "pics") : "",
                CdbPath = edo.Length > 0 ? Path.Combine(edo, "expansions", "cards.cdb") : "",
                Fps = 12
            };
            var repos = CdbRepoStore.FromEdoPro(edo);
            if (repos.Count == 0) repos = CdbRepoStore.Defaults();
            if (Array.IndexOf(args, "--no-repo") >= 0)    // 레포 없이 내장 cdb 만 검증
                foreach (var r in repos) r.Enabled = false;
            req.CdbDirs = CdbRepoStore.ResolveDirs(repos, edo);
            for (int i = at + 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--portrait": req.Portrait = true; break;
                    case "--fps": req.Fps = int.Parse(args[++i]); break;
                    case "--pics": req.PicsDir = args[++i]; break;
                    case "--cdb": req.CdbPath = Path.Combine(args[++i], "expansions", "cards.cdb"); break;
                    case "--no-api": req.Api = false; break;
                    case "--img-api": req.ImageApiFirst = true; break;
                }
            }
            try
            {
                ReplayVideo.Program.Render(req, delegate (int cur, int total, string text)
                {
                    if (!string.IsNullOrEmpty(text)) Console.WriteLine("  [" + cur + "/" + total + "] " + text);
                });
                Console.WriteLine("RENDER OK " + req.OutPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RENDER FAIL " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 화면을 PNG 로 저장한다. WinForms 는 Show() 없이는 자식이 그려지지 않으므로
        /// **화면 밖(-4000,-4000)** 에 잠깐 띄워 렌더한 뒤 바로 닫는다(포커스·소리 없음).
        /// </summary>
        private static int Screenshot(string path, string[] args)
        {
            TextWriter orig = Console.Out;      // 폼이 Console.Out 을 UI 로 바꾸므로 되돌린다
            using (var f = new MainForm())
            {
                Console.SetOut(orig);
                f.NoSaveOnClose = true;
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                if (Array.IndexOf(args, "--busy") >= 0) f.DemoBusy();
                if (Array.IndexOf(args, "--log") >= 0) f.DemoLog();
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
                Application.DoEvents();

                string full = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(full, System.Drawing.Imaging.ImageFormat.Png);
                }
                f.Hide();
                Console.WriteLine("SCREENSHOT OK " + full + " " + f.Width + "x" + f.Height);
            }
            return 0;
        }

        /// <summary>창을 띄우지 않고 UI 구성만 검사한다(에이전트 검증용).</summary>
        private static int UiCheck()
        {
            TextWriter orig = Console.Out;
            using (var f = new MainForm())
            {
                Console.SetOut(orig);          // 폼이 CLI 로그를 UI 로 보내도록 바꾸므로 되돌린다
                f.NoSaveOnClose = true;
                f.CreateControl();
                int n = f.ControlCount();
                string detail = f.CheckControls();
                Console.WriteLine("UICHECK " + detail + " controls=" + n);
                bool ok = detail.IndexOf("FAIL", StringComparison.Ordinal) < 0;
                Console.WriteLine(ok ? "UICHECK OK" : "UICHECK FAIL");
                return ok ? 0 : 2;
            }
        }
    }
}
