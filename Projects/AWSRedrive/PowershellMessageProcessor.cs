using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using NLog;

namespace AWSRedrive
{
    public class PowerShellMessageProcessor : IMessageProcessor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry)
        {
            using (var ps = PowerShell.Create())
            {
                var script = File.ReadAllText(configurationEntry.RedriveScript);

                // We expect that not-throwing an exception signifies a successful execution.
                // For debugging purposes, we'll assemble and show the output.
                var results = ps.AddScript(script).AddParameter("content", message).Invoke();

                var sb = new StringBuilder();
                foreach (var result in results)
                {
                    sb.AppendLine(result.ToString());
                }

                var logString = sb.ToString();
                Logger.Debug(!string.IsNullOrEmpty(logString)
                    ? $"Script output: {logString}"
                    : "No script output was produced");
            }
        }
    }
}