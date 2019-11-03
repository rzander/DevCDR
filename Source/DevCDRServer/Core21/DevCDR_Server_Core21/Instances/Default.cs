using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;

namespace DevCDRServer
{
    public class Default : Hub
    {
        private readonly static ConnectionMapping<string> _connections = new ConnectionMapping<string>();
        public static List<string> lClients = new List<string>();
        public static List<string> lGroups = new List<string>();
        public static JArray jData = new JArray();
        public DevCDR.Extensions.AzureLogAnalytics AzureLog = new DevCDR.Extensions.AzureLogAnalytics("", "", "");
        private static string IP2LocationURL = "";
        private static readonly HttpClient client = new HttpClient();
        private static IMemoryCache _cache;


        public static int ClientCount { get { return lClients.Distinct().Count(); } }

        public async Task Init(string name)
        {
            string sEndPoint = Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];

            name = name.ToUpper();
            _connections.Remove(name, ""); //Remove existing Name
            _connections.Add(name, Context.ConnectionId); //Add Name

            IP2LocationURL = Environment.GetEnvironmentVariable("IP2LocationURL") ?? "";

            lClients.Remove(name);
            lClients.Add(name);
            lClients.Remove("");

            //Request Status
            await Clients.Client(Context.ConnectionId).SendAsync("status", name);

            if (!string.IsNullOrEmpty(name))
            {
                await JoinGroup("Devices");
            }
        }

