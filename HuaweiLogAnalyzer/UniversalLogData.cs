using System;
using System.Collections.Generic;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Supported network device vendors
    /// </summary>
    public enum DeviceVendor
    {
        Unknown = 0,
        Huawei = 1,
        Cisco = 2,
        Juniper = 3,
        Mikrotik = 4,
        TPLink = 5,
        Ubiquiti = 6,
        Fortinet = 7,
        Palo = 8
    }

    /// <summary>
    /// Generic log data structure that all parsers populate
    /// Vendor-specific data can be added as needed
    /// </summary>
    public class UniversalLogData
    {
        public DeviceVendor Vendor { get; set; } = DeviceVendor.Unknown;
        // Detected log "build/type" e.g. running-config, tech-support, syslog, etc.
        public LogBuildType LogType { get; set; } = LogBuildType.Unknown;
        public string OriginalFileName { get; set; } = string.Empty;
        public DateTime ParsedAt { get; set; } = DateTime.Now;

        // Core device information (universal across vendors)
        public string Device { get; set; } = string.Empty;           // Device hostname/name
        public string Version { get; set; } = string.Empty;          // Software version
        public string SerialNumber { get; set; } = string.Empty;     // Serial number or ESN
        public string ModelNumber { get; set; } = string.Empty;      // Model/device type
        public string SystemName { get; set; } = string.Empty;       // System name (optional)
        public string IpAddress { get; set; } = string.Empty;        // Management IP

        // Network elements (universal)
        public List<InterfaceInfo> Interfaces { get; set; } = new();
        public List<string> Vlans { get; set; } = new();
        public List<string> Routes { get; set; } = new();
        public List<string> BgpPeers { get; set; } = new();
        public string BgpAsn { get; set; } = string.Empty;
        public List<string> Acls { get; set; } = new();
        public List<string> LocalUsers { get; set; } = new();
        public List<string> NtpServers { get; set; } = new();
        public List<string> Licenses { get; set; } = new();
        public List<string> Modules { get; set; } = new();

        // System resources
        public SystemResources Resources { get; set; } = new();

        // Analysis results
        public List<AnomalyInfo> Anomalies { get; set; } = new();
        public PerformanceMetrics Performance { get; set; } = new();
        public InterfaceClustering Clustering { get; set; } = new();

        // Raw data and metadata
        public List<string> ParseErrors { get; set; } = new();
        public List<string> WarningMessages { get; set; } = new();
        public int TotalLinesProcessed { get; set; } = 0;
        public int SuccessfullyParsedLines { get; set; } = 0;

        // Vendor-specific extras (stored as generic dictionary for extensibility)
        public Dictionary<string, object> VendorSpecificData { get; set; } = new();

        // Network discovery tables
        public List<ArpEntry> ArpTable { get; set; } = new();
        public List<DhcpLease> DhcpLeases { get; set; } = new();
        public List<LldpNeighbor> LldpNeighbors { get; set; } = new();
        public List<string> MacAddresses { get; set; } = new();
    }

    public class ArpEntry
    {
        public string Ip { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
        public string Interface { get; set; } = string.Empty;
    }

    public class DhcpLease
    {
        public string Ip { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
    }

    public class LldpNeighbor
    {
        public string LocalInterface { get; set; } = string.Empty;
        public string RemoteDevice { get; set; } = string.Empty;
        public string RemotePort { get; set; } = string.Empty;
        public string RemoteIp { get; set; } = string.Empty;
    }

    /// <summary>
    /// Basic classification of log file content to guide parsers and analysis
    /// </summary>
    public enum LogBuildType
    {
        Unknown = 0,
        RunningConfig = 1,
        StartupConfig = 2,
        TechSupport = 3,
        Syslog = 4,
        ShowInterfaces = 5,
        ShowVersion = 6,
        Audit = 7,
        Other = 100
    }

    /// <summary>
    /// Universal interface information
    /// </summary>
    public class InterfaceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Mask { get; set; } = string.Empty;
        public string Status { get; set; } = "UP";                   // UP/DOWN/DORMANT
        public bool IsShutdown { get; set; } = false;
        public bool IsVirtual { get; set; } = false;
        public string InterfaceType { get; set; } = "Physical";      // Physical/Virtual/Loopback
        public List<string> RawLines { get; set; } = new();

        // Performance metrics
        public double InUtilization { get; set; } = 0;
        public double OutUtilization { get; set; } = 0;
        public long InErrors { get; set; } = 0;
        public long OutErrors { get; set; } = 0;
        public long InPackets { get; set; } = 0;
        public long OutPackets { get; set; } = 0;
        public string Speed { get; set; } = string.Empty;
    }

    /// <summary>
    /// System resource information
    /// </summary>
    public class SystemResources
    {
        public double CpuUsage { get; set; } = 0;
        public double MemoryUsage { get; set; } = 0;
        public double DiskUsage { get; set; } = 0;
        public string Temperature { get; set; } = string.Empty;
        public string Voltage { get; set; } = string.Empty;
        public List<string> Alarms { get; set; } = new();
        public DateTime LastUpdate { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Anomaly detection result
    /// </summary>
    public class AnomalyInfo
    {
        public string Type { get; set; } = string.Empty;                    // Security, Performance, Configuration
        public string Category { get; set; } = string.Empty;                // General category
        public string Description { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";                    // Critical, High, Medium, Low
        public string Recommendation { get; set; } = string.Empty;
        public bool IsVendorSpecific { get; set; } = false;                 // Vendor-specific anomaly
        public DateTime DetectedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Performance metrics
    /// </summary>
    public class PerformanceMetrics
    {
        public double AvgCpuUsage { get; set; } = 0;
        public double AvgMemoryUsage { get; set; } = 0;
        public double MaxInterfaceUtilization { get; set; } = 0;
        public long TotalErrors { get; set; } = 0;
        public Dictionary<string, double> InterfaceUtilizations { get; set; } = new();
        public List<string> HighUtilizationInterfaces { get; set; } = new();
        public double HealthScore { get; set; } = 100;                      // 0-100
    }

    /// <summary>
    /// Interface clustering result
    /// </summary>
    public class InterfaceClustering
    {
        public List<InterfaceCluster> Clusters { get; set; } = new();
    }

    public class InterfaceCluster
    {
        public string ClusterName { get; set; } = string.Empty;
        public List<string> Interfaces { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public double AvgUtilization { get; set; } = 0;
        public int TotalErrors { get; set; } = 0;
    }
}
