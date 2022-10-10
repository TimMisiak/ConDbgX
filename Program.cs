using System.Reactive.Concurrency;
using ReactiveUI;
using Terminal.Gui;

namespace ConDbgX
{
    public static class Program
    {
        static void Main (string [] args)
        {
            Application.Init ();
            RxApp.MainThreadScheduler = TerminalScheduler.Default;
            RxApp.TaskpoolScheduler = TaskPoolScheduler.Default;
            Application.Run (new CommandView (new CommandViewModel ()));
            Application.Shutdown ();
        }
    }
}