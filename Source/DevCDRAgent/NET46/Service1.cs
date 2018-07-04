using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.ServiceProcess;
using System.Timers;
using System.Xml;

namespace DevCDRAgent
{
    public partial class Service1 : ServiceBase
    {
        private System.Timers.Timer tReCheck = new System.Timers.Timer(61000); //1min
        private System.Timers.Timer tReInit = new System.Timers.Timer(120100); //2min

        private static HubConnection connection;
        private static IHubProxy myHub;
        private static string sScriptResult = "";
        public string Uri { get; set; } = Properties.Settings.Default.Endpoint;

        public string Instance { get; set; } = Properties.Settings.Default.Instance;
        
        public Service1()
        {
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
                            myHub.Invoke<string>("Init", Environment.MachineName).ContinueWith(task1 =>
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
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
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
            catch {}
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

            if(connection != null)
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
                if(obj.NewState == Microsoft.AspNet.SignalR.Client.ConnectionState.Disconnected)
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
                connection.Start().ContinueWith(task => {
                    if (task.IsFaulted)
                    {
                        Console.WriteLine("There was an error opening the connection:{0}", task.Exception.GetBaseException());
                    }
                    else
                    {
                        //Obsolete
                        myHub.On<string>("runPS", (s1) =>
                        {
                            try
                            {
                                //using (PowerShell PowerShellInstance = PowerShell.Create())
                                //{
                                //    PowerShellInstance.AddScript(s1);
                                //    Console.WriteLine(s1);
                                //    PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();
                                //    IAsyncResult result = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);
                                //    foreach (PSObject pres in outputCollection)
                                //    {
                                //        try
                                //        {
                                //            Console.WriteLine(pres.BaseObject.ToString());
                                //        }
                                //        catch(Exception ex)
                                //        {
                                //            Console.WriteLine(ex.Message);
                                //        }
                                //    }
                                //}

                                //Program.MinimizeFootprint();
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        });

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

                        myHub.On<string>("init", (s1) =>
                        {
                            try
                            {
                                myHub.Invoke<string>("Init", Environment.MachineName).ContinueWith(task1 =>
                                {
                                });

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

                        myHub.On<string>("status", (s1) =>
                        {
                            try
                            {
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
                                            JObject jRes = JObject.Parse(sResult);
                                            jRes.Add("ScriptResult", sScriptResult);
                                            jRes.Add("Groups", Properties.Settings.Default.Groups);
                                            sResult = jRes.ToString();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("There was an error: {0}", ex.Message);
                                    }
                                }

                                myHub.Invoke("Status", new object[] { Environment.MachineName, sResult }).ContinueWith(task1 =>
                                {
                                });
                                Program.MinimizeFootprint();
                            }
                            catch { }
                        });

                        myHub.On<string>("version", (s1) =>
                        {
                            try
                            {
                                string sResult = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                                myHub.Invoke<string>("Respond", s1, Environment.MachineName + ":" + sResult).ContinueWith(task1 =>
                                {
                                    if (task1.IsFaulted)
                                    {
                                        Console.WriteLine("There was an error calling send: {0}", task1.Exception.GetBaseException());
                                    }
                                });
                            }
                            catch { }
                        });

                        myHub.On<string>("wol", (s1) =>
                        {
                            try
                            {
                                WOL.WakeUp(s1);
                            }
                            catch { }
                        });

                        myHub.On<string>("setinstance", (s1) =>
                        {
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
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("setendpoint", (s1) =>
                        {
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
                                    }
                                }
                            }
                            catch { }
                        });

                        myHub.On<string>("setgroups", (s1) =>
                        {
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

                        myHub.Invoke<string>("Init", Environment.MachineName).ContinueWith(task1 => {
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
                        });
                    }

                }).Wait();
            }
            catch(Exception ex)
            {
                Console.WriteLine("There was an error: {0}", ex.Message);
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
                tReCheck.Stop();
                tReInit.Stop();

                connection.Stop();
            }
            catch { }
        }

        public void RestartService()
        {
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
    }
}
