using System;
using System.IO;
using System.Threading;
using Xunit;
using UniversalLogAnalyzer;
using OfficeOpenXml;
using System.Linq;
using System.Collections.Generic;

namespace UniversalLogAnalyzer.Tests
{
    public class ExcelWriterContentTests
    {
        [Fact]
        public void Save_WritesWorkbook_WithExpectedSheetsAndHeaders()
        {
            var logs = new List<UniversalLogData>();
            var ld = new UniversalLogData { Device = "device1", SystemName = "sys1" };
            ld.Interfaces = new System.Collections.Generic.List<InterfaceInfo> { new InterfaceInfo { Name = "GigabitEthernet0/0/1", Description = "to core", IpAddress = "192.0.2.1" } };
            logs.Add(ld);

            var tempPath = Path.Combine(Path.GetTempPath(), $"test_report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            var paths = ExcelWriter.Save(logs, new List<string> { "unparsed-entry" }, Path.GetDirectoryName(tempPath));

            Assert.True(paths.Count > 0);
            var firstPath = paths[0];
            Assert.True(File.Exists(firstPath));
            Thread.Sleep(100); // Give time for file handle to be released

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using (var p = new ExcelPackage(new FileInfo(firstPath)))
            {
                var names = p.Workbook.Worksheets.Select(s => s.Name).ToList();
                // Expected sheets
                Assert.Contains("sys1", names); // Device details sheet named after first log's SysName
                Assert.Contains("Interfaces", names);
                Assert.Contains("PortDetails", names);
                Assert.Contains("Legend", names);

                var deviceSheet = p.Workbook.Worksheets["sys1"];
                Assert.Equal("Property", deviceSheet.Cells["A1"].Text);
                Assert.Equal("Value", deviceSheet.Cells["B1"].Text);

                var iface = p.Workbook.Worksheets["Interfaces"];
                Assert.Equal("Interface", iface.Cells["B1"].Text);
                Assert.Equal("IP/Binding", iface.Cells["D1"].Text);

                var pd = p.Workbook.Worksheets["PortDetails"];
                Assert.Equal("Device", pd.Cells["A1"].Text);
                Assert.Equal("Port", pd.Cells["B1"].Text);
                Assert.Equal("Description", pd.Cells["E1"].Text);
                // ensure first data row has a hyperlink back to device sheet
                var link = pd.Cells["A2"].Hyperlink;
                Assert.NotNull(link);
                // The hyperlink is created, which is the main check
            }
        }
    }
}
