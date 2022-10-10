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
    class TextModel
    {
        List<List<Rune>> lines = new List<List<Rune>>();

        public event Action LinesLoaded;

        public bool LoadFile(string file)
        {
            FilePath = file ?? throw new ArgumentNullException(nameof(file));

            var stream = File.OpenRead(file);
            LoadStream(stream);
            return true;
        }

        public bool CloseFile()
        {
            if (FilePath == null)
                throw new ArgumentNullException(nameof(FilePath));

            FilePath = null;
            lines = new List<List<Rune>>();
            return true;
        }

        // Turns the ustring into runes, this does not split the 
        // contents on a newline if it is present.
        internal static List<Rune> ToRunes(ustring str)
        {
            List<Rune> runes = new List<Rune>();
            foreach (var x in str.ToRunes())
            {
                runes.Add(x);
            }
            return runes;
        }

        // Splits a string into a List that contains a List<Rune> for each line
        public static List<List<Rune>> StringToRunes(ustring content)
        {
            var lines = new List<List<Rune>>();
            int start = 0, i = 0;
            var hasCR = false;
            // ASCII code 13 = Carriage Return.
            // ASCII code 10 = Line Feed.
            for (; i < content.Length; i++)
            {
                if (content [i] == 13)
                {
                    hasCR = true;
                    continue;
                }
                if (content [i] == 10)
                {
                    if (i - start > 0)
                        lines.Add(ToRunes(content [start, hasCR ? i - 1 : i]));
                    else
                        lines.Add(ToRunes(ustring.Empty));
                    start = i + 1;
                    hasCR = false;
                }
            }
            if (i - start >= 0)
                lines.Add(ToRunes(content [start, null]));
            return lines;
        }

        void Append(List<byte> line)
        {
            var str = ustring.Make(line.ToArray());
            lines.Add(ToRunes(str));
        }

        public void LoadStream(Stream input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            lines = new List<List<Rune>>();
            var buff = new BufferedStream(input);
            int v;
            var line = new List<byte>();
            var wasNewLine = false;
            while ((v = buff.ReadByte()) != -1)
            {
                if (v == 13)
                {
                    continue;
                }
                if (v == 10)
                {
                    Append(line);
                    line.Clear();
                    wasNewLine = true;
                    continue;
                }
                line.Add((byte)v);
                wasNewLine = false;
            }
            if (line.Count > 0 || wasNewLine)
                Append(line);
            buff.Dispose();

            OnLinesLoaded();
        }

        public void LoadString(ustring content)
        {
            lines = StringToRunes(content);

            OnLinesLoaded();
        }

        void OnLinesLoaded()
        {
            LinesLoaded?.Invoke();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(ustring.Make(lines [i]));
                if ((i + 1) < lines.Count)
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        public string FilePath { get; set; }

        /// <summary>
        /// The number of text lines in the model
        /// </summary>
        public int Count => lines.Count;

        /// <summary>
        /// Returns the specified line as a List of Rune
        /// </summary>
        /// <returns>The line.</returns>
        /// <param name="line">Line number to retrieve.</param>
        public List<Rune> GetLine(int line)
        {
            if (lines.Count > 0)
            {
                if (line < Count)
                {
                    return lines [line];
                } else
                {
                    return lines [Count - 1];
                }
            } else
            {
                lines.Add(new List<Rune>());
                return lines [0];
            }
        }

        /// <summary>
        /// Adds a line to the model at the specified position.
        /// </summary>
        /// <param name="pos">Line number where the line will be inserted.</param>
        /// <param name="runes">The line of text, as a List of Rune.</param>
        public void AddLine(int pos, List<Rune> runes)
        {
            lines.Insert(pos, runes);
        }

        /// <summary>
        /// Removes the line at the specified position
        /// </summary>
        /// <param name="pos">Position.</param>
        public void RemoveLine(int pos)
        {
            if (lines.Count > 0)
            {
                if (lines.Count == 1 && lines [0].Count == 0)
                {
                    return;
                }
                lines.RemoveAt(pos);
            }
        }

        public void ReplaceLine(int pos, List<Rune> runes)
        {
            if (lines.Count > 0 && pos < lines.Count)
            {
                lines [pos] = new List<Rune>(runes);
            } else if (lines.Count == 0 || (lines.Count > 0 && pos >= lines.Count))
            {
                lines.Add(runes);
            }
        }

        /// <summary>
        /// Returns the maximum line length of the visible lines.
        /// </summary>
        /// <param name="first">The first line.</param>
        /// <param name="last">The last line.</param>
        /// <param name="tabWidth">The tab width.</param>
        public int GetMaxVisibleLine(int first, int last, int tabWidth)
        {
            int maxLength = 0;
            last = last < lines.Count ? last : lines.Count;
            for (int i = first; i < last; i++)
            {
                var line = GetLine(i);
                var tabSum = line.Sum(r => r == '\t' ? Math.Max(tabWidth - 1, 0) : 0);
                var l = line.Count + tabSum;
                if (l > maxLength)
                {
                    maxLength = l;
                }
            }

            return maxLength;
        }

        internal static bool SetCol(ref int col, int width, int cols)
        {
            if (col + cols <= width)
            {
                col += cols;
                return true;
            }

            return false;
        }

        internal static int GetColFromX(List<Rune> t, int start, int x, int tabWidth = 0)
        {
            if (x < 0)
            {
                return x;
            }
            int size = start;
            var pX = x + start;
            for (int i = start; i < t.Count; i++)
            {
                var r = t [i];
                size += Rune.ColumnWidth(r);
                if (r == '\t')
                {
                    size += tabWidth + 1;
                }
                if (i == pX || (size > pX))
                {
                    return i - start;
                }
            }
            return t.Count - start;
        }

        // Returns the size and length in a range of the string.
        internal static (int size, int length) DisplaySize(List<Rune> t, int start = -1, int end = -1,
            bool checkNextRune = true, int tabWidth = 0)
        {
            if (t == null || t.Count == 0)
            {
                return (0, 0);
            }
            int size = 0;
            int len = 0;
            int tcount = end == -1 ? t.Count : end > t.Count ? t.Count : end;
            int i = start == -1 ? 0 : start;
            for (; i < tcount; i++)
            {
                var rune = t [i];
                size += Rune.ColumnWidth(rune);
                len += Rune.RuneLen(rune);
                if (rune == '\t')
                {
                    size += tabWidth + 1;
                    len += tabWidth - 1;
                }
                if (checkNextRune && i == tcount - 1 && t.Count > tcount
                    && IsWideRune(t [i + 1], tabWidth, out int s, out int l))
                {
                    size += s;
                    len += l;
                }
            }

            bool IsWideRune(Rune r, int tWidth, out int s, out int l)
            {
                s = Rune.ColumnWidth(r);
                l = Rune.RuneLen(r);
                if (r == '\t')
                {
                    s += tWidth + 1;
                    l += tWidth - 1;
                }

                return s > 1;
            }

            return (size, len);
        }

        // Returns the left column in a range of the string.
        internal static int CalculateLeftColumn(List<Rune> t, int start, int end, int width, int tabWidth = 0)
        {
            if (t == null || t.Count == 0)
            {
                return 0;
            }
            int size = 0;
            int tcount = end > t.Count - 1 ? t.Count - 1 : end;
            int col = 0;

            for (int i = tcount; i >= 0; i--)
            {
                var rune = t [i];
                size += Rune.ColumnWidth(rune);
                if (rune == '\t')
                {
                    size += tabWidth + 1;
                }
                if (size > width)
                {
                    if (col + width == end)
                    {
                        col++;
                    }
                    break;
                } else if ((end < t.Count && col > 0 && start < end && col == start) || (end - col == width - 1))
                {
                    break;
                }
                col = i;
            }

            return col;
        }

        (Point startPointToFind, Point currentPointToFind, bool found) toFind;

        internal (Point current, bool found) FindNextText(ustring text, out bool gaveFullTurn, bool matchCase = false, bool matchWholeWord = false)
        {
            if (text == null || lines.Count == 0)
            {
                gaveFullTurn = false;
                return (Point.Empty, false);
            }

            if (toFind.found)
            {
                toFind.currentPointToFind.X++;
            }
            var foundPos = GetFoundNextTextPoint(text, lines.Count, matchCase, matchWholeWord, toFind.currentPointToFind);
            if (!foundPos.found && toFind.currentPointToFind != toFind.startPointToFind)
            {
                foundPos = GetFoundNextTextPoint(text, toFind.startPointToFind.Y + 1, matchCase, matchWholeWord, Point.Empty);
            }
            gaveFullTurn = ApplyToFind(foundPos);

            return foundPos;
        }

        internal (Point current, bool found) FindPreviousText(ustring text, out bool gaveFullTurn, bool matchCase = false, bool matchWholeWord = false)
        {
            if (text == null || lines.Count == 0)
            {
                gaveFullTurn = false;
                return (Point.Empty, false);
            }

            if (toFind.found)
            {
                toFind.currentPointToFind.X++;
            }
            var linesCount = toFind.currentPointToFind.IsEmpty ? lines.Count - 1 : toFind.currentPointToFind.Y;
            var foundPos = GetFoundPreviousTextPoint(text, linesCount, matchCase, matchWholeWord, toFind.currentPointToFind);
            if (!foundPos.found && toFind.currentPointToFind != toFind.startPointToFind)
            {
                foundPos = GetFoundPreviousTextPoint(text, lines.Count - 1, matchCase, matchWholeWord,
                    new Point(lines [lines.Count - 1].Count, lines.Count));
            }
            gaveFullTurn = ApplyToFind(foundPos);

            return foundPos;
        }

        internal (Point current, bool found) ReplaceAllText(ustring text, bool matchCase = false, bool matchWholeWord = false, ustring textToReplace = null)
        {
            bool found = false;
            Point pos = Point.Empty;

            for (int i = 0; i < lines.Count; i++)
            {
                var x = lines [i];
                var txt = GetText(x);
                var matchText = !matchCase ? text.ToUpper().ToString() : text.ToString();
                var col = txt.IndexOf(matchText);
                while (col > -1)
                {
                    if (matchWholeWord && !MatchWholeWord(txt, matchText, col))
                    {
                        if (col + 1 > txt.Length)
                        {
                            break;
                        }
                        col = txt.IndexOf(matchText, col + 1);
                        continue;
                    }
                    if (col > -1)
                    {
                        if (!found)
                        {
                            found = true;
                        }
                        lines [i] = ReplaceText(x, textToReplace, matchText, col).ToRuneList();
                        x = lines [i];
                        txt = GetText(x);
                        pos = new Point(col, i);
                        col += (textToReplace.Length - matchText.Length);
                    }
                    if (col + 1 > txt.Length)
                    {
                        break;
                    }
                    col = txt.IndexOf(matchText, col + 1);
                }
            }

            string GetText(List<Rune> x)
            {
                var txt = ustring.Make(x).ToString();
                if (!matchCase)
                {
                    txt = txt.ToUpper();
                }
                return txt;
            }

            return (pos, found);
        }

        ustring ReplaceText(List<Rune> source, ustring textToReplace, string matchText, int col)
        {
            var origTxt = ustring.Make(source);
            (int _, int len) = TextModel.DisplaySize(source, 0, col, false);
            (var _, var len2) = TextModel.DisplaySize(source, col, col + matchText.Length, false);
            (var _, var len3) = TextModel.DisplaySize(source, col + matchText.Length, origTxt.RuneCount, false);

            return origTxt [0, len] +
                textToReplace.ToString() +
                origTxt [len + len2, len + len2 + len3];
        }

        bool ApplyToFind((Point current, bool found) foundPos)
        {
            bool gaveFullTurn = false;
            if (foundPos.found)
            {
                toFind.currentPointToFind = foundPos.current;
                if (toFind.found && toFind.currentPointToFind == toFind.startPointToFind)
                {
                    gaveFullTurn = true;
                }
                if (!toFind.found)
                {
                    toFind.startPointToFind = toFind.currentPointToFind = foundPos.current;
                    toFind.found = foundPos.found;
                }
            }

            return gaveFullTurn;
        }

        (Point current, bool found) GetFoundNextTextPoint(ustring text, int linesCount, bool matchCase, bool matchWholeWord, Point start)
        {
            for (int i = start.Y; i < linesCount; i++)
            {
                var x = lines [i];
                var txt = ustring.Make(x).ToString();
                if (!matchCase)
                {
                    txt = txt.ToUpper();
                }
                var matchText = !matchCase ? text.ToUpper().ToString() : text.ToString();
                var col = txt.IndexOf(matchText, Math.Min(start.X, txt.Length));
                if (col > -1 && matchWholeWord && !MatchWholeWord(txt, matchText, col))
                {
                    continue;
                }
                if (col > -1 && ((i == start.Y && col >= start.X)
                    || i > start.Y)
                    && txt.Contains(matchText))
                {
                    return (new Point(col, i), true);
                } else if (col == -1 && start.X > 0)
                {
                    start.X = 0;
                }
            }

            return (Point.Empty, false);
        }

        (Point current, bool found) GetFoundPreviousTextPoint(ustring text, int linesCount, bool matchCase, bool matchWholeWord, Point start)
        {
            for (int i = linesCount; i >= 0; i--)
            {
                var x = lines [i];
                var txt = ustring.Make(x).ToString();
                if (!matchCase)
                {
                    txt = txt.ToUpper();
                }
                if (start.Y != i)
                {
                    start.X = Math.Max(x.Count - 1, 0);
                }
                var matchText = !matchCase ? text.ToUpper().ToString() : text.ToString();
                var col = txt.LastIndexOf(matchText, toFind.found ? start.X - 1 : start.X);
                if (col > -1 && matchWholeWord && !MatchWholeWord(txt, matchText, col))
                {
                    continue;
                }
                if (col > -1 && ((i <= linesCount && col <= start.X)
                    || i < start.Y)
                    && txt.Contains(matchText))
                {
                    return (new Point(col, i), true);
                }
            }

            return (Point.Empty, false);
        }

        bool MatchWholeWord(string source, string matchText, int index = 0)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(matchText))
            {
                return false;
            }

            var txt = matchText.Trim();
            var start = index > 0 ? index - 1 : 0;
            var end = index + txt.Length;

            if ((start == 0 || Rune.IsWhiteSpace(source [start]))
                && (end == source.Length || Rune.IsWhiteSpace(source [end])))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Redefine column and line tracking.
        /// </summary>
        /// <param name="point">Contains the column and line.</param>
        internal void ResetContinuousFind(Point point)
        {
            toFind.startPointToFind = toFind.currentPointToFind = point;
            toFind.found = false;
        }
    }

}
