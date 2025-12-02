using System.IO;
using Xunit;

namespace UniversalLogAnalyzer.Tests
{
    public class AnalyzerTests
    {
        [Fact]
        public void AnalyzeFile_HappyPath_ParsesInterfaceAndIp()
        {
            var text = @"
sysname TestDevice
display version V200R005C00
interface GigabitEthernet0/5/11
 negotiation auto
 undo shutdown
 ip address 192.0.2.1 255.255.255.0
 description Uplink to core
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
            Assert.Contains(data.Interfaces, i => i.Name == "GigabitEthernet0/5/11");
            var iface = data.Interfaces.Find(i => i.Name == "GigabitEthernet0/5/11");
            Assert.Equal("Uplink to core", iface.Description);
            Assert.Equal("192.0.2.1", iface.Ip);
            Assert.Equal("255.255.255.0", iface.Mask);
        }

        [Fact]
        public void AnalyzeFile_HuaweiVlans_ExtractsVlanIds()
        {
            var text = @"
sysname TestSwitch
display version V200R005C00
vlan 10
#
vlan 20
#
vlan 100
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
            Assert.NotEmpty(data.Vlans);
            Assert.Contains("10", data.Vlans);
            Assert.Contains("20", data.Vlans);
        }

        [Fact]
        public void AnalyzeFile_BgpBlock_ExtractsPeerAndAsn()
        {
            var text = @"
sysname TestRouter
display version V200R005C00
bgp 65000
 peer 192.0.2.2 as-number 65001
 neighbor 192.0.2.3
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.NotEmpty(data.BgpPeers);
        }

        [Fact]
        public void AnalyzeFile_AclDefinition_ExtractsAcls()
        {
            var text = @"
sysname Router
display version V200R005C00
acl 100
#
acl 101
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.NotEmpty(data.Acls);
        }
    }
}
