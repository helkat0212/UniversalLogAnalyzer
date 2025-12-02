using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace UniversalLogAnalyzer
{
    public partial class MainWindow : Window
    {
        private readonly string _outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private readonly Dictionary<string, UniversalLogData> _results = new();
        private CancellationTokenSource? _cancellationTokenSource;

        private readonly List<string> _logBuffer = new();
        private readonly DispatcherTimer _logFlushTimer;
        private readonly object _logBufferLock = new();

        private readonly SemaphoreSlim _processingSemaphore = new(Math.Min(Environment.ProcessorCount, 4));

        private double _zoom = 1.0;
        private const double ZoomStep = 1.12;
        private const double MinZoom = 0.2;
        private const double MaxZoom = 5.0;
        private bool _isPanning = false;
        private System.Windows.Point _lastPanPoint;
        private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
        private readonly TranslateTransform _translateTransform = new(0, 0);
        private readonly TransformGroup _transformGroup = new();

        public MainWindow()
        {
            InitializeComponent();

            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            if (TopologyCanvas != null) TopologyCanvas.RenderTransform = _transform_group_repair();

            Directory.CreateDirectory(_outputFolder);
            UpdateStatus("Idle", 0);

            // Populate LogType selector so users can see detected type and override it
            try
            {
                if (LogTypeComboBox != null)
                {
                    LogTypeComboBox.ItemsSource = Enum.GetValues(typeof(LogBuildType)).Cast<LogBuildType>();
                    LogTypeComboBox.SelectedItem = LogBuildType.Unknown;
                }
            }
            catch { }

            _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _logFlushTimer.Tick += LogFlushTimer_Tick;
            _logFlushTimer.Start();
        }

        // small helper to avoid accidental name collisions while ensuring the transform group instance
        private TransformGroup _transform_group_repair() => _transformGroup;

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            if (CancelButton != null) CancelButton.IsEnabled = false;
        }

        private void UpdateStatus(string status, double progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusText != null) StatusText.Text = status;
                // reference the instance field explicitly so the compiler binds to the generated field
                if (this.ProgressBar != null) this.ProgressBar.Value = progress;
            });
        }

        private void UpdateTrees(string filename, UniversalLogData data, bool isUnparsed = false)
        {
            var treeView = isUnparsed ? UnparsedTree : ResultsTree;
            if (treeView == null) return;

            var existing = treeView.Items.Cast<TreeViewItem>().FirstOrDefault(i => string.Equals(i.Header?.ToString(), Path.GetFileName(filename), StringComparison.OrdinalIgnoreCase));
            if (existing != null) treeView.Items.Remove(existing);

            var root = new TreeViewItem { Header = Path.GetFileName(filename) };
            if (!string.IsNullOrEmpty(data.Device))
            {
                root.Items.Add(new TreeViewItem { Header = $"Device: {data.Device}" });
                if (!string.IsNullOrEmpty(data.SystemName)) root.Items.Add(new TreeViewItem { Header = $"System Name: {data.SystemName}" });
                if (!string.IsNullOrEmpty(data.Version)) root.Items.Add(new TreeViewItem { Header = $"Version: {data.Version}" });

                if (data.Interfaces != null && data.Interfaces.Any())
                {
                    var ifs = new TreeViewItem { Header = "Interfaces" };
                    foreach (var iface in data.Interfaces.OrderBy(i => i.Name))
                    {
                        var it = new TreeViewItem { Header = iface.Name };
                        if (!string.IsNullOrEmpty(iface.IpAddress)) it.Items.Add(new TreeViewItem { Header = $"IP: {iface.IpAddress}" });
                        if (!string.IsNullOrEmpty(iface.Description)) it.Items.Add(new TreeViewItem { Header = $"Description: {iface.Description}" });
                        if (iface.RawLines != null && iface.RawLines.Any())
                        {
                            var raw = new TreeViewItem { Header = "Raw Configuration" };
                            foreach (var line in iface.RawLines.Take(200)) raw.Items.Add(new TreeViewItem { Header = line });
                            it.Items.Add(raw);
                        }
                        ifs.Items.Add(it);
                    }
                    root.Items.Add(ifs);
                }
            }

            treeView.Items.Add(root);
        }

        private void UpdateRawDataTree(string filename, UniversalLogData data)
        {
            if (UnparsedTree == null) return;

            var existing = UnparsedTree.Items.Cast<TreeViewItem>().FirstOrDefault(i => string.Equals(i.Header?.ToString(), Path.GetFileName(filename), StringComparison.OrdinalIgnoreCase));
            if (existing != null) UnparsedTree.Items.Remove(existing);

            var root = new TreeViewItem { Header = $"Raw Data - {Path.GetFileName(filename)}" };

            // Read raw file lines directly
            try
            {
                int lineCount = 0;
                foreach (var line in File.ReadLines(filename))
                {
                    if (lineCount >= 1000) // Limit to 1000 lines to avoid performance issues
                    {
                        root.Items.Add(new TreeViewItem { Header = $"... ({lineCount - 1000} more lines truncated)" });
                        break;
                    }
                    root.Items.Add(new TreeViewItem { Header = line });
                    lineCount++;
                }

                if (lineCount == 0)
                    root.Items.Add(new TreeViewItem { Header = "(Empty file)" });
            }
            catch (Exception ex)
            {
                root.Items.Add(new TreeViewItem { Header = $"Error reading file: {ex.Message}" });
            }

            UnparsedTree.Items.Add(root);
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                await AnalyzeFilesAsync(files);
            }
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; }
        }

        private async Task AnalyzeFilesAsync(IEnumerable<string> files)
        {
            var fileList = files.Where(f => File.Exists(f)).ToList();
            if (!fileList.Any()) { System.Windows.MessageBox.Show("No valid files dropped."); return; }

            var exportExcel = ExcelCheckBox?.IsChecked == true;
            var exportCsv = CsvCheckBox?.IsChecked == true;
            var exportJson = JsonCheckBox?.IsChecked == true;
            if (!exportExcel && !exportCsv && !exportJson)
            {
                System.Windows.MessageBox.Show("Please select at least one export format (Excel, CSV, or JSON).", "Export format", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            if (CancelButton != null) CancelButton.IsEnabled = true;

            var tasks = fileList.Select(async (file, idx) =>
            {
                await _processingSemaphore.WaitAsync();
                try
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) return;
                    UpdateStatus($"Processing {Path.GetFileName(file)}...", (double)idx / fileList.Count * 100);
                    LogToUi($"Starting {Path.GetFileName(file)}\n");

                    var data = await Task.Run(() => ParserFactory.ParseLogFile(file), _cancellationTokenSource.Token);
                    if (data != null && !string.IsNullOrEmpty(data.Device))
                    {
                        // Show detected log type in UI so user can see and override if needed
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (LogTypeComboBox != null)
                                    LogTypeComboBox.SelectedItem = data.LogType;
                            });
                        }
                        catch { }
                        _results[file] = data;
                        UpdateTrees(file, data);
                        UpdateRawDataTree(file, data);
                        LogToUi($"Finished {Path.GetFileName(file)}\n");
                    }
                    else LogToUi($"Warning: {Path.GetFileName(file)} produced no device info\n");
                }
                catch (OperationCanceledException) { LogToUi($"Cancelled {Path.GetFileName(file)}\n"); }
                catch (Exception ex) { LogToUi($"Error {Path.GetFileName(file)}: {ex.Message}\n"); }
                finally { _processingSemaphore.Release(); }
            });

            await Task.WhenAll(tasks);

            try
            {
                if (_results.Any())
                {
                    if (exportExcel)
                    {
                        LogToUi("Saving Excel...\n");
                        var filesOut = await Task.Run(() => ExcelWriter.Save(_results.Values.ToList(), null, _outputFolder));
                        LogToUi($"Excel saved: {string.Join(",", filesOut)}\n");
                    }
                    if (exportCsv)
                    {
                        LogToUi("Saving CSV...\n");
                        var filesOut = await Task.Run(() => CsvWriter.SaveAsCsv(_results.Values.ToList(), null, _outputFolder));
                        LogToUi($"CSV saved: {string.Join(",", filesOut)}\n");
                    }
                    if (exportJson)
                    {
                        LogToUi("Saving JSON...\n");
                        var filesOut = await Task.Run(() => JsonWriter.SaveAsJson(_results.Values.ToList(), null, _outputFolder));
                        LogToUi($"JSON saved: {string.Join(",", filesOut)}\n");
                    }
                }
            }
            finally
            {
                if (CancelButton != null) CancelButton.IsEnabled = false;
                _cancellationTokenSource?.Dispose(); _cancellationTokenSource = null;
            }

            UpdateStatus($"Completed {fileList.Count} files", 100);
            UpdateAnomalyList();
            UpdatePerformanceMetrics();
            RenderTopology();
        }

        private void UpdateAnomalyList()
        {
            Dispatcher.Invoke(() =>
            {
                if (AnomalyList == null) return;
                AnomalyList.Items.Clear();
                foreach (var d in _results.Values)
                    if (d.Anomalies != null)
                        foreach (var a in d.Anomalies) AnomalyList.Items.Add(a);
            });
        }

        private void RenderTopology()
        {
            Dispatcher.Invoke(() =>
            {
                if (TopologyCanvas == null) return;
                var logs = _results.Values.ToList();
                if (!logs.Any()) { TopologyCanvas.Children.Clear(); return; }

                var includeEndpoints = ShowEndpointsCheckBox?.IsChecked == true;
                var includeMacs = ShowMacsCheckBox?.IsChecked == true;
                var collapseHosts = CollapseHostsCheckBox?.IsChecked == true;
                var g = TopologyMapBuilder.BuildGraphFromLogs(logs, includeEndpoints, includeMacs, collapseHosts);
                TopologyCanvas.Children.Clear();

                double canvasW = TopologyCanvas.ActualWidth > 0 ? TopologyCanvas.ActualWidth : 800;
                double canvasH = TopologyCanvas.ActualHeight > 0 ? TopologyCanvas.ActualHeight : 600;
                double refW = 400.0, refH = 300.0;
                double scaleX = canvasW / refW;
                double scaleY = canvasH / refH;

                foreach (var edge in g.Edges)
                {
                    var s = g.Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.SourceId, StringComparison.OrdinalIgnoreCase));
                    var t = g.Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.TargetId, StringComparison.OrdinalIgnoreCase));
                    if (s == null || t == null) continue;
                    var x1 = s.X * scaleX; var y1 = s.Y * scaleY; var x2 = t.X * scaleX; var y2 = t.Y * scaleY;
                    var line = new Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = System.Windows.Media.Brushes.Gray, StrokeThickness = 1.2, Opacity = 0.85 };
                    TopologyCanvas.Children.Add(line);
                }

                foreach (var node in g.Nodes)
                {
                    var cx = node.X * scaleX; var cy = node.Y * scaleY;
                    double w = 120, h = 36;
                    var nodeCanvas = new Canvas { Width = w, Height = h, Tag = node };
                    
                    // Determine color based on node type
                    System.Windows.Media.Brush nodeBrush = node.Type == NodeType.EndpointIp ? System.Windows.Media.Brushes.LightGreen :
                                      node.Type == NodeType.MacAddress ? System.Windows.Media.Brushes.LightCoral :
                                      System.Windows.Media.Brushes.LightSkyBlue;
                    System.Windows.Media.Brush hoverBrush = node.Type == NodeType.EndpointIp ? System.Windows.Media.Brushes.MediumSeaGreen :
                                       node.Type == NodeType.MacAddress ? System.Windows.Media.Brushes.IndianRed :
                                       System.Windows.Media.Brushes.CornflowerBlue;
                    
                    var ellipse = new Shapes.Ellipse { Width = w, Height = h, Fill = nodeBrush, Stroke = System.Windows.Media.Brushes.DimGray, StrokeThickness = 1.2 };
                    nodeCanvas.Children.Add(ellipse);
                    var tb = new TextBlock { Text = node.Label ?? node.Id, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Black, Width = w, TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(tb, 0); Canvas.SetTop(tb, 6); nodeCanvas.Children.Add(tb);
                    
                    // Build tooltip with origin information and OUI for MACs
                    var tooltipContent = node.Label ?? node.Id;
                    if (node.Type == NodeType.MacAddress)
                        tooltipContent += $"\nVendor: {OuiLookup.GetVendor(node.Id)}";
                    if (!string.IsNullOrEmpty(node.Origin))
                        tooltipContent += $"\nOrigin: {node.Origin}";
                    
                    nodeCanvas.ToolTip = new System.Windows.Controls.ToolTip { Content = tooltipContent };
                    nodeCanvas.MouseEnter += (s, ev) => { ellipse.Fill = hoverBrush; ellipse.StrokeThickness = 1.8; };
                    nodeCanvas.MouseLeave += (s, ev) => { ellipse.Fill = nodeBrush; ellipse.StrokeThickness = 1.2; };
                    nodeCanvas.MouseLeftButtonUp += (s, ev) => { UpdateStatus($"Selected: {node.Label ?? node.Id}", 100); };
                    TopologyCanvas.Children.Add(nodeCanvas);
                    Canvas.SetLeft(nodeCanvas, cx - w / 2); Canvas.SetTop(nodeCanvas, cy - h / 2);
                }

                // Ensure transform group is attached
                if (TopologyCanvas.RenderTransform == null) TopologyCanvas.RenderTransform = _transformGroup;

                // Compute bounding box of drawn nodes (in canvas coordinates) and center view
                try
                {
                    var drawnNodes = g.Nodes.Select(n => new { X = n.X * scaleX, Y = n.Y * scaleY }).ToList();
                    if (drawnNodes.Any())
                    {
                        double minX = drawnNodes.Min(x => x.X);
                        double maxX = drawnNodes.Max(x => x.X);
                        double minY = drawnNodes.Min(x => x.Y);
                        double maxY = drawnNodes.Max(x => x.Y);

                        double centerX = (minX + maxX) / 2.0;
                        double centerY = (minY + maxY) / 2.0;

                        // Reset scale to 1 for predictable centering (we use coordinate scaling above)
                        _scaleTransform.ScaleX = 1.0; _scaleTransform.ScaleY = 1.0;

                        // Translate so that graph center aligns with canvas center
                        _translateTransform.X = (canvasW / 2.0) - centerX;
                        _translateTransform.Y = (canvasH / 2.0) - centerY;
                    }
                }
                catch { }
            });
        }

        private void LogToUi(string text) { lock (_logBufferLock) { _logBuffer.Add(text); } }

        private void LogFlushTimer_Tick(object? sender, EventArgs e)
        {
            List<string> msgs;
            lock (_logBufferLock)
            {
                if (_logBuffer.Count == 0) return;
                msgs = new List<string>(_logBuffer); _logBuffer.Clear();
            }
            Dispatcher.Invoke(() => { if (LogBox != null) { foreach (var m in msgs) LogBox.AppendText(m); LogBox.ScrollToEnd(); } });
        }

        private void ExportFormat_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                var excel = ExcelCheckBox?.IsChecked == true;
                var csv = CsvCheckBox?.IsChecked == true;
                var json = JsonCheckBox?.IsChecked == true;
                if (!excel && !csv && !json) if (sender is System.Windows.Controls.CheckBox cb) { cb.IsChecked = true; System.Windows.MessageBox.Show("Please select at least one export format (Excel, CSV, or JSON).", "Export format", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }
            }
            catch { }
        }

        private void ExportFormat_Checked(object sender, RoutedEventArgs e) { }

        private void ShowMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string fileName)
            {
                // noop for now; kept for compatibility with earlier UI
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Title = "Select Huawei log files", Filter = "All files (*.*)|*.*", Multiselect = true };
            if (ofd.ShowDialog() == true) _ = AnalyzeFilesAsync(ofd.FileNames);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

        private void TopologyCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (TopologyCanvas == null) return;
            var pos = e.GetPosition(TopologyCanvas);
            double zoomFactor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
            var newZoom = _zoom * zoomFactor;
            if (newZoom < MinZoom || newZoom > MaxZoom) return;
            _translateTransform.X = (_translateTransform.X - pos.X) * zoomFactor + pos.X;
            _translateTransform.Y = (_translateTransform.Y - pos.Y) * zoomFactor + pos.Y;
            _zoom = newZoom; _scaleTransform.ScaleX = _zoom; _scaleTransform.ScaleY = _zoom; e.Handled = true;
        }

        private void TopologyCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TopologyCanvas == null) return;
            _isPanning = true; _lastPanPoint = e.GetPosition(this); TopologyCanvas.CaptureMouse();
        }

        private void TopologyCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TopologyCanvas == null) return; _isPanning = false; TopologyCanvas.ReleaseMouseCapture();
        }

        private void TopologyCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isPanning || TopologyCanvas == null) return;
            var current = e.GetPosition(this); var delta = current - _lastPanPoint; _lastPanPoint = current; _translateTransform.X += delta.X; _translateTransform.Y += delta.Y;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try loading localized AboutText from resources (will pick culture-specific resource if available)
                var resourceBase = $"{typeof(MainWindow).Assembly.GetName().Name}.Resources";
                var rm = new System.Resources.ResourceManager(resourceBase, typeof(MainWindow).Assembly);
                var about = rm.GetString("AboutText") ?? "Universal Log Analyzer\nA network log analysis tool.";
                System.Windows.MessageBox.Show(about, "About", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Universal Log Analyzer\n{ex.Message}", "About", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void LogTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (LogTypeComboBox == null) return;
                if (LogTypeComboBox.SelectedItem == null) return;
                var selectedType = (LogBuildType)LogTypeComboBox.SelectedItem;

                // If a single result is present, apply override there
                if (_results.Count == 1)
                {
                    var file = _results.Keys.First();
                    _results[file].LogType = selectedType;
                    _results[file].VendorSpecificData["UserSelectedLogType"] = selectedType.ToString();
                    UpdateTrees(file, _results[file]);
                    return;
                }

                // If user has selected an item in the results tree, try to map it back to the file
                if (ResultsTree?.SelectedItem is TreeViewItem selectedItem)
                {
                    var root = selectedItem;
                    while (root.Parent is TreeViewItem parent) root = parent;
                    var header = root.Header?.ToString();
                    if (!string.IsNullOrEmpty(header))
                    {
                        var fileKey = _results.Keys.FirstOrDefault(k => Path.GetFileName(k).Equals(header, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(fileKey))
                        {
                            _results[fileKey].LogType = selectedType;
                            _results[fileKey].VendorSpecificData["UserSelectedLogType"] = selectedType.ToString();
                            UpdateTrees(fileKey, _results[fileKey]);
                        }
                    }
                }
            }
            catch { }
        }

        private void ShowEndpointsCheckBox_Checked(object sender, RoutedEventArgs e) => RenderTopology();
        private void ShowEndpointsCheckBox_Unchecked(object sender, RoutedEventArgs e) => RenderTopology();
        private void ShowMacsCheckBox_Checked(object sender, RoutedEventArgs e) => RenderTopology();
        private void ShowMacsCheckBox_Unchecked(object sender, RoutedEventArgs e) => RenderTopology();
        private void CollapseHostsCheckBox_Checked(object sender, RoutedEventArgs e) => RenderTopology();
        private void CollapseHostsCheckBox_Unchecked(object sender, RoutedEventArgs e) => RenderTopology();

        private void UpdatePerformanceMetrics()
        {
            Dispatcher.Invoke(() =>
            {
                if (PerformanceList == null) return;
                PerformanceList.Items.Clear();
                
                foreach (var log in _results.Values)
                {
                    if (log.Resources != null)
                    {
                        var device = log.Device ?? log.SystemName ?? "Unknown";
                        
                        if (log.Resources.CpuUsage > 0)
                            PerformanceList.Items.Add(new { Device = device, Metric = "CPU Usage", Value = $"{log.Resources.CpuUsage:F2}", Unit = "%" });
                        if (log.Resources.MemoryUsage > 0)
                            PerformanceList.Items.Add(new { Device = device, Metric = "Memory Usage", Value = $"{log.Resources.MemoryUsage:F2}", Unit = "%" });
                        if (log.Resources.DiskUsage > 0)
                            PerformanceList.Items.Add(new { Device = device, Metric = "Disk Usage", Value = $"{log.Resources.DiskUsage:F2}", Unit = "%" });
                        if (!string.IsNullOrEmpty(log.Resources.Temperature))
                            PerformanceList.Items.Add(new { Device = device, Metric = "Temperature", Value = log.Resources.Temperature, Unit = "°C" });
                        if (!string.IsNullOrEmpty(log.Resources.Voltage))
                            PerformanceList.Items.Add(new { Device = device, Metric = "Voltage", Value = log.Resources.Voltage, Unit = "V" });
                        if (log.Resources.Alarms.Any())
                            PerformanceList.Items.Add(new { Device = device, Metric = "Alarms", Value = log.Resources.Alarms.Count.ToString(), Unit = "#" });
                    }
                }
            });
        }
    }
}
