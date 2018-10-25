using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevCDRServer
{
    public class Default : Hub
    {
        private readonly static ConnectionMapping<string> _connections = new ConnectionMapping<string>();
        public static List<string> lClients = new List<string>();
        public static List<string> lGroups = new List<string>();
        public static JArray jData = new JArray();


        public static int ClientCount { get { return lClients.Distinct().Count(); } }

        public async Task Init(string name)
        {
            string sEndPoint = Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];

            name = name.ToUpper();
            _connections.Remove(name, ""); //Remove existing Name
            _connections.Add(name, Context.ConnectionId); //Add Name

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
            string sEndPoint = Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];
            //string sEndPoint = "devcdr.azurewebsites.net";
            //string sEndPoint = Request.GetEncodedUrl().ToLower().Split("/devcdr/")[0];
            Clients.Client(Context.ConnectionId).SendAsync("returnPSAsync", "Invoke-RestMethod -Uri 'https://" + sEndPoint + "/jaindb/getps?filename=compliance_default.ps1' | IEX;''", "Host");
        }

        public void Inventory(string name)
        {
            string sEndPoint = Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];
            //string sEndPoint = "devcdr.azurewebsites.net";
            Clients.Client(Context.ConnectionId).SendAsync("returnPSAsync", "Invoke-RestMethod -Uri 'https://" + sEndPoint + "/jaindb/getps?filename=inventory.ps1' | IEX;'Inventory complete..'", "Host");
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
            var J1 = JObject.Parse(Status);
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

    }
}