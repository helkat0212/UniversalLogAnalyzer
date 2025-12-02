using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UniversalLogAnalyzer
{
    public static class LogTypeDetector
    {
        /// <summary>
        /// Detects basic log "build/type" from file content heuristics.
        /// </summary>
        public static LogBuildType Detect(string filePath, int maxLines = 500)
        {
            try
            {
                var lines = ReadFirstLines(filePath, maxLines);
                if (lines == null || lines.Count == 0) return LogBuildType.Unknown;

                // Normalize a few things for matching
                var joined = string.Join("\n", lines);
                var lower = joined.ToLowerInvariant();

                // Common indicators for running / startup configuration dumps
                if (Regex.IsMatch(lower, @"(?m)^(\s*current\s+configuration|\s*building configuration|^!.*configuration|^system-view|display current-configuration)")
                    || lower.Contains("running-config") || lower.Contains("current configuration"))
                    return LogBuildType.RunningConfig;

                if (lower.Contains("startup-config") || lower.Contains("startup configuration"))
                    return LogBuildType.StartupConfig;

                // Tech-support or show tech outputs
                if (lower.Contains("show tech") || lower.Contains("show tech-support") || lower.Contains("tech-support") || lower.Contains("display diagnosis information") || lower.Contains("display version"))
                    return LogBuildType.TechSupport;

                // Show version outputs
                if (Regex.IsMatch(lower, @"(?m)^\s*version\s+|\bversion\b.+v[0-9]+|\bversion\b.+vrp|\bsoftware\b.*version"))
                    return LogBuildType.ShowVersion;

                // Show interfaces/statistics
                if (Regex.IsMatch(lower, @"(?m)^(\s*show\s+interfaces|^\s*display\s+interface|^\s*interface\s+.+)"))
                    return LogBuildType.ShowInterfaces;

                // Syslog-like patterns (RFC3164 style: "Mmm dd hh:mm:ss host ...")
                if (lines.Count > 0 && Regex.IsMatch(lines[0], @"^[A-Za-z]{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2}\s+"))
                    return LogBuildType.Syslog;

                // If file contains many '!' or 'interface' plus 'ip address' it's probably config
                var interfaceCount = Regex.Matches(lower, "\binterface\b").Count;
                var ipAddrCount = Regex.Matches(lower, "\bip address\b").Count;
                if (interfaceCount > 0 && ipAddrCount > 0)
                    return LogBuildType.RunningConfig;

                // Catch-all heuristics
                if (lower.Contains("configuration") || lower.Contains("hostname") || lower.Contains("acl") || lower.Contains("ip route"))
                    return LogBuildType.RunningConfig;

                return LogBuildType.Other;
            }
            catch
            {
                return LogBuildType.Unknown;
            }
        }

        private static List<string> ReadFirstLines(string filePath, int count)
        {
            var lines = new List<string>();
            try
            {
                using (var sr = new StreamReader(filePath))
                {
                    for (int i = 0; i < count && !sr.EndOfStream; i++)
                    {
                        var line = sr.ReadLine();
                        if (line == null) break;
                        lines.Add(line);
                    }
                }
            }
            catch { }
            return lines;
        }
    }
}
