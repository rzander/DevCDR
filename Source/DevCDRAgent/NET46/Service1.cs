using DevCDRAgent.Modules;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RZUpdate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Net.NetworkInformation;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml;

namespace DevCDRAgent
{
    public partial class Service1 : ServiceBase
    {
        private System.Timers.Timer tReCheck = new System.Timers.Timer(61000); //1min
        private System.Timers.Timer tReInit = new System.Timers.Timer(120100); //2min

        private static string Hostname = Environment.MachineName;
        private static HubConnection connection;
        private static IHubProxy myHub;
        private static string sScriptResult = "";
        public string Uri { get; set; } = Properties.Settings.Default.Endpoint;

        public string Instance { get; set; } = Properties.Settings.Default.Instance;

        public Service1(string Host)
        {
            if (!string.IsNullOrEmpty(Host))
                Hostname = Host;

            InitializeComponent();
        }

        private void TReInit_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                Random rnd = new Random();
                tReInit.Interval = 120100 + rnd.Next(1, 30000); //randomize ReInit intervall

                if (connection != null)
                {
                    if (connection.State == Microsoft.AspNet.SignalR.Client.ConnectionState.Connected)
                    {
                        if (myHub != null)
                        {
                            myHub.Invoke<string>("Init", Hostname).ContinueWith(task1 =>
                            {
                                if (task1.IsFaulted)
                                {
                                    Console.WriteLine("There was an error opening the connection:{0}", task1.Exception.GetBaseException());
                                    OnStart(null);
                                }
                                else
                                {
                                    Program.MinimizeFootprint();
                                }
                            });

                            if (Hostname == Environment.MachineName) //No Inventory or Healthcheck if agent is running as user or with custom Name
                            {
                                if (Properties.Settings.Default.InventoryCheckHours > 0) //Invemtory is enabled
                                {
                                    var tLastCheck = DateTime.Now - Properties.Settings.Default.InventorySuccess;

                                    //Run Inventory every x Hours
                                    if (tLastCheck.TotalHours >= Properties.Settings.Default.InventoryCheckHours)
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + " starting Inventory...");
                                        Trace.Flush();
                                        System.Threading.Thread.Sleep(1000);

                                        myHub.Invoke<string>("Inventory", Hostname).ContinueWith(task1 =>
                                        {
                                            if (task1.IsFaulted)
                                            {
                                                Console.WriteLine("There was an error opening the connection:{0}", task1.Exception.GetBaseException());
                                                OnStart(null);
                                            }
                                            else
                                            {
                                                Properties.Settings.Default.InventorySuccess = DateTime.Now;
                                                Properties.Settings.Default.Save();
                                            }
                                        });
                                    }
                                }

                                if (Properties.Settings.Default.HealtchCheckHours > 0) //Healthcheck is enabled
                                {
                                    var tLastCheck = DateTime.Now - Properties.Settings.Default.HealthCheckSuccess;

                                    //Run HealthChekc every x Hours
                                    if (tLastCheck.TotalHours >= Properties.Settings.Default.HealtchCheckHours)
                                    {
                                        Trace.WriteLine(DateTime.Now.ToString() + " starting HealthCheck...");
                                        Trace.Flush();
                                        System.Threading.Thread.Sleep(5000);

                                        myHub.Invoke<string>("HealthCheck", Hostname).ContinueWith(task1 =>
                                        {
                                            if (task1.IsFaulted)
                                            {
                                                Console.WriteLine("There was an error opening the connection:{0}", task1.Exception.GetBaseException());
                                                OnStart(null);
                                            }
                                            else
                                            {
                                                Properties.Settings.Default.HealthCheckSuccess = DateTime.Now;
                                                Properties.Settings.Default.Save();
                                            }
                                        });

                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Trace.Write(DateTime.Now.ToString() + " ERROR ReInit: " + ex.Message);
                OnStart(null);
            }
        }

        private void TReCheck_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (connection.State != Microsoft.AspNet.SignalR.Client.ConnectionState.Connected)
                {
                    OnStart(null);
                }
            }
            catch { }
        }

