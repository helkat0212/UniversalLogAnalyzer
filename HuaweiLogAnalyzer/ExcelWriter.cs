using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using OfficeOpenXml.Drawing.Chart;
using System.Drawing;

namespace UniversalLogAnalyzer
{
    public static class ExcelWriter
    {
        private static readonly object _saveLock = new object();


        public static List<string> Save(List<UniversalLogData> logs, List<string>? unparsed, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder)) return Save(logs, unparsed);

            // Normalize reports folder so it ends with a single "Reports" segment (avoid nested Reports\Reports)
            var trimmed = outputFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parentForReports;
            if (trimmed.EndsWith("Reports", StringComparison.OrdinalIgnoreCase))
            {
                parentForReports = trimmed.Substring(0, trimmed.Length - "Reports".Length).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                parentForReports = trimmed;
            }
            if (string.IsNullOrEmpty(parentForReports)) parentForReports = Path.GetPathRoot(trimmed) ?? trimmed;
            var reportsFolder = Path.Combine(parentForReports, "Reports");
            Directory.CreateDirectory(reportsFolder);

            var savedFiles = new List<string>();

            // Group logs by device to avoid duplicates in single file
            var logsByDevice = logs.GroupBy(log => SharedUtilities.GetDeviceFolderName(log)).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var deviceGroup in logsByDevice)
            {
                var deviceFolder = Path.Combine(reportsFolder, deviceGroup.Key);
                Directory.CreateDirectory(deviceFolder);

                lock (_saveLock)
                {
                    var deviceName = SharedUtilities.SanitizeFileName(deviceGroup.Key);
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var file = Path.Combine(deviceFolder, $"{deviceName}_Report_{timestamp}.xlsx");
                    // If file exists (very unlikely), append a numeric suffix
                    int idx = 1;
                    while (File.Exists(file))
                    {
                        file = Path.Combine(deviceFolder, $"{deviceName}_Report_{timestamp}_{idx}.xlsx");
                        idx++;
                    }
                    savedFiles.Add(SaveToFileInternal(deviceGroup.Value, unparsed, file));
                }
            }

            return savedFiles;
        }

        public static List<string> Save(List<UniversalLogData> logs, List<string>? unparsed)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads)) downloads = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // Create Reports directory (downloads path won't normally end with 'Reports')
            var reportsFolder = Path.Combine(downloads, "Reports");
            Directory.CreateDirectory(reportsFolder);

            var savedFiles = new List<string>();

            foreach (var log in logs)
            {
                var deviceFolder = Path.Combine(reportsFolder, SharedUtilities.GetDeviceFolderName(log));
                Directory.CreateDirectory(deviceFolder);

                lock (_saveLock)
                {
                    var deviceName = SharedUtilities.SanitizeFileName(SharedUtilities.GetDeviceFolderName(log));
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var file = Path.Combine(deviceFolder, $"{deviceName}_Report_{timestamp}.xlsx");
                    int idx = 1;
                    while (File.Exists(file))
                    {
                        file = Path.Combine(deviceFolder, $"{deviceName}_Report_{timestamp}_{idx}.xlsx");
                        idx++;
                    }
                    savedFiles.Add(SaveToFileInternal(new List<UniversalLogData> { log }, unparsed, file));
                }
            }

            return savedFiles;
        }

        

        // Internal helper that builds the workbook and writes directly to the given file path.
        // Returns the path to the created file
        private static string SaveToFileInternal(List<UniversalLogData> logs, List<string>? unparsed, string file)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                // Create reusable named styles to reduce style allocations
                try
                {
                    var headerStyle = package.Workbook.Styles.CreateNamedStyle("HeaderStyle");
                    headerStyle.Style.Font.Bold = true;
                    headerStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    headerStyle.Style.Fill.BackgroundColor.SetColor(Color.LightSteelBlue);
                    headerStyle.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;

                    var okStyle = package.Workbook.Styles.CreateNamedStyle("OkStyle");
                    okStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    okStyle.Style.Fill.BackgroundColor.SetColor(Color.LightGreen);

                    var partialStyle = package.Workbook.Styles.CreateNamedStyle("PartialStyle");
                    partialStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    partialStyle.Style.Fill.BackgroundColor.SetColor(Color.Khaki);

                    var missingStyle = package.Workbook.Styles.CreateNamedStyle("MissingStyle");
                    missingStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    missingStyle.Style.Fill.BackgroundColor.SetColor(Color.LightCoral);

                    // Additional styles for enhanced color coding
                    var errorStyle = package.Workbook.Styles.CreateNamedStyle("ErrorStyle");
                    errorStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    errorStyle.Style.Fill.BackgroundColor.SetColor(Color.Red);
                    errorStyle.Style.Font.Color.SetColor(Color.White);

                    var warningStyle = package.Workbook.Styles.CreateNamedStyle("WarningStyle");
                    warningStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    warningStyle.Style.Fill.BackgroundColor.SetColor(Color.Yellow);

                    var interfaceStyle = package.Workbook.Styles.CreateNamedStyle("InterfaceStyle");
                    interfaceStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    interfaceStyle.Style.Fill.BackgroundColor.SetColor(Color.LightBlue);

                    var vlanStyle = package.Workbook.Styles.CreateNamedStyle("VlanStyle");
                    vlanStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    vlanStyle.Style.Fill.BackgroundColor.SetColor(Color.LightCyan);

                    var vpnStyle = package.Workbook.Styles.CreateNamedStyle("VpnStyle");
                    vpnStyle.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    vpnStyle.Style.Fill.BackgroundColor.SetColor(Color.LightPink);
                }
                catch { }

                // Detailed Interfaces sheet
                var ifaceWs = package.Workbook.Worksheets.Add("Interfaces");
                ifaceWs.Cells["A1"].Value = "Device";
                ifaceWs.Cells["B1"].Value = "Interface";
                ifaceWs.Cells["C1"].Value = "Description";
                ifaceWs.Cells["D1"].Value = "IP/Binding";
                ifaceWs.Cells["E1"].Value = "Shutdown";
                ApplyHeaderStyle(ifaceWs, "A1:E1");

                int irow = 2;
                foreach (var log in logs)
                {
                    foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                    {
                        ifaceWs.Cells[irow, 1].Value = log.Device;
                        ifaceWs.Cells[irow, 2].Value = iface.Name;
                        ifaceWs.Cells[irow, 3].Value = iface.Description;
                        ifaceWs.Cells[irow, 4].Value = iface.IpAddress;
                        ifaceWs.Cells[irow, 5].Value = iface.IsShutdown ? "Yes" : "No";

                        // Apply color based on shutdown status
                        string ifaceStyle = iface.IsShutdown ? "MissingStyle" : "InterfaceStyle";
                        using (var rowRange = ifaceWs.Cells[irow, 1, irow, 5])
                        {
                            rowRange.StyleName = ifaceStyle;
                        }
                        irow++;
                    }
                }

                // Freeze and autofit interfaces
                ifaceWs.View.FreezePanes(2, 1);
                if (ifaceWs.Dimension != null)
                {
                    ifaceWs.Cells[ifaceWs.Dimension.Address].AutoFitColumns();
                    ifaceWs.Cells["A1:E1"].AutoFilter = true;
                    ifaceWs.Cells[ifaceWs.Dimension.Address].Style.WrapText = true;
                }

                // Interface counters sheet: collect lines from interface RawLines that look like counters
                var counterWs = package.Workbook.Worksheets.Add("InterfaceCounters");
                counterWs.Cells[1, 1].Value = "Device";
                counterWs.Cells[1, 2].Value = "Interface";
                counterWs.Cells[1, 3].Value = "Metric";
                counterWs.Cells[1, 4].Value = "Value";
                ApplyHeaderStyle(counterWs, "A1:D1");
                int counterRow = 2;
                foreach (var log in logs)
                {
                    foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                    {
                        foreach (var rawLine in iface.RawLines ?? Enumerable.Empty<string>())
                        {
                            if (string.IsNullOrWhiteSpace(rawLine)) continue;
                            // Heuristic: lines that contain digits and have multiple tokens are likely counters
                            if (rawLine.Any(char.IsDigit) && System.Text.RegularExpressions.Regex.Split(rawLine.Trim(), "\\s+").Length >= 2)
                            {
                                var parts = System.Text.RegularExpressions.Regex.Split(rawLine.Trim(), "\\s+");
                                var metric = parts.Length > 0 ? parts[0] : rawLine.Trim();
                                var value = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
                                counterWs.Cells[counterRow, 1].Value = log.Device;
                                counterWs.Cells[counterRow, 2].Value = iface.Name;
                                counterWs.Cells[counterRow, 3].Value = metric;
                                counterWs.Cells[counterRow, 4].Value = value;
                                counterRow++;
                            }
                        }
                    }
                }
                try { if (counterWs.Dimension != null) counterWs.Cells[counterWs.Dimension.Address].AutoFitColumns(); } catch { }

                // Device details sheet (named after the first device's channel/name)
                var firstLog = logs.FirstOrDefault();
                string? deviceSheetName = null;
                if (firstLog != null)
                {
                    var dev = string.IsNullOrWhiteSpace(firstLog.SystemName) ? firstLog.Device : firstLog.SystemName;
                    if (string.IsNullOrWhiteSpace(dev)) dev = "unknown";
                    deviceSheetName = SanitizeSheetName(dev);
                    var dws = package.Workbook.Worksheets.Add(deviceSheetName);
                    int cur = 1;
                    // Device Info header
                    dws.Cells[cur, 1].Value = "Property";
                    dws.Cells[cur, 2].Value = "Value";
                    ApplyHeaderStyle(dws, ExcelAddressFrom(cur, 1, cur, 2));
                    cur++;
                    dws.Cells[cur, 1].Value = "Device"; dws.Cells[cur, 2].Value = firstLog.Device; cur++;
                    dws.Cells[cur, 1].Value = "System Name"; dws.Cells[cur, 2].Value = firstLog.SystemName; cur++;
                    dws.Cells[cur, 1].Value = "Version"; dws.Cells[cur, 2].Value = firstLog.Version; cur++;
                    dws.Cells[cur, 1].Value = "Serial"; dws.Cells[cur, 2].Value = firstLog.SerialNumber; cur++;
                    // Licenses / Modules: prefer top-level fields on UniversalLogData, fall back to VendorSpecificData
                    var licenses = new List<string>();
                    if (firstLog.Licenses != null && firstLog.Licenses.Any()) licenses.AddRange(firstLog.Licenses);
                    else if (firstLog.VendorSpecificData != null && firstLog.VendorSpecificData.TryGetValue("Licenses", out var licObj) && licObj is IEnumerable<string> licList) licenses.AddRange(licList);
                    licenses = licenses.Distinct().ToList();
                    if (licenses.Any())
                    {
                        dws.Cells[cur, 1].Value = "Licenses";
                        dws.Cells[cur, 2].Value = string.Join("; ", licenses);
                        cur++;
                    }

                    var modules = new List<string>();
                    if (firstLog.Modules != null && firstLog.Modules.Any()) modules.AddRange(firstLog.Modules);
                    else if (firstLog.VendorSpecificData != null && firstLog.VendorSpecificData.TryGetValue("Modules", out var modObj) && modObj is IEnumerable<string> modList) modules.AddRange(modList);
                    modules = modules.Distinct().ToList();
                    if (modules.Any())
                    {
                        dws.Cells[cur, 1].Value = "Modules";
                        dws.Cells[cur, 2].Value = string.Join("; ", modules);
                        cur++;
                    }
                    
                    // System Resources
                    if (firstLog.Resources != null)
                    {
                        if (firstLog.Resources.CpuUsage > 0)
                        { dws.Cells[cur, 1].Value = "CPU Usage"; dws.Cells[cur, 2].Value = firstLog.Resources.CpuUsage; cur++; }
                        if (firstLog.Resources.MemoryUsage > 0)
                        { dws.Cells[cur, 1].Value = "Memory Usage"; dws.Cells[cur, 2].Value = firstLog.Resources.MemoryUsage; cur++; }
                        if (firstLog.Resources.DiskUsage > 0)
                        { dws.Cells[cur, 1].Value = "Disk Usage"; dws.Cells[cur, 2].Value = firstLog.Resources.DiskUsage; cur++; }
                        if (!string.IsNullOrEmpty(firstLog.Resources.Temperature))
                        { dws.Cells[cur, 1].Value = "Temperature"; dws.Cells[cur, 2].Value = firstLog.Resources.Temperature; cur++; }
                        if (firstLog.Resources.Alarms != null && firstLog.Resources.Alarms.Count > 0)
                        { dws.Cells[cur, 1].Value = "Alarms"; dws.Cells[cur, 2].Value = string.Join("\n", firstLog.Resources.Alarms); cur++; }
                    }
                    cur++;

                    // Ports & Interfaces combined table
                    dws.Cells[cur, 1].Value = "Port/Interface";
                    dws.Cells[cur, 2].Value = "Status";
                    dws.Cells[cur, 3].Value = "Type";
                    dws.Cells[cur, 4].Value = "Description";
                    dws.Cells[cur, 5].Value = "VPN";
                    dws.Cells[cur, 6].Value = "IP Address";
                    ApplyHeaderStyle(dws, ExcelAddressFrom(cur, 1, cur, 6));
                    int portsHeaderRow = cur;
                    cur++;

                    // Use GroupBy to handle duplicate interface names - take the first one for each name
                    var ifaceLookup = (firstLog.Interfaces ?? new List<InterfaceInfo>())
                        .GroupBy(i => i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    // If there are explicit PortInfos in vendor data, prefer them; otherwise use Interfaces
                    if (firstLog.VendorSpecificData != null && firstLog.VendorSpecificData.TryGetValue("PortInfos", out var portObj) && portObj is IEnumerable<dynamic> portInfos)
                    {
                        foreach (var p in portInfos)
                        {
                            string portName = p.Port ?? string.Empty;
                            dws.Cells[cur, 1].Value = portName;
                            dws.Cells[cur, 2].Value = p.Status ?? string.Empty;
                            dws.Cells[cur, 3].Value = p.Type ?? string.Empty;

                            InterfaceInfo? iface = null;
                            ifaceLookup.TryGetValue(portName, out iface);
                            dws.Cells[cur, 4].Value = iface?.Description ?? "";

                            string vpn = "";
                            if (iface?.Description != null)
                            {
                                var vpnMatch = System.Text.RegularExpressions.Regex.Match(iface.Description, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (vpnMatch.Success)
                                    vpn = vpnMatch.Groups[1].Value;
                            }
                            dws.Cells[cur, 5].Value = vpn;
                            dws.Cells[cur, 6].Value = iface?.IpAddress ?? "";

                            string rowStyle = (p.Status ?? string.Empty).Equals("UP", StringComparison.OrdinalIgnoreCase) ? "OkStyle" :
                                            (p.Status ?? string.Empty).Equals("DOWN", StringComparison.OrdinalIgnoreCase) ? "MissingStyle" :
                                            "PartialStyle";
                            using (var rowRange = dws.Cells[cur, 1, cur, 6])
                            {
                                rowRange.StyleName = rowStyle;
                                rowRange.Style.WrapText = true;
                            }
                            cur++;
                        }
                    }
                    else
                    {
                        foreach (var iface in firstLog.Interfaces ?? new List<InterfaceInfo>())
                        {
                            dws.Cells[cur, 1].Value = firstLog.Device;
                            dws.Cells[cur, 2].Value = iface.Name;
                            dws.Cells[cur, 3].Value = iface.IsShutdown ? "DOWN" : "UP";
                            dws.Cells[cur, 4].Value = string.Empty;
                            string vpn = string.Empty;
                            if (!string.IsNullOrEmpty(iface.Description))
                            {
                                var vpnMatch = System.Text.RegularExpressions.Regex.Match(iface.Description, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (vpnMatch.Success) vpn = vpnMatch.Groups[1].Value;
                            }
                            dws.Cells[cur, 5].Value = vpn;
                            dws.Cells[cur, 6].Value = iface.IpAddress ?? string.Empty;
                            string rowStyle = iface.IsShutdown ? "MissingStyle" : "InterfaceStyle";
                            using (var rowRange = dws.Cells[cur, 1, cur, 6]) { rowRange.StyleName = rowStyle; rowRange.Style.WrapText = true; }
                            cur++;
                        }
                    }
                    cur++;

                    // Freeze panes on ports header and autofit this device sheet
                    try
                    {
                        dws.View.FreezePanes(portsHeaderRow + 1, 1);
                        if (dws.Dimension != null) dws.Cells[dws.Dimension.Address].AutoFitColumns();
                    }
                    catch { }

                    // Network Info (VLANs, VPNs, BGP) in one compact section
                    dws.Cells[cur, 1].Value = "Network Info"; dws.Cells[cur, 1].Style.Font.Bold = true; cur++;
                    var vlans = firstLog.Vlans ?? Enumerable.Empty<string>();
                    dws.Cells[cur, 1].Value = "VLANs"; dws.Cells[cur, 2].Value = vlans.Any() ? string.Join(", ", vlans) : "(none)"; cur++;
                    // VPNs (from BgpPeers entries prefixed with VPN:)
                    var vpns = new List<string>();
                    var bgps = new List<string>();
                    var bgpEntries = firstLog.BgpPeers ?? Enumerable.Empty<string>();
                    foreach (var entry in bgpEntries)
                    {
                        if (entry.StartsWith("VPN:", StringComparison.OrdinalIgnoreCase)) vpns.Add(entry.Substring(4));
                        else bgps.Add(entry);
                    }
                    dws.Cells[cur, 1].Value = "VPNs"; dws.Cells[cur, 2].Value = vpns.Any() ? string.Join(", ", vpns) : "(none)"; cur++;
                    dws.Cells[cur, 1].Value = "BGP Peers"; dws.Cells[cur, 2].Value = bgps.Any() ? string.Join(", ", bgps) : "(none)"; cur++;
                }

                // Create a consolidated PortDetails sheet with proper table
                var portDetailsWs = package.Workbook.Worksheets.Add("PortDetails");
                portDetailsWs.Cells[1, 1].Value = "Device";
                portDetailsWs.Cells[1, 2].Value = "Port";
                portDetailsWs.Cells[1, 3].Value = "Status";
                portDetailsWs.Cells[1, 4].Value = "Type";
                portDetailsWs.Cells[1, 5].Value = "Description";
                portDetailsWs.Cells[1, 6].Value = "VPN";
                portDetailsWs.Cells[1, 7].Value = "IP Address";
                ApplyHeaderStyle(portDetailsWs, "A1:G1");
                int portDetailsRow = 2;

                // Populate PortDetails from all logs
                foreach (var log in logs)
                {
                    // Use GroupBy to handle duplicate interface names - take the first one for each name
                    var ifaceLookup = (log.Interfaces ?? new List<InterfaceInfo>())
                        .GroupBy(i => i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    // Prefer vendor-provided PortInfos if present in VendorSpecificData
                    if (log.VendorSpecificData != null && log.VendorSpecificData.TryGetValue("PortInfos", out var portObj) && portObj is IEnumerable<dynamic> portInfos)
                    {
                        foreach (var p in portInfos)
                        {
                            InterfaceInfo? iface = null;
                            ifaceLookup.TryGetValue(p.Port ?? string.Empty, out iface);
                            var desc = iface?.Description ?? string.Empty;
                            portDetailsWs.Cells[portDetailsRow, 1].Value = log.Device;
                            portDetailsWs.Cells[portDetailsRow, 2].Value = p.Port;
                            portDetailsWs.Cells[portDetailsRow, 3].Value = p.Status;
                            portDetailsWs.Cells[portDetailsRow, 4].Value = p.Type;
                            portDetailsWs.Cells[portDetailsRow, 5].Value = desc;
                            // Extract VPN info from interface description if available
                            string vpn = "";
                            if (iface?.Description != null)
                            {
                                var vpnMatch = System.Text.RegularExpressions.Regex.Match(iface.Description, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (vpnMatch.Success)
                                    vpn = vpnMatch.Groups[1].Value;
                            }
                            portDetailsWs.Cells[portDetailsRow, 6].Value = vpn;
                            portDetailsWs.Cells[portDetailsRow, 7].Value = iface?.IpAddress ?? "";

                            // Add hyperlink to device sheet if available
                            if (deviceSheetName != null && firstLog != null && log.Device == firstLog.Device)
                            {
                                portDetailsWs.Cells[portDetailsRow, 1].Hyperlink = new ExcelHyperLink($"#{deviceSheetName}!A1", log.Device ?? "");
                            }

                            portDetailsRow++;
                        }
                    }
                    else
                    {
                        // Fallback: enumerate interfaces as ports
                        foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                        {
                            portDetailsWs.Cells[portDetailsRow, 1].Value = log.Device;
                            portDetailsWs.Cells[portDetailsRow, 2].Value = iface.Name;
                            portDetailsWs.Cells[portDetailsRow, 3].Value = iface.IsShutdown ? "DOWN" : "UP";
                            portDetailsWs.Cells[portDetailsRow, 4].Value = iface.InterfaceType ?? string.Empty;
                            portDetailsWs.Cells[portDetailsRow, 5].Value = iface.Description ?? string.Empty;
                            string vpn = "";
                            if (!string.IsNullOrEmpty(iface.Description))
                            {
                                var vpnMatch = System.Text.RegularExpressions.Regex.Match(iface.Description, @"vpn\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (vpnMatch.Success)
                                    vpn = vpnMatch.Groups[1].Value;
                            }
                            portDetailsWs.Cells[portDetailsRow, 6].Value = vpn;
                            portDetailsWs.Cells[portDetailsRow, 7].Value = iface.IpAddress ?? string.Empty;

                            if (deviceSheetName != null && firstLog != null && log.Device == firstLog.Device)
                            {
                                portDetailsWs.Cells[portDetailsRow, 1].Hyperlink = new ExcelHyperLink($"#{deviceSheetName}!A1", log.Device ?? "");
                            }

                            portDetailsRow++;
                        }
                    }
                }

                // Legend + README combined
                var legend = package.Workbook.Worksheets.Add("Legend");
                legend.Cells[1, 1].Value = "Legend & README";
                legend.Cells[2, 1].Value = "Green = OK; Khaki = partial; Coral = missing or down";
                legend.Cells[3, 1].Value = "Light Blue = Active Interfaces; Light Cyan = VLANs; Light Pink = VPNs";
                legend.Cells[4, 1].Value = "Red = Errors; Yellow = Warnings";
                legend.Cells[6, 1].Value = "Universal Log Analyzer Report";
                legend.Cells[7, 1].Value = "Generated: " + DateTime.Now.ToString("s");

                // Anomaly Detection sheet - only create if there are anomalies to display
                bool hasAnomalies = logs.Any(log => log.Anomalies != null && log.Anomalies.Any());
                if (hasAnomalies)
                {
                    var anomalyWs = package.Workbook.Worksheets.Add("Anomalies");
                    anomalyWs.Cells["A1"].Value = "Device";
                    anomalyWs.Cells["B1"].Value = "Anomaly Type";
                    anomalyWs.Cells["C1"].Value = "Description";
                    anomalyWs.Cells["D1"].Value = "Severity";
                    ApplyHeaderStyle(anomalyWs, "A1:D1");

                    int anomalyRow = 2;
                    foreach (var log in logs)
                    {
                        if (log.Anomalies != null)
                        {
                            foreach (var anomaly in log.Anomalies)
                            {
                                anomalyWs.Cells[anomalyRow, 1].Value = log.Device;
                                anomalyWs.Cells[anomalyRow, 2].Value = anomaly.Type;
                                anomalyWs.Cells[anomalyRow, 3].Value = anomaly.Description;
                                anomalyWs.Cells[anomalyRow, 4].Value = anomaly.Severity;

                                // Color based on severity
                                string anomalyStyle = anomaly.Severity switch
                                {
                                    "Critical" => "ErrorStyle",
                                    "High" => "MissingStyle",
                                    "Medium" => "WarningStyle",
                                    "Low" => "PartialStyle",
                                    _ => "OkStyle"
                                };
                                using (var rowRange = anomalyWs.Cells[anomalyRow, 1, anomalyRow, 4])
                                {
                                    rowRange.StyleName = anomalyStyle;
                                }
                                anomalyRow++;
                            }
                        }
                    }
                    try { if (anomalyWs.Dimension != null) anomalyWs.Cells[anomalyWs.Dimension.Address].AutoFitColumns(); } catch { }
                }

                // System Resources sheet - always create if there is any resource data
                bool hasResourceData = logs.Any(log => log.Resources != null && (log.Resources.CpuUsage > 0 || log.Resources.MemoryUsage > 0 || log.Resources.DiskUsage > 0 || !string.IsNullOrEmpty(log.Resources.Temperature) || !string.IsNullOrEmpty(log.Resources.Voltage) || log.Resources.Alarms.Any()));
                if (hasResourceData)
                {
                    var resWs = package.Workbook.Worksheets.Add("System Resources");
                    resWs.Cells["A1"].Value = "Device";
                    resWs.Cells["B1"].Value = "Metric";
                    resWs.Cells["C1"].Value = "Value";
                    resWs.Cells["D1"].Value = "Unit";
                    ApplyHeaderStyle(resWs, "A1:D1");

                    int resRow = 2;
                    foreach (var log in logs)
                    {
                        if (log.Resources != null)
                        {
                            var device = log.Device ?? log.SystemName ?? "Unknown";
                            
                            if (log.Resources.CpuUsage > 0)
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "CPU Usage";
                                resWs.Cells[resRow, 3].Value = log.Resources.CpuUsage;
                                resWs.Cells[resRow, 4].Value = "%";
                                resRow++;
                            }
                            if (log.Resources.MemoryUsage > 0)
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "Memory Usage";
                                resWs.Cells[resRow, 3].Value = log.Resources.MemoryUsage;
                                resWs.Cells[resRow, 4].Value = "%";
                                resRow++;
                            }
                            if (log.Resources.DiskUsage > 0)
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "Disk Usage";
                                resWs.Cells[resRow, 3].Value = log.Resources.DiskUsage;
                                resWs.Cells[resRow, 4].Value = "%";
                                resRow++;
                            }
                            if (!string.IsNullOrEmpty(log.Resources.Temperature))
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "Temperature";
                                resWs.Cells[resRow, 3].Value = log.Resources.Temperature;
                                resWs.Cells[resRow, 4].Value = "°C";
                                resRow++;
                            }
                            if (!string.IsNullOrEmpty(log.Resources.Voltage))
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "Voltage";
                                resWs.Cells[resRow, 3].Value = log.Resources.Voltage;
                                resWs.Cells[resRow, 4].Value = "V";
                                resRow++;
                            }
                            if (log.Resources.Alarms.Any())
                            {
                                resWs.Cells[resRow, 1].Value = device;
                                resWs.Cells[resRow, 2].Value = "Alarms";
                                resWs.Cells[resRow, 3].Value = string.Join("; ", log.Resources.Alarms);
                                resWs.Cells[resRow, 4].Value = "#";
                                resRow++;
                            }
                        }
                    }
                    try { if (resWs.Dimension != null) resWs.Cells[resWs.Dimension.Address].AutoFitColumns(); } catch { }
                }

                // Performance Metrics sheet - only create if there is performance data
                bool hasPerformanceData = logs.Any(log => log.Performance != null && (log.Performance.AvgCpuUsage > 0 || log.Performance.AvgMemoryUsage > 0 || log.Performance.MaxInterfaceUtilization > 0));
                if (hasPerformanceData)
                {
                    var perfWs = package.Workbook.Worksheets.Add("Performance");
                    perfWs.Cells["A1"].Value = "Device";
                    perfWs.Cells["B1"].Value = "Metric";
                    perfWs.Cells["C1"].Value = "Value";
                    perfWs.Cells["D1"].Value = "Unit";
                    ApplyHeaderStyle(perfWs, "A1:D1");

                    int perfRow = 2;
                    foreach (var log in logs)
                    {
                        if (log.Performance != null)
                        {
                            if (log.Performance.AvgCpuUsage > 0)
                            {
                                perfWs.Cells[perfRow, 1].Value = log.Device;
                                perfWs.Cells[perfRow, 2].Value = "CPU Usage";
                                perfWs.Cells[perfRow, 3].Value = log.Performance.AvgCpuUsage;
                                perfWs.Cells[perfRow, 4].Value = "%";
                                perfRow++;
                            }
                            if (log.Performance.AvgMemoryUsage > 0)
                            {
                                perfWs.Cells[perfRow, 1].Value = log.Device;
                                perfWs.Cells[perfRow, 2].Value = "Memory Usage";
                                perfWs.Cells[perfRow, 3].Value = log.Performance.AvgMemoryUsage;
                                perfWs.Cells[perfRow, 4].Value = "%";
                                perfRow++;
                            }
                            if (log.Performance.MaxInterfaceUtilization > 0)
                            {
                                perfWs.Cells[perfRow, 1].Value = log.Device;
                                perfWs.Cells[perfRow, 2].Value = "Max Interface Utilization";
                                perfWs.Cells[perfRow, 3].Value = log.Performance.MaxInterfaceUtilization;
                                perfWs.Cells[perfRow, 4].Value = "%";
                                perfRow++;
                            }
                        }
                    }
                    try { if (perfWs.Dimension != null) perfWs.Cells[perfWs.Dimension.Address].AutoFitColumns(); } catch { }
                }

                // Chart creation skipped - performance metrics insufficient for visualization

                // Interface Clustering sheet - only create if there are clusters to display
                bool hasClusters = logs.Any(log => log.Clustering != null && log.Clustering.Clusters != null && log.Clustering.Clusters.Any());
                if (hasClusters)
                {
                    var clusterWs = package.Workbook.Worksheets.Add("InterfaceClusters");
                    clusterWs.Cells["A1"].Value = "Device";
                    clusterWs.Cells["B1"].Value = "Cluster Name";
                    clusterWs.Cells["C1"].Value = "Description";
                    clusterWs.Cells["D1"].Value = "Interface Count";
                    clusterWs.Cells["E1"].Value = "Avg Utilization";
                    clusterWs.Cells["F1"].Value = "Total Errors";
                    clusterWs.Cells["G1"].Value = "Interfaces";
                    ApplyHeaderStyle(clusterWs, "A1:G1");

                    int clusterRow = 2;
                    foreach (var log in logs)
                    {
                        if (log.Clustering?.Clusters != null)
                        {
                            foreach (var cluster in log.Clustering.Clusters)
                            {
                                clusterWs.Cells[clusterRow, 1].Value = log.Device;
                                clusterWs.Cells[clusterRow, 2].Value = cluster.ClusterName;
                                clusterWs.Cells[clusterRow, 3].Value = cluster.Description;
                                clusterWs.Cells[clusterRow, 4].Value = cluster.Interfaces.Count;
                                clusterWs.Cells[clusterRow, 5].Value = cluster.AvgUtilization;
                                clusterWs.Cells[clusterRow, 6].Value = cluster.TotalErrors;
                                clusterWs.Cells[clusterRow, 7].Value = string.Join(", ", cluster.Interfaces);

                                // Color code based on cluster type
                                string clusterStyle = cluster.ClusterName switch
                                {
                                    var s when s.Contains("High", StringComparison.OrdinalIgnoreCase) => "ErrorStyle",
                                    var s when s.Contains("Medium", StringComparison.OrdinalIgnoreCase) => "WarningStyle",
                                    var s when s.Contains("Low", StringComparison.OrdinalIgnoreCase) => "OkStyle",
                                    var s when s.Contains("Error", StringComparison.OrdinalIgnoreCase) => "ErrorStyle",
                                    var s when s.Contains("Shutdown", StringComparison.OrdinalIgnoreCase) => "MissingStyle",
                                    _ => "PartialStyle"
                                };
                                using (var rowRange = clusterWs.Cells[clusterRow, 1, clusterRow, 7])
                                {
                                    rowRange.StyleName = clusterStyle;
                                }
                                clusterRow++;
                            }
                        }
                    }
                    try { if (clusterWs.Dimension != null) clusterWs.Cells[clusterWs.Dimension.Address].AutoFitColumns(); } catch { }
                }

                // Final autofit for port details
                try { if (portDetailsWs.Dimension != null) portDetailsWs.Cells[portDetailsWs.Dimension.Address].AutoFitColumns(); } catch { }

                // Save to disk
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Exists) fi.Delete();
                    package.SaveAs(fi);
                    package.Dispose();
                    return fi.FullName;
                }
                catch (Exception ex)
                {
                    package.Dispose();
                    // If save failed, rethrow or return an empty string - choose to rethrow for visibility
                    throw new IOException($"Failed to save Excel report to {file}: {ex.Message}", ex);
                }
            }
        }

        // Helper to apply a consistent header style to a range (address or ExcelAddress string)
        private static void ApplyHeaderStyle(ExcelWorksheet ws, string address)
        {
            using (var header = ws.Cells[address])
            {
                header.Style.Font.Bold = true;
                header.Style.Fill.PatternType = ExcelFillStyle.Solid;
                header.Style.Fill.BackgroundColor.SetColor(Color.LightSteelBlue);
                header.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Left;
            }
        }

        // Helper to produce an address string like "A1:C1" from numeric coordinates
        private static string ExcelAddressFrom(int row1, int col1, int row2, int col2)
        {
            return $"{ToColumnName(col1)}{row1}:{ToColumnName(col2)}{row2}";
        }

        // Helper to convert a numeric column index to an Excel column name (1=A, 2=B, etc)
        private static string ToColumnName(int column)
        {
            int dividend = column;
            string columnName = string.Empty;
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }
            return columnName;
        }

        // Sanitize sheet name to remove invalid characters and limit to 31 chars
        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "sheet";
            var invalid = new[] { '\\', '/', '*', '[', ']', ':', '?' };
            foreach (var c in invalid) name = name.Replace(c, '_');
            name = name.Replace('"', '_');
            name = name.Replace('\'', '_');
            if (name.Length > 31) name = name.Substring(0, 31);
            return name;
        }

        
    }
}
