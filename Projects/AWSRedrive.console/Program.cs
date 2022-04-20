using System;
using AWSRedrive.DI;

namespace AWSRedrive.console
{
    class Program
    {
        static void Main()
        {
            Injector.Inject();
            var orchestrator = Injector.GetOrchestrator();
            orchestrator.Start();
            Console.ReadLine();
            orchestrator.Stop();
        }

        protected Program() { }
    }
}