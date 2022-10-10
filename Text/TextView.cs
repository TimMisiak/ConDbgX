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

// The classes here are adapted from Terminal.Gui.TextModel because there are a few pieces of functionality
// needed that are not in the Terminal.Gui version.
// Specifically, the TextModel is more extensible here, allowing things like colors.

namespace ConDbgX.Text
{

    /// <summary>
    ///   Multi-line text editing <see cref="View"/>
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     <see cref="TextView"/> provides a multi-line text editor. Users interact
    ///     with it with the standard Emacs commands for movement or the arrow
    ///     keys. 
    ///   </para> 
    ///   <list type="table"> 
    ///     <listheader>
    ///       <term>Shortcut</term>
    ///       <description>Action performed</description>
    ///     </listheader>
    ///     <item>
    ///        <term>Left cursor, Control-b</term>
    ///        <description>
    ///          Moves the editing point left.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Right cursor, Control-f</term>
    ///        <description>
    ///          Moves the editing point right.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Alt-b</term>
    ///        <description>
    ///          Moves one word back.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Alt-f</term>
    ///        <description>
    ///          Moves one word forward.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Up cursor, Control-p</term>
    ///        <description>
    ///          Moves the editing point one line up.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Down cursor, Control-n</term>
    ///        <description>
    ///          Moves the editing point one line down
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Home key, Control-a</term>
    ///        <description>
    ///          Moves the cursor to the beginning of the line.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>End key, Control-e</term>
    ///        <description>
    ///          Moves the cursor to the end of the line.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Control-Home</term>
    ///        <description>
    ///          Scrolls to the first line and moves the cursor there.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Control-End</term>
    ///        <description>
    ///          Scrolls to the last line and moves the cursor there.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Delete, Control-d</term>
    ///        <description>
    ///          Deletes the character in front of the cursor.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Backspace</term>
    ///        <description>
    ///          Deletes the character behind the cursor.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Control-k</term>
    ///        <description>
    ///          Deletes the text until the end of the line and replaces the kill buffer
    ///          with the deleted text.   You can paste this text in a different place by
    ///          using Control-y.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Control-y</term>
    ///        <description>
    ///           Pastes the content of the kill ring into the current position.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Alt-d</term>
    ///        <description>
    ///           Deletes the word above the cursor and adds it to the kill ring.  You 
    ///           can paste the contents of the kill ring with Control-y.
    ///        </description>
    ///     </item>
    ///     <item>
    ///        <term>Control-q</term>
    ///        <description>
    ///          Quotes the next input character, to prevent the normal processing of
    ///          key handling to take place.
    ///        </description>
    ///     </item>
    ///   </list>
    /// </remarks>
    public class TextView : View
    {
        TextModel model = new TextModel();
        int topRow;
        int leftColumn;
        int currentRow;
        int currentColumn;
        int selectionStartColumn, selectionStartRow;
        bool selecting;
        bool continuousFind;
        int bottomOffset, rightOffset;
        int tabWidth = 4;
        bool allowsTab = true;
        bool allowsReturn = true;
        bool multiline = true;
        HistoryText historyText = new HistoryText();
        CultureInfo currentCulture;

        /// <summary>
        /// Raised when the <see cref="Text"/> of the <see cref="TextView"/> changes.
        /// </summary>
        public event Action TextChanged;

        /// <summary>
        /// Invoked with the unwrapped <see cref="CursorPosition"/>.
        /// </summary>
        public event Action<Point> UnwrappedCursorPosition;

        /// <summary>
        /// Provides autocomplete context menu based on suggestions at the current cursor
        /// position.  Populate <see cref="Autocomplete.AllSuggestions"/> to enable this feature
        /// </summary>
        public IAutocomplete Autocomplete { get; protected set; } = new TextViewAutocomplete();

#if false
		/// <summary>
		///   Changed event, raised when the text has clicked.
		/// </summary>
		/// <remarks>
		///   Client code can hook up to this event, it is
		///   raised when the text in the entry changes.
		/// </remarks>
		public Action Changed;
#endif
        /// <summary>
        ///   Initializes a <see cref="TextView"/> on the specified area, with absolute position and size.
        /// </summary>
        /// <remarks>
        /// </remarks>
        public TextView(Rect frame) : base(frame)
        {
            Initialize();
        }

        /// <summary>
        ///   Initializes a <see cref="TextView"/> on the specified area, 
        ///   with dimensions controlled with the X, Y, Width and Height properties.
        /// </summary>
        public TextView() : base()
        {
            Initialize();
        }

        void Initialize()
        {
            CanFocus = true;
            Used = true;

            model.LinesLoaded += Model_LinesLoaded;
            historyText.ChangeText += HistoryText_ChangeText;

            Initialized += TextView_Initialized;

            // Things this view knows how to do
            AddCommand(Command.PageDown, () => { ProcessPageDown(); return true; });
            AddCommand(Command.PageDownExtend, () => { ProcessPageDownExtend(); return true; });
            AddCommand(Command.PageUp, () => { ProcessPageUp(); return true; });
            AddCommand(Command.PageUpExtend, () => { ProcessPageUpExtend(); return true; });
            AddCommand(Command.LineDown, () => { ProcessMoveDown(); return true; });
            AddCommand(Command.LineDownExtend, () => { ProcessMoveDownExtend(); return true; });
            AddCommand(Command.LineUp, () => { ProcessMoveUp(); return true; });
            AddCommand(Command.LineUpExtend, () => { ProcessMoveUpExtend(); return true; });
            AddCommand(Command.Right, () => ProcessMoveRight());
            AddCommand(Command.RightExtend, () => { ProcessMoveRightExtend(); return true; });
            AddCommand(Command.Left, () => ProcessMoveLeft());
            AddCommand(Command.LeftExtend, () => { ProcessMoveLeftExtend(); return true; });
            AddCommand(Command.DeleteCharLeft, () => { ProcessDeleteCharLeft(); return true; });
            AddCommand(Command.StartOfLine, () => { ProcessMoveStartOfLine(); return true; });
            AddCommand(Command.StartOfLineExtend, () => { ProcessMoveStartOfLineExtend(); return true; });
            AddCommand(Command.DeleteCharRight, () => { ProcessDeleteCharRight(); return true; });
            AddCommand(Command.EndOfLine, () => { ProcessMoveEndOfLine(); return true; });
            AddCommand(Command.EndOfLineExtend, () => { ProcessMoveEndOfLineExtend(); return true; });
            AddCommand(Command.CutToEndLine, () => { KillToEndOfLine(); return true; });
            AddCommand(Command.CutToStartLine, () => { KillToStartOfLine(); return true; });
            AddCommand(Command.Paste, () => { ProcessPaste(); return true; });
            AddCommand(Command.ToggleExtend, () => { ToggleSelecting(); return true; });
            AddCommand(Command.Copy, () => { ProcessCopy(); return true; });
            AddCommand(Command.Cut, () => { ProcessCut(); return true; });
            AddCommand(Command.WordLeft, () => { ProcessMoveWordBackward(); return true; });
            AddCommand(Command.WordLeftExtend, () => { ProcessMoveWordBackwardExtend(); return true; });
            AddCommand(Command.WordRight, () => { ProcessMoveWordForward(); return true; });
            AddCommand(Command.WordRightExtend, () => { ProcessMoveWordForwardExtend(); return true; });
            AddCommand(Command.KillWordForwards, () => { ProcessKillWordForward(); return true; });
            AddCommand(Command.KillWordBackwards, () => { ProcessKillWordBackward(); return true; });
            AddCommand(Command.NewLine, () => ProcessReturn());
            AddCommand(Command.BottomEnd, () => { MoveBottomEnd(); return true; });
            AddCommand(Command.BottomEndExtend, () => { MoveBottomEndExtend(); return true; });
            AddCommand(Command.TopHome, () => { MoveTopHome(); return true; });
            AddCommand(Command.TopHomeExtend, () => { MoveTopHomeExtend(); return true; });
            AddCommand(Command.SelectAll, () => { ProcessSelectAll(); return true; });
            AddCommand(Command.ToggleOverwrite, () => { ProcessSetOverwrite(); return true; });
            AddCommand(Command.EnableOverwrite, () => { SetOverwrite(true); return true; });
            AddCommand(Command.DisableOverwrite, () => { SetOverwrite(false); return true; });
            AddCommand(Command.Tab, () => ProcessTab());
            AddCommand(Command.BackTab, () => ProcessBackTab());
            AddCommand(Command.NextView, () => ProcessMoveNextView());
            AddCommand(Command.PreviousView, () => ProcessMovePreviousView());
            AddCommand(Command.Undo, () => { UndoChanges(); return true; });
            AddCommand(Command.Redo, () => { RedoChanges(); return true; });
            AddCommand(Command.DeleteAll, () => { DeleteAll(); return true; });
            AddCommand(Command.Accept, () =>
            {
                ContextMenu.Position = new Point(CursorPosition.X - leftColumn + 2, CursorPosition.Y - topRow + 2);
                ShowContextMenu();
                return true;
            });

            // Default keybindings for this view
            AddKeyBinding(Key.PageDown, Command.PageDown);
            AddKeyBinding(Key.V | Key.CtrlMask, Command.PageDown);

            AddKeyBinding(Key.PageDown | Key.ShiftMask, Command.PageDownExtend);

            AddKeyBinding(Key.PageUp, Command.PageUp);
            AddKeyBinding(((int)'V' + Key.AltMask), Command.PageUp);

            AddKeyBinding(Key.PageUp | Key.ShiftMask, Command.PageUpExtend);

            AddKeyBinding(Key.N | Key.CtrlMask, Command.LineDown);
            AddKeyBinding(Key.CursorDown, Command.LineDown);

            AddKeyBinding(Key.CursorDown | Key.ShiftMask, Command.LineDownExtend);

            AddKeyBinding(Key.P | Key.CtrlMask, Command.LineUp);
            AddKeyBinding(Key.CursorUp, Command.LineUp);

            AddKeyBinding(Key.CursorUp | Key.ShiftMask, Command.LineUpExtend);

            AddKeyBinding(Key.F | Key.CtrlMask, Command.Right);
            AddKeyBinding(Key.CursorRight, Command.Right);

            AddKeyBinding(Key.CursorRight | Key.ShiftMask, Command.RightExtend);

            AddKeyBinding(Key.B | Key.CtrlMask, Command.Left);
            AddKeyBinding(Key.CursorLeft, Command.Left);

            AddKeyBinding(Key.CursorLeft | Key.ShiftMask, Command.LeftExtend);

            AddKeyBinding(Key.Delete, Command.DeleteCharLeft);
            AddKeyBinding(Key.Backspace, Command.DeleteCharLeft);

            AddKeyBinding(Key.Home, Command.StartOfLine);
            AddKeyBinding(Key.A | Key.CtrlMask, Command.StartOfLine);

            AddKeyBinding(Key.Home | Key.ShiftMask, Command.StartOfLineExtend);

            AddKeyBinding(Key.DeleteChar, Command.DeleteCharRight);
            AddKeyBinding(Key.D | Key.CtrlMask, Command.DeleteCharRight);

            AddKeyBinding(Key.End, Command.EndOfLine);
            AddKeyBinding(Key.E | Key.CtrlMask, Command.EndOfLine);

            AddKeyBinding(Key.End | Key.ShiftMask, Command.EndOfLineExtend);

            AddKeyBinding(Key.K | Key.CtrlMask, Command.CutToEndLine); // kill-to-end
            AddKeyBinding(Key.DeleteChar | Key.CtrlMask | Key.ShiftMask, Command.CutToEndLine); // kill-to-end

            AddKeyBinding(Key.K | Key.AltMask, Command.CutToStartLine); // kill-to-start
            AddKeyBinding(Key.Backspace | Key.CtrlMask | Key.ShiftMask, Command.CutToStartLine); // kill-to-start

            AddKeyBinding(Key.Y | Key.CtrlMask, Command.Paste); // Control-y, yank
            AddKeyBinding(Key.Space | Key.CtrlMask, Command.ToggleExtend);

            AddKeyBinding(((int)'C' + Key.AltMask), Command.Copy);
            AddKeyBinding(Key.C | Key.CtrlMask, Command.Copy);

            AddKeyBinding(((int)'W' + Key.AltMask), Command.Cut);
            AddKeyBinding(Key.W | Key.CtrlMask, Command.Cut);
            AddKeyBinding(Key.X | Key.CtrlMask, Command.Cut);

            AddKeyBinding(Key.CursorLeft | Key.CtrlMask, Command.WordLeft);
            AddKeyBinding((Key)((int)'B' + Key.AltMask), Command.WordLeft);

            AddKeyBinding(Key.CursorLeft | Key.CtrlMask | Key.ShiftMask, Command.WordLeftExtend);

            AddKeyBinding(Key.CursorRight | Key.CtrlMask, Command.WordRight);
            AddKeyBinding((Key)((int)'F' + Key.AltMask), Command.WordRight);

            AddKeyBinding(Key.CursorRight | Key.CtrlMask | Key.ShiftMask, Command.WordRightExtend);
            AddKeyBinding(Key.DeleteChar | Key.CtrlMask, Command.KillWordForwards); // kill-word-forwards
            AddKeyBinding(Key.Backspace | Key.CtrlMask, Command.KillWordBackwards); // kill-word-backwards

            AddKeyBinding(Key.Enter, Command.NewLine);
            AddKeyBinding(Key.End | Key.CtrlMask, Command.BottomEnd);
            AddKeyBinding(Key.End | Key.CtrlMask | Key.ShiftMask, Command.BottomEndExtend);
            AddKeyBinding(Key.Home | Key.CtrlMask, Command.TopHome);
            AddKeyBinding(Key.Home | Key.CtrlMask | Key.ShiftMask, Command.TopHomeExtend);
            AddKeyBinding(Key.T | Key.CtrlMask, Command.SelectAll);
            AddKeyBinding(Key.InsertChar, Command.ToggleOverwrite);
            AddKeyBinding(Key.Tab, Command.Tab);
            AddKeyBinding(Key.BackTab | Key.ShiftMask, Command.BackTab);

            AddKeyBinding(Key.Tab | Key.CtrlMask, Command.NextView);
            AddKeyBinding(Application.AlternateForwardKey, Command.NextView);

            AddKeyBinding(Key.Tab | Key.CtrlMask | Key.ShiftMask, Command.PreviousView);
            AddKeyBinding(Application.AlternateBackwardKey, Command.PreviousView);

            AddKeyBinding(Key.Z | Key.CtrlMask, Command.Undo);
            AddKeyBinding(Key.R | Key.CtrlMask, Command.Redo);

            AddKeyBinding(Key.G | Key.CtrlMask, Command.DeleteAll);
            AddKeyBinding(Key.D | Key.CtrlMask | Key.ShiftMask, Command.DeleteAll);

            currentCulture = Thread.CurrentThread.CurrentUICulture;

            ContextMenu = new ContextMenu() { MenuItems = BuildContextMenuBarItem() };
            ContextMenu.KeyChanged += ContextMenu_KeyChanged;

            AddKeyBinding(ContextMenu.Key, Command.Accept);
        }

