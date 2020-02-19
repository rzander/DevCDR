using System;
using System.ServiceProcess;
using System.Configuration.Install;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace DevCDRAgent
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            //Log File cleanup
            try
            {
                if (System.IO.File.Exists(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore.log")))
                {
                    var log = new System.IO.FileInfo(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore.log"));
                    if (log.Length > 5242880) //File is more than 5MB
                    {
                        if (System.IO.File.Exists(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore_.log")))
                        {
                            System.IO.File.Delete(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore_.log"));
                        }

                        System.IO.File.Move(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore.log"), Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore_.log"));
                    }
                }
            }
            catch { }

            Trace.Listeners.Add(new TextWriterTraceListener(Environment.ExpandEnvironmentVariables(Environment.ExpandEnvironmentVariables("%temp%\\devcdrcore.log"))));
            Trace.AutoFlush = true;

            //Add SigningCert to TrustedPublishers -> to allow PowerShell script signed by this certificate.
            try
            {
                X509Certificate executingCert = X509Certificate2.CreateFromSignedFile(Assembly.GetExecutingAssembly().Location);

                X509Store store = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                store.Add(new X509Certificate2(executingCert));

            }
            catch { }

            Trace.WriteLine("Starting DevCDRAgent... " + DateTime.Now.ToString());
            Trace.Indent();
            Trace.Flush();

            if (System.Environment.UserInteractive)
            {
                string parameter = string.Concat(args);
                switch (parameter)
                {
                    case "--install":
                        ManagedInstallerClass.InstallHelper(new string[] { Assembly.GetExecutingAssembly().Location });
                        break;
                    case "--uninstall":
                        ManagedInstallerClass.InstallHelper(new string[] { "/u", Assembly.GetExecutingAssembly().Location });
                        break;
                    default:
                        if(args.ToList().Contains("--hidden"))
                        {
                            var handle = GetConsoleWindow();
                            // Hide
                            ShowWindow(handle, 0);
                            parameter = "";
                        }
                        Console.WriteLine(string.Format("--- Zander Tools: DevCDR Service Version: {0} ---", Assembly.GetEntryAssembly().GetName().Version));
                        Console.WriteLine("Optional ServiceInstaller parameters: --install , --uninstall");
                        if (string.IsNullOrEmpty(parameter))
                            parameter = Environment.MachineName.ToUpper() + ":" + Environment.UserName.ToUpper();
                        Trace.WriteLine("Startup Parameter: " + parameter);

                        Service1 ConsoleApp = new Service1(Environment.ExpandEnvironmentVariables(parameter));
                        ConsoleApp.Start(null);
                        MinimizeFootprint();
                        Trace.WriteLine("Started... " + DateTime.Now.ToString());
                        Console.WriteLine("Press ENTER to terminate...");
                        Console.ReadLine();
                        ConsoleApp.Stop();
                        break;
                }

                return 0;
            }
            else
            {
                var sService = new Service1(Environment.MachineName);
                ServiceBase.Run(sService);
                return sService.ExitCode;
            }
        }

        static public void MinimizeFootprint()
        {
            try
            {
                GC.Collect(GC.MaxGeneration);
                GC.WaitForPendingFinalizers();
                //SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle,(UIntPtr)0xFFFFFFFF, (UIntPtr)0xFFFFFFFF);
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process,UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
