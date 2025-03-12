using System;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace CADTR
{
    public partial class App : Application
    {
        private const string APP_NAME = "CADTR";
        private const string ERROR_LOG_FILE = "error.log";
        private string appDataPath;
        private string logFilePath;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            try
            {
                SetupApplicationPaths();
                SetupExceptionHandling();
                SetupLogging();
                
                Debug.WriteLine($"✅ Application initialized successfully");
            }
            catch (Exception ex)
            {
                HandleStartupError(ex);
            }
        }

        private void SetupApplicationPaths()
        {
            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                APP_NAME
            );
            logFilePath = Path.Combine(appDataPath, ERROR_LOG_FILE);

            Directory.CreateDirectory(appDataPath);
            Debug.WriteLine($"✅ Application directory created/verified: {appDataPath}");
        }

        private void SetupExceptionHandling()
        {
            // Handle exceptions in the main UI thread
            DispatcherUnhandledException += (s, e) =>
            {
                LogUnhandledException(e.Exception, "UI Thread Exception");
                e.Handled = true;
            };

            // Handle exceptions in background threads
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogUnhandledException(
                    e.ExceptionObject as Exception ?? 
                    new Exception("Unknown error occurred"), 
                    "Background Thread Exception"
                );
            };

            // Handle exceptions in task threads
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogUnhandledException(e.Exception, "Task Thread Exception");
                e.SetObserved();
            };
        }

        private void SetupLogging()
        {
            // Write a startup entry to the log
            try
            {
                File.AppendAllText(logFilePath, 
                    $"\n[{DateTime.Now}] Application started\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to initialize logging: {ex.Message}");
            }
        }

        private void LogUnhandledException(Exception ex, string source)
        {
            try
            {
                string message = FormatExceptionMessage(ex, source);
                Debug.WriteLine($"❌ {message}");

                File.AppendAllText(logFilePath, message);

                ShowErrorDialog(ex, logFilePath);
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"❌ Failed to log error: {logEx.Message}");
                ShowCriticalErrorDialog();
            }
        }

        private string FormatExceptionMessage(Exception ex, string source)
        {
            return $"\n[{DateTime.Now}] {source}\n" +
                   $"Message: {ex.Message}\n" +
                   $"Stack Trace:\n{ex.StackTrace}\n" +
                   $"Source: {ex.Source}\n" +
                   new string('-', 80) + "\n";
        }

        private void ShowErrorDialog(Exception ex, string logPath)
        {
            string message = $"An unexpected error occurred:\n\n{ex.Message}\n\n" +
                           $"The error has been logged to:\n{logPath}";

            Dispatcher.Invoke(() =>
                MessageBox.Show(
                    message,
                    "Application Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                )
            );
        }

        private void ShowCriticalErrorDialog()
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(
                    "A critical error occurred and could not be logged.\n" +
                    "The application will now close.",
                    "Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                )
            );
            
            Environment.Exit(1);
        }

        private void HandleStartupError(Exception ex)
        {
            Debug.WriteLine($"❌ Startup error: {ex.Message}");
            MessageBox.Show(
                $"Failed to initialize application:\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(1);
        }
    }
}
