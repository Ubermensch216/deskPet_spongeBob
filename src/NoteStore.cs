using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;

namespace BikiniFriends
{
    internal enum NoteSort { UpdatedDescending, CreatedDescending, TitleAscending }

    /// <summary>
    /// 메모 저장소. deskNote 는 SQLite 를 쓰지만 비키니친구들은 외부 라이브러리 없이 EXE 하나라서, 메모 하나를 XML 파일 하나로 둔다.
    ///   %AppData%\비키니친구들\메모\notes\{id}.xml        메모 본문 · 모양 · 위치 · 첨부 목록
    ///   %AppData%\비키니친구들\메모\revisions\{id}.xml    덮어쓰기 전 본문(메모당 50개)
    ///   %AppData%\비키니친구들\메모\attachments\{id}\     복사해 둔 이미지
    ///   %AppData%\비키니친구들\메모\library.xml           노트북 · 알림 · 기본 설정
    /// 파일은 임시 파일에 쓴 뒤 바꿔치기(원자적)하므로 쓰는 도중에 꺼져도 이전 글이 그대로 남는다.
    /// 목록 · 검색 · 태그는 시작할 때 메모리에 올려 둔 색인으로 처리한다. 백업은 폴더 통째로 복사하면 된다.
    /// </summary>
    internal sealed class NoteStore
    {
        internal static NoteStore Current;
        internal static string DefaultRoot { get { return Path.Combine(Paths.Dir, "메모"); } }
        internal const long EmbedSizeLimit = 10L * 1024 * 1024;

        private readonly string root, notesDir, revDir, attachDir, libraryPath;
        private readonly Dictionary<Guid, Note> notes = new Dictionary<Guid, Note>();
        private readonly List<Notebook> notebooks = new List<Notebook>();
        private readonly List<NoteReminder> reminders = new List<NoteReminder>();
        private readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        private readonly HashSet<Guid> dirty = new HashSet<Guid>();
        private bool libraryDirty;

        internal readonly RevisionPolicy Policy = new RevisionPolicy();
        internal string Root { get { return root; } }
        /// <summary>내용 · 노트북 · 삭제처럼 목록에 영향을 주는 변화가 저장될 때.</summary>
        internal event Action Changed;
        internal List<string> LoadProblems = new List<string>();

        internal NoteStore(string rootDir)
        {
            root = rootDir; notesDir = Path.Combine(root, "notes"); revDir = Path.Combine(root, "revisions");
            attachDir = Path.Combine(root, "attachments"); libraryPath = Path.Combine(root, "library.xml");
        }

        // ───────── 불러오기 ─────────

        internal void Load()
        {
            notes.Clear(); notebooks.Clear(); reminders.Clear(); settings.Clear();
            try { Directory.CreateDirectory(notesDir); Directory.CreateDirectory(revDir); } catch { }
            LoadLibrary();
            if (!Directory.Exists(notesDir)) return;
            foreach (string path in Directory.GetFiles(notesDir, "*.xml"))
            {
                try { var n = ReadNote(path); if (n != null && n.IsBlank) { try { File.Delete(path); } catch { } } else if (n != null) notes[n.Id] = n; }   // 이전에 꺼지며 남은 빈 메모 파일은 치운다
                catch (Exception e) { LoadProblems.Add(Path.GetFileName(path) + ": " + e.Message); }
            }
        }

        private static XmlDocument LoadDoc(string path)
        {
            var doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
            return doc;
        }

