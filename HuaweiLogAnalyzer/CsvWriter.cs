using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalLogAnalyzer
{
    public static class CsvWriter
    {
        private static readonly object _saveLock = new object();


        private static void WriteSection(StreamWriter sw, string sectionName)
        {
            sw.WriteLine();
            sw.WriteLine("|----------------------" + sectionName + "----------------------|");
        }

        public static List<string> SaveAsCsv(List<UniversalLogData> logs, List<string>? unparsed, string? outputFolder = null)
        {
            // Determine base folder: use provided outputFolder if set, otherwise fallback to Downloads/Desktop
            string baseFolder;
            if (!string.IsNullOrWhiteSpace(outputFolder))
            {
                baseFolder = outputFolder!;
            }
            else
            {
                baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(baseFolder)) baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            // Normalize logs folder so it ends with a single "logs" segment (avoid nested logs\Logs)
            var trimmed = baseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parentForLogs;
            if (trimmed.EndsWith("logs", StringComparison.OrdinalIgnoreCase))
            {
                // remove the trailing "logs" segment and any trailing separators to get the parent
                parentForLogs = trimmed.Substring(0, trimmed.Length - "logs".Length).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                parentForLogs = trimmed;
            }

            // If parent becomes empty (root), keep trimmed as parent
            if (string.IsNullOrEmpty(parentForLogs)) parentForLogs = Path.GetPathRoot(trimmed) ?? trimmed;

            var logsFolder = Path.Combine(parentForLogs, "logs");
            Directory.CreateDirectory(logsFolder);

            var savedFiles = new List<string>();

            foreach (var log in logs)
            {
                // sanitize folder name to avoid invalid chars
                var deviceFolderName = SharedUtilities.SanitizeFileName(SharedUtilities.GetDeviceFolderName(log));
                var deviceFolder = Path.Combine(logsFolder, deviceFolderName);
                Directory.CreateDirectory(deviceFolder);

                lock (_saveLock)
                {
                    var baseName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var file = Path.Combine(deviceFolder, $"Universal_Report_{baseName}.csv");

                    // If file exists (very unlikely), append a numeric suffix
                    int idx = 1;
                    while (File.Exists(file))
                    {
                        file = Path.Combine(deviceFolder, $"Universal_Report_{baseName}_{idx}.csv");
                        idx++;
                    }

                    using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        WriteSection(sw, "DEVICE INFO");
                        sw.WriteLine("Property, Value");
                        sw.WriteLine($"Device, {EscapeCsv(log.Device)}");
                        sw.WriteLine($"SystemName, {EscapeCsv(log.SystemName)}");
                        sw.WriteLine($"Version, {EscapeCsv(log.Version)}");
                        sw.WriteLine($"Serial, {EscapeCsv(log.SerialNumber)}");

                        // System Resources
                        WriteSection(sw, "SYSTEM RESOURCES");
                        if (log.Resources.CpuUsage > 0)
                            sw.WriteLine($"CPU Usage, {EscapeCsv(log.Resources.CpuUsage.ToString())}");
                        if (log.Resources.MemoryUsage > 0)
                            sw.WriteLine($"Memory Usage, {EscapeCsv(log.Resources.MemoryUsage.ToString())}");
                        if (log.Resources.DiskUsage > 0)
                            sw.WriteLine($"Disk Usage, {EscapeCsv(log.Resources.DiskUsage.ToString())}");
                        if (!string.IsNullOrEmpty(log.Resources.Temperature))
                            sw.WriteLine($"Temperature, {EscapeCsv(log.Resources.Temperature)}");
                        
                        if (log.Resources.Alarms.Any())
                        {
                            WriteSection(sw, "ALARMS");
                            foreach (var alarm in log.Resources.Alarms)
                            {
                                sw.WriteLine(EscapeCsv(alarm));
                            }
                        }

                        WriteSection(sw, "LICENSES");
                        var licenses = new List<string>();
                        if (log.Licenses != null && log.Licenses.Count > 0) licenses.AddRange(log.Licenses);
                        else if (log.VendorSpecificData != null && log.VendorSpecificData.TryGetValue("Licenses", out var licObj) && licObj is IEnumerable<string> licList) licenses.AddRange(licList);
                        foreach (var license in licenses.Distinct()) sw.WriteLine(EscapeCsv(license));

                        WriteSection(sw, "MODULES");
                        var modules = new List<string>();
                        if (log.Modules != null && log.Modules.Count > 0) modules.AddRange(log.Modules);
                        else if (log.VendorSpecificData != null && log.VendorSpecificData.TryGetValue("Modules", out var modObj) && modObj is IEnumerable<string> modList) modules.AddRange(modList);
                        foreach (var module in modules.Distinct()) sw.WriteLine(EscapeCsv(module));

                        WriteSection(sw, "INTERFACES");
                        sw.WriteLine("Name, Description, IP, Mask, Shutdown");
                        foreach (var iface in log.Interfaces)
                        {
                            sw.WriteLine($"{EscapeCsv(iface.Name)}, {EscapeCsv(iface.Description)}, {EscapeCsv(iface.Ip)}, {EscapeCsv(iface.Mask)}, {(iface.IsShutdown ? "Yes" : "No")}");
                        }

                        WriteSection(sw, "PORTS AND INTERFACES");
                        sw.WriteLine("Port/Interface, Status, Type, Description, VPN, IP Address");
                        // PortInfos: if vendor-specific PortInfos exist, use them; otherwise map from interfaces
                        if (log.VendorSpecificData != null && log.VendorSpecificData.TryGetValue("PortInfos", out var portObj) && portObj is IEnumerable<dynamic> portInfos)
                        {
                            foreach (var p in portInfos)
                            {
                                var desc = string.Empty;
                                var vpn = string.Empty;
                                var ipAddress = string.Empty;
                                var iface = log.Interfaces.Find(i => string.Equals(i.Name, p.Port, StringComparison.OrdinalIgnoreCase));
                                if (iface != null)
                                {
                                    desc = iface.Description;
                                    ipAddress = iface.IpAddress;
                                    if (!string.IsNullOrEmpty(desc))
                                    {
                                        var vpnMatch = System.Text.RegularExpressions.Regex.Match(desc, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (vpnMatch.Success) vpn = vpnMatch.Groups[1].Value;
                                    }
                                }
                                sw.WriteLine($"{EscapeCsv(p.Port)}, {EscapeCsv(p.Status)}, {EscapeCsv(p.Type)}, {EscapeCsv(desc)}, {EscapeCsv(vpn)}, {EscapeCsv(ipAddress)}");
                            }
                        }
                        else
                        {
                            foreach (var iface in log.Interfaces)
                            {
                                var desc = iface.Description ?? string.Empty;
                                var vpn = string.Empty;
                                if (!string.IsNullOrEmpty(desc))
                                {
                                    var vpnMatch = System.Text.RegularExpressions.Regex.Match(desc, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (vpnMatch.Success) vpn = vpnMatch.Groups[1].Value;
                                }
                                sw.WriteLine($"{EscapeCsv(iface.Name)}, {EscapeCsv(iface.IsShutdown ? "DOWN" : "UP")}, , {EscapeCsv(desc)}, {EscapeCsv(vpn)}, {EscapeCsv(iface.IpAddress ?? string.Empty)}");
                            }
                        }

                            // Interface counters: extract lines from RawLines that look like counters
                            WriteSection(sw, "INTERFACE COUNTERS");
                            sw.WriteLine("Device, Interface, Metric, Value");
                            foreach (var iface in log.Interfaces)
                            {
                                foreach (var rawLine in iface.RawLines)
                                {
                                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                                    if (rawLine.Any(char.IsDigit) && System.Text.RegularExpressions.Regex.Split(rawLine.Trim(), "\\s+").Length >= 2)
                                    {
                                        var parts = System.Text.RegularExpressions.Regex.Split(rawLine.Trim(), "\\s+");
                                        var metric = parts.Length > 0 ? parts[0] : rawLine.Trim();
                                        var value = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
                                        sw.WriteLine($"{EscapeCsv(log.Device)}, {EscapeCsv(iface.Name)}, {EscapeCsv(metric)}, {EscapeCsv(value)}");
                                    }
                                }
                            }

                        if (unparsed != null && unparsed.Any())
                        {
                            WriteSection(sw, "UNPARSED DATA");
                            Console.WriteLine("WARNING: File contains unparsed data. Check the end of the report.");
                            foreach (var line in unparsed)
                            {
                                sw.WriteLine(EscapeCsv(line));
                            }
                        }
                    }

                    savedFiles.Add(file);
                }
            }

            return savedFiles;
        }

        private static string EscapeCsv(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

    }
}