        public void HealthCheck(string name)
        {
            string regName = Environment.GetEnvironmentVariable("ComputernameRegex") ?? "(.*?)";
            string complianceFile = Environment.GetEnvironmentVariable("ScriptCompliance") ?? "compliance_default.ps1";
            Match m = Regex.Match(name, regName, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string sEndPoint = Environment.GetEnvironmentVariable("DevCDRUrl") ?? Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];
                Clients.Client(Context.ConnectionId).SendAsync("returnPSAsync", "Invoke-RestMethod -Uri '" + sEndPoint + "/jaindb/getps?filename=" + complianceFile + "' | IEX;''", "Host");
            }
        }

        public void Inventory(string name)
        {
            string regName = Environment.GetEnvironmentVariable("ComputernameRegex") ?? "(.*?)";
            string invFile = Environment.GetEnvironmentVariable("ScriptInvemtory") ?? "inventory.ps1";
            Match m = Regex.Match(name, regName, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string sEndPoint = Environment.GetEnvironmentVariable("DevCDRUrl") ?? Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];
                Clients.Client(Context.ConnectionId).SendAsync("returnPSAsync", "Invoke-RestMethod -Uri '" + sEndPoint + "/jaindb/getps?filename=" + invFile + "' | IEX;'Inventory complete..'", "Host");
            }
        }


        public Task JoinGroup(string groupName)
        {
            if (!lGroups.Contains(groupName))
            {
                lGroups.Add(groupName);
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async void Status(string name, string Status)
        {
            if (string.IsNullOrEmpty(AzureLog.WorkspaceId))
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-WorkspaceID")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-SharedKey")))
                {
                    AzureLog = new DevCDR.Extensions.AzureLogAnalytics(Environment.GetEnvironmentVariable("Log-WorkspaceID"), Environment.GetEnvironmentVariable("Log-SharedKey"), "DevCDR_" + (Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default"));
                }
            }

            var J1 = JObject.Parse(Status);

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IP2LocationURL")))
            {
                string ClientIP = Context.GetHttpContext().Connection.RemoteIpAddress.ToString();
                J1["Internal IP"] = ClientIP;
                try
                {
                    J1["Internal IP"] = GetLocAsync(ClientIP).Result;
                }
                catch { }
            }

            bool bChange = false;
            try
            {
                if (jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]").Count() == 0) //Prevent Duplicates
                {
                    lock (jData)
                    {
                        jData.Add(J1);
                    }
                    bChange = true;
                    AzureLog.PostAsync(new { Computer = J1.GetValue("Hostname"), EventID = 3000, Description = J1.GetValue("ScriptResult").ToString() });
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureTableStorage")))
                    {
                        J1.Add("ConnectionId", Context.ConnectionId);
                        DevCDR.Extensions.AzureTableStorage.UpdateEntityAsync(Environment.GetEnvironmentVariable("AzureTableStorage"), Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString() , J1.ToString());
                    }
                }
                else
                {
                    lock (jData)
                    {
                        //Changes ?
                        if (jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]").First().ToString(Formatting.None) != J1.ToString(Formatting.None))
                        {
                            jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]").First().Replace(J1);
                            bChange = true;
                            AzureLog.PostAsync(new { Computer = J1.GetValue("Hostname"), EventID = 3000, Description = J1.GetValue("ScriptResult").ToString() });

                            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureTableStorage")))
                            {
                                J1.Add("ConnectionId", Context.ConnectionId);
                                DevCDR.Extensions.AzureTableStorage.UpdateEntityAsync(Environment.GetEnvironmentVariable("AzureTableStorage"), Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString(), J1.ToString());
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureTableStorage")))
                            {
                                string sJSON = "{\"ConnectionID\":\"" + Context.ConnectionId + "\"}";
                                DevCDR.Extensions.AzureTableStorage.UpdateEntityAsync(Environment.GetEnvironmentVariable("AzureTableStorage"), Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("Hostname").ToString(), sJSON);
                            }
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            if (bChange)
            {
                try
                {
                    await Clients.Group("web").SendAsync("newData", name, jData.ToString()); //Enforce PageUpdate
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                }
            }
        }

        public void RunPS(string who, string message)
        {
            foreach (var connectionId in _connections.GetConnections(who.ToUpper()))
            {
                Clients.Client(connectionId).SendAsync("runPS", message);
            }
        }

        public void GetPS(string who, string ps, string sender)
        {
            if (string.IsNullOrEmpty(sender))
            {
                sender = Context.ConnectionId;
            }

            foreach (var connectionId in _connections.GetConnections(who.ToUpper()))
            {
                Clients.Client(connectionId).SendAsync("getPS", ps, sender);
            }
        }

        public void Version(string who, string ps, string sender)
        {
            if (string.IsNullOrEmpty(sender))
            {
                sender = Context.ConnectionId;
            }

            foreach (var connectionId in _connections.GetConnections(who.ToUpper()))
            {
                Clients.Client(connectionId).SendAsync("version", "");
            }
        }

        public static string GetID(string who)
        {
            foreach (var connectionId in _connections.GetConnections(who.ToUpper()))
            {
                return connectionId;
            }

            return "";
        }

        //Reload all data and refresh page
        public static void Clear(string Instance)
        {
            _connections.Clean();
            lClients.Clear();
            lGroups.Clear();
            jData = new JArray();
            //Clients.All.SendAsync("init", "init");
            //Clients.Group("web").SendAsync("newData", "Hub", ""); //Enforce PageUpdate
        }

        public void WOL(string MAC)
        {
            Clients.All.SendAsync("wol", MAC);
        }


        public override async Task OnConnectedAsync()
        {
            //string name = Context.User.Identity.Name;

            //if (!string.IsNullOrEmpty(name))
            //{
            //    try
            //    {
            //        if (!_connections.GetConnections(name).Contains(Context.ConnectionId))
            //        {
            //            _connections.Add(name, Context.ConnectionId);
            //        }
            //    }
            //    catch { }
            //}

            //await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            string name = Context.User.Identity.Name;

            _connections.Remove(name, Context.ConnectionId);

            lClients = _connections.GetNames();

            try
            {
                if (lClients.Count > 0)
                {
                    foreach (var oObj in jData.Children().ToArray())
                    {
                        if (!lClients.Contains(oObj.Value<string>("Hostname")))
                        {
                            int ix = jData.IndexOf(jData.SelectToken("[?(@.Hostname == '" + ((dynamic)oObj).Hostname + "')]"));
                            jData.RemoveAt(ix);
                        }
                    }
                }
                else
                {
                    jData = new JArray();
                }

            }
            catch { }


            await Clients.Group("web").SendAsync("newData", Context.ConnectionId, "OnDisconnected"); //Enforce PageUpdate

            //await Groups.RemoveFromGroupAsync(Context.ConnectionId, "SignalR Users");
            await base.OnDisconnectedAsync(exception);
        }

        private static async Task<string> GetLocAsync(string IP)
        {
            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            bool bCached = false;
            _cache.TryGetValue(IP, out string Loc);
            if (string.IsNullOrEmpty(Loc))
            {
                try
                {
                    if (!string.IsNullOrEmpty(IP2LocationURL))
                    {
                        var stringTask = client.GetStringAsync($"{IP2LocationURL}?ip={IP}");
                        Loc = await stringTask;

                    }
                }
                catch { return IP; }
            }
            else
            {
                bCached = true;
            }

            if (!string.IsNullOrEmpty(Loc))
            {
                if (!bCached)
                {
                    var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(300)); //cache hash for x Seconds
                    _cache.Set(IP, Loc, cacheEntryOptions);
                }

                var jLoc = JObject.Parse(Loc);
                return jLoc["Location"].ToString();
            }

            return IP;
        }

    }
}