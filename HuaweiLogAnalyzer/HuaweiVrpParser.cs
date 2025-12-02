using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static UniversalLogAnalyzer.UniversalLogData;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Huawei VRP format parser
    /// Converts Huawei-specific log format to universal format
    /// </summary>
    public class HuaweiVrpParser : BaseLogParser
    {
        public override DeviceVendor Vendor => DeviceVendor.Huawei;
        public override string VendorName => "Huawei VRP";

        private static readonly Regex SlotPattern = new Regex(@"(\d+\/\d+\/\d+)", RegexOptions.Compiled);
        private static readonly Regex VpnPattern = new Regex(@"vpn(?:-instance|\s+instance)?\s*(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default)
        {
            var data = new UniversalLogData
            {
                Vendor = DeviceVendor.Huawei,
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
                    bool isTopLevel = !line.StartsWith(" ") && !line.StartsWith("\t");

                    // Device information parsing
                    if (trimmed.StartsWith("sysname", StringComparison.OrdinalIgnoreCase))
                    {
                        data.SystemName = trimmed.Substring("sysname".Length).Trim();
                        if (string.IsNullOrEmpty(data.Device))
                            data.Device = data.SystemName;
                        successCount++;
                    }
                    else if (trimmed.StartsWith("display version", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.Contains("Software Version"))
                    {
                        var versionMatch = Regex.Match(trimmed, @"V\d+R\d+C\d+", RegexOptions.IgnoreCase);
                        if (versionMatch.Success)
                        {
                            data.Version = versionMatch.Value;
                            successCount++;
                        }
                    }
                    else if (trimmed.StartsWith("SN:", StringComparison.OrdinalIgnoreCase))
                    {
                        data.SerialNumber = trimmed.Substring("SN:".Length).Trim();
                        successCount++;
                    }

                    // Interface handling
                    if (trimmed.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);

                        currentInterface = new InterfaceInfo
                        {
                            Name = trimmed.Substring("interface ".Length).Trim()
                        };
                        // mark subinterfaces / aggregated interfaces
                        if (currentInterface.Name.Contains('.'))
                        {
                            currentInterface.IsVirtual = true;
                            currentInterface.InterfaceType = "SubInterface";
                        }
                        else if (currentInterface.Name.StartsWith("Eth-Trunk", StringComparison.OrdinalIgnoreCase) || currentInterface.Name.StartsWith("eth-trunk", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.InterfaceType = "Aggregation";
                        }
                        successCount++;
                        continue;
                    }

                    if (currentInterface != null && !isTopLevel)
                    {
                        if (trimmed.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.Description = trimmed.Substring("description ".Length).Trim();
                            successCount++;
                        }
                        else if (trimmed.StartsWith("ip address ", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IpAddress = trimmed.Substring("ip address ".Length).Trim();
                            var parts = currentInterface.IpAddress.Split(' ');
                            if (parts.Length >= 2)
                            {
                                currentInterface.Ip = parts[0];
                                currentInterface.Mask = parts[1];
                            }
                            successCount++;
                        }
                        else if (trimmed.StartsWith("shutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            currentInterface.IsShutdown = true;
                            currentInterface.Status = "DOWN";
                            successCount++;
                        }
                        else if (trimmed.StartsWith("undo shutdown", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.StartsWith("no shutdown", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.Equals("undo shutdown", StringComparison.OrdinalIgnoreCase))
                        {
                            // Common vendor variants for enabling the interface
                            currentInterface.IsShutdown = false;
                            currentInterface.Status = "UP";
                            successCount++;
                        }
                        else if (trimmed.StartsWith("ip binding vpn-instance", StringComparison.OrdinalIgnoreCase))
                        {
                            // Capture VPN instance binding if present and append to description for reporting
                            var vpn = trimmed.Substring("ip binding vpn-instance".Length).Trim();
                            if (!string.IsNullOrEmpty(vpn))
                            {
                                if (string.IsNullOrEmpty(currentInterface.Description))
                                    currentInterface.Description = $"vpn-instance {vpn}";
                                else
                                    currentInterface.Description += $"; vpn-instance {vpn}";
                            }
                            successCount++;
                        }
                        else if (trimmed.IndexOf("vrrp", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // capture vrrp info (vrid, virtual-ip, priority)
                            var vrrpMatch = Regex.Match(trimmed, @"vrrp(?:\s+vrid)?\s*(\d+)", RegexOptions.IgnoreCase);
                            if (vrrpMatch.Success)
                            {
                                var vrid = vrrpMatch.Groups[1].Value;
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description)
                                    ? $"vrrp vrid {vrid}"
                                    : currentInterface.Description + $"; vrrp vrid {vrid}";
                            }
                            var vipMatch = Regex.Match(trimmed, @"virtual-ip\s+(\S+)", RegexOptions.IgnoreCase);
                            if (vipMatch.Success)
                            {
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description)
                                    ? $"vrrp virtual-ip {vipMatch.Groups[1].Value}"
                                    : currentInterface.Description + $"; vrrp virtual-ip {vipMatch.Groups[1].Value}";
                            }
                            var prMatch = Regex.Match(trimmed, @"priority\s+(\d+)", RegexOptions.IgnoreCase);
                            if (prMatch.Success)
                            {
                                currentInterface.Description = string.IsNullOrEmpty(currentInterface.Description)
                                    ? $"vrrp priority {prMatch.Groups[1].Value}"
                                    : currentInterface.Description + $"; vrrp priority {prMatch.Groups[1].Value}";
                            }
                            successCount++;
                        }
                        else if (trimmed.IndexOf("port trunk allow-pass vlan", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("port trunk allow-pass", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // extract VLAN list from trunk configuration on interface
                            var m = Regex.Match(trimmed, @"vlan\s+(.+)$", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                var vlanSpec = m.Groups[1].Value.Trim();
                                foreach (var token in vlanSpec.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (token.Contains("to"))
                                    {
                                        var parts = token.Split(new[] {'t','o'}, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length>0).ToArray();
                                        if (parts.Length==2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
                                        {
                                            for (int v = Math.Min(a,b); v <= Math.Max(a,b); v++) if (!data.Vlans.Contains(v.ToString())) data.Vlans.Add(v.ToString());
                                        }
                                    }
                                    else if (int.TryParse(token, out var vid))
                                    {
                                        if (!data.Vlans.Contains(vid.ToString())) data.Vlans.Add(vid.ToString());
                                    }
                                }
                            }
                            successCount++;
                        }
                    }

                    // VLAN handling - support single vlan and vlan batch
                    if (trimmed.StartsWith("vlan batch", StringComparison.OrdinalIgnoreCase))
                    {
                        // e.g. "vlan batch 100 to 115 1234 1623"
                        var spec = trimmed.Substring("vlan batch".Length).Trim();
                        var parts = spec.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.Contains("to"))
                            {
                                var rng = part.Split(new[] {'t','o'}, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p=>p.Length>0).ToArray();
                                if (rng.Length==2 && int.TryParse(rng[0], out var a) && int.TryParse(rng[1], out var b))
                                {
                                    for (int v = Math.Min(a,b); v <= Math.Max(a,b); v++) if (!data.Vlans.Contains(v.ToString())) data.Vlans.Add(v.ToString());
                                }
                            }
                            else if (int.TryParse(part, out var vid))
                            {
                                if (!data.Vlans.Contains(vid.ToString())) data.Vlans.Add(vid.ToString());
                            }
                        }
                        successCount++;
                        if (isTopLevel && currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);
                        currentInterface = null;
                    }
                    else if (trimmed.StartsWith("vlan ", StringComparison.OrdinalIgnoreCase))
                    {
                        var vlanMatch = Regex.Match(trimmed, @"\d+");
                        if (vlanMatch.Success)
                        {
                            if (!data.Vlans.Contains(vlanMatch.Value)) data.Vlans.Add(vlanMatch.Value);
                            successCount++;
                        }
                        if (isTopLevel && currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                            data.Interfaces.Add(currentInterface);
                        currentInterface = null;
                    }

                    // ip vpn-instance top-level
                    if (trimmed.StartsWith("ip vpn-instance", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(trimmed, @"ip vpn-instance\s+(\S+)", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var vpn = m.Groups[1].Value;
                            if (!data.VendorSpecificData.ContainsKey("VpnInstances")) data.VendorSpecificData["VpnInstances"] = new List<string>();
                            var list = data.VendorSpecificData["VpnInstances"] as List<string>;
                            if (list != null && !list.Contains(vpn)) list.Add(vpn);
                            successCount++;
                        }
                    }

                    // BGP handling
                    if (trimmed.StartsWith("bgp ", StringComparison.OrdinalIgnoreCase))
                    {
                        inBgp = true;
                        var peerMatch = Regex.Match(trimmed, @"peer (\S+)", RegexOptions.IgnoreCase);
                        if (peerMatch.Success)
                        {
                            data.BgpPeers.Add(peerMatch.Groups[1].Value);
                            successCount++;
                        }
                    }

                    if (inBgp && !isTopLevel && trimmed.Contains("peer", StringComparison.OrdinalIgnoreCase))
                    {
                        var pm = Regex.Match(trimmed, @"peer (\S+)", RegexOptions.IgnoreCase);
                        if (pm.Success)
                        {
                            data.BgpPeers.Add(pm.Groups[1].Value);
                            successCount++;
                        }
                    }

                    // ACL handling
                    if (trimmed.StartsWith("acl ", StringComparison.OrdinalIgnoreCase))
                    {
                        var aclId = Regex.Match(trimmed, @"\d+");
                        if (aclId.Success)
                        {
                            data.Acls.Add(aclId.Value);
                            successCount++;
                        }
                    }

                    // User handling
                    if (trimmed.StartsWith("local-user ", StringComparison.OrdinalIgnoreCase))
                    {
                        data.LocalUsers.Add(trimmed.Substring("local-user ".Length).Trim());
                        successCount++;
                    }

                    // NTP handling
                    if (trimmed.Contains("ntp-service", StringComparison.OrdinalIgnoreCase))
                    {
                        data.NtpServers.Add(trimmed);
                        successCount++;
                    }

                    // Resource handling
                    if (trimmed.Contains("CPU Usage", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("usage", StringComparison.OrdinalIgnoreCase))
                    {
                        var usageMatch = Regex.Match(trimmed, @"(\d+(?:\.\d+)?)\s*%");
                        if (usageMatch.Success && double.TryParse(usageMatch.Groups[1].Value, out var usage))
                        {
                            if (trimmed.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                                data.Resources.CpuUsage = usage;
                            else if (trimmed.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                                data.Resources.MemoryUsage = usage;
                            successCount++;
                        }
                    }
                // Generic network discovery heuristics: ARP / DHCP / LLDP / MAC extraction
                    // IP+MAC pattern (IP then MAC within short distance)
                    var ipMac = System.Text.RegularExpressions.Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,60}([0-9a-fA-F]{2}(?:[:-][0-9a-fA-F]{2}){5})");
                    if (ipMac.Success)
                    {
                        var ip = ipMac.Groups[1].Value;
                        var mac = ipMac.Groups[2].Value;
                        data.ArpTable.Add(new ArpEntry { Ip = ip, Mac = mac, Interface = currentInterface?.Name ?? string.Empty });
                        if (!data.MacAddresses.Contains(mac)) data.MacAddresses.Add(mac);
                        successCount++;
                    }

                    // LLDP / CDP neighbor lines - try to capture neighbor identifier
                    if ((trimmed.IndexOf("lldp neighbor", StringComparison.OrdinalIgnoreCase) >= 0) || (trimmed.IndexOf("cdp neighbor", StringComparison.OrdinalIgnoreCase) >= 0) || (trimmed.IndexOf("lldp", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf("neighbor", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"(?:lldp|cdp).*?neighbor[:\s]+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var remote = m.Groups[1].Value;
                            data.LldpNeighbors.Add(new LldpNeighbor { LocalInterface = currentInterface?.Name ?? string.Empty, RemoteDevice = remote });
                            successCount++;
                        }
                        else
                        {
                            // fallback: take first token as neighbor id
                            var parts = trimmed.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 2)
                            {
                                var remote = parts[parts.Length - 1];
                                data.LldpNeighbors.Add(new LldpNeighbor { LocalInterface = currentInterface?.Name ?? string.Empty, RemoteDevice = remote });
                                successCount++;
                            }
                        }
                    }

                    // DHCP lease/binding detection
                    if (trimmed.IndexOf("dhcp", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("lease", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("binding", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(trimmed, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b).{0,80}([0-9a-fA-F]{2}(?:[:-][0-9a-fA-F]{2}){5})");
                        if (m.Success)
                        {
                            var ip = m.Groups[1].Value; var mac = m.Groups[2].Value;
                            data.DhcpLeases.Add(new DhcpLease { Ip = ip, Mac = mac });
                            if (!data.MacAddresses.Contains(mac)) data.MacAddresses.Add(mac);
                            successCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    data.ParseErrors.Add($"Line {lineCount}: {ex.Message}");
                }
            }

            // Close last interface if open
            if (currentInterface != null && !data.Interfaces.Any(i => string.Equals(i.Name, currentInterface.Name, StringComparison.OrdinalIgnoreCase)))
                data.Interfaces.Add(currentInterface);

            data.SuccessfullyParsedLines = successCount;

            // Detect device name if not found
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

            return content.Contains("huawei") ||
                   content.Contains("vrp") ||
                   content.Contains("interface") && content.Contains("sysname") ||
                   content.Contains("display");
        }

        public override int GetConfidenceScore(string filePath)
        {
            var firstLines = ReadFirstLines(filePath, 30);
            int score = 0;

            var content = string.Join("\n", firstLines);

            if (content.IndexOf("huawei", StringComparison.OrdinalIgnoreCase) >= 0) score += 40;
            if (content.IndexOf("vrp", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (content.IndexOf("sysname", StringComparison.OrdinalIgnoreCase) >= 0) score += 15;
            if (Regex.IsMatch(content, @"V\d+R\d+C\d+", RegexOptions.IgnoreCase)) score += 20;

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
                    Recommendation = "Review if interface should be active. Use 'undo shutdown' command if interface should be operational.",
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
                    Recommendation = "Investigate processes consuming CPU. Consider performance optimization or load distribution.",
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
                    Recommendation = "Clear unused memory or route table entries. Consider upgrading memory if persistent.",
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

            // Check for missing BGP configuration
            if (data.Interfaces.Count > 0 && data.BgpPeers.Count == 0)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Missing BGP Configuration",
                    Description = "Device has interfaces but no BGP peers configured",
                    Severity = "Low",
                    Recommendation = "If BGP routing is required, configure BGP neighbors and ensure proper peer relationships.",
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
                    Recommendation = "Configure NTP servers for accurate time synchronization. Use 'ntp-service unicast-server' command.",
                    IsVendorSpecific = true
                });
            }
        }
    }
}
