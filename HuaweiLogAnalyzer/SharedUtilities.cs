using System;
using System.IO;

namespace UniversalLogAnalyzer
{
    public static class SharedUtilities
    {
        /// <summary>
        /// Gets a device folder name from UniversalLogData, using SystemName, Device, Version, or OriginalFileName in that order.
        /// </summary>
        public static string GetDeviceFolderName(UniversalLogData log)
        {
            if (!string.IsNullOrWhiteSpace(log.SystemName))
                return log.SystemName;
            if (!string.IsNullOrWhiteSpace(log.Device))
                return log.Device;
            if (!string.IsNullOrWhiteSpace(log.Version))
                return log.Version;

            Console.WriteLine($"WARNING: Could not determine device name for log file {log.OriginalFileName}");
            return $"<{log.OriginalFileName} - unknown>";
        }

        /// <summary>
        /// Sanitizes a file name by replacing invalid characters with underscores.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}