        private MenuBarItem BuildContextMenuBarItem()
        {
            return new MenuBarItem(new MenuItem [] {
                    new MenuItem ("_Select All", "", () => SelectAll (), null, null, GetKeyFromCommand (Command.SelectAll)),
                    new MenuItem ("_Delete All", "", () => DeleteAll (), null, null, GetKeyFromCommand (Command.DeleteAll)),
                    new MenuItem ("_Copy", "", () => Copy (), null, null, GetKeyFromCommand (Command.Copy)),
                    new MenuItem ("C_ut", "", () => Cut (), null, null, GetKeyFromCommand (Command.Cut)),
                    new MenuItem ("_Paste", "", () => Paste (), null, null, GetKeyFromCommand (Command.Paste)),
                    new MenuItem ("_Undo", "", () => UndoChanges (), null, null, GetKeyFromCommand (Command.Undo)),
                    new MenuItem ("_Redo", "", () => RedoChanges (), null, null, GetKeyFromCommand (Command.Redo)),
                });
        }

        private void ContextMenu_KeyChanged(Key obj)
        {
            ReplaceKeyBinding(obj, ContextMenu.Key);
        }

        private void Model_LinesLoaded()
        {
            historyText.Clear(Text);
        }

        private void HistoryText_ChangeText(HistoryText.HistoryTextItem obj)
        {
            SetWrapModel();

            var startLine = obj.CursorPosition.Y;

            if (obj.RemovedOnAdded != null)
            {
                int offset;
                if (obj.IsUndoing)
                {
                    offset = Math.Max(obj.RemovedOnAdded.Lines.Count - obj.Lines.Count, 1);
                } else
                {
                    offset = obj.RemovedOnAdded.Lines.Count - 1;
                }
                for (int i = 0; i < offset; i++)
                {
                    if (Lines > obj.RemovedOnAdded.CursorPosition.Y)
                    {
                        model.RemoveLine(obj.RemovedOnAdded.CursorPosition.Y);
                    } else
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < obj.Lines.Count; i++)
            {
                if (i == 0)
                {
                    model.ReplaceLine(startLine, obj.Lines [i]);
                } else if ((obj.IsUndoing && obj.LineStatus == HistoryText.LineStatus.Removed)
                        || !obj.IsUndoing && obj.LineStatus == HistoryText.LineStatus.Added)
                {
                    model.AddLine(startLine, obj.Lines [i]);
                } else if (Lines > obj.CursorPosition.Y + 1)
                {
                    model.RemoveLine(obj.CursorPosition.Y + 1);
                }
                startLine++;
            }

            CursorPosition = obj.FinalCursorPosition;

            UpdateWrapModel();

            Adjust();
        }

        void TextView_Initialized(object sender, EventArgs e)
        {
            Autocomplete.HostControl = this;

            Application.Top.AlternateForwardKeyChanged += Top_AlternateForwardKeyChanged;
            Application.Top.AlternateBackwardKeyChanged += Top_AlternateBackwardKeyChanged;
        }

        void Top_AlternateBackwardKeyChanged(Key obj)
        {
            ReplaceKeyBinding(obj, Application.AlternateBackwardKey);
        }

        void Top_AlternateForwardKeyChanged(Key obj)
        {
            ReplaceKeyBinding(obj, Application.AlternateForwardKey);
        }

        /// <summary>
        /// Tracks whether the text view should be considered "used", that is, that the user has moved in the entry,
        /// so new input should be appended at the cursor position, rather than clearing the entry
        /// </summary>
        public bool Used { get; set; }

        void ResetPosition()
        {
            topRow = leftColumn = currentRow = currentColumn = 0;
            StopSelecting();
            ResetCursorVisibility();
        }

        /// <summary>
        ///   Sets or gets the text in the <see cref="TextView"/>.
        /// </summary>
        /// <remarks>
        /// </remarks>
        public override ustring Text
        {
            get
            {
                return model.ToString();
            }

            set
            {
                ResetPosition();
                model.LoadString(value);
                TextChanged?.Invoke();
                SetNeedsDisplay();

                historyText.Clear(Text);
            }
        }

        ///<inheritdoc/>
        public override Rect Frame
        {
            get => base.Frame;
            set
            {
                base.Frame = value;
                Adjust();
            }
        }

        int frameWidth => Math.Max(Frame.Width - (RightOffset != 0 ? 2 : 1), 0);

        /// <summary>
        /// Gets or sets the top row.
        /// </summary>
        public int TopRow { get => topRow; set => topRow = Math.Max(Math.Min(value, Lines - 1), 0); }

        /// <summary>
        /// Gets or sets the left column.
        /// </summary>
        public int LeftColumn
        {
            get => leftColumn;
            set
            {
                leftColumn = Math.Max(Math.Min(value, Maxlength - 1), 0);
            }
        }

        /// <summary>
        /// Gets the maximum visible length line.
        /// </summary>
        public int Maxlength => model.GetMaxVisibleLine(topRow, topRow + Frame.Height, TabWidth);

        /// <summary>
        /// Gets the  number of lines.
        /// </summary>
        public int Lines => model.Count;

        /// <summary>
        ///    Sets or gets the current cursor position.
        /// </summary>
        public Point CursorPosition
        {
            get => new Point(currentColumn, currentRow);
            set
            {
                var line = model.GetLine(Math.Max(Math.Min(value.Y, model.Count - 1), 0));
                currentColumn = value.X < 0 ? 0 : value.X > line.Count ? line.Count : value.X;
                currentRow = value.Y < 0 ? 0 : value.Y > model.Count - 1
                    ? Math.Max(model.Count - 1, 0) : value.Y;
                SetNeedsDisplay();
                Adjust();
            }
        }

        /// <summary>
        /// Start column position of the selected text.
        /// </summary>
        public int SelectionStartColumn
        {
            get => selectionStartColumn;
            set
            {
                var line = model.GetLine(currentRow);
                selectionStartColumn = value < 0 ? 0 : value > line.Count ? line.Count : value;
                selecting = true;
                SetNeedsDisplay();
                Adjust();
            }
        }

        /// <summary>
        /// Start row position of the selected text.
        /// </summary>
        public int SelectionStartRow
        {
            get => selectionStartRow;
            set
            {
                selectionStartRow = value < 0 ? 0 : value > model.Count - 1
                    ? Math.Max(model.Count - 1, 0) : value;
                selecting = true;
                SetNeedsDisplay();
                Adjust();
            }
        }

        /// <summary>
        /// The selected text.
        /// </summary>
        public ustring SelectedText
        {
            get
            {
                if (!selecting || (model.Count == 1 && model.GetLine(0).Count == 0))
                {
                    return ustring.Empty;
                }

                return GetSelectedRegion();
            }
        }

        /// <summary>
        /// Length of the selected text.
        /// </summary>
        public int SelectedLength => GetSelectedLength();

        /// <summary>
        /// Get or sets the selecting.
        /// </summary>
        public bool Selecting
        {
            get => selecting;
            set => selecting = value;
        }

        /// <summary>
        /// The bottom offset needed to use a horizontal scrollbar or for another reason.
        /// This is only needed with the keyboard navigation.
        /// </summary>
        public int BottomOffset
        {
            get => bottomOffset;
            set
            {
                if (currentRow == Lines - 1 && bottomOffset > 0 && value == 0)
                {
                    topRow = Math.Max(topRow - bottomOffset, 0);
                }
                bottomOffset = value;
                Adjust();
            }
        }

        /// <summary>
        /// The right offset needed to use a vertical scrollbar or for another reason.
        /// This is only needed with the keyboard navigation.
        /// </summary>
        public int RightOffset
        {
            get => rightOffset;
            set
            {
                if (currentColumn == GetCurrentLine().Count && rightOffset > 0 && value == 0)
                {
                    leftColumn = Math.Max(leftColumn - rightOffset, 0);
                }
                rightOffset = value;
                Adjust();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether pressing ENTER in a <see cref="TextView"/>
        /// creates a new line of text in the view or activates the default button for the toplevel.
        /// </summary>
        public bool AllowsReturn
        {
            get => allowsReturn;
            set
            {
                allowsReturn = value;
                if (allowsReturn && !multiline)
                {
                    Multiline = true;
                }
                if (!allowsReturn && multiline)
                {
                    Multiline = false;
                    AllowsTab = false;
                }
                SetNeedsDisplay();
            }
        }

        /// <summary>
        /// Gets or sets whether the <see cref="TextView"/> inserts a tab character into the text or ignores 
        /// tab input. If set to `false` and the user presses the tab key (or shift-tab) the focus will move to the
        /// next view (or previous with shift-tab). The default is `true`; if the user presses the tab key, a tab 
        /// character will be inserted into the text.
        /// </summary>
        public bool AllowsTab
        {
            get => allowsTab;
            set
            {
                allowsTab = value;
                if (allowsTab && tabWidth == 0)
                {
                    tabWidth = 4;
                }
                if (allowsTab && !multiline)
                {
                    Multiline = true;
                }
                if (!allowsTab && tabWidth > 0)
                {
                    tabWidth = 0;
                }
                SetNeedsDisplay();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the number of whitespace when pressing the TAB key.
        /// </summary>
        public int TabWidth
        {
            get => tabWidth;
            set
            {
                tabWidth = Math.Max(value, 0);
                if (tabWidth > 0 && !AllowsTab)
                {
                    AllowsTab = true;
                }
                SetNeedsDisplay();
            }
        }

        Dim savedHeight = null;

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="TextView"/> is a multiline text view.
        /// </summary>
        public bool Multiline
        {
            get => multiline;
            set
            {
                multiline = value;
                if (multiline && !allowsTab)
                {
                    AllowsTab = true;
                }
                if (multiline && !allowsReturn)
                {
                    AllowsReturn = true;
                }

                if (!multiline)
                {
                    AllowsReturn = false;
                    AllowsTab = false;
                    currentColumn = 0;
                    currentRow = 0;
                    savedHeight = Height;
                    var lyout = LayoutStyle;
                    if (LayoutStyle == LayoutStyle.Computed)
                    {
                        LayoutStyle = LayoutStyle.Absolute;
                    }
                    Height = 1;
                    LayoutStyle = lyout;
                    Autocomplete.PopupInsideContainer = false;
                    SetNeedsDisplay();
                } else if (multiline && savedHeight != null)
                {
                    var lyout = LayoutStyle;
                    if (LayoutStyle == LayoutStyle.Computed)
                    {
                        LayoutStyle = LayoutStyle.Absolute;
                    }
                    Height = savedHeight;
                    LayoutStyle = lyout;
                    Autocomplete.PopupInsideContainer = true;
                    SetNeedsDisplay();
                }
            }
        }

        /// <summary>
        /// Indicates whatever the text was changed or not.
        /// <see langword="true"/> if the text was changed <see langword="false"/> otherwise.
        /// </summary>
        public bool IsDirty => historyText.IsDirty(Text);

        /// <summary>
        /// Indicates whatever the text has history changes or not.
        /// <see langword="true"/> if the text has history changes <see langword="false"/> otherwise.
        /// </summary>
        public bool HasHistoryChanges => historyText.HasHistoryChanges;

        /// <summary>
        /// Get the <see cref="ContextMenu"/> for this view.
        /// </summary>
        public ContextMenu ContextMenu { get; private set; }

        int GetSelectedLength()
        {
            return SelectedText.Length;
        }

        CursorVisibility savedCursorVisibility;

        void SaveCursorVisibility()
        {
            if (desiredCursorVisibility != CursorVisibility.Invisible)
            {
                if (savedCursorVisibility == 0)
                {
                    savedCursorVisibility = desiredCursorVisibility;
                }
                DesiredCursorVisibility = CursorVisibility.Invisible;
            }
        }

        void ResetCursorVisibility()
        {
            if (savedCursorVisibility != 0)
            {
                DesiredCursorVisibility = savedCursorVisibility;
                savedCursorVisibility = 0;
            }
        }

        /// <summary>
        /// Loads the contents of the file into the  <see cref="TextView"/>.
        /// </summary>
        /// <returns><c>true</c>, if file was loaded, <c>false</c> otherwise.</returns>
        /// <param name="path">Path to the file to load.</param>
        public bool LoadFile(string path)
        {
            bool res;
            try
            {
                SetWrapModel();
                res = model.LoadFile(path);
                ResetPosition();
            } catch (Exception)
            {
                throw;
            } finally
            {
                UpdateWrapModel();
                SetNeedsDisplay();
                Adjust();
            }
            return res;
        }

        /// <summary>
        /// Loads the contents of the stream into the  <see cref="TextView"/>.
        /// </summary>
        /// <returns><c>true</c>, if stream was loaded, <c>false</c> otherwise.</returns>
        /// <param name="stream">Stream to load the contents from.</param>
        public void LoadStream(Stream stream)
        {
            model.LoadStream(stream);
            ResetPosition();
            SetNeedsDisplay();
        }

        /// <summary>
        /// Closes the contents of the stream into the  <see cref="TextView"/>.
        /// </summary>
        /// <returns><c>true</c>, if stream was closed, <c>false</c> otherwise.</returns>
        public bool CloseFile()
        {
            var res = model.CloseFile();
            ResetPosition();
            SetNeedsDisplay();
            return res;
        }

        /// <summary>
        ///    Gets the current cursor row.
        /// </summary>
        public int CurrentRow => currentRow;

        /// <summary>
        /// Gets the cursor column.
        /// </summary>
        /// <value>The cursor column.</value>
        public int CurrentColumn => currentColumn;

        /// <summary>
        ///   Positions the cursor on the current row and column
        /// </summary>
        public override void PositionCursor()
        {
            if (!CanFocus || !Enabled)
            {
                return;
            }

            if (selecting)
            {
                var minRow = Math.Min(Math.Max(Math.Min(selectionStartRow, currentRow) - topRow, 0), Frame.Height);
                var maxRow = Math.Min(Math.Max(Math.Max(selectionStartRow, currentRow) - topRow, 0), Frame.Height);

                SetNeedsDisplay(new Rect(0, minRow, Frame.Width, maxRow));
            }
            var line = model.GetLine(currentRow);
            var col = 0;
            if (line.Count > 0)
            {
                for (int idx = leftColumn; idx < line.Count; idx++)
                {
                    if (idx >= currentColumn)
                        break;
                    var cols = Rune.ColumnWidth(line [idx]);
                    if (line [idx] == '\t')
                    {
                        cols += TabWidth + 1;
                    }
                    if (!TextModel.SetCol(ref col, Frame.Width, cols))
                    {
                        col = currentColumn;
                        break;
                    }
                }
            }
            var posX = currentColumn - leftColumn;
            var posY = currentRow - topRow;
            if (posX > -1 && col >= posX && posX < Frame.Width - RightOffset
                && topRow <= currentRow && posY < Frame.Height - BottomOffset)
            {
                ResetCursorVisibility();
                Move(col, currentRow - topRow);
            } else
            {
                SaveCursorVisibility();
            }
        }

        void ClearRegion(int left, int top, int right, int bottom)
        {
            for (int row = top; row < bottom; row++)
            {
                Move(left, row);
                for (int col = left; col < right; col++)
                    AddRune(col, row, ' ');
            }
        }

        /// <summary>
        /// Sets the driver to the default color for the control where no text is being rendered.  Defaults to <see cref="ColorScheme.Normal"/>.
        /// </summary>
        protected virtual void SetNormalColor()
        {
            Driver.SetAttribute(GetNormalColor());
        }

        /// <summary>
        /// Sets the <see cref="View.Driver"/> to an appropriate color for rendering the given <paramref name="idx"/> of the
        /// current <paramref name="line"/>.  Override to provide custom coloring by calling <see cref="ConsoleDriver.SetAttribute(Attribute)"/>
        /// Defaults to <see cref="ColorScheme.Normal"/>.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="idx"></param>
        protected virtual void SetNormalColor(List<Rune> line, int idx)
        {
            Driver.SetAttribute(GetNormalColor());
        }

        /// <summary>
        /// Sets the <see cref="View.Driver"/> to an appropriate color for rendering the given <paramref name="idx"/> of the
        /// current <paramref name="line"/>.  Override to provide custom coloring by calling <see cref="ConsoleDriver.SetAttribute(Attribute)"/>
        /// Defaults to <see cref="ColorScheme.Focus"/>.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="idx"></param>
        protected virtual void SetSelectionColor(List<Rune> line, int idx)
        {
            Driver.SetAttribute(new Attribute(ColorScheme.Focus.Background, ColorScheme.Focus.Foreground));
        }

        /// <summary>
        /// Sets the <see cref="View.Driver"/> to an appropriate color for rendering the given <paramref name="idx"/> of the
        /// current <paramref name="line"/>.  Override to provide custom coloring by calling <see cref="ConsoleDriver.SetAttribute(Attribute)"/>
        /// Defaults to <see cref="ColorScheme.Focus"/>.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="idx"></param>
        protected virtual void SetReadOnlyColor(List<Rune> line, int idx)
        {
            Attribute attribute;
            if (ColorScheme.Disabled.Foreground == ColorScheme.Focus.Background)
            {
                attribute = new Attribute(ColorScheme.Focus.Foreground, ColorScheme.Focus.Background);
            } else
            {
                attribute = new Attribute(ColorScheme.Disabled.Foreground, ColorScheme.Focus.Background);
            }
            Driver.SetAttribute(attribute);
        }

        /// <summary>
        /// Sets the <see cref="View.Driver"/> to an appropriate color for rendering the given <paramref name="idx"/> of the
        /// current <paramref name="line"/>.  Override to provide custom coloring by calling <see cref="ConsoleDriver.SetAttribute(Attribute)"/>
        /// Defaults to <see cref="ColorScheme.HotFocus"/>.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="idx"></param>
        protected virtual void SetUsedColor(List<Rune> line, int idx)
        {
            Driver.SetAttribute(ColorScheme.HotFocus);
        }

        bool isReadOnly = false;

        /// <summary>
        /// Gets or sets whether the  <see cref="TextView"/> is in read-only mode or not
        /// </summary>
        /// <value>Boolean value(Default false)</value>
        public bool ReadOnly
        {
            get => isReadOnly;
            set
            {
                if (value != isReadOnly)
                {
                    isReadOnly = value;

                    SetNeedsDisplay();
                    Adjust();
                }
            }
        }

        CursorVisibility desiredCursorVisibility = CursorVisibility.Default;

        /// <summary>
        /// Get / Set the wished cursor when the field is focused
        /// </summary>
        public CursorVisibility DesiredCursorVisibility
        {
            get => desiredCursorVisibility;
            set
            {
                if (HasFocus)
                {
                    Application.Driver.SetCursorVisibility(value);
                }

                desiredCursorVisibility = value;
                SetNeedsDisplay();
            }
        }

        ///<inheritdoc/>
        public override bool OnEnter(View view)
        {
            //TODO: Improve it by handling read only mode of the text field
            Application.Driver.SetCursorVisibility(DesiredCursorVisibility);

            return base.OnEnter(view);
        }

        // Returns an encoded region start..end (top 32 bits are the row, low32 the column)
        void GetEncodedRegionBounds(out long start, out long end,
            int? startRow = null, int? startCol = null, int? cRow = null, int? cCol = null)
        {
            long selection;
            long point;
            if (startRow == null || startCol == null || cRow == null || cCol == null)
            {
                selection = ((long)(uint)selectionStartRow << 32) | (uint)selectionStartColumn;
                point = ((long)(uint)currentRow << 32) | (uint)currentColumn;
            } else
            {
                selection = ((long)(uint)startRow << 32) | (uint)startCol;
                point = ((long)(uint)cRow << 32) | (uint)cCol;
            }
            if (selection > point)
            {
                start = point;
                end = selection;
            } else
            {
                start = selection;
                end = point;
            }
        }

        bool PointInSelection(int col, int row)
        {
            long start, end;
            GetEncodedRegionBounds(out start, out end);
            var q = ((long)(uint)row << 32) | (uint)col;
            return q >= start && q <= end - 1;
        }

        //
        // Returns a ustring with the text in the selected 
        // region.
        //
        ustring GetRegion(int? sRow = null, int? sCol = null, int? cRow = null, int? cCol = null, TextModel model = null)
        {
            long start, end;
            GetEncodedRegionBounds(out start, out end, sRow, sCol, cRow, cCol);
            if (start == end)
            {
                return ustring.Empty;
            }
            int startRow = (int)(start >> 32);
            var maxrow = ((int)(end >> 32));
            int startCol = (int)(start & 0xffffffff);
            var endCol = (int)(end & 0xffffffff);
            var line = model == null ? this.model.GetLine(startRow) : model.GetLine(startRow);

            if (startRow == maxrow)
                return StringFromRunes(line.GetRange(startCol, endCol - startCol));

            ustring res = StringFromRunes(line.GetRange(startCol, line.Count - startCol));

            for (int row = startRow + 1; row < maxrow; row++)
            {
                res = res + ustring.Make(Environment.NewLine) + StringFromRunes(model == null
                    ? this.model.GetLine(row) : model.GetLine(row));
            }
            line = model == null ? this.model.GetLine(maxrow) : model.GetLine(maxrow);
            res = res + ustring.Make(Environment.NewLine) + StringFromRunes(line.GetRange(0, endCol));
            return res;
        }

        //
        // Clears the contents of the selected region
        //
        void ClearRegion()
        {
            SetWrapModel();

            long start, end;
            long currentEncoded = ((long)(uint)currentRow << 32) | (uint)currentColumn;
            GetEncodedRegionBounds(out start, out end);
            int startRow = (int)(start >> 32);
            var maxrow = ((int)(end >> 32));
            int startCol = (int)(start & 0xffffffff);
            var endCol = (int)(end & 0xffffffff);
            var line = model.GetLine(startRow);

            historyText.Add(new List<List<Rune>>() { new List<Rune>(line) }, new Point(startCol, startRow));

            List<List<Rune>> removedLines = new List<List<Rune>>();

            if (startRow == maxrow)
            {
                removedLines.Add(new List<Rune>(line));

                line.RemoveRange(startCol, endCol - startCol);
                currentColumn = startCol;
                SetNeedsDisplay(new Rect(0, startRow - topRow, Frame.Width, startRow - topRow + 1));

                historyText.Add(new List<List<Rune>>(removedLines), CursorPosition, HistoryText.LineStatus.Removed);

                return;
            }

            removedLines.Add(new List<Rune>(line));

            line.RemoveRange(startCol, line.Count - startCol);
            var line2 = model.GetLine(maxrow);
            line.AddRange(line2.Skip(endCol));
            for (int row = startRow + 1; row <= maxrow; row++)
            {

                removedLines.Add(new List<Rune>(model.GetLine(startRow + 1)));

                model.RemoveLine(startRow + 1);
            }
            if (currentEncoded == end)
            {
                currentRow -= maxrow - (startRow);
            }
            currentColumn = startCol;

            historyText.Add(new List<List<Rune>>(removedLines), CursorPosition,
                HistoryText.LineStatus.Removed);

            UpdateWrapModel();

            SetNeedsDisplay();
        }

        /// <summary>
        /// Select all text.
        /// </summary>
        public void SelectAll()
        {
            if (model.Count == 0)
            {
                return;
            }

            StartSelecting();
            selectionStartColumn = 0;
            selectionStartRow = 0;
            currentColumn = model.GetLine(model.Count - 1).Count;
            currentRow = model.Count - 1;
            SetNeedsDisplay();
        }

        /// <summary>
        /// Find the next text based on the match case with the option to replace it.
        /// </summary>
        /// <param name="textToFind">The text to find.</param>
        /// <param name="gaveFullTurn"><c>true</c>If all the text was forward searched.<c>false</c>otherwise.</param>
        /// <param name="matchCase">The match case setting.</param>
        /// <param name="matchWholeWord">The match whole word setting.</param>
        /// <param name="textToReplace">The text to replace.</param>
        /// <param name="replace"><c>true</c>If is replacing.<c>false</c>otherwise.</param>
        /// <returns><c>true</c>If the text was found.<c>false</c>otherwise.</returns>
        public bool FindNextText(ustring textToFind, out bool gaveFullTurn, bool matchCase = false,
            bool matchWholeWord = false, ustring textToReplace = null, bool replace = false)
        {
            if (model.Count == 0)
            {
                gaveFullTurn = false;
                return false;
            }

            SetWrapModel();
            ResetContinuousFind();
            var foundPos = model.FindNextText(textToFind, out gaveFullTurn, matchCase, matchWholeWord);

            return SetFoundText(textToFind, foundPos, textToReplace, replace);
        }

        /// <summary>
        /// Find the previous text based on the match case with the option to replace it.
        /// </summary>
        /// <param name="textToFind">The text to find.</param>
        /// <param name="gaveFullTurn"><c>true</c>If all the text was backward searched.<c>false</c>otherwise.</param>
        /// <param name="matchCase">The match case setting.</param>
        /// <param name="matchWholeWord">The match whole word setting.</param>
        /// <param name="textToReplace">The text to replace.</param>
        /// <param name="replace"><c>true</c>If the text was found.<c>false</c>otherwise.</param>
        /// <returns><c>true</c>If the text was found.<c>false</c>otherwise.</returns>
        public bool FindPreviousText(ustring textToFind, out bool gaveFullTurn, bool matchCase = false,
            bool matchWholeWord = false, ustring textToReplace = null, bool replace = false)
        {
            if (model.Count == 0)
            {
                gaveFullTurn = false;
                return false;
            }

            SetWrapModel();
            ResetContinuousFind();
            var foundPos = model.FindPreviousText(textToFind, out gaveFullTurn, matchCase, matchWholeWord);

            return SetFoundText(textToFind, foundPos, textToReplace, replace);
        }

        /// <summary>
        /// Reset the flag to stop continuous find.
        /// </summary>
        public void FindTextChanged()
        {
            continuousFind = false;
        }

        /// <summary>
        /// Replaces all the text based on the match case.
        /// </summary>
        /// <param name="textToFind">The text to find.</param>
        /// <param name="matchCase">The match case setting.</param>
        /// <param name="matchWholeWord">The match whole word setting.</param>
        /// <param name="textToReplace">The text to replace.</param>
        /// <returns><c>true</c>If the text was found.<c>false</c>otherwise.</returns>
        public bool ReplaceAllText(ustring textToFind, bool matchCase = false, bool matchWholeWord = false,
            ustring textToReplace = null)
        {
            if (isReadOnly || model.Count == 0)
            {
                return false;
            }

            SetWrapModel();
            ResetContinuousFind();
            var foundPos = model.ReplaceAllText(textToFind, matchCase, matchWholeWord, textToReplace);

            return SetFoundText(textToFind, foundPos, textToReplace, false, true);
        }

        bool SetFoundText(ustring text, (Point current, bool found) foundPos,
            ustring textToReplace = null, bool replace = false, bool replaceAll = false)
        {
            if (foundPos.found)
            {
                StartSelecting();
                selectionStartColumn = foundPos.current.X;
                selectionStartRow = foundPos.current.Y;
                if (!replaceAll)
                {
                    currentColumn = selectionStartColumn + text.RuneCount;
                } else
                {
                    currentColumn = selectionStartColumn + textToReplace.RuneCount;
                }
                currentRow = foundPos.current.Y;
                if (!isReadOnly && replace)
                {
                    Adjust();
                    ClearSelectedRegion();
                    InsertText(textToReplace);
                    StartSelecting();
                    selectionStartColumn = currentColumn - textToReplace.RuneCount;
                } else
                {
                    UpdateWrapModel();
                    SetNeedsDisplay();
                    Adjust();
                }
                continuousFind = true;
                return foundPos.found;
            }
            UpdateWrapModel();
            continuousFind = false;

            return foundPos.found;
        }

        void ResetContinuousFind()
        {
            if (!continuousFind)
            {
                var col = selecting ? selectionStartColumn : currentColumn;
                var row = selecting ? selectionStartRow : currentRow;
                model.ResetContinuousFind(new Point(col, row));
            }
        }

        string currentCaller;

        /// <summary>
        /// Restore from original model.
        /// </summary>
        void SetWrapModel([CallerMemberName] string caller = null)
        {
            if (currentCaller != null)
                return;
        }

        /// <summary>
        /// Update the original model.
        /// </summary>
        void UpdateWrapModel([CallerMemberName] string caller = null)
        {
            if (currentCaller != null && currentCaller != caller)
                return;

            if (currentCaller != null)
                throw new InvalidOperationException($"WordWrap settings was changed after the {currentCaller} call.");
        }

        /// <summary>
        /// Invoke the <see cref="UnwrappedCursorPosition"/> event with the unwrapped <see cref="CursorPosition"/>.
        /// </summary>
        public virtual void OnUnwrappedCursorPosition(int? cRow = null, int? cCol = null)
        {
            var row = cRow == null ? currentRow : cRow;
            var col = cCol == null ? currentColumn : cCol;
            UnwrappedCursorPosition?.Invoke(new Point((int)col, (int)row));
        }

        ustring GetSelectedRegion()
        {
            var cRow = currentRow;
            var cCol = currentColumn;
            var startRow = selectionStartRow;
            var startCol = selectionStartColumn;
            var model = this.model;
            OnUnwrappedCursorPosition(cRow, cCol);
            return GetRegion(startRow, startCol, cRow, cCol, model);
        }

        ///<inheritdoc/>
        public override void Redraw(Rect bounds)
        {
            SetNormalColor();

            var offB = OffSetBackground();
            int right = Frame.Width + offB.width + RightOffset;
            int bottom = Frame.Height + offB.height + BottomOffset;
            var row = 0;
            for (int idxRow = topRow; idxRow < model.Count; idxRow++)
            {
                var line = model.GetLine(idxRow);
                int lineRuneCount = line.Count;
                var col = 0;

                Move(0, row);
                for (int idxCol = leftColumn; idxCol < lineRuneCount; idxCol++)
                {
                    var rune = idxCol >= lineRuneCount ? ' ' : line [idxCol];
                    var cols = Rune.ColumnWidth(rune);
                    if (idxCol < line.Count && selecting && PointInSelection(idxCol, idxRow))
                    {
                        SetSelectionColor(line, idxCol);
                    } else if (idxCol == currentColumn && idxRow == currentRow && !selecting && !Used
                        && HasFocus && idxCol < lineRuneCount)
                    {
                        SetSelectionColor(line, idxCol);
                    } else if (ReadOnly)
                    {
                        SetReadOnlyColor(line, idxCol);
                    } else
                    {
                        SetNormalColor(line, idxCol);
                    }

                    if (rune == '\t')
                    {
                        cols += TabWidth + 1;
                        if (col + cols > right)
                        {
                            cols = right - col;
                        }
                        for (int i = 0; i < cols; i++)
                        {
                            if (col + i < right)
                            {
                                AddRune(col + i, row, ' ');
                            }
                        }
                    } else
                    {
                        AddRune(col, row, rune);
                    }
                    if (!TextModel.SetCol(ref col, bounds.Right, cols))
                    {
                        break;
                    }
                    if (idxCol + 1 < lineRuneCount && col + Rune.ColumnWidth(line [idxCol + 1]) > right)
                    {
                        break;
                    }
                }
                if (col < right)
                {
                    SetNormalColor();
                    ClearRegion(col, row, right, row + 1);
                }
                row++;
            }
            if (row < bottom)
            {
                SetNormalColor();
                ClearRegion(bounds.Left, row, right, bottom);
            }

            PositionCursor();

            if (SelectedLength > 0)
                return;

            // draw autocomplete
            Autocomplete.GenerateSuggestions();

            var renderAt = new Point(
                CursorPosition.X - LeftColumn,
                Autocomplete.PopupInsideContainer
                    ? (CursorPosition.Y + 1) - TopRow
                    : 0);

            Autocomplete.RenderOverlay(renderAt);
        }

        /// <inheritdoc/>
        public override Attribute GetNormalColor()
        {
            return Enabled ? ColorScheme.Focus : ColorScheme.Disabled;
        }

        ///<inheritdoc/>
        public override bool CanFocus
        {
            get => base.CanFocus;
            set { base.CanFocus = value; }
        }

        void SetClipboard(ustring text)
        {
            if (text != null)
            {
                Clipboard.Contents = text;
            }
        }

        void AppendClipboard(ustring text)
        {
            Clipboard.Contents += text;
        }


        /// <summary>
        /// Inserts the given <paramref name="toAdd"/> text at the current cursor position
        /// exactly as if the user had just typed it
        /// </summary>
        /// <param name="toAdd">Text to add</param>
        public void InsertText(string toAdd)
        {
            foreach (var ch in toAdd)
            {
                Key key;

                try
                {
                    key = (Key)ch;
                } catch (Exception)
                {

                    throw new ArgumentException($"Cannot insert character '{ch}' because it does not map to a Key");
                }


                InsertText(new KeyEvent() { Key = key });
            }
        }

        public void AppendText(string appendText)
        {
            ustring str = ustring.Make(appendText);
            var lastLine = model.GetLine(model.Count - 1);

            InsertText(str, model.Count - 1, lastLine.Count);
        }

        void Insert(Rune rune)
        {
            var line = GetCurrentLine();
            if (Used)
            {
                line.Insert(Math.Min(currentColumn, line.Count), rune);
            } else
            {
                if (currentColumn < line.Count)
                {
                    line.RemoveAt(currentColumn);
                }
                line.Insert(Math.Min(currentColumn, line.Count), rune);
            }
            var prow = currentRow - topRow;
        }


        ustring StringFromRunes(List<Rune> runes)
        {
            if (runes == null)
                throw new ArgumentNullException(nameof(runes));
            int size = 0;
            foreach (var rune in runes)
            {
                size += Utf8.RuneLen(rune);
            }
            var encoded = new byte [size];
            int offset = 0;
            foreach (var rune in runes)
            {
                offset += Utf8.EncodeRune(rune, encoded, offset);
            }
            return ustring.Make(encoded);
        }

        /// <summary>
        /// Returns the characters on the current line (where the cursor is positioned).
        /// Use <see cref="CurrentColumn"/> to determine the position of the cursor within
        /// that line
        /// </summary>
        /// <returns></returns>
        public List<Rune> GetCurrentLine() => model.GetLine(currentRow);

        void InsertText(ustring text)
        {
            InsertText(text, currentRow, currentColumn);
        }

        void InsertText(ustring text, int row, int col)
        {
            if (ustring.IsNullOrEmpty(text))
            {
                return;
            }

            var lines = TextModel.StringToRunes(text);

            if (lines.Count == 0)
            {
                return;
            }

            SetWrapModel();

            var line = model.GetLine(row);

            historyText.Add(new List<List<Rune>>() { new List<Rune>(line) }, CursorPosition);

            // Optimize single line
            if (lines.Count == 1)
            {
                line.InsertRange(col, lines [0]);
                if (currentRow == row && currentColumn >= col)
                {
                    currentColumn += lines[0].Count;
                }

                historyText.Add(
                    new List<List<Rune>>() { new List<Rune>(line) },
                    new Point(col, row),
                    HistoryText.LineStatus.Replaced);

                // TODO: Only if this is supposed to bring it into view?
                if (currentColumn - leftColumn > Frame.Width)
                {
                    leftColumn = Math.Max(currentColumn - Frame.Width + 1, 0);
                }
                SetNeedsDisplay(new Rect(0, currentRow - topRow, Frame.Width, Math.Max(currentRow - topRow + 1, 0)));

                return;
            }

            List<Rune> rest = null;
            int lastp = 0;

            if (model.Count > 0 && line.Count > 0 && !copyWithoutSelection)
            {
                // Keep a copy of the rest of the line
                var restCount = line.Count - col;
                rest = line.GetRange(col, restCount);
                line.RemoveRange(col, restCount);
            }

            // First line is inserted at the current location, the rest is appended
            line.InsertRange(col, lines [0]);
            //model.AddLine (row, lines [0]);

            var addedLines = new List<List<Rune>>() { new List<Rune>(line) };

            for (int i = 1; i < lines.Count; i++)
            {
                model.AddLine(row + i, lines [i]);

                addedLines.Add(new List<Rune>(lines [i]));
            }

            if (rest != null)
            {
                var last = model.GetLine(row + lines.Count - 1);
                lastp = last.Count;
                last.InsertRange(last.Count, rest);

                addedLines.Last().InsertRange(addedLines.Last().Count, rest);
            }

            historyText.Add(addedLines, new Point(col, row), HistoryText.LineStatus.Added);

            // Now adjust column and row positions
            if (currentRow > row || (currentRow == row && currentColumn >= col))
            {
                currentRow += lines.Count - 1;
                currentColumn = rest != null ? lastp : lines [lines.Count - 1].Count;
            }
            Adjust();

            historyText.Add(new List<List<Rune>>() { new List<Rune>(line) }, new Point(col, row),
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();
        }

        // The column we are tracking, or -1 if we are not tracking any column
        int columnTrack = -1;

        // Tries to snap the cursor to the tracking column
        void TrackColumn()
        {
            // Now track the column
            var line = GetCurrentLine();
            if (line.Count < columnTrack)
                currentColumn = line.Count;
            else if (columnTrack != -1)
                currentColumn = columnTrack;
            else if (currentColumn > line.Count)
                currentColumn = line.Count;
            Adjust();
        }

        Rect NeedDisplay
        {
            get
            {
                // TODO: Is there some way to avoid this?
                var needDisplayProp = GetType().BaseType.GetProperty("NeedDisplay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (Rect)needDisplayProp.GetValue(this);
            }
        }


        void Adjust()
        {
            var offB = OffSetBackground();
            var line = GetCurrentLine();
            bool need = !NeedDisplay.IsEmpty;
            var tSize = TextModel.DisplaySize(line, -1, -1, false, TabWidth);
            var dSize = TextModel.DisplaySize(line, leftColumn, currentColumn, true, TabWidth);
            if (currentColumn < leftColumn)
            {
                leftColumn = currentColumn;
                need = true;
            } else if ((currentColumn - leftColumn + RightOffset > Frame.Width + offB.width
                || dSize.size + RightOffset >= Frame.Width + offB.width))
            {
                leftColumn = TextModel.CalculateLeftColumn(line, leftColumn, currentColumn,
                    Frame.Width + offB.width - RightOffset, TabWidth);
                need = true;
            } else if ((dSize.size + RightOffset < Frame.Width + offB.width
                && tSize.size + RightOffset < Frame.Width + offB.width))
            {
                leftColumn = 0;
                need = true;
            }

            if (currentRow < topRow)
            {
                topRow = currentRow;
                need = true;
            } else if (currentRow - topRow + BottomOffset >= Frame.Height + offB.height)
            {
                topRow = Math.Min(Math.Max(currentRow - Frame.Height + 1 + BottomOffset, 0), currentRow);
                need = true;
            } else if (topRow > 0 && currentRow == topRow)
            {
                topRow = Math.Max(topRow - 1, 0);
            }
            if (need)
            {
                SetNeedsDisplay();
            } else
            {
                PositionCursor();
            }

            OnUnwrappedCursorPosition();
        }

        (int width, int height) OffSetBackground()
        {
            int w = 0;
            int h = 0;
            if (SuperView?.Frame.Right - Frame.Right < 0)
            {
                w = SuperView.Frame.Right - Frame.Right - 1;
            }
            if (SuperView?.Frame.Bottom - Frame.Bottom < 0)
            {
                h = SuperView.Frame.Bottom - Frame.Bottom - 1;
            }
            return (w, h);
        }

        /// <summary>
        /// Will scroll the <see cref="TextView"/> to display the specified row at the top if <paramref name="isRow"/> is true or
        /// will scroll the <see cref="TextView"/> to display the specified column at the left if <paramref name="isRow"/> is false.
        /// </summary>
        /// <param name="idx">Row that should be displayed at the top or Column that should be displayed at the left,
        ///  if the value is negative it will be reset to zero</param>
        /// <param name="isRow">If true (default) the <paramref name="idx"/> is a row, column otherwise.</param>
        public void ScrollTo(int idx, bool isRow = true)
        {
            if (idx < 0)
            {
                idx = 0;
            }
            if (isRow)
            {
                topRow = Math.Max(idx > model.Count - 1 ? model.Count - 1 : idx, 0);
            } else
            {
                var maxlength = model.GetMaxVisibleLine(topRow, topRow + Frame.Height + RightOffset, TabWidth);
                leftColumn = Math.Max(idx > maxlength - 1 ? maxlength - 1 : idx, 0);
            }
            SetNeedsDisplay();
        }

        bool lastWasKill;
        bool shiftSelecting;

        ///<inheritdoc/>
        public override bool ProcessKey(KeyEvent kb)
        {
            if (!CanFocus)
            {
                return true;
            }

            // Give autocomplete first opportunity to respond to key presses
            if (SelectedLength == 0 && Autocomplete.ProcessKey(kb))
            {
                return true;
            }

            var result = InvokeKeybindings(new KeyEvent(ShortcutHelper.GetModifiersKey(kb),
                new KeyModifiers() { Alt = kb.IsAlt, Ctrl = kb.IsCtrl, Shift = kb.IsShift }));
            if (result != null)
                return (bool)result;

            ResetColumnTrack();
            // Ignore control characters and other special keys
            if (kb.Key < Key.Space || kb.Key > Key.CharMask)
                return false;

            InsertText(kb);
            DoNeededAction();

            return true;
        }

        void RedoChanges()
        {
            if (ReadOnly)
                return;

            historyText.Redo();
        }

        void UndoChanges()
        {
            if (ReadOnly)
                return;

            historyText.Undo();
        }

        bool ProcessMovePreviousView()
        {
            ResetColumnTrack();
            return MovePreviousView();
        }

        bool ProcessMoveNextView()
        {
            ResetColumnTrack();
            return MoveNextView();
        }

        void ProcessSetOverwrite()
        {
            ResetColumnTrack();
            SetOverwrite(!Used);
        }

        void ProcessSelectAll()
        {
            ResetColumnTrack();
            SelectAll();
        }

        void MoveTopHomeExtend()
        {
            ResetColumnTrack();
            StartSelecting();
            MoveHome();
        }

        void MoveTopHome()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveHome();
        }

        void MoveBottomEndExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveEnd();
        }

        void MoveBottomEnd()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveEnd();
        }

        void ProcessKillWordBackward()
        {
            ResetColumnTrack();
            KillWordBackward();
        }

        void ProcessKillWordForward()
        {
            ResetColumnTrack();
            KillWordForward();
        }

        void ProcessMoveWordForwardExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveWordForward();
        }

        void ProcessMoveWordForward()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveWordForward();
        }

        void ProcessMoveWordBackwardExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveWordBackward();
        }

        void ProcessMoveWordBackward()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveWordBackward();
        }

