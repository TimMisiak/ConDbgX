using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DbgX;
using DbgX.Interfaces.Services;
using DbgX.Requests;
using DbgX.Requests.Initialization;
using NStack;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using static Terminal.Gui.View;

namespace ConDbgX
{
    [DataContract]
    public class CommandViewModel : ReactiveObject
    {
        DebugEngine m_engine;
        List<string> m_completions;
        int m_currentCompletion;

        public CommandViewModel()
        {
            Execute = ReactiveCommand.Create<KeyEventEventArgs>((x) => { x.Handled = true; });
            Execute.Subscribe(unit =>
            {
                var cmd = CommandInput.ToString();
                if (cmd == "q" || cmd =="qq")
                {
                    Environment.Exit(0);
                }
                m_engine.SendRequestAsync(new ExecuteRequest(cmd)).AwaitAndLog();
                CommandInput = ustring.Empty;
            });

            this.WhenAnyValue(x => x.CommandInput)
                .Subscribe(x =>
                {
                    if (m_completions != null && !m_completions.Contains(x.ToString()))
                    {
                        m_completions = null;
                    }
                });

            InitAsync().AwaitAndLog();
        }

        private async Task InitAsync()
        {
            m_engine = new DebugEngine();
            m_engine.DmlOutput += Engine_DmlOutput;
            m_engine.DebuggingState.PropertyChanged += DebuggingState_PropertyChanged;

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                var exe = args[1];
                // This is totally wrong, but close enough for now.
                var childArgs = string.Join(' ', args.Skip(2)
                                                     .Select(x => "\"" + x.Replace("\"", "\\\"") + "\""));
                await m_engine.SendRequestAsync(new CreateProcessRequest(exe, childArgs, new EngineOptions()));
            }
            await m_engine.SendRequestAsync(new ExecuteRequest(".prefer_dml 0"));
        }

        private void DebuggingState_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(m_engine.DebuggingState.EngineBusy))
            {
                UpdatePromptAsync().AwaitAndLog();
            }
        }

        private async Task UpdatePromptAsync()
        {
            Prompt = await m_engine.SendRequestAsync(new GetPromptTextRequest());
        }


        public event EventHandler<OutputEventArgs> DmlOutput;

        private void Engine_DmlOutput(object sender, OutputEventArgs e)
        {
            DmlOutput?.Invoke(this, e);
        }

        [Reactive, DataMember]
        public ustring CommandInput { get; set; } = ustring.Empty;

        [Reactive, DataMember]
        public ustring CommandOutput { get; set; } = ustring.Empty;


        [Reactive, DataMember]
        public ustring Prompt { get; set; } = ustring.Empty;

        [IgnoreDataMember]
        public ReactiveCommand<KeyEventEventArgs, Unit> Execute { get; }

        [IgnoreDataMember]
        public ReactiveCommand<KeyEventEventArgs, Unit> Autocomplete { get; }

        internal void DoAutocomplete()
        {
            if (m_completions == null)
            {
                DoAutocompleteAsync().AwaitAndLog();
            }
            else if (m_completions.Count > 0)
            {
                m_currentCompletion = (m_currentCompletion + 1) % m_completions.Count;
                UpdateCurrentCompletion();
            }
        }


        internal event Action AutocompletedText;
        private async Task DoAutocompleteAsync()
        {
            string text = CommandInput.ToString();
            var completion = await m_engine.SendRequestAsync(new ModelQueryRequest(text, true));
            XDocument xdoc = XDocument.Parse(completion);
            var replaceIndex = int.Parse(xdoc.Root.Attribute("ReplaceIndex").Value);
            var prefixString = text.Substring(0, replaceIndex);
            var replacements = xdoc.Root.Elements("Completion")
                                        .Select(x => prefixString + x.Attribute("Value").Value)
                                        .ToList();
            m_completions = replacements;
            m_currentCompletion = 0;
            UpdateCurrentCompletion();
        }

        private void UpdateCurrentCompletion()
        {
            if (m_completions.Count > 0)
            {
                CommandInput = m_completions[m_currentCompletion];
                AutocompletedText?.Invoke();
            }
        }

        public void ClearCompletions()
        {
            m_completions = null;
        }
    }
}