using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Interface for vendor-specific log parsers
    /// Each vendor implements this to parse their specific log format
    /// </summary>
    public interface ILogParser
    {
        DeviceVendor Vendor { get; }
        string VendorName { get; }
        
        /// <summary>
        /// Parse log file and return universal log data
        /// </summary>
        UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Async version of Parse
        /// </summary>
        Task<UniversalLogData> ParseAsync(string filePath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Auto-detect if this parser can handle the file
        /// </summary>
        bool CanParse(string filePath);
        
        /// <summary>
        /// Get confidence score (0-100) for parsing this file
        /// </summary>
        int GetConfidenceScore(string filePath);
    }

    /// <summary>
    /// Factory for creating appropriate parser based on log content
    /// </summary>
    public static class ParserFactory
    {
        private static readonly Dictionary<DeviceVendor, ILogParser> _parsers = new();

        static ParserFactory()
        {
            // Register all available parsers
            RegisterParser(new HuaweiVrpParser());
            RegisterParser(new CiscoIosParser());
            RegisterParser(new JuniperJunosParser());
            RegisterParser(new MikrotikRouterOsParser());
            RegisterParser(new GenericTextLogParser()); // Fallback parser
        }

        public static void RegisterParser(ILogParser parser)
        {
            _parsers[parser.Vendor] = parser;
        }

        /// <summary>
        /// Auto-detect vendor and parse log file
        /// </summary>
        public static UniversalLogData ParseLogFile(string filePath)
        {
            // Detect the log type early (running-config, tech-support, syslog, etc.)
            var detectedLogType = LogTypeDetector.Detect(filePath);

            // Score all available parsers and try them in order of adjusted confidence.
            // We compute both the raw confidence reported by the parser and an adjusted
            // score that prefers parsers matching the detected log build type.
            var scores = new List<(ILogParser parser, int rawScore, int adjustedScore)>();
            foreach (var p in _parsers.Values)
            {
                try
                {
                    int raw = p.GetConfidenceScore(filePath);
                    int adjusted = raw;

                    // Prefer configuration/tech-support parsers for config-like logs,
                    // and prefer the generic text parser for syslog/text logs.
                    if (detectedLogType == LogBuildType.Syslog)
                    {
                        if (p.Vendor == DeviceVendor.Unknown) adjusted = Math.Min(100, adjusted + 30);
                    }
                    else if (detectedLogType == LogBuildType.RunningConfig ||
                             detectedLogType == LogBuildType.StartupConfig ||
                             detectedLogType == LogBuildType.ShowInterfaces ||
                             detectedLogType == LogBuildType.ShowVersion ||
                             detectedLogType == LogBuildType.TechSupport ||
                             detectedLogType == LogBuildType.Audit)
                    {
                        // For config/tech outputs prefer vendor-aware parsers (non-Unknown)
                        if (p.Vendor != DeviceVendor.Unknown) adjusted = Math.Min(100, adjusted + 20);
                    }

                    scores.Add((p, raw, adjusted));
                }
                catch
                {
                    scores.Add((p, 0, 0));
                }
            }

            if (!scores.Any())
                throw new InvalidOperationException($"No parsers registered for {filePath}");

            // Sort by adjusted score (preferred), then by raw score as tie-breaker
            scores.Sort((a, b) =>
            {
                var cmp = b.adjustedScore.CompareTo(a.adjustedScore);
                return cmp != 0 ? cmp : b.rawScore.CompareTo(a.rawScore);
            });

            var tried = new List<(ILogParser parser, UniversalLogData? data)>();

            foreach (var (parser, rawScore, adjustedScore) in scores)
            {
                try
                {
                    var data = parser.Parse(filePath);
                    tried.Add((parser, data));

                    if (data == null)
                        continue;

                    // Heuristic: accept immediately if parser produced meaningful results
                    if (IsMeaningful(data))
                    {
                        if (data != null)
                        {
                            data.LogType = detectedLogType;
                            data.VendorSpecificData["DetectedLogType"] = detectedLogType.ToString();
                        }
                        return data!;
                    }
                }
                catch
                {
                    // ignore parser exceptions and try next
                }
            }

            // If none returned a clearly good result, pick the best parsed result we've got
            var best = tried
                .Where(t => t.data != null)
                .OrderByDescending(t => (t.data!.Interfaces?.Count ?? 0))
                .ThenByDescending(t => (t.data!.SuccessfullyParsedLines))
                .FirstOrDefault();

            if (best.data != null)
            {
                best.data.LogType = detectedLogType;
                best.data.VendorSpecificData["DetectedLogType"] = detectedLogType.ToString();
                return best.data;
            }

            throw new InvalidOperationException($"Could not determine log format for {filePath}");
        }

        private static bool IsMeaningful(UniversalLogData data)
        {
            if (data == null) return false;
            if (!string.IsNullOrEmpty(data.Device) && !string.Equals(data.Device, System.IO.Path.GetFileNameWithoutExtension(data.OriginalFileName), StringComparison.OrdinalIgnoreCase))
                return true;
            if ((data.Interfaces?.Count ?? 0) > 0) return true;
            if ((data.Vlans?.Count ?? 0) > 0) return true;
            if ((data.Anomalies?.Count ?? 0) > 0) return true;
            if (data.SuccessfullyParsedLines > 5) return true;
            return false;
        }

        /// <summary>
        /// Parse with specific vendor
        /// </summary>
        public static UniversalLogData ParseLogFile(string filePath, DeviceVendor vendor)
        {
            if (!_parsers.TryGetValue(vendor, out var parser))
                throw new NotSupportedException($"Parser for {vendor} is not available");

            return parser.Parse(filePath);
        }

        /// <summary>
        /// Auto-detect vendor by analyzing file content
        /// </summary>
        private static ILogParser? DetectVendor(string filePath)
        {
            ILogParser? bestParser = null;
            int bestConfidence = 0;

            foreach (var (vendor, parser) in _parsers)
            {
                int confidence = parser.GetConfidenceScore(filePath);
                
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestParser = parser;
                }
            }

            // If confidence is too low, use generic parser
            if (bestConfidence < 30)
            {
                bestParser = _parsers[DeviceVendor.Unknown];
            }

            return bestParser;
        }

        public static List<ILogParser> GetAllParsers()
        {
            return new List<ILogParser>(_parsers.Values);
        }

        public static bool IsParsersAvailable(DeviceVendor vendor)
        {
            return _parsers.ContainsKey(vendor);
        }
    }

    /// <summary>
    /// Base class for parsers with common utilities
    /// </summary>
    public abstract class BaseLogParser : ILogParser
    {
        public abstract DeviceVendor Vendor { get; }
        public abstract string VendorName { get; }


        public virtual async Task<UniversalLogData> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => Parse(filePath, cancellationToken), cancellationToken);
        }

        public abstract UniversalLogData Parse(string filePath, CancellationToken cancellationToken = default);
        
        public abstract bool CanParse(string filePath);
        
        public abstract int GetConfidenceScore(string filePath);

        /// <summary>
        /// Read first N lines of file to detect vendor
        /// </summary>
        protected List<string> ReadFirstLines(string filePath, int count = 50)
        {
            var lines = new List<string>();
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    for (int i = 0; i < count && !reader.EndOfStream; i++)
                    {
                        var line = reader.ReadLine();
                        if (line != null)
                            lines.Add(line);
                    }
                }
            }
            catch { }
            return lines;
        }

        /// <summary>
        /// Stream-based line reading for large files
        /// </summary>
        protected IEnumerable<string> ReadLines(string filePath, CancellationToken cancellationToken = default)
        {
            using (var reader = new StreamReader(filePath))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return line;
                }
            }
        }
    }
}