        protected override void OnStart(string[] args)
        {
            sScriptResult = DateTime.Now.ToString();
            tReCheck.Elapsed -= TReCheck_Elapsed;
            tReCheck.Elapsed += TReCheck_Elapsed;
            tReCheck.Enabled = true;
            tReCheck.AutoReset = true;

            tReInit.Elapsed -= TReInit_Elapsed;
            tReInit.Elapsed += TReInit_Elapsed;
            tReInit.Enabled = true;
            tReInit.AutoReset = true;

            if (connection != null)
            {
                try
                {
                    connection.Stop();
                }
                catch { }
            }
            connection = new HubConnection(Uri);
            myHub = connection.CreateHubProxy(Instance);
            connection.StateChanged -= Connection_StateChanged;
            connection.StateChanged += Connection_StateChanged;
            Connect();
        }

        private void Connection_StateChanged(StateChange obj)
        {
            if (serviceController1.Status != ServiceControllerStatus.StopPending)
            {
                Console.WriteLine("State: " + obj.NewState.ToString());
                if (obj.NewState == Microsoft.AspNet.SignalR.Client.ConnectionState.Disconnected)
                {
                }

                if (obj.NewState == Microsoft.AspNet.SignalR.Client.ConnectionState.Reconnecting)
                {
                }

                if (obj.NewState == Microsoft.AspNet.SignalR.Client.ConnectionState.Connected)
                {
                }

                if (obj.NewState == Microsoft.AspNet.SignalR.Client.ConnectionState.Connecting)
                {
                }
            }
        }

