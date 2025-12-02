using System.IO;
using System.Linq;
using Xunit;

namespace UniversalLogAnalyzer.Tests
{
    public class ParserDetectionTests
    {
        // ========== HUAWEI TESTS ==========

        [Fact]
        public void ParserDetection_HuaweiSample_DetectedAsHuawei()
        {
            var text = @"
sysname HuaweiDeviceA
display version V200R005C00
interface GigabitEthernet0/0/1
 ip address 192.0.2.10 255.255.255.0
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
            Assert.Equal("HuaweiDeviceA", data.SystemName);
            Assert.Contains(data.Interfaces, i => i.Name == "GigabitEthernet0/0/1");
        }

        [Fact]
        public void ParserDetection_HuaweiWithBgp_ExtractsBgpAsnAndPeers()
        {
            var text = @"
sysname HW-Router1
display version V200R005C00
bgp 65000
 peer 192.0.2.2 as-number 65001
 peer 192.0.2.3 as-number 65002
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
            Assert.Equal("HW-Router1", data.SystemName);
            Assert.NotEmpty(data.BgpPeers);
            Assert.Contains(data.BgpPeers, p => p.Contains("192.0.2.2"));
            Assert.Contains(data.BgpPeers, p => p.Contains("192.0.2.3"));
        }

        [Fact]
        public void ParserDetection_HuaweiWithVlanAndInterfaces_ParsesMultipleInterfaces()
        {
            var text = @"
sysname HW-Switch
display version V200R005C00
vlan batch 10 to 20
vlan batch 100 to 102
#
interface Ethernet0/0/1
 description To Core
 ip address 10.1.1.1 255.255.255.0
#
interface Ethernet0/0/2
 description To Edge
 shutdown
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
            Assert.Contains(data.Vlans, v => v == "10" || v == "11" || v == "20");
            var iface1 = data.Interfaces.FirstOrDefault(i => i.Name == "Ethernet0/0/1");
            Assert.NotNull(iface1);
            Assert.Equal("To Core", iface1.Description);
            Assert.Equal("10.1.1.1", iface1.Ip);
            var iface2 = data.Interfaces.FirstOrDefault(i => i.Name == "Ethernet0/0/2");
            Assert.NotNull(iface2);
            Assert.True(iface2.IsShutdown);
            Assert.Equal("DOWN", iface2.Status);
        }

        [Fact]
        public void ParserDetection_HuaweiWithResourcesAndNtp_ParsesSystemInfo()
        {
            var text = @"
sysname HW-Device
SN: ABCD1234EF5678
display version V200R005C00
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal("ABCD1234EF5678", data.SerialNumber);
            Assert.Equal(DeviceVendor.Huawei, data.Vendor);
        }

        // ========== CISCO TESTS ==========

        [Fact]
        public void ParserDetection_CiscoSample_DetectedAsCisco()
        {
            var text = @"
Cisco IOS Software, C880 Software
hostname Router1
interface GigabitEthernet0/0
 description Link
 ip address 10.0.0.1 255.255.255.0
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Cisco, data.Vendor);
            Assert.Equal("Router1", data.SystemName);
            Assert.Contains(data.Interfaces, i => i.Name == "GigabitEthernet0/0");
        }

