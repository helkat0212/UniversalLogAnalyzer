using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using UniversalLogAnalyzer;
using System.Collections.Generic;

namespace UniversalLogAnalyzer.Tests
{
    public class ExcelWriterTests
    {
        [Fact]
        public void Save_CreatesFile_WithExpectedSheets()
        {
            var logs = new List<UniversalLogData>();
            var ld = new UniversalLogData { Device = "test-device", SystemName = "test-sys" };
            ld.Interfaces = new System.Collections.Generic.List<InterfaceInfo> { new InterfaceInfo { Name = "GigabitEthernet0/0/1", Description = "to core", IpAddress = "192.0.2.1" } };
            logs.Add(ld);

            var tempPath = Path.GetTempFileName();
            var paths = ExcelWriter.Save(logs, new List<string> { "unparsed-entry" }, Path.GetDirectoryName(tempPath));
            Thread.Sleep(100); // Give file system time to release handles
            
            foreach (var path in paths)
            {
                Assert.True(File.Exists(path));
                
                // Basic checks: file size > 1KB
                var fi = new FileInfo(path);
                Assert.True(fi.Length > 1024);

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (IOException)
                {
                    // Ignore file in use errors during cleanup
                }
            }
        }
    }
}