        private static string Attr(XmlNode n, string name, string fallback = "") { var a = n.Attributes == null ? null : n.Attributes[name]; return a == null ? fallback : a.Value; }
        private static int AttrInt(XmlNode n, string name, int fallback) { int v; return int.TryParse(Attr(n, name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static long AttrLong(XmlNode n, string name, long fallback) { long v; return long.TryParse(Attr(n, name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        private static double AttrDouble(XmlNode n, string name, double fallback) { double v; return double.TryParse(Attr(n, name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : fallback; }
        private static DateTime AttrTime(XmlNode n, string name, DateTime fallback) { DateTime t; return DateTime.TryParse(Attr(n, name, ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t.ToUniversalTime() : fallback; }
        private static DateTime? AttrTimeOpt(XmlNode n, string name) { DateTime t; return DateTime.TryParse(Attr(n, name, ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t.ToLocalTime() : (DateTime?)null; }
        private static string Iso(DateTime utc) { return utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
        private static string Iso(DateTime? local) { return local.HasValue ? local.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : ""; }
        private static string N(Guid id) { return id.ToString("N"); }

        private Note ReadNote(string path)
        {
            var doc = LoadDoc(path); var el = doc.DocumentElement;
            Guid id; if (el == null || el.Name != "note" || !Guid.TryParse(Attr(el, "id"), out id)) return null;
            var n = new Note { Id = id };
            var content = el.SelectSingleNode("content"); n.Content = NoteContent.NormalizeLineEndings(content == null ? "" : content.InnerText);
            n.ColorKey = NoteColors.Normalize(Attr(el, "color")); n.Opacity = Math.Max(Note.MinOpacity, Math.Min(1.0, AttrDouble(el, "opacity", 1.0)));
            n.AlwaysOnTop = Attr(el, "top") == "1"; n.IsOpen = Attr(el, "open", "1") != "0";
            n.Geometry = new NoteGeometry
            {
                X = AttrInt(el, "x", 0), Y = AttrInt(el, "y", 0), MonitorKey = Attr(el, "monitor"),
                Width = Math.Max(NoteGeometry.MinW, AttrInt(el, "w", NoteGeometry.MediumW)), Height = Math.Max(NoteGeometry.MinH, AttrInt(el, "h", NoteGeometry.MediumH)),
                Preset = (NoteSizePreset)Math.Max(0, Math.Min(3, AttrInt(el, "preset", 1))),
            };
            Guid nb; n.NotebookId = Guid.TryParse(Attr(el, "notebook"), out nb) ? nb : (Guid?)null;
            n.CreatedUtc = AttrTime(el, "created", DateTime.UtcNow); n.UpdatedUtc = AttrTime(el, "updated", n.CreatedUtc); n.Rev = Math.Max(1, AttrLong(el, "rev", 1));
            foreach (XmlNode a in el.SelectNodes("attachments/att"))
            {
                Guid aid; if (!Guid.TryParse(Attr(a, "id"), out aid)) continue;
                n.Attachments.Add(new NoteAttachment { Id = aid, Path = Attr(a, "path"), DisplayName = Attr(a, "name"), Embedded = Attr(a, "kind") == "embedded", Size = AttrLong(a, "size", 0) });
            }
            n.Derive(); return n;
        }

        // ───────── 쓰기 ─────────

        private static bool Atomic(string path, XmlDocument doc)
        {
            string tmp = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; doc.Save(tmp);
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                return true;
            }
            catch { try { if (tmp != null) File.Delete(tmp); } catch { } return false; }
        }

        private string NotePath(Guid id) { return Path.Combine(notesDir, N(id) + ".xml"); }

        private bool WriteNote(Note n)
        {
            // 빈 메모는 파일로 남기지 않는다. 쓰다가 글을 다 지웠다면 있던 파일도 지운다(창이 열려 있는 동안만 메모리에 있다).
            if (n.IsBlank) { try { File.Delete(NotePath(n.Id)); } catch { } return true; }
            var doc = new XmlDocument(); var el = doc.CreateElement("note"); doc.AppendChild(el);
            Action<string, string> set = (k, v) => el.SetAttribute(k, v);
            set("id", n.Id.ToString()); set("color", n.ColorKey); set("opacity", n.Opacity.ToString("0.###", CultureInfo.InvariantCulture));
            set("top", n.AlwaysOnTop ? "1" : "0"); set("open", n.IsOpen ? "1" : "0");
            set("x", n.Geometry.X.ToString(CultureInfo.InvariantCulture)); set("y", n.Geometry.Y.ToString(CultureInfo.InvariantCulture));
            set("w", n.Geometry.Width.ToString(CultureInfo.InvariantCulture)); set("h", n.Geometry.Height.ToString(CultureInfo.InvariantCulture));
            set("monitor", n.Geometry.MonitorKey ?? ""); set("preset", ((int)n.Geometry.Preset).ToString(CultureInfo.InvariantCulture));
            set("notebook", n.NotebookId.HasValue ? n.NotebookId.Value.ToString() : "");
            set("created", Iso(n.CreatedUtc)); set("updated", Iso(n.UpdatedUtc)); set("rev", n.Rev.ToString(CultureInfo.InvariantCulture));
            var content = doc.CreateElement("content"); content.SetAttribute("xml:space", "preserve");
            content.InnerText = NoteContent.StripInvalidXml(n.Content); el.AppendChild(content);
            var atts = doc.CreateElement("attachments"); el.AppendChild(atts);
            foreach (var a in n.Attachments)
            {
                var e = doc.CreateElement("att"); e.SetAttribute("id", a.Id.ToString()); e.SetAttribute("kind", a.Embedded ? "embedded" : "link");
                e.SetAttribute("path", a.Path ?? ""); e.SetAttribute("name", a.DisplayName ?? ""); e.SetAttribute("size", a.Size.ToString(CultureInfo.InvariantCulture)); atts.AppendChild(e);
            }
            return Atomic(NotePath(n.Id), doc);
        }

        private void RaiseChanged() { var h = Changed; if (h != null) h(); }

        // ───────── 메모 ─────────

        internal Note Get(Guid id) { Note n; return notes.TryGetValue(id, out n) ? n : null; }
        /// <summary>라이브러리에 올라가는(빈 메모가 아닌) 메모 수.</summary>
        internal int Count { get { return notes.Values.Count(n => !n.IsBlank); } }
        internal IEnumerable<Note> All { get { return notes.Values.Where(n => !n.IsBlank); } }
        internal List<Note> OpenNotes() { return notes.Values.Where(n => n.IsOpen && !n.IsBlank).OrderBy(n => n.UpdatedUtc).ToList(); }

        internal Note Create(NoteGeometry geometry, string colorKey, string content = "", bool open = true)
        {
            var now = DateTime.UtcNow;
            var n = new Note { Id = Guid.NewGuid(), Geometry = geometry, ColorKey = NoteColors.Normalize(colorKey), Content = NoteContent.NormalizeLineEndings(content), IsOpen = open, CreatedUtc = now, UpdatedUtc = now };
            n.Derive(); notes[n.Id] = n; WriteNote(n); RaiseChanged(); return n;
        }

        /// <summary>
        /// 본문 저장. 내용이 바뀌었으면 덮어쓰기 전 본문을 정책에 따라 기록에 남긴다.
        /// 글을 통째로 바꾸는 일(기록 복원)은 BulkReplace 로 불러 늘 남긴다.
        /// </summary>
        internal bool SaveContent(Guid id, string content, RevisionReason reason = RevisionReason.Edit)
        {
            var n = Get(id); if (n == null) return false;
            content = NoteContent.NormalizeLineEndings(content ?? "");
            bool changed = !string.Equals(n.Content, content, StringComparison.Ordinal);
            if (changed)
            {
                var now = DateTime.UtcNow;
                if (Policy.ShouldSnapshot(id, n.Content, now, reason)) { AddRevision(id, n.Content, now); Policy.MarkTaken(id, now); }
                n.Content = content; n.UpdatedUtc = now; n.Rev++; n.Derive();
            }
            bool ok = WriteNote(n);
            if (changed) RaiseChanged();
            return ok;
        }

        internal void SetGeometry(Guid id, NoteGeometry g) { var n = Get(id); if (n == null) return; n.Geometry = g; dirty.Add(id); }
        internal void SetAppearance(Guid id, string color, double opacity, bool top)
        { var n = Get(id); if (n == null) return; n.ColorKey = NoteColors.Normalize(color); n.Opacity = Math.Max(Note.MinOpacity, Math.Min(1.0, opacity)); n.AlwaysOnTop = top; dirty.Add(id); }
        internal void SetOpen(Guid id, bool open) { var n = Get(id); if (n == null) return; n.IsOpen = open; dirty.Add(id); }
        internal void SetNotebook(Guid id, Guid? notebook) { var n = Get(id); if (n == null) return; n.NotebookId = notebook; WriteNote(n); RaiseChanged(); }

        /// <summary>자리·모양처럼 자주 바뀌는 값은 모았다가 한꺼번에 쓴다(타이머가 부른다).</summary>
        internal void FlushDirty()
        {
            if (dirty.Count > 0) { foreach (var id in dirty.ToList()) { var n = Get(id); if (n != null) WriteNote(n); } dirty.Clear(); }
            if (libraryDirty) SaveLibrary();
        }

        /// <summary>삭제는 되돌릴 수 없다(휴지통 없음): 본문 · 기록 · 복사해 둔 이미지 · 알림이 함께 사라진다.</summary>
        /// <summary>메모가 비어 있으면 통째로 치운다(본문 · 기록 · 알림). 치웠으면 true.</summary>
        internal bool PurgeIfBlank(Guid id) { var n = Get(id); if (n == null || !n.IsBlank) return false; Delete(id); return true; }

        /// <summary>끌 때: 아직 메모리에 남은 빈 메모를 모두 치운다.</summary>
        internal void PurgeBlanks() { foreach (var id in notes.Values.Where(n => n.IsBlank).Select(n => n.Id).ToList()) Delete(id); }

        internal void Delete(Guid id)
        {
            if (!notes.Remove(id)) return;
            dirty.Remove(id); Policy.Forget(id);
            try { File.Delete(NotePath(id)); } catch { }
            try { File.Delete(RevPath(id)); } catch { }
            try { string dir = Path.Combine(attachDir, N(id)); if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            if (reminders.RemoveAll(r => r.NoteId == id) > 0) { libraryDirty = true; SaveLibrary(); }
            RaiseChanged();
        }

        // ───────── 목록 · 검색 · 태그 ─────────

        internal List<Note> Query(string text, Guid? notebook, string tagNormalized, NoteSort sort = NoteSort.UpdatedDescending, int limit = 500)
        {
            IEnumerable<Note> q = notes.Values.Where(n => !n.IsBlank);
            if (notebook.HasValue) q = q.Where(n => n.NotebookId == notebook);
            if (!string.IsNullOrEmpty(tagNormalized)) q = q.Where(n => n.Tags.Any(t => TagParser.Normalize(t) == tagNormalized));
            if (!string.IsNullOrWhiteSpace(text))
            {
                string t = text.Trim();
                q = q.Where(n => n.Title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || n.Content.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            switch (sort)
            {
                case NoteSort.CreatedDescending: q = q.OrderByDescending(n => n.CreatedUtc); break;
                case NoteSort.TitleAscending: q = q.OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase); break;
                default: q = q.OrderByDescending(n => n.UpdatedUtc); break;
            }
            return q.Take(limit).ToList();
        }

        /// <summary>사용 중인 태그와 그 태그가 달린 메모 수(많은 순).</summary>
        internal List<KeyValuePair<string, int>> TagUsage()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal); var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in notes.Values)
                foreach (string t in n.Tags) { string k = TagParser.Normalize(t); int c; counts.TryGetValue(k, out c); counts[k] = c + 1; if (!names.ContainsKey(k)) names[k] = t; }
            return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new KeyValuePair<string, int>(names[kv.Key], kv.Value)).ToList();
        }

        // ───────── 노트북 ─────────

        internal List<Notebook> Notebooks { get { return notebooks.OrderBy(n => n.Ordinal).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); } }
        internal Notebook CreateNotebook(string name)
        { var nb = new Notebook { Id = Guid.NewGuid(), Name = name.Trim(), CreatedUtc = DateTime.UtcNow }; notebooks.Add(nb); libraryDirty = true; SaveLibrary(); RaiseChanged(); return nb; }
        internal void RenameNotebook(Guid id, string name) { var nb = notebooks.FirstOrDefault(n => n.Id == id); if (nb == null) return; nb.Name = name.Trim(); libraryDirty = true; SaveLibrary(); RaiseChanged(); }
        /// <summary>노트북을 지워도 안의 메모는 지워지지 않고 노트북에서만 빠진다.</summary>
        internal void DeleteNotebook(Guid id)
        {
            if (notebooks.RemoveAll(n => n.Id == id) == 0) return;
            foreach (var n in notes.Values.Where(x => x.NotebookId == id).ToList()) { n.NotebookId = null; WriteNote(n); }
            libraryDirty = true; SaveLibrary(); RaiseChanged();
        }

        // ───────── 알림 ─────────

        internal NoteReminder AddReminder(Guid noteId, DateTime dueLocal, string rule = null)
        {
            var r = new NoteReminder { Id = Guid.NewGuid(), NoteId = noteId, DueLocal = dueLocal, RecurrenceRule = rule };
            reminders.Add(r); libraryDirty = true; SaveLibrary(); RaiseChanged(); return r;
        }
        internal List<NoteReminder> DueReminders(DateTime now) { return ReminderScheduler.SelectDue(reminders, now); }
        internal void MarkNotified(Guid id, DateTime now) { var r = reminders.FirstOrDefault(x => x.Id == id); if (r == null) return; r.NotifiedLocal = now; libraryDirty = true; SaveLibrary(); RaiseChanged(); }
        internal void Reschedule(Guid id, DateTime next) { var r = reminders.FirstOrDefault(x => x.Id == id); if (r == null) return; r.DueLocal = next; r.NotifiedLocal = null; libraryDirty = true; SaveLibrary(); RaiseChanged(); }
        internal DateTime? NextReminder(Guid noteId, DateTime now)
        {
            var due = reminders.Where(r => r.NoteId == noteId && !r.IsCompleted && !r.NotifiedLocal.HasValue && r.DueLocal >= now).Select(r => r.DueLocal).ToList();
            return due.Count == 0 ? (DateTime?)null : due.Min();
        }
        internal List<NoteReminder> RemindersOf(Guid noteId) { return reminders.Where(r => r.NoteId == noteId).ToList(); }

        // ───────── 설정 ─────────

        internal string GetSetting(string key, string fallback = null) { string v; return settings.TryGetValue(key, out v) ? v : fallback; }
        internal void SetSetting(string key, string value) { settings[key] = value; libraryDirty = true; SaveLibrary(); }

        private void LoadLibrary()
        {
            if (!File.Exists(libraryPath)) return;
            try
            {
                var el = LoadDoc(libraryPath).DocumentElement; if (el == null) return;
                foreach (XmlNode n in el.SelectNodes("notebooks/notebook"))
                { Guid id; if (Guid.TryParse(Attr(n, "id"), out id)) notebooks.Add(new Notebook { Id = id, Name = Attr(n, "name"), Ordinal = AttrInt(n, "ordinal", 0), CreatedUtc = AttrTime(n, "created", DateTime.UtcNow) }); }
                foreach (XmlNode n in el.SelectNodes("reminders/reminder"))
                {
                    Guid id, note; if (!Guid.TryParse(Attr(n, "id"), out id) || !Guid.TryParse(Attr(n, "note"), out note)) continue;
                    var due = AttrTimeOpt(n, "due"); if (!due.HasValue) continue;
                    string rule = Attr(n, "rule");
                    reminders.Add(new NoteReminder { Id = id, NoteId = note, DueLocal = due.Value, RecurrenceRule = rule.Length == 0 ? null : rule, CompletedLocal = AttrTimeOpt(n, "completed"), NotifiedLocal = AttrTimeOpt(n, "notified") });
                }
                foreach (XmlNode n in el.SelectNodes("settings/set")) settings[Attr(n, "key")] = n.InnerText;
            }
            catch (Exception e) { LoadProblems.Add("library.xml: " + e.Message); }
        }

        private bool SaveLibrary()
        {
            var doc = new XmlDocument(); var el = doc.CreateElement("library"); doc.AppendChild(el);
            var nbs = doc.CreateElement("notebooks"); el.AppendChild(nbs);
            foreach (var nb in notebooks) { var e = doc.CreateElement("notebook"); e.SetAttribute("id", nb.Id.ToString()); e.SetAttribute("name", NoteContent.StripInvalidXml(nb.Name)); e.SetAttribute("ordinal", nb.Ordinal.ToString(CultureInfo.InvariantCulture)); e.SetAttribute("created", Iso(nb.CreatedUtc)); nbs.AppendChild(e); }
            var rs = doc.CreateElement("reminders"); el.AppendChild(rs);
            foreach (var r in reminders)
            {
                var e = doc.CreateElement("reminder"); e.SetAttribute("id", r.Id.ToString()); e.SetAttribute("note", r.NoteId.ToString()); e.SetAttribute("due", Iso(r.DueLocal));
                e.SetAttribute("rule", r.RecurrenceRule ?? ""); e.SetAttribute("completed", Iso(r.CompletedLocal)); e.SetAttribute("notified", Iso(r.NotifiedLocal)); rs.AppendChild(e);
            }
            var ss = doc.CreateElement("settings"); el.AppendChild(ss);
            foreach (var kv in settings) { var e = doc.CreateElement("set"); e.SetAttribute("key", kv.Key); e.InnerText = NoteContent.StripInvalidXml(kv.Value); ss.AppendChild(e); }
            bool ok = Atomic(libraryPath, doc); if (ok) libraryDirty = false; return ok;
        }

        // ───────── 기록(덮어쓰기 전 본문) ─────────

        private string RevPath(Guid id) { return Path.Combine(revDir, N(id) + ".xml"); }

        internal void AddRevision(Guid noteId, string content, DateTime utc)
        {
            var revs = ReadRevisions(noteId); revs.Add(new NoteRevision { Id = Guid.NewGuid(), NoteId = noteId, Content = content, CreatedUtc = utc });
            while (revs.Count > RevisionPolicy.KeepPerNote) revs.RemoveAt(0);
            var doc = new XmlDocument(); var el = doc.CreateElement("revisions"); doc.AppendChild(el);
            foreach (var r in revs) { var e = doc.CreateElement("rev"); e.SetAttribute("id", r.Id.ToString()); e.SetAttribute("time", Iso(r.CreatedUtc)); e.SetAttribute("xml:space", "preserve"); e.InnerText = NoteContent.StripInvalidXml(r.Content); el.AppendChild(e); }
            Atomic(RevPath(noteId), doc);
        }

        private List<NoteRevision> ReadRevisions(Guid noteId)
        {
            var list = new List<NoteRevision>(); string path = RevPath(noteId); if (!File.Exists(path)) return list;
            try
            {
                foreach (XmlNode n in LoadDoc(path).DocumentElement.SelectNodes("rev"))
                { Guid id; if (Guid.TryParse(Attr(n, "id"), out id)) list.Add(new NoteRevision { Id = id, NoteId = noteId, Content = NoteContent.NormalizeLineEndings(n.InnerText), CreatedUtc = AttrTime(n, "time", DateTime.UtcNow) }); }
            }
            catch { }
            return list;
        }

        /// <summary>새 것이 앞.</summary>
        internal List<NoteRevision> Revisions(Guid noteId) { var l = ReadRevisions(noteId); l.Reverse(); return l; }

        // ───────── 첨부 ─────────

        internal static bool IsImage(string fileName)
        {
            string ext = (Path.GetExtension(fileName) ?? "").ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp" || ext == ".tif" || ext == ".tiff";
        }

        /// <summary>붙여넣은 이미지: 항상 메모 폴더에 복사해 둔다(같은 그림은 해시가 같아 한 번만).</summary>
        internal NoteAttachment Embed(Guid noteId, string displayName, byte[] bytes)
        {
            var n = Get(noteId); if (n == null) return null;
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string dir = Path.Combine(attachDir, N(noteId)); Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, hash + Path.GetExtension(displayName)); if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            return Record(n, path, displayName, true, bytes.LongLength);
        }

        /// <summary>끌어다 놓거나 고른 파일: 10MB 이하 이미지는 복사, 그 밖에는 원래 자리를 링크.</summary>
        internal NoteAttachment AddFile(Guid noteId, string sourcePath)
        {
            var n = Get(noteId); if (n == null) return null;
            var info = new FileInfo(sourcePath);
            if (info.Exists && info.Length <= EmbedSizeLimit && IsImage(info.Name)) return Embed(noteId, info.Name, File.ReadAllBytes(sourcePath));
            return Record(n, sourcePath, info.Name, false, info.Exists ? info.Length : 0);
        }

        private NoteAttachment Record(Note n, string path, string name, bool embedded, long size)
        {
            var a = new NoteAttachment { Id = Guid.NewGuid(), Path = path, DisplayName = name, Embedded = embedded, Size = size };
            n.Attachments.Add(a); WriteNote(n); return a;
        }

        internal void RemoveAttachment(Guid noteId, Guid attachmentId)
        {
            var n = Get(noteId); if (n == null) return;
            var a = n.Attachments.FirstOrDefault(x => x.Id == attachmentId); if (a == null) return;
            n.Attachments.Remove(a); WriteNote(n);
            if (a.Embedded && !n.Attachments.Any(o => string.Equals(o.Path, a.Path, StringComparison.OrdinalIgnoreCase))) { try { File.Delete(a.Path); } catch { } }
        }

        /// <summary>이미지가 아닌 첨부는 본문에 링크 한 줄로 들어간다.</summary>
        internal static string LinkMarkdown(NoteAttachment a) { return "[" + a.DisplayName + "](" + a.Path.Replace(" ", "%20") + ")"; }
    }
}
