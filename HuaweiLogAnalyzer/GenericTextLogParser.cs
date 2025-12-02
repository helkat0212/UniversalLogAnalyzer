using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static UniversalLogAnalyzer.UniversalLogData;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Generic text log parser - fallback for unknown vendor formats
    /// Extracts common elements from any text-based log file
    /// </summary>
    public class GenericTextLogParser : BaseLogParser
    {
        public override DeviceVendor Vendor => DeviceVendor.Unknown;
        public override string VendorName => "Generic Text Log";

        public override UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default)
        {
            var data = new UniversalLogData
            {
                Vendor = DeviceVendor.Unknown,
                OriginalFileName = System.IO.Path.GetFileName(filePath)
            };

            int lineCount = 0;
            int successCount = 0;
            var seenIps = new HashSet<string>();
            var seenInterfaces = new HashSet<string>();

            foreach (var line in ReadLines(filePath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineCount++;
                data.TotalLinesProcessed = lineCount;

                try
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var trimmed = line.Trim();

                    // Try to extract device name from various patterns
                    if (string.IsNullOrEmpty(data.Device) || data.Device == "Unknown")
                    {
                        if (trimmed.Contains("Device", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.Contains("Hostname", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.Contains("System", StringComparison.OrdinalIgnoreCase))
                        {
                            var deviceMatch = Regex.Match(trimmed, @"(?:Device|Hostname|System)[\s:=]+([^\s,;]+)");
                            if (deviceMatch.Success && !string.IsNullOrWhiteSpace(deviceMatch.Groups[1].Value))
                            {
                                data.Device = deviceMatch.Groups[1].Value;
                                successCount++;
                            }
                        }
                    }

                    // Extract version info
                    if (string.IsNullOrEmpty(data.Version))
                    {
                        var verMatch = Regex.Match(trimmed, @"[Vv]ersion[\s:=]+([^\s,;]+)");
                        if (verMatch.Success)
                        {
                            data.Version = verMatch.Groups[1].Value;
                            successCount++;
                        }
                    }

                    // Extract IP addresses
                    var ipMatches = Regex.Matches(trimmed, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                    foreach (Match ipMatch in ipMatches)
                    {
                        var ip = ipMatch.Groups[1].Value;
                        if (IsValidIpAddress(ip) && !seenIps.Contains(ip))
                        {
                            seenIps.Add(ip);
                            
                            // Set primary IP if not set
                            if (string.IsNullOrEmpty(data.IpAddress) && !IsPrivateIp(ip))
                                data.IpAddress = ip;

                            successCount++;
                        }
                    }

                    // Extract interface names
                    var interfaceMatches = Regex.Matches(trimmed, 
                        @"\b((?:eth|ether|Gi|Fa|Te|lo|vlan|port|interface)\d+(?:/\d+)*)\b", 
                        RegexOptions.IgnoreCase);
                    foreach (Match ifMatch in interfaceMatches)
                    {
                        var ifName = ifMatch.Groups[1].Value;
                        if (!seenInterfaces.Contains(ifName))
                        {
                            seenInterfaces.Add(ifName);
                            data.Interfaces.Add(new InterfaceInfo { Name = ifName });
                            successCount++;
                        }
                    }

                    // Extract VLAN IDs
                    if (trimmed.Contains("vlan", StringComparison.OrdinalIgnoreCase))
                    {
                        var vlanMatches = Regex.Matches(trimmed, @"vlan[\s:=]*(\d+)");
                        foreach (Match vlanMatch in vlanMatches)
                        {
                            var vlanId = vlanMatch.Groups[1].Value;
                            if (!data.Vlans.Contains(vlanId))
                            {
                                data.Vlans.Add(vlanId);
                                successCount++;
                            }
                        }
                    }

                    // Extract BGP info
                    if (trimmed.Contains("bgp", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("asn", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("autonomous", StringComparison.OrdinalIgnoreCase))
                    {
                        var asnMatch = Regex.Match(trimmed, @"(?:asn|as|autonomous.*?system)[\s:=]*(\d+)");
                        if (asnMatch.Success)
                        {
                            data.BgpAsn = asnMatch.Groups[1].Value;
                            successCount++;
                        }

                        var peerMatches = Regex.Matches(trimmed, @"peer[\s:=]*(\d+\.\d+\.\d+\.\d+)");
                        foreach (Match peerMatch in peerMatches)
                        {
                            var peer = peerMatch.Groups[1].Value;
                            if (!data.BgpPeers.Contains(peer))
                            {
                                data.BgpPeers.Add(peer);
                                successCount++;
                            }
                        }
                    }

                    // Extract resource utilization
                    if (trimmed.Contains("cpu", StringComparison.OrdinalIgnoreCase))
                    {
                        var cpuMatch = Regex.Match(trimmed, @"(\d+(?:\.\d+)?)\s*%");
                        if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, out var cpu))
                        {
                            data.Resources.CpuUsage = cpu;
                            successCount++;
                        }
                    }

                    if (trimmed.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("memory", StringComparison.OrdinalIgnoreCase))
                    {
                        var memMatch = Regex.Match(trimmed, @"memory[\s:=]*(\d+(?:\.\d+)?)\s*%");
                        if (memMatch.Success && double.TryParse(memMatch.Groups[1].Value, out var mem))
                        {
                            data.Resources.MemoryUsage = mem;
                            successCount++;
                        }
                    }

                    // Extract user accounts
                    if (trimmed.Contains("user", StringComparison.OrdinalIgnoreCase))
                    {
                        var userMatch = Regex.Match(trimmed, @"user[\s:=]+(\w+)");
                        if (userMatch.Success)
                        {
                            var user = userMatch.Groups[1].Value;
                            if (!data.LocalUsers.Contains(user))
                            {
                                data.LocalUsers.Add(user);
                                successCount++;
                            }
                        }
                    }

                    // Extract NTP servers
                    if (trimmed.Contains("ntp", StringComparison.OrdinalIgnoreCase))
                    {
                        var ntpMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                        if (ntpMatch.Success)
                        {
                            var ntp = ntpMatch.Groups[1].Value;
                            if (!data.NtpServers.Contains(ntp))
                            {
                                data.NtpServers.Add(ntp);
                                successCount++;
                            }
                        }
                    }

                    // Count interface errors and issues
                    if (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("down", StringComparison.OrdinalIgnoreCase))
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    data.ParseErrors.Add($"Line {lineCount}: {ex.Message}");
                }
            }

            data.SuccessfullyParsedLines = successCount;

            // Set device name from filename if not found
            if (string.IsNullOrEmpty(data.Device) || data.Device == "Unknown")
                data.Device = System.IO.Path.GetFileNameWithoutExtension(filePath);

            // Detect anomalies
            DetectAnomalies(data);

            return data;
        }

        public override bool CanParse(string filePath)
        {
            // Generic parser can handle any text file
            return true;
        }

        public override int GetConfidenceScore(string filePath)
        {
            // Always give low confidence - used as fallback
            return 5;
        }

        private bool IsValidIpAddress(string ip)
        {
            if (!System.Net.IPAddress.TryParse(ip, out _))
                return false;

            var parts = ip.Split('.');
            return parts.Length == 4 && 
                   parts.All(p => int.TryParse(p, out var n) && n >= 0 && n <= 255);
        }

        private bool IsPrivateIp(string ip)
        {
            var parts = ip.Split('.');
            if (parts.Length != 4)
                return false;

            if (!int.TryParse(parts[0], out var first))
                return false;

            // RFC 1918 private ranges
            return first == 10 ||
                   (first == 172 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31) ||
                   (first == 192 && int.TryParse(parts[1], out var third) && third == 168);
        }

        private void DetectAnomalies(UniversalLogData data)
        {
            // Check for duplicate IP addresses
            var ipGroups = data.Interfaces
                .Where(i => !string.IsNullOrEmpty(i.Ip))
                .GroupBy(i => i.Ip)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in ipGroups)
            {
                var interfaces = string.Join(", ", group.Select(i => i.Name));
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "IP Address Conflict",
                    Description = $"Duplicate IP address detected: {group.Key} on interfaces: {interfaces}",
                    Severity = "Critical",
                    Recommendation = "Resolve IP address duplication to prevent routing conflicts and connectivity issues.",
                    IsVendorSpecific = true
                });
            }

            // Check for high CPU usage
            if (data.Resources.CpuUsage > 80)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Performance",
                    Category = "High CPU Usage",
                    Description = $"High CPU usage detected: {data.Resources.CpuUsage:F2}%",
                    Severity = "High",
                    Recommendation = "Investigate and optimize high CPU consuming processes.",
                    IsVendorSpecific = false
                });
            }

            // Check for high memory usage
            if (data.Resources.MemoryUsage > 80)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Performance",
                    Category = "High Memory Usage",
                    Description = $"High memory usage detected: {data.Resources.MemoryUsage:F2}%",
                    Severity = "High",
                    Recommendation = "Monitor and optimize memory consumption. Consider reboot if memory leaks detected.",
                    IsVendorSpecific = false
                });
            }
        }
    }
}
