using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static UniversalLogAnalyzer.UniversalLogData;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Mikrotik RouterOS format parser
    /// Converts Mikrotik-specific log format to universal format
    /// </summary>
    public class MikrotikRouterOsParser : BaseLogParser
    {
        public override DeviceVendor Vendor => DeviceVendor.Mikrotik;
        public override string VendorName => "Mikrotik RouterOS";

        public override UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default)
        {
            var data = new UniversalLogData
            {
                Vendor = DeviceVendor.Mikrotik,
                OriginalFileName = System.IO.Path.GetFileName(filePath)
            };

            var currentInterface = (InterfaceInfo?)null;
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

                    // Device information
                    if (trimmed.StartsWith("identity:", StringComparison.OrdinalIgnoreCase))
                    {
                        data.SystemName = trimmed.Substring("identity:".Length).Trim();
                        if (string.IsNullOrEmpty(data.Device))
                            data.Device = data.SystemName;
                        successCount++;
                    }
                    else if (trimmed.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var verMatch = Regex.Match(trimmed, @"(\d+\.\d+[\d.]*)");
                        if (verMatch.Success)
                        {
                            data.Version = verMatch.Groups[1].Value;
                            successCount++;
                        }
                    }
                    else if (trimmed.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                    {
                        data.ModelNumber = trimmed.Substring("model:".Length).Trim();
                        successCount++;
                    }
                    else if (trimmed.StartsWith("serial number:", StringComparison.OrdinalIgnoreCase))
                    {
                        data.SerialNumber = trimmed.Substring("serial number:".Length).Trim();
                        successCount++;
                    }

                    // Interface handling for multiple formats: ether1, ether2, etc.
                    if (trimmed.StartsWith("ether") && (trimmed.Contains(":") || !trimmed.Contains("[")))
                    {
                        if (currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);

                        string ifName = trimmed.Split(':')[0].Trim();
                        currentInterface = new InterfaceInfo { Name = ifName };
                        // mark subinterfaces if named like ether1.10
                        if (currentInterface.Name.Contains('.'))
                        {
                            currentInterface.IsVirtual = true;
                            currentInterface.InterfaceType = "SubInterface";
                        }
                        // Check for inline properties like ether1: disabled true
                        if (trimmed.Contains("disabled"))
                        {
                            bool disabled = trimmed.Contains("true");
                            currentInterface.IsShutdown = disabled;
                            currentInterface.Status = disabled ? "DOWN" : "UP";
                        }
                        successCount++;
                        continue;
                    }

                    if (trimmed.StartsWith("[admin@", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains("interface ethernet"))
                    {
                        if (currentInterface != null)
                            data.Interfaces.Add(currentInterface);
                        currentInterface = null;
                        continue;
                    }

                    // Interface properties (with > prefix)
                    if (currentInterface != null && trimmed.StartsWith(">"))
                    {
                        var propLine = trimmed.Substring(1).Trim();

                        if (propLine.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.Name = propLine.Substring("name:".Length).Trim();
                            successCount++;
                        }
                        else if (propLine.StartsWith("disabled:", StringComparison.OrdinalIgnoreCase))
                        {
                            bool disabled = propLine.Substring("disabled:".Length).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                            currentInterface.IsShutdown = disabled;
                            currentInterface.Status = disabled ? "DOWN" : "UP";
                            successCount++;
                        }
                        else if (propLine.StartsWith("mtu:", StringComparison.OrdinalIgnoreCase))
                        {
                            var mtuMatch = Regex.Match(propLine, @"(\d+)");
                            if (mtuMatch.Success)
                            {
                                currentInterface.Speed = $"MTU {mtuMatch.Groups[1].Value}";
                                successCount++;
                            }
                        }
                        else if (propLine.IndexOf("vrrp", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var g = Regex.Match(propLine, @"vrrp\s*(\d+)", RegexOptions.IgnoreCase);
                            if (g.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp {g.Groups[1].Value}" : currentInterface.Description + $"; vrrp {g.Groups[1].Value}";
                            successCount++;
                        }
                        else if (propLine.IndexOf("vlan", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Match(propLine, @"(\d+)");
                            if (m.Success && !data.Vlans.Contains(m.Groups[1].Value)) data.Vlans.Add(m.Groups[1].Value);
                            successCount++;
                        }
                        else if (propLine.StartsWith("address:", StringComparison.OrdinalIgnoreCase))
                        {
                            var addrMatch = Regex.Match(propLine, @"(\d+\.\d+\.\d+\.\d+)/(\d+)");
                            if (addrMatch.Success)
                            {
                                currentInterface.Ip = addrMatch.Groups[1].Value;
                                currentInterface.Mask = addrMatch.Groups[2].Value;
                                currentInterface.IpAddress = $"{addrMatch.Groups[1].Value}/{addrMatch.Groups[2].Value}";
                                successCount++;
                            }
                        }
                    }

                    // Routing table
                    if (trimmed.StartsWith("[admin@", StringComparison.OrdinalIgnoreCase) &&
                        (trimmed.Contains("route") || trimmed.Contains("bgp")))
                    {
                        if (trimmed.Contains("bgp peer"))
                        {
                            var peerMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                            if (peerMatch.Success)
                            {
                                data.BgpPeers.Add(peerMatch.Groups[1].Value);
                                successCount++;
                            }
                        }
                    }

                    // IP firewall rules (ACLs)
                    if (trimmed.StartsWith("[admin@", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains("ip firewall"))
                    {
                        var ruleMatch = Regex.Match(trimmed, @"rule\s+([\d\w]+)");
                        if (ruleMatch.Success)
                        {
                            data.Acls.Add(ruleMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // VLAN
                    if (trimmed.Contains("vlan", StringComparison.OrdinalIgnoreCase))
                    {
                        var vlanMatch = Regex.Match(trimmed, @"vlan\s+(\d+)");
                        if (vlanMatch.Success)
                        {
                            data.Vlans.Add(vlanMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // Users
                    if (trimmed.StartsWith("[admin@", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains("user"))
                    {
                        var userMatch = Regex.Match(trimmed, @"user\s+(\S+)");
                        if (userMatch.Success)
                        {
                            data.LocalUsers.Add(userMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // NTP
                    if (trimmed.Contains("ntp", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains("server"))
                    {
                        var ntpMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                        if (ntpMatch.Success)
                        {
                            data.NtpServers.Add(ntpMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // CPU and memory resources
                    if (trimmed.StartsWith("cpu:", StringComparison.OrdinalIgnoreCase))
                    {
                        var cpuMatch = Regex.Match(trimmed, @"(\d+(?:\.\d+)?)\s*%");
                        if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, out var cpu))
                        {
                            data.Resources.CpuUsage = cpu;
                            successCount++;
                        }
                    }
                    // Network discovery heuristics: ARP / DHCP / LLDP / MAC extraction
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
                    if (trimmed.StartsWith("memory:", StringComparison.OrdinalIgnoreCase))
                    {
                        var memMatch = Regex.Match(trimmed, @"(\d+(?:\.\d+)?)\s*%");
                        if (memMatch.Success && double.TryParse(memMatch.Groups[1].Value, out var mem))
                        {
                            data.Resources.MemoryUsage = mem;
                            successCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    data.ParseErrors.Add($"Line {lineCount}: {ex.Message}");
                }
            }

            if (currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
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

            return content.Contains("mikrotik") ||
                   content.Contains("routeros") ||
                   content.Contains("identity:") ||
                   content.Contains("[admin@") ||
                   content.Contains("RouterOS");
        }

        public override int GetConfidenceScore(string filePath)
        {
            var firstLines = ReadFirstLines(filePath, 30);
            int score = 0;

            var content = string.Join("\n", firstLines).ToLower();

            if (content.Contains("mikrotik")) score += 40;
            if (content.Contains("routeros")) score += 35;
            if (content.Contains("identity:")) score += 20;
            if (Regex.IsMatch(content, @"\[admin@")) score += 20;
            if (content.Contains("interface ethernet")) score += 10;

            return Math.Min(100, score);
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
                    Description = $"Interface '{iface.Name}' is in shutdown/disabled state",
                    Severity = "High",
                    Recommendation = "Review if interface should be active. Use '/interface ethernet enable' command if interface should be operational.",
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
                    Recommendation = "Monitor with '/system resource print' and '/process print stats'. Optimize rules and queues.",
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
                    Recommendation = "Check memory with '/system resource print'. Consider reboot or disable unnecessary features.",
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
                    Description = "No firewall rules configured and minimal user accounts",
                    Severity = "High",
                    Recommendation = "Configure firewall rules under '/ip firewall filter' and create additional user accounts.",
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
                    Recommendation = "Configure NTP under '/system ntp client' for accurate time synchronization.",
                    IsVendorSpecific = true
                });
            }
        }
    }
}
