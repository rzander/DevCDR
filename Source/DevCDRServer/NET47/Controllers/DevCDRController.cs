using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;

namespace DevCDRServer.Controllers
{
    [System.Web.Mvc.Authorize]

    public class DevCDRController : Controller
    {
        [AllowAnonymous]
        public ActionResult Demo()
        {
            ViewBag.Title = "Demo (read-only)";
            ViewBag.Instance = "xLab";
            ViewBag.Route = "/Chat";
            return View();
        }

        [AllowAnonymous]
        public ActionResult Dashboard()
        {
            ViewBag.Title = "Dashboard";
            ViewBag.Instance = "";
            ViewBag.Route = "/Chat";
            var cCache = new Cache();
            var itotalDeviceCount = (int)(cCache.Get("totalDeviceCount") ?? -1);
            if (itotalDeviceCount == -1)
            {
                itotalDeviceCount = new JainDBController().totalDeviceCount(HttpContext.Server.MapPath("~/App_Data/JainDB/_Chain"));
            }

            int iZander = ClientCount("Zander");
            int ixLab = ClientCount("xLab");
            int iTest = ClientCount("Test");
            int iDefault = ClientCount("Default");
            int iOnlineCount = iZander + ixLab + iTest + iDefault;
            int iOfflineCount = itotalDeviceCount - iOnlineCount;

            if (iOfflineCount < 0)
                iOfflineCount = 0;

            if (iOnlineCount > itotalDeviceCount)
                itotalDeviceCount = iOnlineCount;

            ViewBag.TotalDeviceCount = itotalDeviceCount;
            ViewBag.OnlineDeviceCount = iOnlineCount;
            ViewBag.OfflineDeviceCount = iOfflineCount;
            ViewBag.TotalZander = iZander;
            ViewBag.TotalxLab = ixLab;
            ViewBag.TotalTest = iTest;
            ViewBag.TotalDefault = iDefault;
            return View();
        }

        [AllowAnonymous]
        public ActionResult Test()
        {
            ViewBag.Title = "Test Environment";
            ViewBag.Instance = "Test";
            ViewBag.Route = "/Chat";

            return View();
        }

        [System.Web.Mvc.Authorize]
        public ActionResult Default()
        {
            ViewBag.Title = "Default Environment";
            ViewBag.Instance = "Default";
            ViewBag.Route = "/Chat";
            return View();
        }

        //[AllowAnonymous]
        [System.Web.Mvc.Authorize]
        public ActionResult XLab()
        {
            if (User.IsInRole("administrator") || User.IsInRole("readonly") || User.Identity.Name.EndsWith("@itnetx.ch"))
            {
                ViewBag.Title = "itnetX - Lab";
                ViewBag.Instance = "xLab";
                ViewBag.Route = "/Chat";
                return View();
            }
            else
                return Redirect("DevCDR/Default");
        }

        //[AllowAnonymous]
        [System.Web.Mvc.Authorize]
        public ActionResult Zander()
        {
            if (User.IsInRole("administrator"))
            {
                ViewBag.Title = "Zander Devices";
                ViewBag.Instance = "Zander";
                ViewBag.Route = "/Chat";
                return View();
            }
            else
                return Redirect("DevCDR/Dashboard");
        }

        [AllowAnonymous]
        public ActionResult About()
        {
            ViewBag.Message = "Device Commander details...";

            return View();
        }

        [AllowAnonymous]
        public ActionResult Contact()
        {
            ViewBag.Message = "Device Commander Contact....";

            return View();
        }

        [AllowAnonymous]
        public ActionResult GetData(string Instance)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;
            }
            catch { }

            JObject oObj = new JObject
            {
                { "data", jData }
            };

