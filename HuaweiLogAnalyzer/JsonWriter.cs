using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniversalLogAnalyzer
{
    public static class JsonWriter
    {
        private static readonly object _saveLock = new object();


        public static List<string> SaveAsJson(List<UniversalLogData> logs, List<string>? unparsed, string? outputFolder = null)
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

            // Serialize each UniversalLogData instance as a JSON file (human-readable)
            foreach (var log in logs)
            {
                var deviceFolderName = SharedUtilities.SanitizeFileName(SharedUtilities.GetDeviceFolderName(log));
                var deviceFolder = Path.Combine(logsFolder, deviceFolderName);
                Directory.CreateDirectory(deviceFolder);

                lock (_saveLock)
                {
                    var baseName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var file = Path.Combine(deviceFolder, $"Universal_Report_{baseName}.json");
                    int idx = 1;
                    while (File.Exists(file))
                    {
                        file = Path.Combine(deviceFolder, $"Universal_Report_{baseName}_{idx}.json");
                        idx++;
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(log, options);
                    File.WriteAllText(file, json, Encoding.UTF8);
                    savedFiles.Add(file);
                }
            }
            return savedFiles;
        }


        // JSON export data structures
        private class JsonExportData
        {
            public string? Device { get; set; }
            public string? SysName { get; set; }
            public string? Version { get; set; }
            public string? Esn { get; set; }
            public string? OriginalFileName { get; set; }
            public JsonResources Resources { get; set; } = new();
            public List<string> Licenses { get; set; } = new();
            public List<string> Modules { get; set; } = new();
            public List<JsonInterface> Interfaces { get; set; } = new();
            public List<JsonPortInfo> PortInfos { get; set; } = new();
            public List<string> Vlans { get; set; } = new();
            public List<string> Vpns { get; set; } = new();
            public List<string> BgpPeers { get; set; } = new();
            public List<JsonInterfaceCounter> InterfaceCounters { get; set; } = new();
            public List<JsonAnomaly> Anomalies { get; set; } = new();
            public JsonPerformance Performance { get; set; } = new();
            public JsonClustering Clustering { get; set; } = new();
            public List<string> ParseErrors { get; set; } = new();
            public DateTime GeneratedAt { get; set; }
        }

        private class JsonResources
        {
            public string? CpuUsage { get; set; }
            public string? MemoryUsage { get; set; }
            public string? DiskUsage { get; set; }
            public string? Voltage { get; set; }
            public string? Current { get; set; }
            public string? Power { get; set; }
            public string? Temperature { get; set; }
            public List<string> Alarms { get; set; } = new();
        }

        private class JsonInterface
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Ip { get; set; }
            public string? IpAddress { get; set; }
            public string? Mask { get; set; }
            public bool Shutdown { get; set; }
            public List<string> RawLines { get; set; } = new();
        }

        private class JsonPortInfo
        {
            public string? Port { get; set; }
            public string? Status { get; set; }
            public string? Type { get; set; }
        }

        private class JsonInterfaceCounter
        {
            public string? Interface { get; set; }
            public string? Phy { get; set; }
            public string? Protocol { get; set; }
            public string? InUti { get; set; }
            public string? OutUti { get; set; }
            public string? InErrors { get; set; }
            public string? OutErrors { get; set; }
        }

        private class JsonAnomaly
        {
            public string? Type { get; set; }
            public string? Description { get; set; }
            public string? Severity { get; set; }
        }

        private class JsonPerformance
        {
            public double AvgCpuUsage { get; set; }
            public double AvgMemoryUsage { get; set; }
            public double MaxInterfaceUtilization { get; set; }
            public int TotalErrors { get; set; }
        }

        private class JsonCluster
        {
            public string? ClusterName { get; set; }
            public List<string> Interfaces { get; set; } = new();
            public string? Description { get; set; }
            public double AvgUtilization { get; set; }
            public int TotalErrors { get; set; }
        }

        private class JsonClustering
        {
            public List<JsonCluster> Clusters { get; set; } = new();
        }
    }
}
