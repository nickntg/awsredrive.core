using System;
using AWSRedrive.DI;

namespace AWSRedrive.console
{
    class Program
    {
        static void Main()
        {
            var orchestrator = Injector.GetOrchestrator();
            orchestrator.Start();
            Console.ReadLine();
            orchestrator.Stop();
        }
    }
}