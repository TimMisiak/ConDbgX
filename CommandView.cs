using System.Reactive.Disposables;
using System.Reactive.Linq;
using NStack;
using ReactiveUI;
using Terminal.Gui;
using ReactiveMarbles.ObservableEvents;
using System;
using System.Reactive;
using Attribute = Terminal.Gui.Attribute;
using System.Collections.Generic;
using TextView = ConDbgX.Text.TextView;

namespace ConDbgX
{
    public class CommandView : Window, IViewFor<CommandViewModel>
    {
        readonly CompositeDisposable _disposable = new CompositeDisposable();

        public CommandView(CommandViewModel viewModel) : base("ConDbgX")
        {
            ViewModel = viewModel;
            var outputView = OutputView();
            var prompt = PromptLabel(outputView);
            var inputView = CommandInput(prompt);

            // If we make it not focusable, you can't scroll.
            // outputView.CanFocus = false;
        }

        public CommandViewModel ViewModel { get; set; }

        protected override void Dispose(bool disposing)
        {
            _disposable.Dispose();
            base.Dispose(disposing);
        }

        TextView OutputView()
        {
            var attribute = new Attribute(Color.White, Color.Black);
            var textView = new TextView()
            {
                X = 0,
                Y = 0,
                Height = Dim.Fill() - 1,
                Width = Dim.Fill(),
                ReadOnly = true,
                ColorScheme = new ColorScheme()
                {
                    Normal = attribute,
                    Disabled = attribute,
                    Focus = attribute,
                    HotFocus = attribute,
                    HotNormal = attribute,
                }
            };

            // Originally I was binding to CommandOutput in the view model, but that will end up
            // being slow when the output is large
            /*
            ViewModel
                .WhenAnyValue(x => x.CommandOutput)
                .BindTo(textView, x => x.Text)
                .DisposeWith(_disposable);
            
            ViewModel
                .WhenAnyValue(x => x.CommandOutput)
                .Subscribe((x) =>
                {
                    textView.GetCurrentHeight(out int currentHeight);
                    int lines = x.Count("\n");
                    if (lines > currentHeight)
                    {
                        textView.ScrollTo(lines - currentHeight, true);
                    }
                })
                .DisposeWith(_disposable);
            */

            ViewModel.DmlOutput += (sender, args) =>
            {
                string output = args.Output;
                output = output.Replace("&gt;", ">");
                output = output.Replace("&lt;", "<");
                textView.AppendText(output);
            };

            Add(textView);
            return textView;
        }

        Label PromptLabel(View previous)
        {
            var label = new Label("0:000>")
            {
                X = Pos.Left(previous),
                Y = Pos.Bottom(previous),
                Width = 7,
            };
            ViewModel
                .WhenAnyValue(x => x.Prompt)
                .BindTo(label, x => x.Text)
                .DisposeWith(_disposable);

            Add(label);
            return label;
        }

        TextField CommandInput(View previous)
        {
            var commandInput = new TextField(ViewModel.CommandInput)
            {
                X = Pos.Right(previous),
                Y = Pos.Top(previous),
                Width = Dim.Fill(),
            };
            ViewModel
                .WhenAnyValue(x => x.CommandInput)
                .BindTo(commandInput, x => x.Text)
                .DisposeWith(_disposable);
            commandInput
                .Events()
                .TextChanged
                .Select(old => commandInput.Text)
                .DistinctUntilChanged()
                .BindTo(ViewModel, x => x.CommandInput)
                .DisposeWith(_disposable);
            commandInput
                .Events()
                .KeyPress
                .Where(x => x.KeyEvent.Key == Key.Enter)
                .InvokeCommand(ViewModel.Execute)
                .DisposeWith(_disposable);

            ViewModel.AutocompletedText += () =>
            {
                commandInput.CursorPosition = commandInput.Text.Length;
            };
            commandInput.KeyPress += (evt) =>
            {
                if (evt.KeyEvent.Key == Key.Tab)
                {
                    ViewModel.DoAutocomplete();
                    evt.Handled = true;
                }
            };
            Add(commandInput);

            return commandInput;
        }

        object IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = (CommandViewModel)value;
        }
    }
}