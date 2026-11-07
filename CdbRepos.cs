// 카드 DB 레포 — EDOPro 와 같은 모델(configs.json / user_configs.json 의 repos[])을 읽어
// 각 레포를 **git clone/pull**(없으면 zip 다운로드)로 D:\ReplayVideo\cdb\repos\<slug> 에 받고,
// 그 안의 *.cdb 를 카드 정보 소스로 넘긴다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace ReplayVideoApp
{
    public sealed class CdbRepo
    {
        public string Name = "";        // repo_name
        public string Url = "";         // https://github.com/owner/repo
        public string SubPath = "";     // repo_path ("./repositories/languages/한국어") — 표시용
        public bool IsLanguage;
        public bool Enabled = true;     // should_read
        public bool Update = true;      // should_update
        public string Status = "";      // 마지막 결과(표시용)

        public CdbRepo Clone()
        {
            return new CdbRepo
            {
                Name = Name, Url = Url, SubPath = SubPath, IsLanguage = IsLanguage,
                Enabled = Enabled, Update = Update, Status = Status
            };
        }

        public string Slug
        {
            get
            {
                string s = Url ?? "";
                s = s.Replace("https://github.com/", "").Replace("http://github.com/", "").TrimEnd('/');
                if (s.EndsWith(".git")) s = s.Substring(0, s.Length - 4);
                return s.Replace('/', '_').Replace('\\', '_');
            }
        }
    }

    public static class CdbRepoStore
    {
        /// <summary>내려받은 레포 보관 위치 — C: 를 쓰지 않는다.</summary>
        public static string CacheRoot
        {
            get { return Path.Combine(ReplayVideo.Program.BaseDir(), "cdb", "repos"); }
        }

        public static string LocalDirOf(CdbRepo r)
        {
            return Path.Combine(CacheRoot, r.Slug);
        }

        public static bool IsCdbRelevant(string repoPath)
        {
            string p = (repoPath ?? "").ToLowerInvariant();
            if (p.Contains("puzzles")) return false;      // 카드 DB 아님
            if (p.Contains("lflists")) return false;      // 금제 목록만
            return true;
        }

        /// <summary>EDOPro 폴더의 config/configs.json + user_configs.json 에서 레포 목록을 읽는다.</summary>
        public static List<CdbRepo> FromEdoPro(string edoDir)
        {
            var list = new List<CdbRepo>();
            if (string.IsNullOrEmpty(edoDir) || !Directory.Exists(edoDir)) return list;
            foreach (string name in new[] { "configs.json", "user_configs.json" })
            {
                string p = Path.Combine(edoDir, "config", name);
                if (!File.Exists(p)) continue;
                try
                {
                    using (var doc = JsonDocument.Parse(File.ReadAllText(p)))
                    {
                        JsonElement repos;
                        if (!doc.RootElement.TryGetProperty("repos", out repos)) continue;
                        foreach (JsonElement e in repos.EnumerateArray())
                        {
                            var r = new CdbRepo
                            {
                                Url = Str(e, "url"),
                                Name = Str(e, "repo_name"),
                                SubPath = Str(e, "repo_path"),
                                IsLanguage = Bool(e, "is_language"),
                                Enabled = Bool(e, "should_read", true),
                                Update = Bool(e, "should_update", true)
                            };
                            if (string.IsNullOrEmpty(r.Url)) continue;
                            if (!IsCdbRelevant(r.SubPath)) continue;
                            if (list.Any(x => string.Equals(x.Url, r.Url, StringComparison.OrdinalIgnoreCase))) continue;
                            if (string.IsNullOrEmpty(r.Name)) r.Name = r.Slug;
                            list.Add(r);
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>EDOPro 설정을 못 읽었을 때 쓰는 기본 목록(이 PC 의 configs 두 파일 기준).</summary>
        public static List<CdbRepo> Defaults()
        {
            return new List<CdbRepo>
            {
                new CdbRepo { Name = "한국어 패치", Url = "https://github.com/Team-AllYGOPro/edopro-korean", SubPath = "./repositories/languages/한국어", IsLanguage = true },
                new CdbRepo { Name = "한국어 패치 업데이트", Url = "https://github.com/Team-AllYGOPro/edopro-korean-delta", SubPath = "./repositories/languages/한국어-1", IsLanguage = true },
                new CdbRepo { Name = "커스텀 한국어 패치", Url = "https://github.com/CCC-Server/EMP-TY", SubPath = "./repositories/languages/한국어-2", IsLanguage = true, Update = false },
                new CdbRepo { Name = "한국어 패치(Hellz)", Url = "https://github.com/Hellzgate/EdoKroCDB", SubPath = "./repositories/languages/한국어-3", IsLanguage = true },
                new CdbRepo { Name = "최신 카드 업데이트", Url = "https://github.com/ProjectIgnis/DeltaBagooska", SubPath = "./repositories/delta-bagooska" },
                new CdbRepo { Name = "커스텀 확장팩", Url = "https://github.com/CCC-Server/EMP-TY", SubPath = "./expansions-2", Update = false }
            };
        }

        private static string Str(JsonElement e, string key)
        {
            JsonElement v;
            return e.TryGetProperty(key, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        }

        private static bool Bool(JsonElement e, string key, bool dflt = false)
        {
            JsonElement v;
            if (!e.TryGetProperty(key, out v)) return dflt;
            return v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true");
        }

        // ── 동기화 ────────────────────────────────────────────
        /// <summary>레포 하나를 받거나 갱신한다. 실패하면 false + 사유를 Status 에.</summary>
        public static bool Sync(CdbRepo r, Action<string> log, bool forceZip = false)
        {
            if (r == null || string.IsNullOrEmpty(r.Url)) return false;
            string dir = LocalDirOf(r);
            try
            {
                Directory.CreateDirectory(CacheRoot);
            }
            catch (Exception ex)
            {
                r.Status = "캐시 폴더 생성 실패: " + ex.Message;
                return false;
            }

            string git = FindGit();
            bool hasGitDir = Directory.Exists(Path.Combine(dir, ".git"));
            try
            {
                if (forceZip) return SyncZip(r, dir, log);
                if (git != null)
                {
                    bool ok;
                    if (hasGitDir)
                    {
                        log?.Invoke("  git pull  " + r.Name);
                        ok = RunGit(git, "-C \"" + dir + "\" fetch --depth 1 origin", log)
                          && RunGit(git, "-C \"" + dir + "\" reset --hard FETCH_HEAD", log);
                    }
                    else
                    {
                        DeleteDirSafe(dir);
                        log?.Invoke("  git clone " + r.Url);
                        ok = RunGit(git, "clone --depth 1 --single-branch \"" + r.Url + "\" \"" + dir + "\"", log);
                    }
                    if (ok)
                    {
                        int n = CountCdb(dir);
                        r.Status = n > 0 ? ("완료 · cdb " + n + "개") : "완료(이 레포에 cdb 없음)";
                        return n > 0;
                    }
                    log?.Invoke("  ⚠ git 실패 → zip 다운로드로 재시도");
                }
                else
                {
                    log?.Invoke("  (git 없음 → zip 다운로드)");
                }
                return SyncZip(r, dir, log);
            }
            catch (Exception ex)
            {
                r.Status = "오류: " + ex.Message;
                log?.Invoke("  ✗ " + ex.Message);
                return false;
            }
        }

        private static bool SyncZip(CdbRepo r, string dir, Action<string> log)
        {
            string slug = r.Slug;                       // owner_repo
            string owner = slug.Split('_')[0];
            string repo = slug.Substring(slug.IndexOf('_') + 1);
            string zip = Path.Combine(CacheRoot, slug + ".zip");
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    http.DefaultRequestHeaders.Add("User-Agent", "ReplayVideoApp");
                    bool got = false;
                    foreach (string branch in new[] { "main", "master" })
                    {
                        string url = string.Format("https://codeload.github.com/{0}/{1}/zip/refs/heads/{2}", owner, repo, branch);
                        try
                        {
                            log?.Invoke("  zip 받는 중… " + url);
                            byte[] bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                            if (bytes == null || bytes.Length < 1000) continue;
                            File.WriteAllBytes(zip, bytes);
                            got = true;
                            break;
                        }
                        catch { }
                    }
                    if (!got) { r.Status = "다운로드 실패(비공개이거나 주소 오류)"; return false; }

                    // 먼저 임시 폴더에 풀고, 성공하면 기존 폴더를 지우고 옮긴다(실패해도 기존 캐시 유지)
                    string tmp = Path.Combine(CacheRoot, "_unzip_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                    ZipFile.ExtractToDirectory(zip, tmp);
                    string inner = Directory.GetDirectories(tmp).FirstOrDefault() ?? tmp;

                    DeleteDirSafe(dir);                     // .git 읽기전용 파일 때문에 실패하던 지점
                    Directory.CreateDirectory(dir);
                    MoveDir(inner, dir);
                    try { DeleteDirSafe(tmp); } catch { }
                    try { File.Delete(zip); } catch { }
                }
                int n = CountCdb(dir);
                r.Status = n > 0 ? ("완료(zip) · cdb " + n + "개") : "완료(zip)(cdb 없음)";
                return n > 0;
            }
            catch (Exception ex)
            {
                r.Status = "zip 오류: " + ex.Message;
                log?.Invoke("  ✗ zip 오류: " + ex.Message);
                return false;
            }
        }

        /// <summary>읽기전용(.git pack) 파일 때문에 Directory.Delete 가 실패하는 문제 회피.</summary>
        private static void DeleteDirSafe(string dir)
        {
            if (!Directory.Exists(dir)) return;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    ClearReadOnly(dir);
                    Directory.Delete(dir, true);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }
            try
            {
                string keep = dir + ".old-" + DateTime.Now.ToString("HHmmss");
                Directory.Move(dir, keep);
            }
            catch { }
        }

        private static void ClearReadOnly(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var a = File.GetAttributes(f);
                        if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void MoveDir(string from, string to)
        {
            foreach (string d in Directory.GetDirectories(from))
            {
                string target = Path.Combine(to, Path.GetFileName(d));
                Directory.CreateDirectory(target);
                MoveDir(d, target);
            }
            foreach (string f in Directory.GetFiles(from))
            {
                string target = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(target)) File.Delete(target);
                File.Move(f, target);
            }
        }

        private static string FindGit()
        {
            string[] cand =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinGit", "cmd", "git.exe"),
                @"C:\Program Files\Git\cmd\git.exe",
                @"C:\Program Files (x86)\Git\cmd\git.exe"
            };
            foreach (string p in cand) { try { if (File.Exists(p)) return p; } catch { } }
            try
            {
                foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try { if (File.Exists(Path.Combine(dir.Trim(), "git.exe"))) return Path.Combine(dir.Trim(), "git.exe"); }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static bool RunGit(string git, string args, Action<string> log)
        {
            var psi = new ProcessStartInfo(git, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    log?.Invoke("    git: " + Shorten(err));
                    return false;
                }
                return true;
            }
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }

        public static int CountCdb(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetFiles(dir, "*.cdb", SearchOption.AllDirectories).Length;
            }
            catch { return 0; }
        }

        /// <summary>배포본에 함께 들어 있는 기본 cdb 폴더(exe 옆 carddata).</summary>
        public static string BundledDir
        {
            get
            {
                try
                {
                    string d = Path.Combine(AppContext.BaseDirectory ?? "", "carddata");
                    if (!Directory.Exists(d)) return "";
                    return Directory.GetFiles(d, "*.cdb").Length > 0 ? d : "";
                }
                catch { return ""; }
            }
        }

        /// <summary>카드 정보를 읽을 폴더들 — EDOPro 폴더(있으면) + 레포 캐시 + 내장 carddata.</summary>
        public static List<string> ResolveDirs(IEnumerable<CdbRepo> repos, string edoDir)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(edoDir) && Directory.Exists(edoDir)) dirs.Add(edoDir);
            if (repos != null)
                foreach (var r in repos)
                {
                    if (!r.Enabled) continue;
                    string d = LocalDirOf(r);
                    if (Directory.Exists(d) && !dirs.Contains(d)) dirs.Add(d);
                }
            string bundled = BundledDir;
            if (bundled.Length > 0 && !dirs.Contains(bundled)) dirs.Add(bundled);
            return dirs;
        }

        public static int TotalCdb(IEnumerable<CdbRepo> repos)
        {
            int n = 0;
            if (repos == null) return 0;
            foreach (var r in repos) if (r.Enabled) n += CountCdb(LocalDirOf(r));
            return n;
        }
    }
}
