using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using NStack;
using Terminal.Gui.Resources;
using Terminal.Gui;
using Rune = System.Rune;
using Attribute = Terminal.Gui.Attribute;

namespace ConDbgX.Text
{
    class HistoryText
    {
        public enum LineStatus
        {
            Original,
            Replaced,
            Removed,
            Added
        }

        public class HistoryTextItem
        {
            public List<List<Rune>> Lines;
            public Point CursorPosition;
            public LineStatus LineStatus;
            public bool IsUndoing;
            public Point FinalCursorPosition;
            public HistoryTextItem RemovedOnAdded;

            public HistoryTextItem(List<List<Rune>> lines, Point curPos, LineStatus linesStatus)
            {
                Lines = lines;
                CursorPosition = curPos;
                LineStatus = linesStatus;
            }

            public HistoryTextItem(HistoryTextItem historyTextItem)
            {
                Lines = new List<List<Rune>>(historyTextItem.Lines);
                CursorPosition = new Point(historyTextItem.CursorPosition.X, historyTextItem.CursorPosition.Y);
                LineStatus = historyTextItem.LineStatus;
            }

            public override string ToString()
            {
                return $"(Count: {Lines.Count}, Cursor: {CursorPosition}, Status: {LineStatus})";
            }
        }

        List<HistoryTextItem> historyTextItems = new List<HistoryTextItem>();
        int idxHistoryText = -1;
        ustring originalText;

        public bool IsFromHistory { get; private set; }

        public bool HasHistoryChanges => idxHistoryText > -1;

        public event Action<HistoryTextItem> ChangeText;

        public void Add(List<List<Rune>> lines, Point curPos, LineStatus lineStatus = LineStatus.Original)
        {
            if (lineStatus == LineStatus.Original && historyTextItems.Count > 0
                && historyTextItems.Last().LineStatus == LineStatus.Original)
            {
                return;
            }
            if (lineStatus == LineStatus.Replaced && historyTextItems.Count > 0
                && historyTextItems.Last().LineStatus == LineStatus.Replaced)
            {
                return;
            }

            if (historyTextItems.Count == 0 && lineStatus != LineStatus.Original)
                throw new ArgumentException("The first item must be the original.");

            if (idxHistoryText >= 0 && idxHistoryText + 1 < historyTextItems.Count)
                historyTextItems.RemoveRange(idxHistoryText + 1, historyTextItems.Count - idxHistoryText - 1);

            historyTextItems.Add(new HistoryTextItem(lines, curPos, lineStatus));
            idxHistoryText++;
        }

        public void ReplaceLast(List<List<Rune>> lines, Point curPos, LineStatus lineStatus)
        {
            var found = historyTextItems.FindLast(x => x.LineStatus == lineStatus);
            if (found != null)
            {
                found.Lines = lines;
                found.CursorPosition = curPos;
            }
        }

        public void Undo()
        {
            if (historyTextItems?.Count > 0 && idxHistoryText > 0)
            {
                IsFromHistory = true;

                idxHistoryText--;

                var historyTextItem = new HistoryTextItem(historyTextItems [idxHistoryText])
                {
                    IsUndoing = true
                };

                ProcessChanges(ref historyTextItem);

                IsFromHistory = false;
            }
        }

        public void Redo()
        {
            if (historyTextItems?.Count > 0 && idxHistoryText < historyTextItems.Count - 1)
            {
                IsFromHistory = true;

                idxHistoryText++;

                var historyTextItem = new HistoryTextItem(historyTextItems [idxHistoryText])
                {
                    IsUndoing = false
                };

                ProcessChanges(ref historyTextItem);

                IsFromHistory = false;
            }
        }

