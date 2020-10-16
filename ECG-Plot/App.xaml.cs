using System;
using System.Windows;
using System.Windows.Threading;

namespace ECG_Plot
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            Dicom.Log.LogManager.SetImplementation(Dicom.Log.NLogManager.Instance);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ExecuteOnUnhandledException(e.ExceptionObject as Exception);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ExecuteOnUnhandledException(e.Exception);
        }

        private void ExecuteOnUnhandledException(Exception ex)
        {
            Logging.LoggerService.Instance.Error(ex);
            MessageBox.Show(ex.Message, "Oops! An error occured!");
        }
    }
}
