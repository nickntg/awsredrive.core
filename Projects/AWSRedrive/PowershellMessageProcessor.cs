using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var scriptPath = configurationEntry.RedriveScript;
            
            logger.Debug($"Executing PowerShell script: {scriptPath}");
            logger.Trace($"Message size: {message?.Length ?? 0} chars");
            
            if (!File.Exists(scriptPath))
            {
                logger.Debug($"Script not found: {scriptPath}");
                throw new FileNotFoundException($"PowerShell script not found: {scriptPath}");
            }

            var stopwatch = Stopwatch.StartNew();
            
            using (var ps = PowerShell.Create())
            {
                var script = File.ReadAllText(scriptPath);
                logger.Trace($"Script loaded: {script.Length} chars");

                ps.AddScript(script).AddParameter("content", message);
                
                // Add attributes as parameters if available
                if (attributes != null && attributes.Count > 0)
                {
                    ps.AddParameter("attributes", attributes);
                    logger.Trace($"Added {attributes.Count} attributes as parameter");
                }

                var results = ps.Invoke();
                stopwatch.Stop();

                var hasErrors = ps.Streams.Error.Count > 0;
                
                // Collect output
                var sb = new StringBuilder();
                foreach (var result in results)
                {
                    sb.AppendLine(result.ToString());
                }
                var output = sb.ToString().TrimEnd();

                if (hasErrors)
                {
                    var errorSb = new StringBuilder();
                    foreach (var error in ps.Streams.Error)
                    {
                        errorSb.AppendLine(error.ToString());
                    }
                    var errors = errorSb.ToString().TrimEnd();
                    
                    logger.Debug($"Script completed with errors ({stopwatch.ElapsedMilliseconds}ms)");
                    logger.Trace($"Errors: {TruncateForLog(errors, 500)}");
                    
                    if (!string.IsNullOrEmpty(output))
                    {
                        logger.Trace($"Output: {TruncateForLog(output, 500)}");
                    }
                    
                    throw new InvalidOperationException($"PowerShell script failed: {TruncateForLog(errors, 200)}");
                }

                logger.Debug($"Script completed successfully ({stopwatch.ElapsedMilliseconds}ms)");
                
                if (!string.IsNullOrEmpty(output))
                {
                    logger.Trace($"Output ({output.Length} chars): {TruncateForLog(output, 500)}");
                }
                else
                {
                    logger.Trace("No output produced");
                }

                // Log warnings if any
                if (ps.Streams.Warning.Count > 0)
                {
                    foreach (var warning in ps.Streams.Warning)
                    {
                        logger.Trace($"Warning: {warning}");
                    }
                }
            }
        }

        private static string TruncateForLog(string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content))
                return "(empty)";

            if (content.Length <= maxLength)
                return content;

            return content.Substring(0, maxLength) + "...";
        }
    }
}