        private void Connect()
        {
            try
            {
                connection.Stop();
                connection.Start().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        Console.WriteLine("There was an error opening the connection:{0}", task.Exception.GetBaseException());
                    }
                    else
                    {
                        //Obsolete
                        myHub.On<string, string>("getPS", (s1, s2) =>
                        {
                            //using (PowerShell PowerShellInstance = PowerShell.Create())
                            //{
                            //    try
                            //    {
                            //        PowerShellInstance.AddScript(s1);
                            //        var PSResult = PowerShellInstance.Invoke();
                            //        if (PSResult.Count() > 0)
                            //        {
                            //            string sResult = PSResult.Last().BaseObject.ToString();
                            //            if (sResult != sScriptResult) //obsolete from 1.0.07 -> returnPS
                            //            {
                            //                sScriptResult = sResult;
                            //                Random rnd = new Random();
                            //                tReInit.Interval = rnd.Next(1000, 10000); //wait max 10s to ReInit
                            //            }

                            //            myHub.Invoke<string>("Respond", s2, Environment.MachineName + ":" + sResult).ContinueWith(task1 =>
                            //            {
                            //                if (task1.IsFaulted)
                            //                {
                            //                    Console.WriteLine("There was an error calling send: {0}", task1.Exception.GetBaseException());
                            //                }
                            //            });
                            //        }
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //        Console.WriteLine("There was an error: {0}", ex.Message);
                            //    }
                            //}

                            //Program.MinimizeFootprint();
                        });

                        myHub.On<string, string>("returnPS", (s1, s2) =>
                        {
                            TimeSpan timeout = new TimeSpan(0, 5, 0); //default timeout = 5min
                            DateTime dStart = DateTime.Now;
                            TimeSpan dDuration = DateTime.Now - dStart;

                            using (PowerShell PowerShellInstance = PowerShell.Create())
                            {
                                Trace.WriteLine(DateTime.Now.ToString() + "\t run PS... " + s1);
                                try
                                {
                                    PowerShellInstance.AddScript(s1);
                                    PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();

                                    outputCollection.DataAdding += ConsoleOutput;
                                    PowerShellInstance.Streams.Error.DataAdding += ConsoleError;

                                    IAsyncResult async = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                                    while (async.IsCompleted == false || dDuration > timeout)
                                    {
                                        Thread.Sleep(200);
                                        dDuration = DateTime.Now - dStart;
                                        if (tReInit.Interval > 5000)
                                            tReInit.Interval = 5000;
                                    }

                                    //var PSResult = PowerShellInstance.Invoke();
                                    //if (PSResult.Count() > 0)
                                    //{
                                    //    string sResult = PSResult.Last().BaseObject.ToString();
                                    //    if (sResult != sScriptResult)
                                    //    {
                                    //        sScriptResult = sResult;
                                    //        Trace.WriteLine(" done. Result: " + sResult);
                                    //        Random rnd = new Random();
                                    //        tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                                    //    }
                                    //}
                                    //else
                                    //{
                                    //    Trace.WriteLine(" done. no result.");
                                    //}
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("There was an error: {0}", ex.Message);
                                }
                            }

                            Program.MinimizeFootprint();
                        });

                        //New 0.9.0.6
                        myHub.On<string, string>("returnPSAsync", (s1, s2) =>
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t run PS async... " + s1);
                            var tSWScan = Task.Run(() =>
                            {
                                using (PowerShell PowerShellInstance = PowerShell.Create())
                                {
                                    try
                                    {
                                        PowerShellInstance.AddScript(s1);
                                        var PSResult = PowerShellInstance.Invoke();
                                        if (PSResult.Count() > 0)
                                        {
                                            string sResult = PSResult.Last().BaseObject.ToString();
                                            if (sResult != sScriptResult)
                                            {
                                                sScriptResult = sResult;
                                                Random rnd = new Random();
                                                tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max Xs to ReInit
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("There was an error: {0}", ex.Message);
                                    }
                                }

                                Program.MinimizeFootprint();
                            });
                        });

                        myHub.On<string>("init", (s1) =>
                        {
                            try
                            {
                                Trace.Write(DateTime.Now.ToString() + "\t Agent init... ");
                                myHub.Invoke<string>("Init", Hostname).ContinueWith(task1 =>
                                {
                                });
                                Trace.WriteLine(" done.");
                            }
                            catch { }
                            try
                            {
                                foreach (string sGroup in Properties.Settings.Default.Groups.Split(';'))
                                {
                                    myHub.Invoke<string>("JoinGroup", sGroup).ContinueWith(task1 =>
                                    {
                                    });
                                }
                                Program.MinimizeFootprint();
                            }
                            catch { }
                        });

                        myHub.On<string>("reinit", (s1) =>
                        {
                            try
                            {
                                Properties.Settings.Default.InventorySuccess = new DateTime();
                                Properties.Settings.Default.HealthCheckSuccess = new DateTime();
                                Properties.Settings.Default.Save();

                                Random rnd = new Random();
                                tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                            catch { }
                        });

                        myHub.On<string>("status", (s1) =>
                        {
                            try
                            {
                                Trace.Write(DateTime.Now.ToString() + "\t send status...");
                                string sResult = "{}";
                                using (PowerShell PowerShellInstance = PowerShell.Create())
                                {
                                    try
                                    {
                                        PowerShellInstance.AddScript(Properties.Settings.Default.PSStatus);
                                        var PSResult = PowerShellInstance.Invoke();
                                        if (PSResult.Count() > 0)
                                        {
                                            sResult = PSResult.Last().BaseObject.ToString();
                                            sResult = sResult.Replace(Environment.MachineName, Hostname);
                                            JObject jRes = JObject.Parse(sResult);
                                            jRes.Add("ScriptResult", sScriptResult);
                                            jRes.Add("Groups", Properties.Settings.Default.Groups);
                                            sResult = jRes.ToString();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(" There was an error: {0}", ex.Message);
                                    }
                                }

                                myHub.Invoke("Status", new object[] { Hostname, sResult }).ContinueWith(task1 =>
                                {
                                });
                                Trace.WriteLine(" done.");
                                Program.MinimizeFootprint();
                            }
                            catch (Exception ex)
                            {
                                Trace.Write(DateTime.Now.ToString() + " ERROR: " + ex.Message);
                            }
                        });

                        myHub.On<string>("version", (s1) =>
                        {
                            try
                            {
                                Trace.Write(DateTime.Now.ToString() + "\t Get Version... ");
                                //Get File-Version
                                sScriptResult = (FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)).FileVersion.ToString();
                                Trace.WriteLine(sScriptResult);

                                Random rnd = new Random();
                                tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                            catch (Exception ex)
                            {
                                Trace.Write(DateTime.Now.ToString() + " ERROR: " + ex.Message);
                            }
                        });

                        myHub.On<string>("wol", (s1) =>
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(s1))
                                {
                                    foreach (string sMAC in s1.Split(';'))
                                    {
                                        try
                                        {
                                            WOL.WakeUp(sMAC); //Send Broadcast

                                            //Send to local Gateway
                                            foreach (NetworkInterface f in NetworkInterface.GetAllNetworkInterfaces())
                                                if (f.OperationalStatus == OperationalStatus.Up)
                                                    foreach (GatewayIPAddressInformation d in f.GetIPProperties().GatewayAddresses)
                                                    {
                                                        //Only use IPv4
                                                        if (d.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                                        {
                                                            WOL.WakeUp(d.Address, 9, sMAC);
                                                        }
                                                    }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("setinstance", (s1) =>
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t Set instance: " + s1);
                            try
                            {
                                if (!string.IsNullOrEmpty(s1))
                                {
                                    string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                    XmlDocument doc = new XmlDocument();
                                    doc.Load(sConfig);
                                    doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Instance']/value").InnerText = s1;
                                    doc.Save(sConfig);
                                    RestartService();

                                    //Update Advanced Installer Persistent Properties
                                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{0AC43E24-4308-4BE7-A369-D50DB4056B32}", true);
                                    if (myKey != null)
                                    {
                                        myKey.SetValue("INSTANCE", s1.Trim(), RegistryValueKind.String);
                                        myKey.Close();
                                    }
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("setendpoint", (s1) =>
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t Set Endpoint: " + s1);
                            try
                            {
                                if (!string.IsNullOrEmpty(s1))
                                {
                                    if (s1.StartsWith("https://"))
                                    {
                                        string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                        XmlDocument doc = new XmlDocument();
                                        doc.Load(sConfig);
                                        doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Endpoint']/value").InnerText = s1;
                                        doc.Save(sConfig);
                                        RestartService();

                                        //Update Advanced Installer Persistent Properties
                                        RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{0AC43E24-4308-4BE7-A369-D50DB4056B32}", true);
                                        if (myKey != null)
                                        {
                                            myKey.SetValue("ENDPOINT", s1.Trim(), RegistryValueKind.String);
                                            myKey.Close();
                                        }
                                    }
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("setgroups", (s1) =>
                        {
                            Trace.WriteLine(DateTime.Now.ToString() + "\t Set Groups: " + s1);
                            try
                            {
                                if (!string.IsNullOrEmpty(s1))
                                {
                                    string sConfig = Assembly.GetExecutingAssembly().Location + ".config";
                                    XmlDocument doc = new XmlDocument();
                                    doc.Load(sConfig);
                                    doc.SelectSingleNode("/configuration/applicationSettings/DevCDRAgent.Properties.Settings/setting[@name='Groups']/value").InnerText = s1;
                                    doc.Save(sConfig);

                                    RestartService();

                                    //Update Advanced Installer Persistent Properties
                                    RegistryKey myKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Zander Tools\\{0AC43E24-4308-4BE7-A369-D50DB4056B32}", true);
                                    if (myKey != null)
                                    {
                                        myKey.SetValue("GROUPS", s1.Trim(), RegistryValueKind.String);
                                        myKey.Close();
                                    }
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("getgroups", (s1) =>
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(s1))
                                {
                                    sScriptResult = Properties.Settings.Default.Groups;

                                    Random rnd = new Random();
                                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("restartservice", (s1) =>
                        {
                            try
                            {
                                RestartService();
                                sScriptResult = "restart Agent...";
                            }
                            catch { }
                        });

                        myHub.On<string>("rzinstall", (s1) =>
                        {
                            RZInst(s1);
                        });

                        myHub.On<string>("rzupdate", (s1) =>
                        {
                            var tSWScan = Task.Run(() =>
                            {
                                try
                                {
                                    sScriptResult = "Detecting RZ updates...";
                                    Random rnd = new Random();
                                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                                    RZUpdater oUpdate = new RZUpdater();
                                    RZScan oScan = new RZScan(false, false);

                                    oScan.GetSWRepository().Wait(30000);
                                    oScan.SWScan().Wait(30000);
                                    oScan.CheckUpdates(null).Wait(30000);

                                    if (string.IsNullOrEmpty(s1))
                                    {
                                        sScriptResult = oScan.NewSoftwareVersions.Count.ToString() + " RZ updates found";
                                        rnd = new Random();
                                        tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                                    }

                                    List<string> lSW = new List<string>();
                                    foreach (var oSW in oScan.NewSoftwareVersions)
                                    {
                                        if (string.IsNullOrEmpty(s1) || s1 == "HUB")
                                        {
                                            RZInst(oSW.Shortname);
                                        }
                                        else
                                        {
                                            var SWList = s1.Split(';');
                                            if (SWList.Contains(oSW.Shortname))
                                                RZInst(oSW.Shortname);
                                        }
                                    }
                                }
                                catch { }
                            });
                        });

                        myHub.On<string>("rzscan", (s1) =>
                        {
                            var tSWScan = Task.Run(() =>
                            {
                                try
                                {
                                    sScriptResult = "Detecting updates...";
                                    Random rnd = new Random();
                                    tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                                    RZUpdater oUpdate = new RZUpdater();
                                    RZScan oScan = new RZScan(false, false);

                                    oScan.GetSWRepository().Wait(30000);
                                    oScan.SWScan().Wait(30000);
                                    oScan.CheckUpdates(null).Wait(30000);

                                    List<string> lSW = new List<string>();
                                    foreach (var SW in oScan.NewSoftwareVersions)
                                    {
                                        lSW.Add(SW.Shortname + " " + SW.ProductVersion + " (old:" + SW.MSIProductID + ")");
                                    }

                                    sScriptResult = JsonConvert.SerializeObject(lSW);
                                    rnd = new Random();
                                    tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                                }
                                catch { }
                            });
                        });

                        myHub.On<string>("inject", (s1) =>
                        {
                            var tSWScan = Task.Run(() =>
                            {
                                try
                                {
                                    sScriptResult = "Inject external code...";
                                    try
                                    {
                                        ManagedInjection.Inject(s1);
                                        sScriptResult = "External code executed.";
                                    }
                                    catch (Exception ex)
                                    {
                                        sScriptResult = "Injection error:" + ex.Message;
                                    }
                                }
                                catch { }
                            });
                        });

                        myHub.On<string, string>("userprocess", (cmd, arg) =>
                        {
                            var tSWScan = Task.Run(() =>
                            {
                                if (string.IsNullOrEmpty(cmd))
                                {
                                    cmd = Assembly.GetExecutingAssembly().Location;
                                    arg = Environment.MachineName + ":" + "%USERNAME%" + " --hidden";
                                }

                                try
                                {
                                    if (string.IsNullOrEmpty(arg))
                                    {
                                        ProcessExtensions.StartProcessAsCurrentUser(cmd);
                                    }
                                    else
                                    {
                                        ProcessExtensions.StartProcessAsCurrentUser(null, cmd + " " + arg, "", false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            });

                        });

                        myHub.Invoke<string>("Init", Hostname).ContinueWith(task1 =>
                        {
                            try
                            {
                                if (task1.IsFaulted)
                                {
                                    Console.WriteLine("There was an error calling send: {0}", task1.Exception.GetBaseException());
                                }
                                else
                                {
                                    try
                                    {
                                        foreach (string sGroup in Properties.Settings.Default.Groups.Split(';'))
                                        {
                                            myHub.Invoke<string>("JoinGroup", sGroup).ContinueWith(task2 =>
                                            {
                                            });
                                        }
                                        Program.MinimizeFootprint();
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        });


                    }

                }).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was an error: {0}", ex.Message);
            }
        }

        public void RZInst(string s1)
        {
            try
            {
                Random rnd = new Random();
                RZUpdater oRZSW = new RZUpdater();
                oRZSW.SoftwareUpdate = new SWUpdate(s1);
                if (string.IsNullOrEmpty(oRZSW.SoftwareUpdate.SW.ProductName))
                {
                    sScriptResult = "'" + s1 + "' is NOT available in RuckZuck...!";
                    rnd = new Random();
                    tReInit.Interval = rnd.Next(200, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                }

                foreach (string sPreReq in oRZSW.SoftwareUpdate.SW.PreRequisites)
                {
                    if (!string.IsNullOrEmpty(sPreReq))
                    {
                        RZUpdater oRZSWPreReq = new RZUpdater();
                        oRZSWPreReq.SoftwareUpdate = new SWUpdate(sPreReq);

                        sScriptResult = "..downloading dependencies (" + oRZSWPreReq.SoftwareUpdate.SW.Shortname + ")";
                        rnd = new Random();
                        tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                        if (oRZSWPreReq.SoftwareUpdate.Download().Result)
                        {
                            sScriptResult = "..installing dependencies (" + oRZSWPreReq.SoftwareUpdate.SW.Shortname + ")";
                            rnd = new Random();
                            tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                            if (oRZSWPreReq.SoftwareUpdate.Install(false, true).Result)
                            {

                            }
                            else
                            {
                                sScriptResult = oRZSWPreReq.SoftwareUpdate.SW.Shortname + " failed.";
                                rnd = new Random();
                                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                            }
                        }
                    }

                }

                sScriptResult = "..downloading " + oRZSW.SoftwareUpdate.SW.Shortname;
                rnd = new Random();
                tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                if (oRZSW.SoftwareUpdate.Download().Result)
                {
                    sScriptResult = "..installing " + oRZSW.SoftwareUpdate.SW.Shortname;
                    rnd = new Random();
                    tReInit.Interval = rnd.Next(2000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit

                    if (oRZSW.SoftwareUpdate.Install(false, true).Result)
                    {
                        sScriptResult = "Installed: " + oRZSW.SoftwareUpdate.SW.Shortname;
                        rnd = new Random();
                        tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                    }
                    else
                    {
                        sScriptResult = "Failed: " + oRZSW.SoftwareUpdate.SW.Shortname;
                        rnd = new Random();
                        tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
                    }
                }
            }
            catch (Exception ex)
            {
                sScriptResult = s1 + " : " + ex.Message;
                Random rnd = new Random();
                tReInit.Interval = rnd.Next(3000, Properties.Settings.Default.StatusDelay); //wait max 5s to ReInit
            }
        }

        public void Start(string[] args)
        {
            OnStart(args);
        }

        protected override void OnStop()
        {
            try
            {
                Trace.WriteLine(DateTime.Now.ToString() + "\t stopping DevCDRAgent...");
                Trace.Flush();
                Trace.Listeners.Clear();

                tReCheck.Enabled = false;
                tReInit.Enabled = false;
                tReCheck.Stop();
                tReInit.Stop();

                connection.Stop(new TimeSpan(0, 0, 30));
                System.Threading.Thread.Sleep(500);
                connection.Dispose();
            }
            catch { }
        }

        public void RestartService()
        {
            Trace.WriteLine(DateTime.Now.ToString() + "\t restarting Service..");
            try
            {
                using (PowerShell PowerShellInstance = PowerShell.Create())
                {
                    try
                    {
                        PowerShellInstance.AddScript("powershell.exe -command stop-service DevCDRAgent -Force;sleep 5;start-service DevCDRAgent");
                        var PSResult = PowerShellInstance.Invoke();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void ConsoleError(object sender, DataAddingEventArgs e)
        {
            if (e.ItemAdded != null)
            {
                sScriptResult = "ERROR: " + e.ItemAdded.ToString();
                Trace.WriteLine("ERROR: " + e.ItemAdded.ToString());
            }
        }

        private void ConsoleOutput(object sender, DataAddingEventArgs e)
        {
            if (e.ItemAdded != null)
            {
                sScriptResult = e.ItemAdded.ToString();
                Trace.WriteLine(e.ItemAdded.ToString());
            }
        }
    }
}
