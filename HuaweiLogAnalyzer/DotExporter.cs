using System;
using System.Collections.Generic;

namespace UniversalLogAnalyzer
{
    [Obsolete("The DOT exporter was removed. Topology visualization is provided in the GUI only.")]
    public static class DotExporter
    {
        public static List<string> ExportTopologyAsDot(List<UniversalLogData> logs, string? outputFolder = null)
        {
            throw new NotSupportedException("DOT export was intentionally removed. Use the GUI Topology tab instead.");
        }
    }
}
