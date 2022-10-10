using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConDbgX
{
    static class TaskExtensionMethods
    {
        public static async void AwaitAndLog(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception)
            {
                // TODO: Log
            }
        }
    }
}