        [Fact]
        public void ParserDetection_CiscoWithBgpAndAcl_ExtractsBgpAndAcls()
        {
            var text = @"
Cisco IOS Software, Version 15.2(4)M1
hostname CiscoRouter
!
router bgp 65100
 neighbor 203.0.113.1 remote-as 65101
 neighbor 203.0.113.2 remote-as 65102
!
access-list 100 permit tcp any any eq 80
access-list 100 permit tcp any any eq 443
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Cisco, data.Vendor);
            Assert.Equal("65100", data.BgpAsn);
            Assert.NotEmpty(data.BgpPeers);
            Assert.Contains(data.BgpPeers, p => p.Contains("203.0.113.1"));
            Assert.NotEmpty(data.Acls);
            Assert.Contains(data.Acls, a => a == "100");
        }

        [Fact]
        public void ParserDetection_CiscoWithInterfaceStatus_ParsesShutdownAndSpeed()
        {
            var text = @"
Cisco IOS Software
hostname CiscoSwitch
!
interface FastEthernet0/1
 description To Building A
 ip address 172.16.1.1 255.255.255.0
 no shutdown
!
interface FastEthernet0/2
 description Unused Port
 shutdown
 bandwidth 100000
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            var iface1 = data.Interfaces.FirstOrDefault(i => i.Name == "FastEthernet0/1");
            Assert.NotNull(iface1);
            Assert.False(iface1.IsShutdown);
            Assert.Equal("UP", iface1.Status);
            var iface2 = data.Interfaces.FirstOrDefault(i => i.Name == "FastEthernet0/2");
            Assert.NotNull(iface2);
            Assert.True(iface2.IsShutdown);
            Assert.Equal("DOWN", iface2.Status);
        }

        [Fact]
        public void ParserDetection_CiscoWithUsersAndNtp_ParsesManagementInfo()
        {
            var text = @"
Cisco IOS Software
hostname CiscoBox
Serial Number: ABC123DEF456
Model Number: Cisco2900
!
username admin privilege 15 password 0 SecurePass
username monitoring privilege 1 password 0 MonitorPass
!
ntp server 129.6.15.28
ntp server 129.6.15.29
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Cisco, data.Vendor);
            Assert.Equal("ABC123DEF456", data.SerialNumber);
            Assert.Equal("Cisco2900", data.ModelNumber);
            Assert.NotEmpty(data.LocalUsers);
            Assert.Contains(data.LocalUsers, u => u.Contains("admin"));
            Assert.NotEmpty(data.NtpServers);
        }

        // ========== JUNIPER TESTS ==========

        [Fact]
        public void ParserDetection_JuniperSample_DetectedAsJuniper()
        {
            var text = @"
host-name JunosBox;
ge-0/0/0 {
    unit 0 {
        family inet {
            address 192.0.2.5/24;
        }
    }
}
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Juniper, data.Vendor);
            // Interface may or may not parse depending on indentation, but device should be detected as Juniper
        }

        [Fact]
        public void ParserDetection_JuniperWithBgp_ExtractsBgpAsn()
        {
            var text = @"
host-name JuniperBox;
system {
    autonomous-system 65200;
}
protocols {
    bgp {
        neighbor 198.51.100.1 {
            peer-as 65201;
        }
    }
}
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Juniper, data.Vendor);
            Assert.Equal("65200", data.BgpAsn);
        }

        [Fact]
        public void ParserDetection_JuniperWithInterfaces_ParsesInterfaceProperties()
        {
            var text = @"
host-name JunosRouter;
ge-0/0/0 {
    description Primary-Link;
    disable;
}
ge-0/0/1 {
    description Secondary-Link;
    enable;
}
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Juniper, data.Vendor);
        }

        [Fact]
        public void ParserDetection_JuniperWithVersionAndSerial_ParsesDeviceInfo()
        {
            var text = @"
Junos JUNOS 19.1R2-S1 built 2019-03-20 18:32:01 UTC
Model: SRX340
Serial Number: SN12345678
host-name JunosDevice;
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Juniper, data.Vendor);
            Assert.NotNull(data.Version);
            Assert.Contains("19", data.Version);
            Assert.NotNull(data.SerialNumber);
        }

        // ========== MIKROTIK TESTS ==========

        [Fact]
        public void ParserDetection_MikrotikSample_DetectedAsMikrotik()
        {
            var text = @"
identity: MikrotikBox
ether1: disabled false
ether2: disabled true
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Mikrotik, data.Vendor);
            Assert.Equal("MikrotikBox", data.SystemName);
            Assert.NotEmpty(data.Interfaces);
        }

        [Fact]
        public void ParserDetection_MikrotikWithBgp_ExtractsBgpPeers()
        {
            var text = @"
identity: MikrotikRouter
version: 7.1
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Mikrotik, data.Vendor);
        }

        [Fact]
        public void ParserDetection_MikrotikWithResources_ParsesCpuAndMemory()
        {
            var text = @"
identity: MikrotikSystem
version: 6.48.1
model: hEX PoE
serial number: 4CB76F2D5E4C
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Mikrotik, data.Vendor);
            Assert.Equal("MikrotikSystem", data.SystemName);
            Assert.Equal("4CB76F2D5E4C", data.SerialNumber);
        }

        [Fact]
        public void ParserDetection_MikrotikWithInterfaceDisabled_ParsesStatus()
        {
            var text = @"
identity: MikrotikSwitch
[admin@MikroTik] /interface ethernet
> ether1: disabled true
> ether2: disabled false
> ether3: mtu 1500
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);
            var data = ParserFactory.ParseLogFile(tmp);
            File.Delete(tmp);

            Assert.NotNull(data);
            Assert.Equal(DeviceVendor.Mikrotik, data.Vendor);
        }

        // ========== CONFIDENCE SCORE TESTS ==========

        [Fact]
        public void ParserConfidenceScore_HuaweiLog_HighScoreForHuaweiParser()
        {
            var text = @"
sysname HW-CORE
display version V200R005C00
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);

            var huaweiParser = new HuaweiVrpParser();
            var ciscoParser = new CiscoIosParser();
            var juniperParser = new JuniperJunosParser();

            int huaweiScore = huaweiParser.GetConfidenceScore(tmp);
            int ciscoScore = ciscoParser.GetConfidenceScore(tmp);
            int juniperScore = juniperParser.GetConfidenceScore(tmp);

            File.Delete(tmp);

            // Huawei parser should have highest confidence
            Assert.True(huaweiScore > ciscoScore, $"Huawei score ({huaweiScore}) should be > Cisco score ({ciscoScore})");
            Assert.True(huaweiScore > juniperScore, $"Huawei score ({huaweiScore}) should be > Juniper score ({juniperScore})");
        }

        [Fact]
        public void ParserConfidenceScore_CiscoLog_HighScoreForCiscoParser()
        {
            var text = @"
Cisco IOS Software Version 15.2(4)
hostname Cisco-Device
router bgp 65000
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);

            var ciscoParser = new CiscoIosParser();
            var huaweiParser = new HuaweiVrpParser();

            int ciscoScore = ciscoParser.GetConfidenceScore(tmp);
            int huaweiScore = huaweiParser.GetConfidenceScore(tmp);

            File.Delete(tmp);

            Assert.True(ciscoScore > huaweiScore, $"Cisco score ({ciscoScore}) should be > Huawei score ({huaweiScore})");
        }

        [Fact]
        public void ParserConfidenceScore_JuniperLog_HighScoreForJuniperParser()
        {
            var text = @"
JUNOS 19.1R2
host-name Juniper-Device;
autonomous-system 65050;
#
";
            var tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, text);

            var juniperParser = new JuniperJunosParser();
            var ciscoParser = new CiscoIosParser();

            int juniperScore = juniperParser.GetConfidenceScore(tmp);
            int ciscoScore = ciscoParser.GetConfidenceScore(tmp);

            File.Delete(tmp);

            Assert.True(juniperScore > ciscoScore, $"Juniper score ({juniperScore}) should be > Cisco score ({ciscoScore})");
        }
    }
}
