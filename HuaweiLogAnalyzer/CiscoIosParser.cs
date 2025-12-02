using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static UniversalLogAnalyzer.UniversalLogData;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Cisco IOS format parser
    /// Converts Cisco-specific log format to universal format
    /// </summary>
    public class CiscoIosParser : BaseLogParser
    {
        public override DeviceVendor Vendor => DeviceVendor.Cisco;
        public override string VendorName => "Cisco IOS";

        public override UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default)
        {
            var data = new UniversalLogData
            {
                Vendor = DeviceVendor.Cisco,
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
                    bool isIndented = line.StartsWith(" ") || line.StartsWith("\t");

                    // Device information
                    if (trimmed.StartsWith("hostname ", StringComparison.OrdinalIgnoreCase))
                    {
                        data.SystemName = trimmed.Substring("hostname ".Length).Trim();
                        if (string.IsNullOrEmpty(data.Device))
                            data.Device = data.SystemName;
                        successCount++;
                    }
                    else if (trimmed.Contains("Cisco IOS Software", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.Contains("IOS (tm)", StringComparison.OrdinalIgnoreCase))
                    {
                        var verMatch = Regex.Match(trimmed, @"Version\s+([\d.]+)");
                        if (verMatch.Success)
                        {
                            data.Version = verMatch.Groups[1].Value;
                            successCount++;
                        }
                    }
                    else if (trimmed.StartsWith("Model Number", StringComparison.OrdinalIgnoreCase))
                    {
                        var modelPart = trimmed.Substring("Model Number".Length).Trim();
                        data.ModelNumber = modelPart.TrimStart(':').Trim();
                        successCount++;
                    }
                    else if (trimmed.StartsWith("Serial Number", StringComparison.OrdinalIgnoreCase))
                    {
                        var snPart = trimmed.Substring("Serial Number".Length).Trim();
                        data.SerialNumber = snPart.TrimStart(':').Trim();
                        successCount++;
                    }

                    // Interface handling
                    if (!isIndented && trimmed.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);

                        currentInterface = new InterfaceInfo
                        {
                            Name = trimmed.Substring("interface ".Length).Trim()
                        };
                        // mark subinterfaces and aggregation (Port-channel/Po)
                        if (currentInterface.Name.Contains('.'))
                        {
                            currentInterface.IsVirtual = true;
                            currentInterface.InterfaceType = "SubInterface";
                        }
                        else if (currentInterface.Name.StartsWith("Port-channel", StringComparison.OrdinalIgnoreCase) || currentInterface.Name.StartsWith("Po", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.InterfaceType = "Aggregation";
                        }
                        successCount++;
                        continue;
                    }

                    if (currentInterface != null && isIndented)
                    {
                        if (trimmed.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.Description = trimmed.Substring("description ".Length).Trim();
                            successCount++;
                        }
                        else if (trimmed.StartsWith("ip address ", StringComparison.OrdinalIgnoreCase))
                        {
                            var ipPart = trimmed.Substring("ip address ".Length).Trim();
                            var parts = ipPart.Split(' ');
                            if (parts.Length >= 2)
                            {
                                currentInterface.Ip = parts[0];
                                currentInterface.Mask = parts[1];
                                currentInterface.IpAddress = $"{parts[0]} {parts[1]}";
                            }
                            successCount++;
                        }
                        else if (trimmed.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IsShutdown = true;
                            currentInterface.Status = "DOWN";
                            successCount++;
                        }
                        else if (trimmed.StartsWith("no shutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IsShutdown = false;
                            currentInterface.Status = "UP";
                            successCount++;
                        }
                        else if (trimmed.IndexOf("vrrp ", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var g = Regex.Match(trimmed, @"vrrp\s+(\d+)", RegexOptions.IgnoreCase);
                            if (g.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp group {g.Groups[1].Value}" : currentInterface.Description + $"; vrrp group {g.Groups[1].Value}";
                            var ipm = Regex.Match(trimmed, @"ip\s+(\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                            if (ipm.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp ip {ipm.Groups[1].Value}" : currentInterface.Description + $"; vrrp ip {ipm.Groups[1].Value}";
                            var prm = Regex.Match(trimmed, @"priority\s+(\d+)", RegexOptions.IgnoreCase);
                            if (prm.Success)
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description) ? $"vrrp priority {prm.Groups[1].Value}" : currentInterface.Description + $"; vrrp priority {prm.Groups[1].Value}";
                            successCount++;
                        }
                        else if (trimmed.IndexOf("switchport trunk allowed vlan", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("switchport trunk allowed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Match(trimmed, @"vlan\s+(.+)$", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                var spec = m.Groups[1].Value;
                                foreach (var token in spec.Split(new[] {',',' '}, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (token.Contains("to"))
                                    {
                                        var parts = token.Split(new[] {'t','o'}, StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Trim()).Where(p=>p.Length>0).ToArray();
                                        if (parts.Length==2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
                                            for (int v=Math.Min(a,b); v<=Math.Max(a,b); v++) if(!data.Vlans.Contains(v.ToString())) data.Vlans.Add(v.ToString());
                                    }
                                    else if (int.TryParse(token, out var vid)) if(!data.Vlans.Contains(vid.ToString())) data.Vlans.Add(vid.ToString());
                                }
                            }
                            successCount++;
                        }
                        else if (trimmed.Contains("speed ", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.Contains("bandwidth ", StringComparison.OrdinalIgnoreCase))
                        {
                            var speedMatch = Regex.Match(trimmed, @"(\d+)\s*(Mbps|Gbps|Kbps)?");
                            if (speedMatch.Success)
                            {
                                currentInterface.Speed = trimmed;
                                successCount++;
                            }
                        }
                    }

                    // VLAN handling
                    if (trimmed.StartsWith("vlan ", StringComparison.OrdinalIgnoreCase))
                    {
                        var vlanMatch = Regex.Match(trimmed, @"vlan\s+(\d+)", RegexOptions.IgnoreCase);
                        if (vlanMatch.Success)
                        {
                            data.Vlans.Add(vlanMatch.Groups[1].Value);
                            successCount++;
                        }
                        if (!isIndented && currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);
                        currentInterface = null;
                    }

                    // BGP handling
                    if (trimmed.StartsWith("router bgp ", StringComparison.OrdinalIgnoreCase))
                    {
                        inBgp = true;
                        var asnMatch = Regex.Match(trimmed, @"router bgp (\d+)");
                        if (asnMatch.Success)
                        {
                            data.BgpAsn = asnMatch.Groups[1].Value;
                            successCount++;
                        }
                    }

                    if (inBgp && !isIndented && !trimmed.StartsWith("router bgp"))
                        inBgp = false;

                    if (inBgp && isIndented && trimmed.StartsWith("neighbor ", StringComparison.OrdinalIgnoreCase))
                    {
                        var neighborMatch = Regex.Match(trimmed, @"neighbor\s+(\S+)");
                        if (neighborMatch.Success)
                        {
                            data.BgpPeers.Add(neighborMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // ACL handling
                    if (trimmed.StartsWith("access-list ", StringComparison.OrdinalIgnoreCase))
                    {
                        var aclMatch = Regex.Match(trimmed, @"access-list\s+([\d\w]+)");
                        if (aclMatch.Success)
                        {
                            data.Acls.Add(aclMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // User handling
                    if (trimmed.StartsWith("username ", StringComparison.OrdinalIgnoreCase))
                    {
                        var userMatch = Regex.Match(trimmed, @"username\s+(\S+)");
                        if (userMatch.Success)
                        {
                            data.LocalUsers.Add(userMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // NTP handling
                    if (trimmed.StartsWith("ntp server ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("ntp peer ", StringComparison.OrdinalIgnoreCase))
                    {
                        data.NtpServers.Add(trimmed);
                        successCount++;
                    }

                    // Management IP
                    if (trimmed.StartsWith("ip route 0.0.0.0 0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                        (trimmed.Contains("management") && trimmed.Contains("ip address")))
                    {
                        var ipMatch = Regex.Match(trimmed, @"(\d+\.\d+\.\d+\.\d+)");
                        if (ipMatch.Success)
                        {
                            data.IpAddress = ipMatch.Groups[1].Value;
                            successCount++;
                        }
                    }

                    // Network discovery heuristics: IP+MAC, LLDP/CDP, DHCP bindings
                    try
                    {
                        // IP+MAC lines (ARP-like)
                        var ipMac = Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,60}([0-9a-fA-F]{2}(?:[:\\-][0-9a-fA-F]{2}){5})");
                        if (ipMac.Success)
                        {
                            var ip = ipMac.Groups[1].Value; var mac = ipMac.Groups[2].Value;
                            data.ArpTable.Add(new ArpEntry { Ip = ip, Mac = mac, Interface = currentInterface?.Name ?? string.Empty });
                            if (!data.MacAddresses.Contains(mac)) data.MacAddresses.Add(mac);
                            successCount++;
                        }

                        // LLDP / CDP neighbor lines
                        if (trimmed.IndexOf("cdp neighbor", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("lldp neighbor", StringComparison.OrdinalIgnoreCase) >= 0 || (trimmed.IndexOf("cdp", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf("neighbor", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            var m = Regex.Match(trimmed, @"(?:cdp|lldp).*?neighbor[:\s]+(\S+)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                var remote = m.Groups[1].Value;
                                data.LldpNeighbors.Add(new LldpNeighbor { LocalInterface = currentInterface?.Name ?? string.Empty, RemoteDevice = remote });
                                successCount++;
                            }
                        }

                        // DHCP bindings / lease lines
                        if (trimmed.IndexOf("dhcp" , StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("lease", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("binding", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var m = Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,80}([0-9a-fA-F]{2}(?:[:\\-][0-9a-fA-F]{2}){5})");
                            if (m.Success)
                            {
                                var ip = m.Groups[1].Value; var mac = m.Groups[2].Value;
                                data.DhcpLeases.Add(new DhcpLease { Ip = ip, Mac = mac });
                                if (!data.MacAddresses.Contains(mac)) data.MacAddresses.Add(mac);
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

            return content.Contains("cisco") ||
                   content.Contains("ios") ||
                   content.Contains("hostname") && content.Contains("interface") ||
                   content.Contains("router bgp") ||
                   Regex.IsMatch(content, @"show\s+(version|interfaces|running-config)");
        }

        public override int GetConfidenceScore(string filePath)
        {
            var firstLines = ReadFirstLines(filePath, 30);
            int score = 0;

            var content = string.Join("\n", firstLines).ToLower();

            if (content.Contains("cisco")) score += 35;
            if (content.Contains("ios")) score += 30;
            if (content.Contains("hostname")) score += 15;
            if (Regex.IsMatch(content, @"Cisco IOS Software|IOS \(tm\)")) score += 20;
            if (Regex.IsMatch(content, @"router bgp")) score += 10;

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
                    Description = $"Interface '{iface.Name}' is in shutdown state",
                    Severity = "High",
                    Recommendation = "Review if interface should be active. Use 'no shutdown' command if interface should be operational.",
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
                    Recommendation = "Investigate processes consuming CPU. Use 'show processes cpu' to identify heavy processes.",
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
                    Recommendation = "Monitor memory with 'show memory' command. Consider reloading if memory leaks detected.",
                    IsVendorSpecific = false
                });
            }

            // Check for interfaces without IP addresses
            var interfacesWithoutIp = data.Interfaces
                .Where(i => !i.IsShutdown && string.IsNullOrEmpty(i.Ip))
                .ToList();

            if (interfacesWithoutIp.Count > 0)
            {
                var names = string.Join(", ", interfacesWithoutIp.Select(i => i.Name));
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Missing IP Configuration",
                    Description = $"Interfaces without IP addresses detected: {names}",
                    Severity = "Medium",
                    Recommendation = "Configure IP addresses on active interfaces if they require routing.",
                    IsVendorSpecific = true
                });
            }

            // Check for lack of security controls
            if (data.Acls.Count == 0 && data.LocalUsers.Count < 2)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "Insufficient Access Controls",
                    Description = "No ACLs configured and minimal user accounts",
                    Severity = "High",
                    Recommendation = "Configure ACLs for traffic filtering and create additional user accounts for management.",
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
                    Recommendation = "Configure NTP servers using 'ntp server' command for accurate time synchronization.",
                    IsVendorSpecific = true
                });
            }
        }
    }
}
