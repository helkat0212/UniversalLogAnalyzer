using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static UniversalLogAnalyzer.UniversalLogData;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Juniper Junos format parser
    /// Converts Juniper-specific log format to universal format
    /// </summary>
    public class JuniperJunosParser : BaseLogParser
    {
        public override DeviceVendor Vendor => DeviceVendor.Juniper;
        public override string VendorName => "Juniper Junos";

        public override UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default)
        {
            var data = new UniversalLogData
            {
                Vendor = DeviceVendor.Juniper,
                OriginalFileName = System.IO.Path.GetFileName(filePath)
            };

            var currentInterface = (InterfaceInfo?)null;
            bool inBgp = false;
            int lineCount = 0;
            int successCount = 0;

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
                    int indentLevel = GetIndentLevel(line);
                    bool isTopLevel = indentLevel == 0;

                    // Device information
                    if (trimmed.StartsWith("host-name", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(trimmed, @"host-name\s+(\S+);?");
                        if (match.Success)
                        {
                            data.SystemName = match.Groups[1].Value.TrimEnd(';');
                            if (string.IsNullOrEmpty(data.Device))
                                data.Device = data.SystemName;
                            successCount++;
                        }
                    }
                    else if (trimmed.Contains("Junos", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.Contains("JUNOS", StringComparison.OrdinalIgnoreCase))
                    {
                        var verMatch = Regex.Match(trimmed, @"(?:JUNOS|Junos)\s+([\d.]+)");
                        if (verMatch.Success)
                        {
                            data.Version = verMatch.Groups[1].Value;
                            successCount++;
                        }
                    }
                    else if (trimmed.Contains("Model", StringComparison.OrdinalIgnoreCase))
                    {
                        var modelMatch = Regex.Match(trimmed, @"Model:\s*([^\n]+)");
                        if (modelMatch.Success)
                        {
                            data.ModelNumber = modelMatch.Groups[1].Value.Trim();
                            successCount++;
                        }
                    }
                    else if (trimmed.Contains("Serial", StringComparison.OrdinalIgnoreCase))
                    {
                        var snMatch = Regex.Match(trimmed, @"Serial[:\s]+([^\n]+)");
                        if (snMatch.Success)
                        {
                            data.SerialNumber = snMatch.Groups[1].Value.Trim();
                            successCount++;
                        }
                    }

                    // Interface handling
                    if (isTopLevel && trimmed.StartsWith("interfaces", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Match interface names like ge-0/0/0 { or just ge-0/0/0
                    if ((isTopLevel || indentLevel == 1) && 
                        (trimmed.StartsWith("ge-") || trimmed.StartsWith("interface ") || 
                         trimmed.StartsWith("et-") || trimmed.StartsWith("xe-")))
                    {
                        if (currentInterface != null && !trimmed.Contains("{"))
                            if (!data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                                data.Interfaces.Add(currentInterface);

                        string ifName = trimmed;
                        if (trimmed.Contains("{"))
                            ifName = trimmed.Substring(0, trimmed.IndexOf('{')).Trim();
                        else if (trimmed.EndsWith(";"))
                            ifName = trimmed.TrimEnd(';').Trim();
                        else if (trimmed.StartsWith("interface "))
                            ifName = trimmed.Substring("interface ".Length).Trim();

                        currentInterface = new InterfaceInfo
                        {
                            Name = ifName
                        };
                        // mark subinterfaces and aggregation
                        if (currentInterface.Name.Contains('.'))
                        {
                            currentInterface.IsVirtual = true;
                            currentInterface.InterfaceType = "SubInterface";
                        }
                        else if (currentInterface.Name.StartsWith("ae", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.InterfaceType = "Aggregation";
                        }
                        successCount++;
                        if (!trimmed.Contains("{"))
                            continue;
                    }

                    if (currentInterface != null && indentLevel > 0)
                    {
                        if (trimmed.StartsWith("description", StringComparison.OrdinalIgnoreCase))
                        {
                            var descMatch = Regex.Match(trimmed, @"description\s+.+");
                            if (descMatch.Success)
                                currentInterface.Description = descMatch.Groups[1].Value.Trim();
                            successCount++;
                        }
                        else if (trimmed.Contains("address") && trimmed.Contains("/"))
                        {
                            var addrMatch = Regex.Match(trimmed, @"([\d.]+)/([\d]+)");
                            if (addrMatch.Success)
                            {
                                currentInterface.Ip = addrMatch.Groups[1].Value;
                                currentInterface.Mask = addrMatch.Groups[2].Value;
                                currentInterface.IpAddress = $"{addrMatch.Groups[1].Value}/{addrMatch.Groups[2].Value}";
                                successCount++;
                            }
                        }
                        else if (trimmed.StartsWith("disable", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IsShutdown = true;
                            currentInterface.Status = "DOWN";
                            successCount++;
                        }
                        else if (trimmed.StartsWith("enable", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IsShutdown = false;
                            currentInterface.Status = "UP";
                            successCount++;
                        }
                        else if (trimmed.StartsWith("speed", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.StartsWith("bandwidth", StringComparison.OrdinalIgnoreCase))
                        {
                            var speedMatch = Regex.Match(trimmed, @"([\d.]+)\s*(gbps|mbps)");
                            if (speedMatch.Success)
                            {
                                currentInterface.Speed = $"{speedMatch.Groups[1].Value} {speedMatch.Groups[2].Value}";
                                successCount++;
                            }
                        }
                        else if (trimmed.IndexOf("vrrp", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var g = Regex.Match(trimmed, @"vrrp\s*(?:group\s*)?(\d+)", RegexOptions.IgnoreCase);
                            if (g.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp group {g.Groups[1].Value}" : currentInterface.Description + $"; vrrp group {g.Groups[1].Value}";
                            var ipm = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                            if (ipm.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp ip {ipm.Groups[1].Value}" : currentInterface.Description + $"; vrrp ip {ipm.Groups[1].Value}";
                            successCount++;
                        }
                        else if (trimmed.IndexOf("vlan", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Matches(trimmed, @"(\d+)(?:-(\d+))?");
                            foreach (Match mm in m)
                            {
                                if (mm.Groups[2].Success)
                                {
                                    int a = int.Parse(mm.Groups[1].Value);
                                    int b = int.Parse(mm.Groups[2].Value);
                                    for (int v = Math.Min(a, b); v <= Math.Max(a, b); v++) if (!data.Vlans.Contains(v.ToString())) data.Vlans.Add(v.ToString());
                                }
                                else
                                {
                                    if (!data.Vlans.Contains(mm.Groups[1].Value)) data.Vlans.Add(mm.Groups[1].Value);
                                }
                            }
                            successCount++;
                        }
                    }

                    // BGP handling
                    if (trimmed.Contains("autonomous-system", StringComparison.OrdinalIgnoreCase))
                    {
                        var asnMatch = Regex.Match(trimmed, @"autonomous-system\s+(\d+)");
                        if (asnMatch.Success)
                        {
                            data.BgpAsn = asnMatch.Groups[1].Value;
                            successCount++;
                        }
                    }

                    if (isTopLevel && trimmed.StartsWith("protocols", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (isTopLevel && trimmed.Contains("bgp", StringComparison.OrdinalIgnoreCase))
                    {
                        inBgp = true;
                    }

                    if (inBgp && !isTopLevel && trimmed.Contains("neighbor", StringComparison.OrdinalIgnoreCase))
                    {
                        var neighborMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                        if (neighborMatch.Success)
                        {
                            data.BgpPeers.Add(neighborMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // VLAN handling
                    if (trimmed.Contains("vlan", StringComparison.OrdinalIgnoreCase))
                    {
                        var vlanMatch = Regex.Match(trimmed, @"vlan[id-]*\s*(\d+)");
                        if (vlanMatch.Success)
                        {
                            data.Vlans.Add(vlanMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // ACL (firewall filter) handling
                    if (trimmed.StartsWith("firewall", StringComparison.OrdinalIgnoreCase))
                    {
                        var filterMatch = Regex.Match(trimmed, @"filter\s+(\S+)");
                        if (filterMatch.Success)
                        {
                            data.Acls.Add(filterMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // User handling
                    if (trimmed.StartsWith("user ", StringComparison.OrdinalIgnoreCase))
                    {
                        var userMatch = Regex.Match(trimmed, @"user\s+(\S+)");
                        if (userMatch.Success)
                        {
                            data.LocalUsers.Add(userMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // NTP handling
                    if (trimmed.StartsWith("ntp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (trimmed.Contains("server") || trimmed.Contains("peer"))
                        {
                            var ntpMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                            if (ntpMatch.Success)
                            {
                                data.NtpServers.Add(ntpMatch.Groups[1].Value);
                                successCount++;
                            }
                        }
                    }

                    // Network discovery heuristics: ARP/DHCP/LLDP/MAC
                    try
                    {
                        var ipMac = Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,60}([0-9a-fA-F]{2}(?:[:\\-][0-9a-fA-F]{2}){5})");
                        if (ipMac.Success)
                        {
                            var ip = ipMac.Groups[1].Value; var mac = ipMac.Groups[2].Value;
                            data.ArpTable.Add(new ArpEntry { Ip = ip, Mac = mac, Interface = currentInterface?.Name ?? string.Empty });
                            if (!data.MacAddresses.Contains(mac)) data.MacAddresses.Add(mac);
                            successCount++;
                        }

                        if (trimmed.IndexOf("lldp", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf("neighbor", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Match(trimmed, @"lldp.*?neighbor[:\s]+(\S+)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                data.LldpNeighbors.Add(new LldpNeighbor { LocalInterface = currentInterface?.Name ?? string.Empty, RemoteDevice = m.Groups[1].Value });
                                successCount++;
                            }
                        }

                        if (trimmed.IndexOf("dhcp", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("lease", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,80}([0-9a-fA-F]{2}(?:[:\\-][0-9a-fA-F]{2}){5})");
                            if (m.Success)
                            {
                                data.DhcpLeases.Add(new DhcpLease { Ip = m.Groups[1].Value, Mac = m.Groups[2].Value });
                                if (!data.MacAddresses.Contains(m.Groups[2].Value)) data.MacAddresses.Add(m.Groups[2].Value);
                                successCount++;
                            }
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    data.ParseErrors.Add($"Line {lineCount}: {ex.Message}");
                }
            }

            if (currentInterface != null)
                data.Interfaces.Add(currentInterface);

            data.SuccessfullyParsedLines = successCount;

            if (string.IsNullOrEmpty(data.Device))
                data.Device = System.IO.Path.GetFileNameWithoutExtension(filePath);

            // Detect anomalies
            DetectAnomalies(data);

            return data;
        }

        public override bool CanParse(string filePath)
        {
            var firstLines = ReadFirstLines(filePath);
            var content = string.Join("\n", firstLines).ToLower();

            return content.Contains("juniper") ||
                   content.Contains("junos") ||
                   content.Contains("host-name") ||
                   Regex.IsMatch(content, @"show\s+(version|configuration|interfaces)") ||
                   content.Contains("configuration") && content.Contains("interfaces");
        }

        public override int GetConfidenceScore(string filePath)
        {
            var firstLines = ReadFirstLines(filePath, 30);
            int score = 0;

            var content = string.Join("\n", firstLines).ToLower();

            if (content.Contains("juniper")) score += 40;
            if (content.Contains("junos")) score += 35;
            if (content.Contains("host-name")) score += 15;
            if (Regex.IsMatch(content, @"Configuration\s+\{")) score += 15;
            if (content.Contains("autonomous-system")) score += 10;

            return Math.Min(100, score);
        }

        /// <summary>
        /// Calculate indentation level (Juniper uses { } for hierarchy)
        /// </summary>
        private int GetIndentLevel(string line)
        {
            int level = 0;
            foreach (var ch in line)
            {
                if (ch == ' ' || ch == '\t')
                    level++;
                else
                    break;
            }
            return level / 4; // 4 spaces = 1 level
        }

        private void DetectAnomalies(UniversalLogData data)
        {
            // Check for shutdown interfaces
            var shutdownInterfaces = data.Interfaces.Where(i => i.IsShutdown).ToList();
            foreach (var iface in shutdownInterfaces)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Interface Status",
                    Description = $"Interface '{iface.Name}' is in shutdown state",
                    Severity = "High",
                    Recommendation = "Review if interface should be active. Use 'delete interfaces <name> disable' if interface should be operational.",
                    IsVendorSpecific = true
                });
            }

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
                    Recommendation = "Monitor CPU load with 'show system processes extensive'. Optimize configuration or redistribute load.",
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
                    Recommendation = "Check memory utilization with 'show system memory'. Consider reboot if memory leaks suspected.",
                    IsVendorSpecific = false
                });
            }

            // Check for lack of security controls
            if (data.Acls.Count == 0 && data.LocalUsers.Count < 2)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "Insufficient Access Controls",
                    Description = "No filters (ACLs) configured and minimal user accounts",
                    Severity = "High",
                    Recommendation = "Configure firewall filters for traffic filtering and create additional user accounts for management.",
                    IsVendorSpecific = true
                });
            }

            // Check for lack of NTP configuration
            if (data.NtpServers.Count == 0)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "No NTP Server",
                    Description = "No NTP server configured for time synchronization",
                    Severity = "Medium",
                    Recommendation = "Configure NTP servers under 'system ntp' for accurate time synchronization.",
                    IsVendorSpecific = true
                });
            }
        }
    }
}