        void ProcessChanges(ref HistoryTextItem historyTextItem)
        {
            if (historyTextItem.IsUndoing)
            {
                if (idxHistoryText - 1 > -1 && ((historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Added)
                    || historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Removed
                    || (historyTextItem.LineStatus == LineStatus.Replaced &&
                    historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Original)))
                {

                    idxHistoryText--;

                    while (historyTextItems [idxHistoryText].LineStatus == LineStatus.Added
                        && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Removed)
                    {

                        idxHistoryText--;
                    }
                    historyTextItem = new HistoryTextItem(historyTextItems [idxHistoryText]);
                    historyTextItem.IsUndoing = true;
                    historyTextItem.FinalCursorPosition = historyTextItem.CursorPosition;
                }

                if (historyTextItem.LineStatus == LineStatus.Removed && historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Added)
                {
                    historyTextItem.RemovedOnAdded = new HistoryTextItem(historyTextItems [idxHistoryText + 1]);
                }

                if ((historyTextItem.LineStatus == LineStatus.Added && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Original)
                    || (historyTextItem.LineStatus == LineStatus.Removed && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Original)
                    || (historyTextItem.LineStatus == LineStatus.Added && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Removed))
                {

                    if (!historyTextItem.Lines [0].SequenceEqual(historyTextItems [idxHistoryText - 1].Lines [0])
                        && historyTextItem.CursorPosition == historyTextItems [idxHistoryText - 1].CursorPosition)
                    {
                        historyTextItem.Lines [0] = new List<Rune>(historyTextItems [idxHistoryText - 1].Lines [0]);
                    }
                    if (historyTextItem.LineStatus == LineStatus.Added && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Removed)
                    {
                        historyTextItem.FinalCursorPosition = historyTextItems [idxHistoryText - 2].CursorPosition;
                    } else
                    {
                        historyTextItem.FinalCursorPosition = historyTextItems [idxHistoryText - 1].CursorPosition;
                    }
                } else
                {
                    historyTextItem.FinalCursorPosition = historyTextItem.CursorPosition;
                }

                OnChangeText(historyTextItem);
                while (historyTextItems [idxHistoryText].LineStatus == LineStatus.Removed
                    || historyTextItems [idxHistoryText].LineStatus == LineStatus.Added)
                {

                    idxHistoryText--;
                }
            } else if (!historyTextItem.IsUndoing)
            {
                if (idxHistoryText + 1 < historyTextItems.Count && (historyTextItem.LineStatus == LineStatus.Original
                    || historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Added
                    || historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Removed))
                {

                    idxHistoryText++;
                    historyTextItem = new HistoryTextItem(historyTextItems [idxHistoryText]);
                    historyTextItem.IsUndoing = false;
                    historyTextItem.FinalCursorPosition = historyTextItem.CursorPosition;
                }

                if (historyTextItem.LineStatus == LineStatus.Added && historyTextItems [idxHistoryText - 1].LineStatus == LineStatus.Removed)
                {
                    historyTextItem.RemovedOnAdded = new HistoryTextItem(historyTextItems [idxHistoryText - 1]);
                }

                if ((historyTextItem.LineStatus == LineStatus.Removed && historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Replaced)
                    || (historyTextItem.LineStatus == LineStatus.Removed && historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Original)
                    || (historyTextItem.LineStatus == LineStatus.Added && historyTextItems [idxHistoryText + 1].LineStatus == LineStatus.Replaced))
                {

                    if (historyTextItem.LineStatus == LineStatus.Removed
                        && !historyTextItem.Lines [0].SequenceEqual(historyTextItems [idxHistoryText + 1].Lines [0]))
                    {
                        historyTextItem.Lines [0] = new List<Rune>(historyTextItems [idxHistoryText + 1].Lines [0]);
                    }
                    historyTextItem.FinalCursorPosition = historyTextItems [idxHistoryText + 1].CursorPosition;
                } else
                {
                    historyTextItem.FinalCursorPosition = historyTextItem.CursorPosition;
                }

                OnChangeText(historyTextItem);
                while (historyTextItems [idxHistoryText].LineStatus == LineStatus.Removed
                    || historyTextItems [idxHistoryText].LineStatus == LineStatus.Added)
                {

                    idxHistoryText++;
                }
            }
        }

        void OnChangeText(HistoryTextItem lines)
        {
            ChangeText?.Invoke(lines);
        }

        public void Clear(ustring text)
        {
            historyTextItems.Clear();
            idxHistoryText = -1;
            originalText = text;
        }

        public bool IsDirty(ustring text)
        {
            return originalText != text;
        }
    }

}