            return new ContentResult
            {
                Content = oObj.ToString(Newtonsoft.Json.Formatting.None),
                ContentType = "application/json",
                ContentEncoding = Encoding.UTF8
            };
        }

        public int ClientCount(string Instance)
        {
            int iCount = 0;
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("ClientCount", BindingFlags.Public | BindingFlags.Static);

                var oCount = ((PropertyInfo)memberInfos[0]).GetValue(new int());

                if (oCount == null)
                    iCount = 0;
                else
                    iCount = (int)oCount;
            }
            catch { }

            return iCount;
        }

        [AllowAnonymous]
        public ActionResult Groups(string Instance)
        {
            List<string> lGroups = new List<string>();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("lGroups", BindingFlags.Public | BindingFlags.Static);

                lGroups = ((FieldInfo)memberInfos[0]).GetValue(new List<string>()) as List<string>;
            }
            catch { }

            lGroups.Remove("web");
            lGroups.Remove("Devices");

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lGroups, Formatting.None),
                ContentType = "application/json",
                ContentEncoding = Encoding.UTF8
            };
        }

        [AllowAnonymous]
        public ActionResult GetRZCatalog(string Instance)
        {
            List<string> lRZCat = new List<string>();
            try
            {
                string sCat = SWResults("");
                JArray oCat = JArray.Parse(sCat);
                lRZCat = JArray.Parse(sCat).SelectTokens("..Shortname").Values<string>().OrderBy(t => t).ToList();
            }
            catch { }

            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(lRZCat, Formatting.None),
                ContentType = "application/json",
                ContentEncoding = Encoding.UTF8
            };
        }

        internal ActionResult SetResult(string Instance, string Hostname, string Result)
        {
            JArray jData = new JArray();
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MemberInfo[] memberInfos = xType.GetMember("jData", BindingFlags.Public | BindingFlags.Static);

                jData = ((FieldInfo)memberInfos[0]).GetValue(new JArray()) as JArray;

                var tok = jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult");
                tok = Result;
                jData.SelectToken("[?(@.Hostname == '" + Hostname + "')].ScriptResult").Replace(tok);

                ((FieldInfo)memberInfos[0]).SetValue(new JArray(), jData);
            }
            catch { }


            return new ContentResult();
        }

        internal string GetID(string Instance, string Host)
        {
            string sID = "";
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "GetID",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Host }) as string;
            }
            catch { }

            return sID;
        }

        internal void Reload(string Instance)
        {
            string sID = "";
            try
            {
                Type xType = Type.GetType("DevCDRServer." + Instance);

                MethodInfo methodInfo = xType.GetMethod(
                                            "Reload",
                                            BindingFlags.Public | BindingFlags.Static
                                        );
                sID = methodInfo.Invoke(new object(), new object[] { Instance }) as string;
            }
            catch { }
        }

        [AllowAnonymous] //Test instance
        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object Command()
        {
            string sParams = "";
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();
            JObject oParams = JObject.Parse(sParams);

            string sCommand = oParams.SelectToken(@"$.command").Value<string>(); //get command name
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sArgs = oParams.SelectToken(@"$.args").Value<string>(); //get parameters

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    lHostnames.Add(oRow.Value<string>("Hostname"));
                }
                catch { }
            }

            switch(sCommand)
            {
                case "AgentVersion":
                    AgentVersion(lHostnames, sInstance);
                    break;
                case "Inv":
                    string sEndPoint = Request.Url.Authority;
                    RunCommand(lHostnames, "Invoke-RestMethod -Uri 'https://" + sEndPoint  + "/jaindb/getps' | IEX;'Inventory complete..'", sInstance, sCommand);
                    break;
                case "Restart":
                    RunCommand(lHostnames, "restart-computer -force", sInstance, sCommand);
                    break;
                case "Shutdown":
                    RunCommand(lHostnames, "stop-computer -force", sInstance, sCommand);
                    break;
                case "Logoff":
                    RunCommand(lHostnames, "(gwmi win32_operatingsystem).Win32Shutdown(4);'Logoff enforced..'", sInstance, sCommand);
                    break;
                case "Init":
                    Reload(sInstance);
                    break;
                case "GetRZUpdates":
                    RZScan(lHostnames, sInstance);
                    break;
                case "InstallRZUpdates":
                    RZUpdate(lHostnames, sInstance);
                    break;
                case "InstallRZSW":
                    InstallRZSW(lHostnames, sInstance, sArgs);
                    break;
                case "GetGroups":
                    GetGroups(lHostnames, sInstance);
                    break;
                case "SetGroups":
                    SetGroups(lHostnames, sInstance, sArgs);
                    break;
                case "GetUpdates":
                    RunCommand(lHostnames, "(Get-WUList -MicrosoftUpdate) | select Title | ConvertTo-Json", sInstance, sCommand);
                    break;
                case "InstallUpdates":
                    RunCommand(lHostnames, "Install-WindowsUpdate -MicrosoftUpdate -IgnoreReboot -AcceptAll -Install;installing Updates...", sInstance, sCommand);
                    break;
                case "RestartAgent":
                    RestartAgent(lHostnames, sInstance);
                    break;
                case "SetInstance":
                    SetInstance(lHostnames, sInstance, sArgs);
                    break;
                case "SetEndpoint":
                    SetEndpoint(lHostnames, sInstance, sArgs);
                    break;
                case "DevCDRUser":
                    runUserCmd(lHostnames, sInstance, "", "");
                    break;
            }

            return new ContentResult();
        }

        internal void RunCommand(List<string> Hostnames, string sCommand, string sInstance, string CmdName)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + CmdName); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                }
            }
        }

        internal void GetGroups(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get Groups"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "get Groups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).getgroups("Host");
                }
            }
        }

        internal void SetGroups(List<string> Hostnames, string sInstance, string Args)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Groups"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "set Groups"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).setgroups(Args);
                }
            }
        }

        internal void AgentVersion(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get AgentVersion"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "get AgentVersion"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).version("HUB");
                }
            }
        }

        internal void SetInstance(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Instance"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "set Instance"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).setinstance(Args);
                }
            }
        }

        internal void SetEndpoint(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "set Endpoint"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "set Endpoint"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).setendpoint(Args);
                }
            }
        }

        internal void RestartAgent(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "restart Agent"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "restart Agent"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).restartservice("HUB");
                }
            }
        }

        internal void InstallRZSW(List<string> Hostnames, string sInstance, string Args)
        {
            if (string.IsNullOrEmpty(Args))
                return;

            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "Install SW"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "Install SW"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).rzinstall(Args);
                }
            }
        }

        internal void RZScan(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "get RZ Updates"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "get RZ Updates"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).rzscan("HUB");
                }
            }
        }

        internal void RZUpdate(List<string> Hostnames, string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "install RZ Updates"); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "install RZ Updates"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).rzupdate("HUB");
                }
            }
        }

        internal void runUserCmd(List<string> Hostnames, string sInstance, string cmd, string args)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (string sHost in Hostnames)
            {
                SetResult(sInstance, sHost, "triggered:" + "run command as User..."); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", "run command as User"); //Enforce PageUpdate

            foreach (string sHost in Hostnames)
            {
                if (string.IsNullOrEmpty(sHost))
                    continue;

                //Get ConnectionID from HostName
                string sID = GetID(sInstance, sHost);

                if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                {
                    hubContext.Clients.Client(sID).userprocess(cmd, args);
                }
            }
        }

        [AllowAnonymous] //Test instance
        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object RunPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;
            
            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        
                        hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        [AllowAnonymous] //Test instance
        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object RunUserPS()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {
                        hubContext.Clients.Client(sID).userprocess("powershell.exe", "-command \"& { " + sCommand + " }\"");
                        //hubContext.Clients.Client(sID).returnPS(sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        [AllowAnonymous] //Test instance
        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object RunPSAsync()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sCommand = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.psscript").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title

            if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                return new ContentResult();

            List<string> lHostnames = new List<string>();
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            foreach (var oRow in oParams["rows"])
            {
                string sHost = oRow.Value<string>("Hostname");
                SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
            }
            hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

            foreach (var oRow in oParams["rows"])
            {
                try
                {
                    //Get Hostname from Row
                    string sHost = oRow.Value<string>("Hostname");

                    if (string.IsNullOrEmpty(sHost))
                        continue;

                    //Get ConnectionID from HostName
                    string sID = GetID(sInstance, sHost);

                    if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                    {

                        hubContext.Clients.Client(sID).returnPSAsync(sCommand, "Host");
                    }
                }
                catch { }
            }

            return new ContentResult();
        }

        [AllowAnonymous] //Test instance
        [System.Web.Mvc.Authorize]
        [HttpPost]
        public object RunPSFile()
        {
            string sParams = "";
            //Load response
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
                sParams = reader.ReadToEnd();

            if (string.IsNullOrEmpty(sParams))
                return new ContentResult(); ;

            //Parse response as JSON
            JObject oParams = JObject.Parse(sParams);

            string sFile = System.Uri.UnescapeDataString(oParams.SelectToken(@"$.file").Value<string>()); //get command
            string sInstance = oParams.SelectToken(@"$.instance").Value<string>(); //get instance name
            string sTitle = oParams.SelectToken(@"$.title").Value<string>(); //get title
            string sFilePath = HttpContext.Server.MapPath(sFile);
            if (System.IO.File.Exists(sFilePath))
            {
                if (!sFilePath.StartsWith(HttpContext.Server.MapPath("~/App_Data/PSScripts/")))
                    return new ContentResult();

                string sCommand = System.IO.File.ReadAllText(sFilePath);

                if (string.IsNullOrEmpty(sInstance)) //Skip if instance is null
                    return new ContentResult();

                List<string> lHostnames = new List<string>();
                IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

                foreach (var oRow in oParams["rows"])
                {
                    string sHost = oRow.Value<string>("Hostname");
                    SetResult(sInstance, sHost, "triggered:" + sTitle); //Update Status
                }
                hubContext.Clients.Group("web").newData("HUB", sCommand); //Enforce PageUpdate

                foreach (var oRow in oParams["rows"])
                {
                    try
                    {
                        //Get Hostname from Row
                        string sHost = oRow.Value<string>("Hostname");

                        if (string.IsNullOrEmpty(sHost))
                            continue;

                        //Get ConnectionID from HostName
                        string sID = GetID(sInstance, sHost);

                        if (!string.IsNullOrEmpty(sID)) //Do we have a ConnectionID ?!
                        {
                            hubContext.Clients.Client(sID).returnPSAsync(sCommand, "Host");
                        }
                    }
                    catch { }
                }
            }

            return new ContentResult();
        }

        internal string SWResults(string Searchstring)
        {
            string sCatFile = @"/App_Data/rzcat.json";
            string sResult = "";
            string sURL = "https://ruckzuck.azurewebsites.net/wcf/RZService.svc";
            
            sCatFile = HttpContext.Server.MapPath("~/App_Data/rzcat.json");
            //if (_cache.TryGetValue("SWResult-" + Searchstring, out sResult))
            //{
            //    return sResult;
            //}
            try
            {
                if (string.IsNullOrEmpty(Searchstring))
                {
                    if (System.IO.File.Exists(sCatFile))
                    {
                        if (DateTime.Now.ToUniversalTime() - System.IO.File.GetCreationTime(sCatFile).ToUniversalTime() <= new TimeSpan(1, 0, 1))
                        {
                            sResult = System.IO.File.ReadAllText(sCatFile);
                            if (sResult.StartsWith("[") & sResult.Length > 64) //check if it's JSON
                            {
                                return sResult;
                            }
                        }
                    }
                }
                else
                {

                }

                HttpClient oClient = new HttpClient();
                oClient.DefaultRequestHeaders.Accept.Clear();
                oClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = oClient.GetStringAsync(sURL + "/rest/SWResults?search=" + Searchstring);
                response.Wait(10000); //10s max.
                if (response.IsCompleted)
                {
                    sResult = response.Result;
                    if (sResult.StartsWith("[") & sResult.Length > 64)
                    {
                        if (string.IsNullOrEmpty(Searchstring))
                            System.IO.File.WriteAllText(sCatFile, sResult);

                        return sResult;
                    }
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            //return old File
            if (System.IO.File.Exists(sCatFile))
            {
                return System.IO.File.ReadAllText(sCatFile);
            }

            return "";
        }

    }
}