using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BikiniFriends
{
    internal enum RecurrenceFrequency { Daily = 0, Weekly = 1, Monthly = 2, Yearly = 3 }

    /// <summary>
    /// 반복 알림. RFC 5545 RRULE 조각(FREQ=WEEKLY;INTERVAL=2)으로 저장한다. 다음 시각은 "지금" 이 아니라 처음 시각에서 센다 —
    /// 며칠 비워도 매일 9시 알림은 그대로 9시이고, 1월 31일 월간 알림이 2월 28일을 거쳐 영영 28일로 밀리지도 않는다.
    /// </summary>
    internal struct Recurrence
    {
        internal RecurrenceFrequency Frequency; internal int Interval;
        internal Recurrence(RecurrenceFrequency f, int interval = 1) { Frequency = f; Interval = Math.Max(1, interval); }

        internal string ToRule()
        {
            string f = Frequency.ToString().ToUpperInvariant();
            return Interval == 1 ? "FREQ=" + f : "FREQ=" + f + ";INTERVAL=" + Interval.ToString(CultureInfo.InvariantCulture);
        }

        internal static Recurrence? Parse(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return null;
            RecurrenceFrequency? freq = null; int interval = 1;
            foreach (string part in rule.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length != 2) continue;
                string key = pair[0].Trim().ToUpperInvariant(), val = pair[1].Trim().ToUpperInvariant();
                if (key == "FREQ") { switch (val) { case "DAILY": freq = RecurrenceFrequency.Daily; break; case "WEEKLY": freq = RecurrenceFrequency.Weekly; break; case "MONTHLY": freq = RecurrenceFrequency.Monthly; break; case "YEARLY": freq = RecurrenceFrequency.Yearly; break; default: freq = null; break; } }
                else if (key == "INTERVAL") { int n; if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0) interval = n; }
            }
            return freq.HasValue ? new Recurrence(freq.Value, interval) : (Recurrence?)null;
        }

        /// <summary>from 에서 세어 after 보다 뒤인 첫 시각.</summary>
        internal DateTime NextAfter(DateTime from, DateTime after)
        {
            const int MaxSteps = 10000; int step = 0; DateTime next;
            do { next = Advance(from, ++step); } while (next <= after && step < MaxSteps);
            return next;
        }

        private DateTime Advance(DateTime origin, int steps)
        {
            switch (Frequency)
            {
                case RecurrenceFrequency.Daily: return origin.AddDays((double)Interval * steps);
                case RecurrenceFrequency.Weekly: return origin.AddDays(7d * Interval * steps);
                case RecurrenceFrequency.Monthly: return origin.AddMonths(Interval * steps);
                default: return origin.AddYears(Interval * steps);
            }
        }

        internal string Describe()
        {
            string unit; switch (Frequency) { case RecurrenceFrequency.Daily: unit = "일"; break; case RecurrenceFrequency.Weekly: unit = "주"; break; case RecurrenceFrequency.Monthly: unit = "개월"; break; default: unit = "년"; break; }
            return Interval == 1 ? "매" + (Frequency == RecurrenceFrequency.Daily ? "일" : Frequency == RecurrenceFrequency.Weekly ? "주" : Frequency == RecurrenceFrequency.Monthly ? "달" : "년") + " 반복" : Interval + unit + "마다 반복";
        }
    }

    /// <summary>울린 알림이 다음에 어떻게 되는지. 반복은 놓친 횟수만큼이 아니라 "지금 이후 첫 회" 로 건너뛴다(일주일 뒤에 켜도 알림은 7번이 아니라 1번).</summary>
    internal static class ReminderScheduler
    {
        internal static List<NoteReminder> SelectDue(IEnumerable<NoteReminder> reminders, DateTime now)
        { return reminders.Where(r => !r.IsCompleted && !r.NotifiedLocal.HasValue && r.DueLocal <= now).OrderBy(r => r.DueLocal).ToList(); }

        internal static DateTime? NextDue(NoteReminder r, DateTime now)
        { var rec = Recurrence.Parse(r.RecurrenceRule); return rec.HasValue ? rec.Value.NextAfter(r.DueLocal, now) : (DateTime?)null; }
    }

    internal enum RevisionReason { Edit = 0, BulkReplace = 1 }

    /// <summary>
    /// 덮어쓰기 전 본문을 언제 남길지. 타이핑마다 남기면 탐색할 수 없는 잡음이라 일반 입력은 5분 간격으로만,
    /// 글을 통째로 바꾸는 일(기록에서 복원 등)은 늘 남긴다. 메모당 50개까지.
    /// </summary>
    internal sealed class RevisionPolicy
    {
        internal const int KeepPerNote = 50;
        internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private readonly Dictionary<Guid, DateTime> last = new Dictionary<Guid, DateTime>();

        internal bool ShouldSnapshot(Guid noteId, string previousContent, DateTime now, RevisionReason reason)
        {
            if (string.IsNullOrEmpty(previousContent)) return false;
            if (reason == RevisionReason.BulkReplace) return true;
            DateTime t; return !last.TryGetValue(noteId, out t) || now - t >= Interval;
        }
        internal void MarkTaken(Guid noteId, DateTime now) { last[noteId] = now; }
        internal void Forget(Guid noteId) { last.Remove(noteId); }
    }

    internal enum StartupNoteAction { Nothing, OpenMostRecent, CreateFirstNote }

    /// <summary>켤 때 바탕화면에 무엇이 있어야 하는지: 메모가 있으면 그대로, 열린 게 없으면 마지막 메모, 아무것도 없으면 빈 메모 하나.</summary>
    internal static class StartupNotePolicy
    {
        internal static StartupNoteAction Decide(int openWindowCount, int liveNoteCount)
        {
            if (openWindowCount > 0) return StartupNoteAction.Nothing;
            return liveNoteCount > 0 ? StartupNoteAction.OpenMostRecent : StartupNoteAction.CreateFirstNote;
        }
    }
}
