using System;
using System.Collections.Generic;
using System.Linq;

namespace BikiniFriends
{
    internal sealed class NoteAttachment
    {
        internal Guid Id; internal string Path, DisplayName; internal bool Embedded; internal long Size;
    }

    internal sealed class Note
    {
        internal const double MinOpacity = 0.35;
        internal Guid Id;
        internal string Content = "", ColorKey = NoteColors.Default;
        internal double Opacity = 1.0;
        internal bool AlwaysOnTop, IsOpen = true;
        internal NoteGeometry Geometry = NoteGeometry.ForPreset(NoteSizePreset.Medium, 0, 0, "");
        internal Guid? NotebookId;
        internal DateTime CreatedUtc, UpdatedUtc;
        internal long Rev = 1;
        internal List<NoteAttachment> Attachments = new List<NoteAttachment>();

        // 본문에서 읽어 둔 값(저장·불러올 때 다시 계산한다).
        internal string Title = "";
        internal int ChecklistDone, ChecklistTotal;
        internal List<string> Tags = new List<string>();
        /// <summary>글도 첨부도 없는 메모. 빈 메모는 저장하지 않고 라이브러리에도 올리지 않는다.</summary>
        internal bool IsBlank { get { return string.IsNullOrWhiteSpace(Content) && Attachments.Count == 0; } }

        internal void Derive()
        {
            Title = NoteContent.DeriveTitle(Content);
            var items = ChecklistParser.Parse(Content);
            ChecklistTotal = items.Count; ChecklistDone = items.Count(i => i.IsDone);
            Tags = TagParser.Parse(Content).Select(t => t.Name).ToList();
        }
    }

    internal sealed class Notebook { internal Guid Id; internal string Name; internal int Ordinal; internal DateTime CreatedUtc; }

    internal sealed class NoteReminder
    {
        internal Guid Id, NoteId; internal DateTime DueLocal; internal string RecurrenceRule;
        internal DateTime? CompletedLocal, NotifiedLocal;
        internal bool IsCompleted { get { return CompletedLocal.HasValue; } }
    }

    internal enum RevisionSource { User = 0 }
    internal sealed class NoteRevision { internal Guid Id, NoteId; internal string Content; internal DateTime CreatedUtc; internal RevisionSource Source; }
}
