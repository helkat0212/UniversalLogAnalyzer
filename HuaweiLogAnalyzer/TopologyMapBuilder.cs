using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalLogAnalyzer
{
    // Node types for visual differentiation
    public enum NodeType { Device, EndpointIp, MacAddress }

    // Simple graph model used by the GUI topology renderer
    public class GraphNode
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Mass { get; set; } = 1.0;
        public NodeType Type { get; set; } = NodeType.Device;
        public string? Origin { get; set; } // e.g., "ARP from eth0", "DHCP from ge-0/0/0"
    }

    public class GraphEdge
    {
        public string SourceId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public class Graph
    {
        public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();
    }

    // Simple OUI (Organizationally Unique Identifier) lookup for MAC addresses
    public static class OuiLookup
    {
        private static readonly Dictionary<string, string> OuiDatabase = new(StringComparer.OrdinalIgnoreCase)
        {
            { "00:00:00", "Xerox" }, { "00:00:5E", "IANA" }, { "00:0C:29", "VMware" }, { "08:00:27", "Cadillac" },
            { "52:54:00", "QEMU" }, { "AA:BB:CC", "Test" }, { "FF:FF:FF", "Broadcast" },
            { "00:1A:2B", "Cisco" }, { "00:11:22", "Huawei" }, { "00:50:F2", "Microsoft" },
            { "00:13:10", "Juniper" }, { "08:54:C9", "Mikrotik" }
        };

        public static string GetVendor(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return "Unknown";
            var oui = mac.Substring(0, Math.Min(8, mac.Length));
            return OuiDatabase.TryGetValue(oui, out var vendor) ? vendor : "Unknown";
        }
    }

    public static class TopologyMapBuilder
    {
        // Build a simple node/edge graph from parsed UniversalLogData
        public static Graph BuildGraphFromLogs(List<UniversalLogData> logs, bool includeEndpoints = true, bool includeMacs = true, bool collapseHosts = false)
        {
            var g = new Graph();

            // Create device nodes
            foreach (var log in logs)
            {
                var id = log.Device ?? log.SystemName ?? Guid.NewGuid().ToString();
                if (!g.Nodes.Any(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    g.Nodes.Add(new GraphNode { Id = id, Label = id });
                }
            }

            // Create edges via description matching, IP matching and BGP peers
            // Build IP -> device map
            var ipToDevice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var log in logs)
            {
                foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                {
                    if (!string.IsNullOrWhiteSpace(iface.IpAddress))
                    {
                        var ip = iface.IpAddress.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (!string.IsNullOrWhiteSpace(ip) && !ipToDevice.ContainsKey(ip)) ipToDevice[ip] = log.Device ?? log.SystemName ?? ip;
                    }
                }
            }

            foreach (var log in logs)
            {
                var device = log.Device ?? log.SystemName ?? Guid.NewGuid().ToString();

                // description-based
                foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                {
                    if (!string.IsNullOrWhiteSpace(iface.Description))
                    {
                        foreach (var other in logs)
                        {
                            if (other == log) continue;
                            var otherId = other.Device ?? other.SystemName ?? string.Empty;
                            if (string.IsNullOrEmpty(otherId)) continue;
                            if (iface.Description.Contains(otherId, StringComparison.OrdinalIgnoreCase))
                            {
                                AddEdge(g, device, otherId, iface.Name);
                            }
                        }
                    }
                }

                // IP-based
                foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                {
                    if (string.IsNullOrWhiteSpace(iface.IpAddress)) continue;
                    var ip = iface.IpAddress.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (string.IsNullOrWhiteSpace(ip)) continue;
                    if (ipToDevice.TryGetValue(ip, out var otherDevice) && !string.Equals(otherDevice, device, StringComparison.OrdinalIgnoreCase))
                    {
                        AddEdge(g, device, otherDevice, iface.Name + " / " + ip);
                    }
                }

                // BGP peers (extract IPs)
                foreach (var peer in log.BgpPeers ?? new List<string>())
                {
                    var ipMatch = System.Text.RegularExpressions.Regex.Match(peer, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b)");
                    if (ipMatch.Success)
                    {
                        var peerIp = ipMatch.Groups[1].Value;
                        if (ipToDevice.TryGetValue(peerIp, out var otherDevice) && !string.Equals(otherDevice, device, StringComparison.OrdinalIgnoreCase))
                        {
                            AddEdge(g, device, otherDevice, "BGP");
                        }
                    }
                }

                if (includeEndpoints)
                {
                    // Extract other connected endpoint IPs from vendor-specific data and interface descriptions
                    var connectedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Look in vendor-specific data for IPs (ARP tables, DHCP leases, LLDP/CDP neighbors etc.)
                    if (log.VendorSpecificData != null)
                    {
                        foreach (var kv in log.VendorSpecificData)
                        {
                            if (kv.Value == null) continue;
                            if (kv.Value is string sVal)
                            {
                                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(sVal, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b)"))
                                    connectedIps.Add(m.Groups[1].Value);
                            }
                            else if (kv.Value is IEnumerable<string> listVal)
                            {
                                foreach (var item in listVal)
                                    if (!string.IsNullOrWhiteSpace(item))
                                        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(item, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b)"))
                                            connectedIps.Add(m.Groups[1].Value);
                            }
                        }
                    }

                    // Look in interface descriptions and raw IP fields for host IPs
                    foreach (var iface in log.Interfaces ?? new List<InterfaceInfo>())
                    {
                        if (!string.IsNullOrWhiteSpace(iface.Description))
                        {
                            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(iface.Description, "(\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b)"))
                                connectedIps.Add(m.Groups[1].Value);
                        }
                        if (!string.IsNullOrWhiteSpace(iface.IpAddress))
                        {
                            // Some interface ip fields contain secondary/peer info separated by spaces
                            var parts = iface.IpAddress.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                                if (System.Net.IPAddress.TryParse(p, out _)) connectedIps.Add(p);
                        }
                    }

                    // Add edges for connected IPs: if IP maps to known device -> connect devices; else create IP node
                    foreach (var cip in connectedIps)
                    {
                        if (string.IsNullOrWhiteSpace(cip)) continue;
                        if (ipToDevice.TryGetValue(cip, out var mappedDevice))
                        {
                            if (!string.Equals(mappedDevice, device, StringComparison.OrdinalIgnoreCase)) AddEdge(g, device, mappedDevice, "connected");
                        }
                        else
                        {
                            // create an endpoint node for the IP and connect
                            var ipNodeId = cip;
                            if (!g.Nodes.Any(n => string.Equals(n.Id, ipNodeId, StringComparison.OrdinalIgnoreCase)))
                                g.Nodes.Add(new GraphNode { Id = ipNodeId, Label = ipNodeId, Mass = 0.5, Type = NodeType.EndpointIp });
                            AddEdge(g, device, ipNodeId, "connected");
                        }
                    }
                }

                // Vendor neighbors
                if (log.VendorSpecificData != null && log.VendorSpecificData.TryGetValue("Neighbors", out var neighObj) && neighObj is IEnumerable<string> neighList)
                {
                    foreach (var n in neighList)
                    {
                        var other = n?.Trim();
                        if (string.IsNullOrWhiteSpace(other)) continue;
                        var mapped = logs.FirstOrDefault(l => string.Equals(l.Device, other, StringComparison.OrdinalIgnoreCase))?.Device;
                        if (!string.IsNullOrWhiteSpace(mapped)) AddEdge(g, device, mapped, "neighbor");
                    }
                }
            }

            // Initialize positions in a circle
            var count = g.Nodes.Count;
            int idx = 0;
            foreach (var n in g.Nodes)
            {
                var angle = 2.0 * Math.PI * idx / Math.Max(1, count);
                n.X = 400 + 200 * Math.Cos(angle);
                n.Y = 200 + 200 * Math.Sin(angle);
                idx++;
            }

            // Optionally add MAC-address nodes and link them to devices
            if (includeMacs)
            {
                foreach (var log in logs)
                {
                    var device = log.Device ?? log.SystemName ?? Guid.NewGuid().ToString();
                    // ARP-based MACs
                    foreach (var a in log.ArpTable ?? new List<ArpEntry>())
                    {
                        if (string.IsNullOrWhiteSpace(a.Mac)) continue;
                        var macId = a.Mac.ToUpperInvariant();
                        if (!g.Nodes.Any(n => string.Equals(n.Id, macId, StringComparison.OrdinalIgnoreCase)))
                            g.Nodes.Add(new GraphNode { Id = macId, Label = macId, Mass = 0.4, Type = NodeType.MacAddress, Origin = $"ARP from {a.Interface}" });
                        AddEdge(g, device, macId, "MAC");
                        // If endpoint IP node exists, connect IP to MAC
                        if (includeEndpoints && !string.IsNullOrWhiteSpace(a.Ip) && g.Nodes.Any(n => string.Equals(n.Id, a.Ip, StringComparison.OrdinalIgnoreCase)))
                        {
                            AddEdge(g, a.Ip, macId, "ip-mac");
                        }
                    }

                    // DHCP leases
                    foreach (var d in log.DhcpLeases ?? new List<DhcpLease>())
                    {
                        if (string.IsNullOrWhiteSpace(d.Mac)) continue;
                        var macId = d.Mac.ToUpperInvariant();
                        if (!g.Nodes.Any(n => string.Equals(n.Id, macId, StringComparison.OrdinalIgnoreCase)))
                            g.Nodes.Add(new GraphNode { Id = macId, Label = macId, Mass = 0.4, Type = NodeType.MacAddress, Origin = "DHCP lease" });
                        AddEdge(g, device, macId, "DHCP");
                        if (includeEndpoints && !string.IsNullOrWhiteSpace(d.Ip) && g.Nodes.Any(n => string.Equals(n.Id, d.Ip, StringComparison.OrdinalIgnoreCase)))
                        {
                            AddEdge(g, d.Ip, macId, "ip-mac");
                        }
                    }
                }
            }

            // Optionally collapse endpoint IP and MAC nodes connected to each device into a cluster
            if (collapseHosts && includeEndpoints)
            {
                CollapseHostClusters(g);
            }

            // Perform light force-directed layout to improve positions
            ForceDirectedLayout(g, 400, 300, iterations: 300);

            return g;
        }

        private static void AddEdge(Graph g, string a, string b, string label)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
            if (!g.Nodes.Any(n => string.Equals(n.Id, a, StringComparison.OrdinalIgnoreCase))) g.Nodes.Add(new GraphNode { Id = a, Label = a });
            if (!g.Nodes.Any(n => string.Equals(n.Id, b, StringComparison.OrdinalIgnoreCase))) g.Nodes.Add(new GraphNode { Id = b, Label = b });
            // Avoid duplicate edges (undirected)
            if (!g.Edges.Any(e => (string.Equals(e.SourceId, a, StringComparison.OrdinalIgnoreCase) && string.Equals(e.TargetId, b, StringComparison.OrdinalIgnoreCase)) ||
                                   (string.Equals(e.SourceId, b, StringComparison.OrdinalIgnoreCase) && string.Equals(e.TargetId, a, StringComparison.OrdinalIgnoreCase))))
            {
                g.Edges.Add(new GraphEdge { SourceId = a, TargetId = b, Label = label });
            }
        }

        // Simple force-directed layout (Fruchterman-Reingold style) tuned for moderate graphs
        public static void ForceDirectedLayout(Graph g, double width, double height, int iterations = 200)
        {
            if (g == null) return;
            var rand = new Random(0);
            double area = width * height;
            double k = Math.Sqrt(area / Math.Max(1, g.Nodes.Count));
            double t = Math.Max(width, height) / 10.0;
            double dt = t / (iterations + 1);

            var pos = g.Nodes.ToDictionary(n => n.Id, n => new Tuple<double, double>(n.X, n.Y));

            for (int iter = 0; iter < iterations; iter++)
            {
                var disp = g.Nodes.ToDictionary(n => n.Id, n => new { X = 0.0, Y = 0.0 });

                // repulsive forces
                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    for (int j = i + 1; j < g.Nodes.Count; j++)
                    {
                        var vi = g.Nodes[i];
                        var vj = g.Nodes[j];
                        double dx = vi.X - vj.X;
                        double dy = vi.Y - vj.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                        double force = (k * k) / dist;
                        double ux = dx / dist;
                        double uy = dy / dist;
                        vi.X += ux * force * 0.001;
                        vi.Y += uy * force * 0.001;
                        vj.X -= ux * force * 0.001;
                        vj.Y -= uy * force * 0.001;
                    }
                }

                // attractive forces (edges)
                foreach (var e in g.Edges)
                {
                    var s = g.Nodes.First(n => string.Equals(n.Id, e.SourceId, StringComparison.OrdinalIgnoreCase));
                    var tnode = g.Nodes.First(n => string.Equals(n.Id, e.TargetId, StringComparison.OrdinalIgnoreCase));
                    double dx = s.X - tnode.X;
                    double dy = s.Y - tnode.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                    double force = (dist * dist) / k;
                    double ux = dx / dist;
                    double uy = dy / dist;
                    s.X -= ux * force * 0.001;
                    s.Y -= uy * force * 0.001;
                    tnode.X += ux * force * 0.001;
                    tnode.Y += uy * force * 0.001;
                }

                // cool down
                t -= dt;
            }

            // clamp to area
            foreach (var n in g.Nodes)
            {
                n.X = Math.Max(20, Math.Min(width - 20, n.X));
                n.Y = Math.Max(20, Math.Min(height - 20, n.Y));
            }
        }

        /// <summary>
        /// Collapse endpoint and MAC nodes into clusters per device to reduce clutter
        /// </summary>
        private static void CollapseHostClusters(Graph g)
        {
            if (g == null || !g.Nodes.Any()) return;

            // Find device nodes (Type == NodeType.Device)
            var devices = g.Nodes.Where(n => n.Type == NodeType.Device).ToList();

            foreach (var device in devices)
            {
                // Find all endpoint and MAC nodes connected directly to this device
                var connectedEdges = g.Edges.Where(e => 
                    (string.Equals(e.SourceId, device.Id, StringComparison.OrdinalIgnoreCase) && g.Nodes.Any(n => string.Equals(n.Id, e.TargetId) && n.Type != NodeType.Device)) ||
                    (string.Equals(e.TargetId, device.Id, StringComparison.OrdinalIgnoreCase) && g.Nodes.Any(n => string.Equals(n.Id, e.SourceId) && n.Type != NodeType.Device))
                ).ToList();

                if (connectedEdges.Count <= 3) continue; // Don't collapse if few connections

                // Create a cluster node
                var clusterId = $"{device.Id}_cluster";
                var clusterLabel = $"{device.Label ?? device.Id}\n({connectedEdges.Count} hosts)";
                
                // Remove endpoint nodes connected to device and replace with cluster
                var nodesToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var edge in connectedEdges)
                {
                    var targetId = string.Equals(edge.SourceId, device.Id, StringComparison.OrdinalIgnoreCase) ? edge.TargetId : edge.SourceId;
                    var targetNode = g.Nodes.FirstOrDefault(n => string.Equals(n.Id, targetId, StringComparison.OrdinalIgnoreCase));
                    if (targetNode?.Type != NodeType.Device)
                    {
                        nodesToRemove.Add(targetId);
                    }
                }

                if (nodesToRemove.Count > 0)
                {
                    // Remove the nodes
                    g.Nodes.RemoveAll(n => nodesToRemove.Contains(n.Id));
                    // Remove edges to those nodes
                    g.Edges.RemoveAll(e => nodesToRemove.Contains(e.SourceId) || nodesToRemove.Contains(e.TargetId));
                    // Add a single cluster summary line to the device's label if it doesn't already have it
                    if (device?.Label != null && !device.Label.Contains("cluster", StringComparison.OrdinalIgnoreCase))
                    {
                        device.Label += $"\n[{nodesToRemove.Count} collapsed]";
                    }
                }
            }
        }
    }
}

