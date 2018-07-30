using System;
using System.ServiceProcess;
using System.Configuration.Install;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using DevCDRAgent.Modules;

namespace DevCDRAgent
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(string[] args)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Environment.ExpandEnvironmentVariables("%temp%\\devcdr.log")));
            Trace.AutoFlush = true;
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
                        Console.WriteLine(string.Format("--- Zander Tools: DevCDR Service Version: {0} ---", Assembly.GetEntryAssembly().GetName().Version));
                        Console.WriteLine("Optional ServiceInstaller parameters: --install , --uninstall");
                        if (string.IsNullOrEmpty(parameter))
                            parameter = Environment.MachineName.ToUpper() + ":" + Environment.UserName.ToUpper();
                        Trace.WriteLine("Startup Parameter: " + parameter);

                        Service1 ConsoleApp = new Service1(Environment.ExpandEnvironmentVariables(parameter));
                        ConsoleApp.Start(null);
                        MinimizeFootprint();
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
            GC.Collect(GC.MaxGeneration);
            GC.WaitForPendingFinalizers();
            //SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle,(UIntPtr)0xFFFFFFFF, (UIntPtr)0xFFFFFFFF);
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process,UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);
    }
}
