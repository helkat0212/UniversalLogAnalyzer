using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Universal anomaly detection engine
    /// Applies vendor-agnostic rules that work for any network device
    /// </summary>
    public static class AnomalyDetector
    {
        /// <summary>
        /// Detect universal anomalies (not vendor-specific)
        /// </summary>
        public static void DetectAnomalies(UniversalLogData data)
        {
            data.Anomalies.Clear();

            // Security anomalies
            DetectSecurityAnomalies(data);

            // Performance anomalies
            DetectPerformanceAnomalies(data);

            // Configuration anomalies
            DetectConfigurationAnomalies(data);

            // Calculate health score based on anomalies
            CalculateHealthScore(data);
        }

        private static void DetectSecurityAnomalies(UniversalLogData data)
        {
            // 1. Interfaces with IP but no ACL
            foreach (var iface in data.Interfaces)
            {
                if (!string.IsNullOrEmpty(iface.IpAddress) && iface.Ip != "127.0.0.1" && !data.Acls.Any())
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Access Control",
                        Description = $"Interface {iface.Name} ({iface.IpAddress}) has IP configured but no ACLs found on device",
                        Severity = "High",
                        Recommendation = "Configure access control lists (ACLs) to restrict access to this interface",
                        IsVendorSpecific = false
                    });
                }
            }

            // 2. Default or weak credentials (generic patterns)
            foreach (var user in data.LocalUsers)
            {
                if (IsWeakCredential(user))
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Authentication",
                        Description = $"Potential weak or default credential detected: {user}",
                        Severity = "High",
                        Recommendation = "Use strong, unique passwords and consider two-factor authentication",
                        IsVendorSpecific = false
                    });
                }
            }

            // 3. Routing protocols without authentication (if detected)
            if (data.BgpPeers.Any() && !data.Acls.Any())
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "Routing Security",
                    Description = "Dynamic routing peers detected but no access controls found",
                    Severity = "High",
                    Recommendation = "Implement route filtering and authentication for routing protocols (BGP MD5, etc.)",
                    IsVendorSpecific = false
                });
            }

            // 4. Management IP exposed (if IP found)
            if (!string.IsNullOrEmpty(data.IpAddress) && !IsPrivateIp(data.IpAddress))
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "Network Exposure",
                    Description = $"Device management IP {data.IpAddress} appears to be public/routable",
                    Severity = "Medium",
                    Recommendation = "Ensure management interface is only accessible from trusted networks",
                    IsVendorSpecific = false
                });
            }

            // 5. BGP misconfigurations
            DetectBgpMisconfigurations(data);

            // 6. Exposed services
            DetectExposedServices(data);

            // 7. Weak encryption
            DetectWeakEncryption(data);

            // 8. Vendor-specific security issues
            DetectVendorSpecificSecurityIssues(data);
        }

        private static void DetectPerformanceAnomalies(UniversalLogData data)
        {
            // 1. High CPU usage
            if (data.Resources.CpuUsage > 80)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Performance",
                    Category = "CPU",
                    Description = $"High CPU utilization: {data.Resources.CpuUsage:F1}%",
                    Severity = data.Resources.CpuUsage > 95 ? "Critical" : "High",
                    Recommendation = "Monitor processes, optimize configuration, or consider hardware upgrade",
                    IsVendorSpecific = false
                });
            }

            // 2. High memory usage
            if (data.Resources.MemoryUsage > 85)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Performance",
                    Category = "Memory",
                    Description = $"High memory utilization: {data.Resources.MemoryUsage:F1}%",
                    Severity = data.Resources.MemoryUsage > 95 ? "Critical" : "High",
                    Recommendation = "Check for memory leaks, restart services, or upgrade memory",
                    IsVendorSpecific = false
                });
            }

            // 3. High disk usage
            if (data.Resources.DiskUsage > 90)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Performance",
                    Category = "Disk",
                    Description = $"High disk utilization: {data.Resources.DiskUsage:F1}%",
                    Severity = "High",
                    Recommendation = "Clean up old logs, increase storage, or archive data",
                    IsVendorSpecific = false
                });
            }

            // 4. High errors on interfaces
            foreach (var iface in data.Interfaces)
            {
                long totalErrors = iface.InErrors + iface.OutErrors;
                if (totalErrors > 100)
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Performance",
                        Category = "Interface Health",
                        Description = $"High error count on interface {iface.Name}: In={iface.InErrors}, Out={iface.OutErrors}",
                        Severity = totalErrors > 1000 ? "High" : "Medium",
                        Recommendation = "Check cable connections, driver versions, and interface configuration",
                        IsVendorSpecific = false
                    });
                }
            }

            // 5. High interface utilization
            var highUtilInterfaces = data.Interfaces.Where(i =>
                i.InUtilization > 85 || i.OutUtilization > 85
            ).ToList();

            if (highUtilInterfaces.Any())
            {
                foreach (var iface in highUtilInterfaces)
                {
                    double maxUtil = Math.Max(iface.InUtilization, iface.OutUtilization);
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Performance",
                        Category = "Bandwidth",
                        Description = $"High bandwidth utilization on {iface.Name}: {maxUtil:F1}%",
                        Severity = maxUtil > 95 ? "High" : "Medium",
                        Recommendation = "Monitor traffic patterns, consider link aggregation or upgrade bandwidth",
                        IsVendorSpecific = false
                    });
                }
            }
        }

        private static void DetectConfigurationAnomalies(UniversalLogData data)
        {
            // 1. No NTP configured
            if (!data.NtpServers.Any())
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Time Sync",
                    Description = "No NTP servers configured",
                    Severity = "Low",
                    Recommendation = "Configure NTP for accurate time synchronization (essential for logs and security)",
                    IsVendorSpecific = false
                });
            }

            // 2. No DNS configured (if can be detected)
            // This would need vendor-specific detection

            // 3. Many errors parsing log
            if (data.ParseErrors.Count > 0)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Log Format",
                    Description = $"Parser encountered {data.ParseErrors.Count} parsing errors",
                    Severity = "Low",
                    Recommendation = "Review log format, ensure it matches expected device output",
                    IsVendorSpecific = false
                });
            }

            // 4. No users configured
            if (!data.LocalUsers.Any())
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Access",
                    Description = "No local users found (device may not be fully configured)",
                    Severity = "Low",
                    Recommendation = "Ensure administrative users are properly configured",
                    IsVendorSpecific = false
                });
            }

            // 5. Many interfaces but few active
            var activeInterfaces = data.Interfaces.Count(i => i.Status == "UP" && !i.IsShutdown);
            if (data.Interfaces.Count > 10 && activeInterfaces < data.Interfaces.Count * 0.3)
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Configuration",
                    Category = "Interface Status",
                    Description = $"Device has {data.Interfaces.Count} interfaces but only {activeInterfaces} are active",
                    Severity = "Low",
                    Recommendation = "Review shutdown interfaces to ensure configuration is intentional",
                    IsVendorSpecific = false
                });
            }
        }

        private static void CalculateHealthScore(UniversalLogData data)
        {
            double healthScore = 100;

            foreach (var anomaly in data.Anomalies)
            {
                int penalty = anomaly.Severity switch
                {
                    "Critical" => 25,
                    "High" => 15,
                    "Medium" => 8,
                    "Low" => 3,
                    _ => 1
                };
                healthScore -= penalty;
            }

            data.Performance.HealthScore = Math.Max(0, Math.Min(100, healthScore));
        }

        private static bool IsWeakCredential(string user)
        {
            var weakPatterns = new[]
            {
                "admin", "password", "123456", "12345678", "qwerty",
                "root", "test", "guest", "default", "123"
            };

            var lowerUser = user.ToLower();
            return weakPatterns.Any(p => lowerUser.Contains(p));
        }

        private static bool IsPrivateIp(string ip)
        {
            // Check for private IP ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 127.0.0.1
            if (ip.StartsWith("10.") || ip.StartsWith("127.") || ip == "localhost")
                return true;
            if (ip.StartsWith("172."))
            {
                var parts = ip.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var secondOctet))
                    return secondOctet >= 16 && secondOctet <= 31;
            }
            if (ip.StartsWith("192.168."))
                return true;
            return false;
        }

        private static void DetectBgpMisconfigurations(UniversalLogData data)
        {
            // Check for BGP peers without authentication
            foreach (var peer in data.BgpPeers)
            {
                if (!peer.Contains("MD5") && !peer.Contains("auth"))
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "BGP Security",
                        Description = $"BGP peer {peer} configured without authentication",
                        Severity = "High",
                        Recommendation = "Enable BGP MD5 authentication or use IPsec for BGP sessions",
                        IsVendorSpecific = false
                    });
                }
            }

            // Check for default ASN usage
            var defaultAsns = new[] { "64512", "65535" };
            foreach (var peer in data.BgpPeers)
            {
                foreach (var defaultAsn in defaultAsns)
                {
                    if (peer.Contains($"AS{defaultAsn}") || peer.Contains(defaultAsn))
                    {
                        data.Anomalies.Add(new AnomalyInfo
                        {
                            Type = "Configuration",
                            Category = "BGP",
                            Description = $"BGP peer using default ASN {defaultAsn}",
                            Severity = "Medium",
                            Recommendation = "Use a properly assigned ASN from your RIR",
                            IsVendorSpecific = false
                        });
                    }
                }
            }

            // Check for missing route filtering
            if (data.BgpPeers.Any() && !data.Acls.Any(a => a.Contains("prefix-list") || a.Contains("route-map")))
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "BGP Filtering",
                    Description = "BGP configured but no route filtering (prefix-lists or route-maps) found",
                    Severity = "High",
                    Recommendation = "Implement inbound and outbound route filtering to prevent route leaks and hijacks",
                    IsVendorSpecific = false
                });
            }
        }

        private static void DetectExposedServices(UniversalLogData data)
        {
            // Check for Telnet services (insecure)
            foreach (var iface in data.Interfaces)
            {
                if (iface.RawLines.Any(line => line.Contains("telnet", StringComparison.OrdinalIgnoreCase)))
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Service Exposure",
                        Description = $"Telnet service detected on interface {iface.Name}",
                        Severity = "High",
                        Recommendation = "Disable Telnet and use SSH for secure remote access",
                        IsVendorSpecific = false
                    });
                }
            }

            // Check for SNMP without access controls
            if (data.VendorSpecificData != null &&
                data.VendorSpecificData.ContainsKey("SNMP") &&
                !data.Acls.Any(a => a.Contains("snmp", StringComparison.OrdinalIgnoreCase)))
            {
                data.Anomalies.Add(new AnomalyInfo
                {
                    Type = "Security",
                    Category = "SNMP Security",
                    Description = "SNMP service configured but no access controls found",
                    Severity = "Medium",
                    Recommendation = "Configure SNMP communities with restricted access and consider SNMPv3",
                    IsVendorSpecific = false
                });
            }
        }

        private static void DetectWeakEncryption(UniversalLogData data)
        {
            // Check for weak SSH configurations
            foreach (var iface in data.Interfaces)
            {
                if (iface.RawLines.Any(line => line.Contains("ssh", StringComparison.OrdinalIgnoreCase) &&
                                             (line.Contains("des") || line.Contains("3des") || line.Contains("rc4"))))
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Encryption",
                        Description = $"Weak SSH encryption detected on interface {iface.Name}",
                        Severity = "High",
                        Recommendation = "Use strong encryption algorithms (AES-256, etc.) for SSH",
                        IsVendorSpecific = false
                    });
                }
            }

            // Check for SSL/TLS with weak ciphers
            if (data.VendorSpecificData != null && data.VendorSpecificData.ContainsKey("SSL"))
            {
                var sslConfig = data.VendorSpecificData["SSL"] as string;
                if (sslConfig != null && (sslConfig.Contains("TLSv1.0") || sslConfig.Contains("TLSv1.1") ||
                                         sslConfig.Contains("RC4") || sslConfig.Contains("DES")))
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "SSL/TLS",
                        Description = "Weak SSL/TLS configuration detected",
                        Severity = "High",
                        Recommendation = "Use TLS 1.2 or higher with strong cipher suites",
                        IsVendorSpecific = false
                    });
                }
            }
        }

        private static void DetectVendorSpecificSecurityIssues(UniversalLogData data)
        {
            // Huawei-specific issues
            if (data.Device?.Contains("Huawei", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check for default Huawei credentials
                foreach (var user in data.LocalUsers)
                {
                    if (user.Contains("huawei") || user.Contains("admin123"))
                    {
                        data.Anomalies.Add(new AnomalyInfo
                        {
                            Type = "Security",
                            Category = "Vendor Default",
                            Description = "Potential Huawei default credentials detected",
                            Severity = "Critical",
                            Recommendation = "Change all default Huawei credentials immediately",
                            IsVendorSpecific = true
                        });
                    }
                }
            }

            // Cisco-specific issues
            if (data.Device?.Contains("Cisco", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check for Cisco default enable password
                if (data.VendorSpecificData != null &&
                    data.VendorSpecificData.ContainsKey("EnablePassword") &&
                    data.VendorSpecificData["EnablePassword"] as string == "cisco")
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Vendor Default",
                        Description = "Cisco default enable password detected",
                        Severity = "Critical",
                        Recommendation = "Change the enable password from default 'cisco'",
                        IsVendorSpecific = true
                    });
                }
            }

            // Juniper-specific issues
            if (data.Device?.Contains("Juniper", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check for root login enabled
                if (data.VendorSpecificData != null &&
                    data.VendorSpecificData.ContainsKey("RootLogin") &&
                    (bool?)data.VendorSpecificData["RootLogin"] == true)
                {
                    data.Anomalies.Add(new AnomalyInfo
                    {
                        Type = "Security",
                        Category = "Root Access",
                        Description = "Root login enabled on Juniper device",
                        Severity = "High",
                        Recommendation = "Disable direct root login and use role-based access",
                        IsVendorSpecific = true
                    });
                }
            }
        }
        /// <summary>
        /// Search raw lines and vendor data for user-provided keywords or regex patterns and add anomalies for matches.
        /// This is a lightweight search-based anomaly helper intended to find occurrences of suspicious strings
        /// across parsed interface raw lines and vendor-specific data values.
        /// </summary>
        /// <param name="data">UniversalLogData to search</param>
        /// <param name="patterns">List of keywords or regex patterns to search for</param>
        /// <param name="useRegex">Treat patterns as regular expressions when true; otherwise do case-insensitive substring match</param>
        /// <param name="severity">Severity to assign for found matches</param>
        public static void SearchAndAddAnomalies(UniversalLogData data, IEnumerable<string> patterns, bool useRegex = false, string severity = "Medium")
        {
            if (data == null || patterns == null) return;

            var patternList = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct().ToList();
            if (!patternList.Any()) return;

            foreach (var pat in patternList)
            {
                System.Text.RegularExpressions.Regex? rx = null;
                if (useRegex)
                {
                    try { rx = new System.Text.RegularExpressions.Regex(pat, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                    catch { rx = null; }
                }

                // Search interface raw lines
                foreach (var iface in data.Interfaces ?? new List<InterfaceInfo>())
                {
                    foreach (var line in iface.RawLines ?? new List<string>())
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        bool matched = false;
                        if (useRegex && rx != null) matched = rx.IsMatch(line);
                        else matched = line.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (matched)
                        {
                            data.Anomalies.Add(new AnomalyInfo
                            {
                                Type = "Search",
                                Category = "PatternMatch",
                                Description = $"Pattern '{pat}' matched in interface {iface.Name}: '{Truncate(line, 200)}'",
                                Severity = severity,
                                Recommendation = "Review the matched configuration/log line for potential issues",
                                IsVendorSpecific = false
                            });
                        }
                    }
                }

                // Search in vendor-specific data values (strings and enumerables)
                if (data.VendorSpecificData != null)
                {
                    foreach (var kv in data.VendorSpecificData)
                    {
                        var val = kv.Value;
                        if (val == null) continue;
                        if (val is string s)
                        {
                            bool matched = useRegex && rx != null ? rx.IsMatch(s) : s.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (matched)
                            {
                                data.Anomalies.Add(new AnomalyInfo
                                {
                                    Type = "Search",
                                    Category = "VendorData",
                                    Description = $"Pattern '{pat}' matched in vendor field '{kv.Key}': '{Truncate(s,200)}'",
                                    Severity = severity,
                                    Recommendation = "Inspect vendor-specific configuration or output for relevance",
                                    IsVendorSpecific = true
                                });
                            }
                        }
                        else if (val is System.Collections.IEnumerable enm)
                        {
                            foreach (var item in enm)
                            {
                                if (item == null) continue;
                                var itemStr = item.ToString() ?? string.Empty;
                                bool matched = useRegex && rx != null ? rx.IsMatch(itemStr) : itemStr.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0;
                                if (matched)
                                {
                                    data.Anomalies.Add(new AnomalyInfo
                                    {
                                        Type = "Search",
                                        Category = "VendorData",
                                        Description = $"Pattern '{pat}' matched in vendor field '{kv.Key}': '{Truncate(itemStr,200)}'",
                                        Severity = severity,
                                        Recommendation = "Inspect vendor-specific configuration or output for relevance",
                                        IsVendorSpecific = true
                                    });
                                }
                            }
                        }
                    }
                }
            }
            // Recalculate health score after adding search-based anomalies
            CalculateHealthScore(data);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
