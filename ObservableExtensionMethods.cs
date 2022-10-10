using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConDbgX
{
    static class ObservableExtensionMethods
    {
        public static IObservable<Unit> ToSignal<TDontCare>(this IObservable<TDontCare> source)
            => source.Select(_ => Unit.Default);
    }
}