        void ProcessCut()
        {
            ResetColumnTrack();
            Cut();
        }

        void ProcessCopy()
        {
            ResetColumnTrack();
            Copy();
        }

        void ToggleSelecting()
        {
            ResetColumnTrack();
            selecting = !selecting;
            selectionStartColumn = currentColumn;
            selectionStartRow = currentRow;
        }

        void ProcessPaste()
        {
            ResetColumnTrack();
            if (isReadOnly)
                return;
            Paste();
        }

        void ProcessMoveEndOfLineExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveEndOfLine();
        }

        void ProcessMoveEndOfLine()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveEndOfLine();
        }

        void ProcessDeleteCharRight()
        {
            ResetColumnTrack();
            DeleteCharRight();
        }

        void ProcessMoveStartOfLineExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveStartOfLine();
        }

        void ProcessMoveStartOfLine()
        {
            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveStartOfLine();
        }

        void ProcessDeleteCharLeft()
        {
            ResetColumnTrack();
            DeleteCharLeft();
        }

        void ProcessMoveLeftExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveLeft();
        }

        bool ProcessMoveLeft()
        {
            // if the user presses Left (without any control keys) and they are at the start of the text
            if (currentColumn == 0 && currentRow == 0)
            {
                // do not respond (this lets the key press fall through to navigation system - which usually changes focus backward)
                return false;
            }

            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveLeft();
            return true;
        }

        void ProcessMoveRightExtend()
        {
            ResetAllTrack();
            StartSelecting();
            MoveRight();
        }

        bool ProcessMoveRight()
        {
            // if the user presses Right (without any control keys)
            // determine where the last cursor position in the text is
            var lastRow = model.Count - 1;
            var lastCol = model.GetLine(lastRow).Count;

            // if they are at the very end of all the text do not respond (this lets the key press fall through to navigation system - which usually changes focus forward)
            if (currentColumn == lastCol && currentRow == lastRow)
            {
                return false;
            }

            ResetAllTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveRight();
            return true;
        }

        void ProcessMoveUpExtend()
        {
            ResetColumnTrack();
            StartSelecting();
            MoveUp();
        }

        void ProcessMoveUp()
        {
            ResetContinuousFindTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveUp();
        }

        void ProcessMoveDownExtend()
        {
            ResetColumnTrack();
            StartSelecting();
            MoveDown();
        }

        void ProcessMoveDown()
        {
            ResetContinuousFindTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MoveDown();
        }

        void ProcessPageUpExtend()
        {
            ResetColumnTrack();
            StartSelecting();
            MovePageUp();
        }

        void ProcessPageUp()
        {
            ResetColumnTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MovePageUp();
        }

        void ProcessPageDownExtend()
        {
            ResetColumnTrack();
            StartSelecting();
            MovePageDown();
        }

        void ProcessPageDown()
        {
            ResetColumnTrack();
            if (shiftSelecting && selecting)
            {
                StopSelecting();
            }
            MovePageDown();
        }

        bool MovePreviousView()
        {
            if (Application.MdiTop != null)
            {
                return SuperView?.FocusPrev() == true;
            }

            return false;
        }

        bool MoveNextView()
        {
            if (Application.MdiTop != null)
            {
                return SuperView?.FocusNext() == true;
            }

            return false;
        }

        bool ProcessBackTab()
        {
            ResetColumnTrack();

            if (!AllowsTab || isReadOnly)
            {
                return ProcessMovePreviousView();
            }
            if (currentColumn > 0)
            {
                SetWrapModel();

                var currentLine = GetCurrentLine();
                if (currentLine.Count > 0 && currentLine [currentColumn - 1] == '\t')
                {

                    historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

                    currentLine.RemoveAt(currentColumn - 1);
                    currentColumn--;

                    historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                        HistoryText.LineStatus.Replaced);
                }

                UpdateWrapModel();
            }
            DoNeededAction();
            return true;
        }

        bool ProcessTab()
        {
            ResetColumnTrack();

            if (!AllowsTab || isReadOnly)
            {
                return ProcessMoveNextView();
            }
            InsertText(new KeyEvent((Key)'\t', null));
            DoNeededAction();
            return true;
        }

        void SetOverwrite(bool overwrite)
        {
            Used = overwrite;
            SetNeedsDisplay();
            DoNeededAction();
        }

        bool ProcessReturn()
        {
            ResetColumnTrack();

            if (!AllowsReturn || isReadOnly)
            {
                return false;
            }

            SetWrapModel();

            var currentLine = GetCurrentLine();

            historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

            if (selecting)
            {
                ClearSelectedRegion();
                currentLine = GetCurrentLine();
            }
            var restCount = currentLine.Count - currentColumn;
            var rest = currentLine.GetRange(currentColumn, restCount);
            currentLine.RemoveRange(currentColumn, restCount);

            var addedLines = new List<List<Rune>>() { new List<Rune>(currentLine) };

            model.AddLine(currentRow + 1, rest);

            addedLines.Add(new List<Rune>(model.GetLine(currentRow + 1)));

            historyText.Add(addedLines, CursorPosition, HistoryText.LineStatus.Added);

            currentRow++;

            bool fullNeedsDisplay = false;
            if (currentRow >= topRow + Frame.Height)
            {
                topRow++;
                fullNeedsDisplay = true;
            }
            currentColumn = 0;

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            if (currentColumn < leftColumn)
            {
                fullNeedsDisplay = true;
                leftColumn = 0;
            }

            if (fullNeedsDisplay)
                SetNeedsDisplay();
            else
                SetNeedsDisplay(new Rect(0, currentRow - topRow, 2, Frame.Height));

            UpdateWrapModel();

            DoNeededAction();
            return true;
        }

        void KillWordBackward()
        {
            if (isReadOnly)
                return;

            SetWrapModel();

            var currentLine = GetCurrentLine();

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition);

            if (currentColumn == 0)
            {
                DeleteTextBackwards();

                historyText.ReplaceLast(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                UpdateWrapModel();

                return;
            }
            var newPos = WordBackward(currentColumn, currentRow);
            if (newPos.HasValue && currentRow == newPos.Value.row)
            {
                var restCount = currentColumn - newPos.Value.col;
                currentLine.RemoveRange(newPos.Value.col, restCount);
                currentColumn = newPos.Value.col;
            } else if (newPos.HasValue)
            {
                var restCount = currentLine.Count - currentColumn;
                currentLine.RemoveRange(currentColumn, restCount);
                currentColumn = newPos.Value.col;
                currentRow = newPos.Value.row;
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();

            SetNeedsDisplay(new Rect(0, currentRow - topRow, Frame.Width, Frame.Height));
            DoNeededAction();
        }

        void KillWordForward()
        {
            if (isReadOnly)
                return;

            SetWrapModel();

            var currentLine = GetCurrentLine();

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition);

            if (currentLine.Count == 0 || currentColumn == currentLine.Count)
            {
                DeleteTextForwards();

                historyText.ReplaceLast(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                UpdateWrapModel();

                return;
            }
            var newPos = WordForward(currentColumn, currentRow);
            var restCount = 0;
            if (newPos.HasValue && currentRow == newPos.Value.row)
            {
                restCount = newPos.Value.col - currentColumn;
                currentLine.RemoveRange(currentColumn, restCount);
            } else if (newPos.HasValue)
            {
                restCount = currentLine.Count - currentColumn;
                currentLine.RemoveRange(currentColumn, restCount);
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();

            SetNeedsDisplay(new Rect(0, currentRow - topRow, Frame.Width, Frame.Height));
            DoNeededAction();
        }

        void MoveWordForward()
        {
            var newPos = WordForward(currentColumn, currentRow);
            if (newPos.HasValue)
            {
                currentColumn = newPos.Value.col;
                currentRow = newPos.Value.row;
            }
            Adjust();
            DoNeededAction();
        }

        void MoveWordBackward()
        {
            var newPos = WordBackward(currentColumn, currentRow);
            if (newPos.HasValue)
            {
                currentColumn = newPos.Value.col;
                currentRow = newPos.Value.row;
            }
            Adjust();
            DoNeededAction();
        }

        void KillToStartOfLine()
        {
            if (isReadOnly)
                return;
            if (model.Count == 1 && GetCurrentLine().Count == 0)
            {
                // Prevents from adding line feeds if there is no more lines.
                return;
            }

            SetWrapModel();

            var currentLine = GetCurrentLine();
            var setLastWasKill = true;
            if (currentLine.Count > 0 && currentColumn == 0)
            {
                UpdateWrapModel();

                DeleteTextBackwards();
                return;
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

            if (currentLine.Count == 0)
            {
                if (currentRow > 0)
                {
                    model.RemoveLine(currentRow);

                    if (model.Count > 0 || lastWasKill)
                    {
                        var val = ustring.Make(Environment.NewLine);
                        if (lastWasKill)
                        {
                            AppendClipboard(val);
                        } else
                        {
                            SetClipboard(val);
                        }
                    }
                    if (model.Count == 0)
                    {
                        // Prevents from adding line feeds if there is no more lines.
                        setLastWasKill = false;
                    }

                    currentRow--;
                    currentLine = model.GetLine(currentRow);

                    var removedLine = new List<List<Rune>>() { new List<Rune>(currentLine) };

                    removedLine.Add(new List<Rune>());

                    historyText.Add(new List<List<Rune>>(removedLine), CursorPosition, HistoryText.LineStatus.Removed);

                    currentColumn = currentLine.Count;
                }
            } else
            {
                var restCount = currentColumn;
                var rest = currentLine.GetRange(0, restCount);
                var val = ustring.Empty;
                val += StringFromRunes(rest);
                if (lastWasKill)
                {
                    AppendClipboard(val);
                } else
                {
                    SetClipboard(val);
                }
                currentLine.RemoveRange(0, restCount);
                currentColumn = 0;
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();

            SetNeedsDisplay(new Rect(0, currentRow - topRow, Frame.Width, Frame.Height));
            lastWasKill = setLastWasKill;
            DoNeededAction();
        }

        void KillToEndOfLine()
        {
            if (isReadOnly)
                return;
            if (model.Count == 1 && GetCurrentLine().Count == 0)
            {
                // Prevents from adding line feeds if there is no more lines.
                return;
            }

            SetWrapModel();

            var currentLine = GetCurrentLine();
            var setLastWasKill = true;
            if (currentLine.Count > 0 && currentColumn == currentLine.Count)
            {
                UpdateWrapModel();

                DeleteTextForwards();
                return;
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

            if (currentLine.Count == 0)
            {
                if (currentRow < model.Count - 1)
                {
                    var removedLines = new List<List<Rune>>() { new List<Rune>(currentLine) };

                    model.RemoveLine(currentRow);

                    removedLines.Add(new List<Rune>(GetCurrentLine()));

                    historyText.Add(new List<List<Rune>>(removedLines), CursorPosition,
                        HistoryText.LineStatus.Removed);
                }
                if (model.Count > 0 || lastWasKill)
                {
                    var val = ustring.Make(Environment.NewLine);
                    if (lastWasKill)
                    {
                        AppendClipboard(val);
                    } else
                    {
                        SetClipboard(val);
                    }
                }
                if (model.Count == 0)
                {
                    // Prevents from adding line feeds if there is no more lines.
                    setLastWasKill = false;
                }
            } else
            {
                var restCount = currentLine.Count - currentColumn;
                var rest = currentLine.GetRange(currentColumn, restCount);
                var val = ustring.Empty;
                val += StringFromRunes(rest);
                if (lastWasKill)
                {
                    AppendClipboard(val);
                } else
                {
                    SetClipboard(val);
                }
                currentLine.RemoveRange(currentColumn, restCount);
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();

            SetNeedsDisplay(new Rect(0, currentRow - topRow, Frame.Width, Frame.Height));
            lastWasKill = setLastWasKill;
            DoNeededAction();
        }

        void MoveEndOfLine()
        {
            var currentLine = GetCurrentLine();
            currentColumn = currentLine.Count;
            Adjust();
            DoNeededAction();
        }

        void MoveStartOfLine()
        {
            currentColumn = 0;
            leftColumn = 0;
            Adjust();
            DoNeededAction();
        }

        /// <summary>
        /// Deletes all the selected or a single character at right from the position of the cursor.
        /// </summary>
        public void DeleteCharRight()
        {
            if (isReadOnly)
                return;

            SetWrapModel();

            if (selecting)
            {
                historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Original);

                ClearSelectedRegion();

                var currentLine = GetCurrentLine();

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                UpdateWrapModel();

                return;
            }
            if (DeleteTextForwards())
            {
                UpdateWrapModel();

                return;
            }

            UpdateWrapModel();

            DoNeededAction();
        }

        /// <summary>
        /// Deletes all the selected or a single character at left from the position of the cursor.
        /// </summary>
        public void DeleteCharLeft()
        {
            if (isReadOnly)
                return;

            SetWrapModel();

            if (selecting)
            {
                historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Original);

                ClearSelectedRegion();

                var currentLine = GetCurrentLine();

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                UpdateWrapModel();

                return;
            }
            if (DeleteTextBackwards())
            {
                UpdateWrapModel();

                return;
            }

            UpdateWrapModel();

            DoNeededAction();
        }

        void MoveLeft()
        {
            if (currentColumn > 0)
            {
                currentColumn--;
            } else
            {
                if (currentRow > 0)
                {
                    currentRow--;
                    if (currentRow < topRow)
                    {
                        topRow--;
                        SetNeedsDisplay();
                    }
                    var currentLine = GetCurrentLine();
                    currentColumn = currentLine.Count;
                }
            }
            Adjust();
            DoNeededAction();
        }

        void MoveRight()
        {
            var currentLine = GetCurrentLine();
            if (currentColumn < currentLine.Count)
            {
                currentColumn++;
            } else
            {
                if (currentRow + 1 < model.Count)
                {
                    currentRow++;
                    currentColumn = 0;
                    if (currentRow >= topRow + Frame.Height)
                    {
                        topRow++;
                        SetNeedsDisplay();
                    }
                }
            }
            Adjust();
            DoNeededAction();
        }

        void MovePageUp()
        {
            int nPageUpShift = Frame.Height - 1;
            if (currentRow > 0)
            {
                if (columnTrack == -1)
                    columnTrack = currentColumn;
                currentRow = currentRow - nPageUpShift < 0 ? 0 : currentRow - nPageUpShift;
                if (currentRow < topRow)
                {
                    topRow = topRow - nPageUpShift < 0 ? 0 : topRow - nPageUpShift;
                    SetNeedsDisplay();
                }
                TrackColumn();
                PositionCursor();
            }
            DoNeededAction();
        }

        void MovePageDown()
        {
            int nPageDnShift = Frame.Height - 1;
            if (currentRow >= 0 && currentRow < model.Count)
            {
                if (columnTrack == -1)
                    columnTrack = currentColumn;
                currentRow = (currentRow + nPageDnShift) > model.Count
                    ? model.Count > 0 ? model.Count - 1 : 0
                    : currentRow + nPageDnShift;
                if (topRow < currentRow - nPageDnShift)
                {
                    topRow = currentRow >= model.Count ? currentRow - nPageDnShift : topRow + nPageDnShift;
                    SetNeedsDisplay();
                }
                TrackColumn();
                PositionCursor();
            }
            DoNeededAction();
        }

        void ResetContinuousFindTrack()
        {
            // Handle some state here - whether the last command was a kill
            // operation and the column tracking (up/down)
            lastWasKill = false;
            continuousFind = false;
        }

        void ResetColumnTrack()
        {
            // Handle some state here - whether the last command was a kill
            // operation and the column tracking (up/down)
            lastWasKill = false;
            columnTrack = -1;
        }

        void ResetAllTrack()
        {
            // Handle some state here - whether the last command was a kill
            // operation and the column tracking (up/down)
            lastWasKill = false;
            columnTrack = -1;
            continuousFind = false;
        }

        bool InsertText(KeyEvent kb)
        {
            //So that special keys like tab can be processed
            if (isReadOnly)
                return true;

            SetWrapModel();

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition);

            if (selecting)
            {
                ClearSelectedRegion();
            }
            if (kb.Key == Key.Enter)
            {
                model.AddLine(currentRow + 1, new List<Rune>());
                currentRow++;
                currentColumn = 0;
            } else if ((uint)kb.Key == 13)
            {
                currentColumn = 0;
            } else
            {
                if (Used)
                {
                    Insert((uint)kb.Key);
                    currentColumn++;
                    if (currentColumn >= leftColumn + Frame.Width)
                    {
                        leftColumn++;
                    }
                    SetNeedsDisplay();
                } else
                {
                    Insert((uint)kb.Key);
                    currentColumn++;
                }
            }

            historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                HistoryText.LineStatus.Replaced);

            UpdateWrapModel();

            return true;
        }

        void ShowContextMenu()
        {
            if (currentCulture != Thread.CurrentThread.CurrentUICulture)
            {

                currentCulture = Thread.CurrentThread.CurrentUICulture;

                ContextMenu.MenuItems = BuildContextMenuBarItem();
            }
            ContextMenu.Show();
        }

        /// <summary>
        /// Deletes all text.
        /// </summary>
        public void DeleteAll()
        {
            if (Lines == 0)
            {
                return;
            }

            selectionStartColumn = 0;
            selectionStartRow = 0;
            MoveBottomEndExtend();
            DeleteCharLeft();
            SetNeedsDisplay();
        }

        ///<inheritdoc/>
        public override bool OnKeyUp(KeyEvent kb)
        {
            switch (kb.Key)
            {
            case Key.Space | Key.CtrlMask:
                return true;
            }

            return false;
        }

        void DoNeededAction()
        {
            if (NeedDisplay.IsEmpty)
            {
                PositionCursor();
            } else
            {
                Adjust();
            }
        }

        bool DeleteTextForwards()
        {
            SetWrapModel();

            var currentLine = GetCurrentLine();
            if (currentColumn == currentLine.Count)
            {
                if (currentRow + 1 == model.Count)
                {
                    UpdateWrapModel();

                    return true;
                }

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

                var removedLines = new List<List<Rune>>() { new List<Rune>(currentLine) };

                var nextLine = model.GetLine(currentRow + 1);

                removedLines.Add(new List<Rune>(nextLine));

                historyText.Add(removedLines, CursorPosition, HistoryText.LineStatus.Removed);

                currentLine.AddRange(nextLine);
                model.RemoveLine(currentRow + 1);

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                var sr = currentRow - topRow;
                SetNeedsDisplay(new Rect(0, sr, Frame.Width, sr + 1));
            } else
            {
                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

                currentLine.RemoveAt(currentColumn);

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                var r = currentRow - topRow;
                SetNeedsDisplay(new Rect(currentColumn - leftColumn, r, Frame.Width, r + 1));
            }

            UpdateWrapModel();

            return false;
        }

        bool DeleteTextBackwards()
        {
            SetWrapModel();

            if (currentColumn > 0)
            {
                // Delete backwards 
                var currentLine = GetCurrentLine();

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

                currentLine.RemoveAt(currentColumn - 1);
                currentColumn--;

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);

                if (currentColumn < leftColumn)
                {
                    leftColumn--;
                    SetNeedsDisplay();
                } else
                    SetNeedsDisplay(new Rect(0, currentRow - topRow, 1, Frame.Width));
            } else
            {
                // Merges the current line with the previous one.
                if (currentRow == 0)
                    return true;
                var prowIdx = currentRow - 1;
                var prevRow = model.GetLine(prowIdx);

                historyText.Add(new List<List<Rune>>() { new List<Rune>(prevRow) }, CursorPosition);

                List<List<Rune>> removedLines = new List<List<Rune>>() { new List<Rune>(prevRow) };

                removedLines.Add(new List<Rune>(GetCurrentLine()));

                historyText.Add(removedLines, new Point(currentColumn, prowIdx),
                    HistoryText.LineStatus.Removed);

                var prevCount = prevRow.Count;
                model.GetLine(prowIdx).AddRange(GetCurrentLine());
                model.RemoveLine(currentRow);
                currentRow--;

                historyText.Add(new List<List<Rune>>() { GetCurrentLine() }, new Point(currentColumn, prowIdx),
                    HistoryText.LineStatus.Replaced);

                currentColumn = prevCount;
                SetNeedsDisplay();
            }

            UpdateWrapModel();

            return false;
        }

        bool copyWithoutSelection;

        /// <summary>
        /// Copy the selected text to the clipboard contents.
        /// </summary>
        public void Copy()
        {
            SetWrapModel();
            if (selecting)
            {
                SetClipboard(GetRegion());
                copyWithoutSelection = false;
            } else
            {
                var currentLine = GetCurrentLine();
                SetClipboard(ustring.Make(currentLine));
                copyWithoutSelection = true;
            }
            UpdateWrapModel();
            DoNeededAction();
        }

        /// <summary>
        /// Cut the selected text to the clipboard contents.
        /// </summary>
        public void Cut()
        {
            SetWrapModel();
            SetClipboard(GetRegion());
            if (!isReadOnly)
            {
                ClearRegion();

                historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);
            }
            UpdateWrapModel();
            selecting = false;
            DoNeededAction();
        }

        /// <summary>
        /// Paste the clipboard contents into the current selected position.
        /// </summary>
        public void Paste()
        {
            if (isReadOnly)
            {
                return;
            }

            SetWrapModel();
            var contents = Clipboard.Contents;
            if (copyWithoutSelection && contents.FirstOrDefault(x => x == '\n' || x == '\r') == 0)
            {
                var runeList = contents == null ? new List<Rune>() : contents.ToRuneList();
                var currentLine = GetCurrentLine();

                historyText.Add(new List<List<Rune>>() { new List<Rune>(currentLine) }, CursorPosition);

                var addedLine = new List<List<Rune>>() { new List<Rune>(currentLine) };

                addedLine.Add(runeList);

                historyText.Add(new List<List<Rune>>(addedLine), CursorPosition, HistoryText.LineStatus.Added);

                model.AddLine(currentRow, runeList);
                currentRow++;

                historyText.Add(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                    HistoryText.LineStatus.Replaced);
            } else
            {
                if (selecting)
                {
                    ClearRegion();
                }
                copyWithoutSelection = false;
                InsertText(contents);

                if (selecting)
                {
                    historyText.ReplaceLast(new List<List<Rune>>() { new List<Rune>(GetCurrentLine()) }, CursorPosition,
                        HistoryText.LineStatus.Original);
                }
            }
            UpdateWrapModel();
            selecting = false;
            DoNeededAction();
        }

        void StartSelecting()
        {
            if (shiftSelecting && selecting)
            {
                return;
            }
            shiftSelecting = true;
            selecting = true;
            selectionStartColumn = currentColumn;
            selectionStartRow = currentRow;
        }

        void StopSelecting()
        {
            shiftSelecting = false;
            selecting = false;
            isButtonShift = false;
        }

        void ClearSelectedRegion()
        {
            SetWrapModel();
            if (!isReadOnly)
            {
                ClearRegion();
            }
            UpdateWrapModel();
            selecting = false;
            DoNeededAction();
        }

        void MoveUp()
        {
            if (currentRow > 0)
            {
                if (columnTrack == -1)
                {
                    columnTrack = currentColumn;
                }
                currentRow--;
                if (currentRow < topRow)
                {
                    topRow--;
                    SetNeedsDisplay();
                }
                TrackColumn();
                PositionCursor();
            }
            DoNeededAction();
        }

        void MoveDown()
        {
            if (currentRow + 1 < model.Count)
            {
                if (columnTrack == -1)
                {
                    columnTrack = currentColumn;
                }
                currentRow++;
                if (currentRow + BottomOffset >= topRow + Frame.Height)
                {
                    topRow++;
                    SetNeedsDisplay();
                }
                TrackColumn();
                PositionCursor();
            } else if (currentRow > Frame.Height)
            {
                Adjust();
            }
            DoNeededAction();
        }

        IEnumerable<(int col, int row, Rune rune)> ForwardIterator(int col, int row)
        {
            if (col < 0 || row < 0)
                yield break;
            if (row >= model.Count)
                yield break;
            var line = GetCurrentLine();
            if (col >= line.Count)
                yield break;

            while (row < model.Count)
            {
                for (int c = col; c < line.Count; c++)
                {
                    yield return (c, row, line [c]);
                }
                col = 0;
                row++;
                line = GetCurrentLine();
            }
        }

        Rune RuneAt(int col, int row)
        {
            var line = model.GetLine(row);
            if (line.Count > 0)
            {
                return line [col > line.Count - 1 ? line.Count - 1 : col];
            } else
            {
                return 0;
            }
        }

        /// <summary>
        /// Will scroll the <see cref="TextView"/> to the last line and position the cursor there.
        /// </summary>
        public void MoveEnd()
        {
            currentRow = model.Count - 1;
            var line = GetCurrentLine();
            currentColumn = line.Count;
            TrackColumn();
            PositionCursor();
        }

        /// <summary>
        /// Will scroll the <see cref="TextView"/> to the first line and position the cursor there.
        /// </summary>
        public void MoveHome()
        {
            currentRow = 0;
            topRow = 0;
            currentColumn = 0;
            leftColumn = 0;
            TrackColumn();
            PositionCursor();
        }

        bool MoveNext(ref int col, ref int row, out Rune rune)
        {
            var line = model.GetLine(row);
            if (col + 1 < line.Count)
            {
                col++;
                rune = line [col];
                if (col + 1 == line.Count && !Rune.IsLetterOrDigit(rune)
                    && !Rune.IsWhiteSpace(line [col - 1]))
                {
                    col++;
                }
                return true;
            } else if (col + 1 == line.Count)
            {
                col++;
            }
            while (row + 1 < model.Count)
            {
                col = 0;
                row++;
                line = model.GetLine(row);
                if (line.Count > 0)
                {
                    rune = line [0];
                    return true;
                }
            }
            rune = 0;
            return false;
        }

        bool MovePrev(ref int col, ref int row, out Rune rune)
        {
            var line = model.GetLine(row);

            if (col > 0)
            {
                col--;
                rune = line [col];
                return true;
            }
            if (row == 0)
            {
                rune = 0;
                return false;
            }
            while (row > 0)
            {
                row--;
                line = model.GetLine(row);
                col = line.Count - 1;
                if (col >= 0)
                {
                    rune = line [col];
                    return true;
                }
            }
            rune = 0;
            return false;
        }

        (int col, int row)? WordForward(int fromCol, int fromRow)
        {
            var col = fromCol;
            var row = fromRow;
            try
            {
                var rune = RuneAt(col, row);

                void ProcMoveNext(ref int nCol, ref int nRow, Rune nRune)
                {
                    if (Rune.IsSymbol(nRune) || Rune.IsWhiteSpace(nRune))
                    {
                        while (MoveNext(ref nCol, ref nRow, out nRune))
                        {
                            if (Rune.IsLetterOrDigit(nRune) || Rune.IsPunctuation(nRune))
                                return;
                        }
                        if (nRow != fromRow && (Rune.IsLetterOrDigit(nRune) || Rune.IsPunctuation(nRune)))
                        {
                            return;
                        }
                        while (MoveNext(ref nCol, ref nRow, out nRune))
                        {
                            if (!Rune.IsLetterOrDigit(nRune) && !Rune.IsPunctuation(nRune))
                                break;
                        }
                    } else
                    {
                        if (!MoveNext(ref nCol, ref nRow, out nRune))
                        {
                            return;
                        }

                        var line = model.GetLine(fromRow);
                        if ((nRow != fromRow && fromCol < line.Count)
                            || (nRow == fromRow && nCol == line.Count - 1))
                        {
                            nCol = line.Count;
                            nRow = fromRow;
                            return;
                        } else if (nRow != fromRow && fromCol == line.Count)
                        {
                            line = model.GetLine(nRow);
                            if (Rune.IsLetterOrDigit(line [nCol]) || Rune.IsPunctuation(line [nCol]))
                            {
                                return;
                            }
                        }
                        ProcMoveNext(ref nCol, ref nRow, nRune);
                    }
                }

                ProcMoveNext(ref col, ref row, rune);

                if (fromCol != col || fromRow != row)
                    return (col, row);
                return null;
            } catch (Exception)
            {
                return null;
            }
        }

        (int col, int row)? WordBackward(int fromCol, int fromRow)
        {
            if (fromRow == 0 && fromCol == 0)
                return null;

            var col = Math.Max(fromCol - 1, 0);
            var row = fromRow;
            try
            {
                var rune = RuneAt(col, row);
                int lastValidCol = Rune.IsLetterOrDigit(rune) || Rune.IsPunctuation(rune) ? col : -1;

                void ProcMovePrev(ref int nCol, ref int nRow, Rune nRune)
                {
                    if (Rune.IsSymbol(nRune) || Rune.IsWhiteSpace(nRune))
                    {
                        while (MovePrev(ref nCol, ref nRow, out nRune))
                        {
                            if (Rune.IsLetterOrDigit(nRune) || Rune.IsPunctuation(nRune))
                            {
                                lastValidCol = nCol;
                                break;
                            }
                        }
                        if (nRow != fromRow && (Rune.IsLetterOrDigit(nRune) || Rune.IsPunctuation(nRune)))
                        {
                            if (lastValidCol > -1)
                            {
                                nCol = lastValidCol;
                            }
                            return;
                        }
                        while (MovePrev(ref nCol, ref nRow, out nRune))
                        {
                            if (!Rune.IsLetterOrDigit(nRune) && !Rune.IsPunctuation(nRune))
                                break;
                            if (nRow != fromRow)
                            {
                                break;
                            }
                            lastValidCol = nCol;
                        }
                        if (lastValidCol > -1)
                        {
                            nCol = lastValidCol;
                            nRow = fromRow;
                        }
                    } else
                    {
                        if (!MovePrev(ref nCol, ref nRow, out nRune))
                        {
                            return;
                        }

                        var line = model.GetLine(nRow);
                        if (nCol == 0 && nRow == fromRow && (Rune.IsLetterOrDigit(line [0]) || Rune.IsPunctuation(line [0])))
                        {
                            return;
                        }
                        lastValidCol = Rune.IsLetterOrDigit(nRune) || Rune.IsPunctuation(nRune) ? nCol : lastValidCol;
                        if (lastValidCol > -1 && (Rune.IsSymbol(nRune) || Rune.IsWhiteSpace(nRune)))
                        {
                            nCol = lastValidCol;
                            return;
                        }
                        if (fromRow != nRow)
                        {
                            nCol = line.Count;
                            return;
                        }
                        ProcMovePrev(ref nCol, ref nRow, nRune);
                    }
                }

                ProcMovePrev(ref col, ref row, rune);

                if (fromCol != col || fromRow != row)
                    return (col, row);
                return null;
            } catch (Exception)
            {
                return null;
            }
        }

        bool isButtonShift;

        ///<inheritdoc/>
        public override bool MouseEvent(MouseEvent ev)
        {
            if (!ev.Flags.HasFlag(MouseFlags.Button1Clicked) && !ev.Flags.HasFlag(MouseFlags.Button1Pressed)
                && !ev.Flags.HasFlag(MouseFlags.Button1Pressed | MouseFlags.ReportMousePosition)
                && !ev.Flags.HasFlag(MouseFlags.Button1Released)
                && !ev.Flags.HasFlag(MouseFlags.Button1Pressed | MouseFlags.ButtonShift)
                && !ev.Flags.HasFlag(MouseFlags.WheeledDown) && !ev.Flags.HasFlag(MouseFlags.WheeledUp)
                && !ev.Flags.HasFlag(MouseFlags.Button1DoubleClicked)
                && !ev.Flags.HasFlag(MouseFlags.Button1DoubleClicked | MouseFlags.ButtonShift)
                && !ev.Flags.HasFlag(MouseFlags.Button1TripleClicked)
                && !ev.Flags.HasFlag(ContextMenu.MouseFlags))
            {
                return false;
            }

            if (!CanFocus)
            {
                return true;
            }

            if (!HasFocus)
            {
                SetFocus();
            }

            continuousFind = false;

            // Give autocomplete first opportunity to respond to mouse clicks
            if (SelectedLength == 0 && Autocomplete.MouseEvent(ev, true))
            {
                return true;
            }

            if (ev.Flags == MouseFlags.Button1Clicked)
            {
                if (shiftSelecting && !isButtonShift)
                {
                    StopSelecting();
                }
                ProcessMouseClick(ev, out _);
                PositionCursor();
                lastWasKill = false;
                columnTrack = currentColumn;
            } else if (ev.Flags == MouseFlags.WheeledDown)
            {
                lastWasKill = false;
                columnTrack = currentColumn;
                ScrollTo(topRow + 1);
            } else if (ev.Flags == MouseFlags.WheeledUp)
            {
                lastWasKill = false;
                columnTrack = currentColumn;
                ScrollTo(topRow - 1);
            } else if (ev.Flags == MouseFlags.WheeledRight)
            {
                lastWasKill = false;
                columnTrack = currentColumn;
                ScrollTo(leftColumn + 1, false);
            } else if (ev.Flags == MouseFlags.WheeledLeft)
            {
                lastWasKill = false;
                columnTrack = currentColumn;
                ScrollTo(leftColumn - 1, false);
            } else if (ev.Flags.HasFlag(MouseFlags.Button1Pressed | MouseFlags.ReportMousePosition))
            {
                ProcessMouseClick(ev, out List<Rune> line);
                PositionCursor();
                if (model.Count > 0 && shiftSelecting && selecting)
                {
                    if (currentRow - topRow + BottomOffset >= Frame.Height - 1
                        && model.Count + BottomOffset > topRow + currentRow)
                    {
                        ScrollTo(topRow + Frame.Height);
                    } else if (topRow > 0 && currentRow <= topRow)
                    {
                        ScrollTo(topRow - Frame.Height);
                    } else if (ev.Y >= Frame.Height)
                    {
                        ScrollTo(model.Count + BottomOffset);
                    } else if (ev.Y < 0 && topRow > 0)
                    {
                        ScrollTo(0);
                    }
                    if (currentColumn - leftColumn + RightOffset >= Frame.Width - 1
                        && line.Count + RightOffset > leftColumn + currentColumn)
                    {
                        ScrollTo(leftColumn + Frame.Width, false);
                    } else if (leftColumn > 0 && currentColumn <= leftColumn)
                    {
                        ScrollTo(leftColumn - Frame.Width, false);
                    } else if (ev.X >= Frame.Width)
                    {
                        ScrollTo(line.Count + RightOffset, false);
                    } else if (ev.X < 0 && leftColumn > 0)
                    {
                        ScrollTo(0, false);
                    }
                }
                lastWasKill = false;
                columnTrack = currentColumn;
            } else if (ev.Flags.HasFlag(MouseFlags.Button1Pressed | MouseFlags.ButtonShift))
            {
                if (!shiftSelecting)
                {
                    isButtonShift = true;
                    StartSelecting();
                }
                ProcessMouseClick(ev, out _);
                PositionCursor();
                lastWasKill = false;
                columnTrack = currentColumn;
            } else if (ev.Flags.HasFlag(MouseFlags.Button1Pressed))
            {
                if (shiftSelecting)
                {
                    StopSelecting();
                }
                ProcessMouseClick(ev, out _);
                PositionCursor();
                if (!selecting)
                {
                    StartSelecting();
                }
                lastWasKill = false;
                columnTrack = currentColumn;
                //if (Application.MouseGrabView == null)
                //{
                //    Application.GrabMouse(this);
                //}
            } else if (ev.Flags.HasFlag(MouseFlags.Button1Released))
            {
                Application.UngrabMouse();
            } else if (ev.Flags.HasFlag(MouseFlags.Button1DoubleClicked))
            {
                if (ev.Flags.HasFlag(MouseFlags.ButtonShift))
                {
                    if (!selecting)
                    {
                        StartSelecting();
                    }
                } else if (selecting)
                {
                    StopSelecting();
                }
                ProcessMouseClick(ev, out List<Rune> line);
                (int col, int row)? newPos;
                if (currentColumn == line.Count || (currentColumn > 0 && (line [currentColumn - 1] != ' '
                    || line [currentColumn] == ' ')))
                {

                    newPos = WordBackward(currentColumn, currentRow);
                    if (newPos.HasValue)
                    {
                        currentColumn = currentRow == newPos.Value.row ? newPos.Value.col : 0;
                    }
                }
                if (!selecting)
                {
                    StartSelecting();
                }
                newPos = WordForward(currentColumn, currentRow);
                if (newPos != null && newPos.HasValue)
                {
                    currentColumn = currentRow == newPos.Value.row ? newPos.Value.col : line.Count;
                }
                PositionCursor();
                lastWasKill = false;
                columnTrack = currentColumn;
            } else if (ev.Flags.HasFlag(MouseFlags.Button1TripleClicked))
            {
                if (selecting)
                {
                    StopSelecting();
                }
                ProcessMouseClick(ev, out List<Rune> line);
                currentColumn = 0;
                if (!selecting)
                {
                    StartSelecting();
                }
                currentColumn = line.Count;
                PositionCursor();
                lastWasKill = false;
                columnTrack = currentColumn;
            } else if (ev.Flags == ContextMenu.MouseFlags)
            {
                ContextMenu.Position = new Point(ev.X + 2, ev.Y + 2);
                ShowContextMenu();
            }

            return true;
        }

        void ProcessMouseClick(MouseEvent ev, out List<Rune> line)
        {
            List<Rune> r = null;
            if (model.Count > 0)
            {
                var maxCursorPositionableLine = Math.Max((model.Count - 1) - topRow, 0);
                if (Math.Max(ev.Y, 0) > maxCursorPositionableLine)
                {
                    currentRow = maxCursorPositionableLine + topRow;
                } else
                {
                    currentRow = Math.Max(ev.Y + topRow, 0);
                }
                r = GetCurrentLine();
                var idx = TextModel.GetColFromX(r, leftColumn, Math.Max(ev.X, 0), TabWidth);
                if (idx - leftColumn >= r.Count + RightOffset)
                {
                    currentColumn = Math.Max(r.Count - leftColumn + RightOffset, 0);
                } else
                {
                    currentColumn = idx + leftColumn;
                }
            }

            line = r;
        }

        ///<inheritdoc/>
        public override bool OnLeave(View view)
        {
            //if (Application.MouseGrabView != null && Application.MouseGrabView == this)
            //{
            //    Application.UngrabMouse();
            //}

            return base.OnLeave(view);
        }

        /// <summary>
        /// Allows clearing the <see cref="HistoryText.HistoryTextItem"/> items updating the original text.
        /// </summary>
        public void ClearHistoryChanges()
        {
            historyText?.Clear(Text);
        }
    }

    /// <summary>
    /// Renders an overlay on another view at a given point that allows selecting
    /// from a range of 'autocomplete' options.
    /// An implementation on a TextView.
    /// </summary>
    public class TextViewAutocomplete : Autocomplete
    {

        ///<inheritdoc/>
        protected override string GetCurrentWord()
        {
            var host = (TextView)HostControl;
            var currentLine = host.GetCurrentLine();
            var cursorPosition = Math.Min(host.CurrentColumn, currentLine.Count);
            return IdxToWord(currentLine, cursorPosition);
        }

        /// <inheritdoc/>
        protected override void DeleteTextBackwards()
        {
            ((TextView)HostControl).DeleteCharLeft();
        }

        /// <inheritdoc/>
        protected override void InsertText(string accepted)
        {
            ((TextView)HostControl).InsertText(accepted);
        }
    }
}
