using System;
using System.IO;
using UniversalLogAnalyzer;

class Program
{
    static int Main(string[] args)
    {
        var path = args.Length>0? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "log_example2.txt");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) { Console.WriteLine($"File not found: {path}"); return 2; }
        try
        {
            var detected = UniversalLogAnalyzer.LogTypeDetector.Detect(path);
            Console.WriteLine($"Detected log type: {detected}");
            Console.WriteLine("Parser confidence scores:");
            foreach (var p in ParserFactory.GetAllParsers())
            {
                try { Console.WriteLine($" - {p.VendorName}: {p.GetConfidenceScore(path)}"); } catch { Console.WriteLine($" - {p.VendorName}: (error)"); }
            }

            var data = ParserFactory.ParseLogFile(path);
            Console.WriteLine($"Parser factory returned log type: {data.LogType}");
            // If factory chose Generic (unknown) but file looks Huawei-like, try Huawei explicitly
            if (data != null && data.Vendor == DeviceVendor.Unknown)
            {
                Console.WriteLine("Factory selected Generic parser. Trying Huawei parser explicitly...");
                try
                {
                    var hData = ParserFactory.ParseLogFile(path, DeviceVendor.Huawei);
                    if (hData != null && (hData.Interfaces?.Count ?? 0) > 0)
                    {
                        Console.WriteLine("Huawei parser produced richer output; using Huawei results below.");
                        data = hData;
                    }
                }
                catch { }
            }
            if (data==null) { Console.WriteLine("No parser produced output."); return 1; }
            Console.WriteLine($"Device: {data.Device} (Vendor: {data.Vendor})");
            Console.WriteLine($"SystemName: {data.SystemName}");
            Console.WriteLine($"Version: {data.Version}");
            Console.WriteLine($"Interfaces: {data.Interfaces?.Count ?? 0}");
            Console.WriteLine($"VLANs: {string.Join(",", data.Vlans)}");
            Console.WriteLine($"BGP peers: {string.Join(",", data.BgpPeers)}");
            Console.WriteLine($"NTP servers: {string.Join(",", data.NtpServers)}");
            Console.WriteLine($"Anomalies: {data.Anomalies?.Count ?? 0}");
            if (data.Anomalies!=null) foreach(var a in data.Anomalies) Console.WriteLine($" - {a.Category}: {a.Description} ({a.Severity})");
            return 0;
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Error parsing file: {ex}");
            return 3;
        }
    }
}
