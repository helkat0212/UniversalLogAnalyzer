using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace UniversalLogAnalyzer
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            // Ensure GUI startup
            this.Startup += App_Startup;
        }

        public void LoadMaterialDesignResources()
        {
            // No longer using MaterialDesignThemes
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Resources should already be loaded in Program.cs before Run()
            // Create and show the main window
            try
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
                mainWindow.Activate();
            }
            catch (Exception ex)
            {
                // Log detailed error
                try
                {
                    var errorDetails = $"Startup error: {ex.Message}\n\n" +
                                      $"Type: {ex.GetType().FullName}\n" +
                                      $"Stack trace:\n{ex.StackTrace}\n\n" +
                                      $"Inner exception: {ex.InnerException?.ToString() ?? "None"}\n\n";
                    
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "UniversalLogAnalyzer_startup.log"),
                        DateTime.Now + "\n" + errorDetails);
                    
                    System.Windows.MessageBox.Show(
                        $"Failed to start application:\n\n{ex.Message}\n\n" +
                        $"Error type: {ex.GetType().Name}\n\n" +
                        $"Check log file for details:\n%TEMP%\\UniversalLogAnalyzer_startup.log",
                        "Startup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                throw; // Re-throw to prevent app from running with no window
            }
        }

        private void App_DispatcherUnhandledException(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "UniversalLogAnalyzer_unhandled.log"),
                    DateTime.Now + "\n" + e.Exception.ToString() + "\n\n");
            }
            catch { }
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "UniversalLogAnalyzer_unhandled.log"),
                    DateTime.Now + "\n" + (ex?.ToString() ?? e.ExceptionObject.ToString()) + "\n\n");
            }
            catch { }
        }
    }
}
