using System;
using System.Windows;

namespace UniversalLogAnalyzer
{
    /// <summary>
    /// Entry point for the WPF application
    /// </summary>
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Set STA thread mode for WPF
                System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA);
                
                var app = new App();
                
                // Initialize component to load XAML resources FIRST
                // This must happen before any resource access
                app.InitializeComponent(); // Load App.xaml resources (auto-generated)
                
                // Load MaterialDesign resources BEFORE running app
                // This must happen before MainWindow tries to use MaterialDesign resources
                app.LoadMaterialDesignResources();
                
                // Run the application
                app.Run();
            }
            catch (Exception ex)
            {
                // Fallback error handling if WPF fails to initialize
                System.Windows.MessageBox.Show(
                    $"Application failed to start:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
    }
}

