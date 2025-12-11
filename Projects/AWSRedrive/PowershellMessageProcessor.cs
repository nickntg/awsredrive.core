using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Text;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive
{
    public class PowerShellMessageProcessor : IMessageProcessor
    {
        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            using (var ps = PowerShell.Create())
            {
                var script = File.ReadAllText(configurationEntry.RedriveScript);

                var results = ps.AddScript(script).AddParameter("content", message).Invoke();

                var sb = new StringBuilder();
                foreach (var result in results)
                {
                    sb.AppendLine(result.ToString());
                }

                var logString = sb.ToString();
                if (!string.IsNullOrEmpty(logString))
                {
                    logger.Debug($"Script output: {logString}");
                }
                else
                {
                    logger.Debug("No script output was produced");
                }
            }
        }
    }
}
