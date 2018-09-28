using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevCDRServer
{
    public class Test : Hub
    {
        private readonly static ConnectionMapping<string> _connections = new ConnectionMapping<string>();
        public static List<string> lClients = new List<string>();
        public static List<string> lGroups = new List<string>();
        public static JArray jData = new JArray();
        public static int ClientCount { get { return lClients.Count(); } }

        public void Init(string name)
        {
            name = name.ToUpper();
            _connections.Remove(name, ""); //Remove existing Name
            _connections.Add(name, Context.ConnectionId); //Add Name

            lClients.Remove(name);
            lClients.Add(name);
            lClients.Remove("");

            //Request Status
            Clients.Client(Context.ConnectionId).status(name);

            if (!string.IsNullOrEmpty(name))
            {
                JoinGroup("Devices");
            }
        }

        public void HealthCheck(string name)
        {
            string sEndPoint = "devcdr.azurewebsites.net";
            Clients.Client(Context.ConnectionId).returnPSAsync("Invoke-RestMethod -Uri 'https://" + sEndPoint + "/jaindb/getps?filename=compliance_default.ps1' | IEX;'Check complete..'", "Host");
        }

        public void Inventory(string name)
        {
            string sEndPoint = "devcdr.azurewebsites.net";
            Clients.Client(Context.ConnectionId).returnPSAsync("Invoke-RestMethod -Uri 'https://" + sEndPoint + "/jaindb/getps?filename=inventory.ps1' | IEX;'Inventory complete..'", "Host");
        }
        public Task JoinGroup(string groupName)
        {
            if (!lGroups.Contains(groupName))
            {
                lGroups.Add(groupName);
            }

            return Groups.Add(Context.ConnectionId, groupName);
        }

        public Task LeaveGroup(string groupName)
        {
            if (lGroups.Contains(groupName))
            {
                lGroups.Remove(groupName);
            }
            return Groups.Remove(Context.ConnectionId, groupName);
        }

        public void Status(string name, string Status)
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
                    Clients.Group("web").newData(name, jData.ToString()); //Enforce PageUpdate
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                }
            }
        }

        public void Send(string name, string message)
        {
            /*string lMessage = message.ToLower();

            if (message.StartsWith("@"))
            {
                if (lMessage.StartsWith("@all:"))
                {
                    Clients.AllExcept(Context.ConnectionId).addMessage(name, message.Substring(5));
                    return;
                }
                if (lMessage.StartsWith("@:"))
                {
                    Clients.AllExcept(Context.ConnectionId).addMessage(name, message.Substring(2));
                    return;
                }
                if (lMessage.StartsWith("@all&ps:"))
                {
                    Clients.AllExcept(Context.ConnectionId).runPS(message.Substring(8));
                    return;
                }
                if (lMessage.StartsWith("@&ps:"))
                {
                    Clients.AllExcept(Context.ConnectionId).runPS(message.Substring(5));
                    return;
                }
                if (lMessage.StartsWith("@all&get:"))
                {
                    Clients.AllExcept(Context.ConnectionId).getPS(message.Substring(9), Context.ConnectionId);
                    return;
                }
                if (lMessage.StartsWith("@&get:"))
                {
                    Clients.AllExcept(Context.ConnectionId).getPS(message.Substring(6), Context.ConnectionId);
                    return;
                }

                if (lMessage.Split(':')[0].Contains("&get"))
                {
                    string target = message.Substring(1, lMessage.IndexOf("&get:") - 1);
                    GetPS(target, message.Substring(lMessage.IndexOf("&get:") + 5), Context.ConnectionId);
                    return;

                }
            }

            if (message.StartsWith("&"))
            {
                if (lMessage.StartsWith("&list"))
                {
                    Clients.Client(Context.ConnectionId).showMsg(string.Join("\r\n", _connections.GetNames().ToArray().OrderBy(t => t)));
                    return;
                }

                if (lMessage.StartsWith("&version"))
                {
                    Clients.All.version(Context.ConnectionId);
                    return;
                }

                if (lMessage.StartsWith("&online"))
                {
                    var clients = _connections.GetNames();
                    clients.Remove(Context.ConnectionId); //Hide own Name
                    Clients.Client(Context.ConnectionId).showMsg(string.Join("\r\n", clients.ToArray().OrderBy(t => t)));
                    return;
                }

                if (lMessage.StartsWith("&init"))
                {
                    _connections.Clean();
                    lClients.Clear();
                    Clients.All.init("init");
                    jData = new JArray();
                    Clients.Group("web").newData(name, jData.ToString()); //Enforce PageUpdate
                    return;
                }
            }
            */
        }

        public void Respond(string senderId, string message)
        {
            Clients.Client(senderId).showMsg(message);
        }

        public void ShowError(string who, string message)
        {
            string name = Context.User.Identity.Name;

            foreach (var connectionId in _connections.GetConnections(who))
            {
                Clients.Client(connectionId).showError(message);
            }
        }

        public void ShowMsg(string who, string message)
        {
            foreach (var connectionId in _connections.GetConnections(who))
            {
                Clients.Client(connectionId).showMsg(message);
            }
        }

        public void RunPS(string who, string message)
        {
            foreach (var connectionId in _connections.GetConnections(who))
            {
                Clients.Client(connectionId).runPS(message);
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
                Clients.Client(connectionId).getPS(ps, sender);
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
                Clients.Client(connectionId).version("");
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
        public static void Reload(string sInstance)
        {
            IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext(sInstance);

            _connections.Clean();
            lClients.Clear();
            lGroups.Clear();
            hubContext.Clients.All.init("init");
            jData = new JArray();
            hubContext.Clients.Group("web").newData("Hub", ""); //Enforce PageUpdate
        }

        public void WOL(string MAC)
        {
            Clients.All.wol(MAC);
        }

        public override Task OnConnected()
        {
            string name = Context.User.Identity.Name;

            if (!string.IsNullOrEmpty(name))
            {
                _connections.Add(name, Context.ConnectionId);
            }

            //Clients.Group("web").newData(Context.ConnectionId, "OnConnected"); //Enforce PageUpdate?

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
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


            Clients.Group("web").newData(Context.ConnectionId, "OnDisconnected"); //Enforce PageUpdate

            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            string name = Context.User.Identity.Name;

            if (!_connections.GetConnections(name).Contains(Context.ConnectionId))
            {
                _connections.Add(name, Context.ConnectionId);
            }

            lClients = _connections.GetNames();

            //Clients.Group("web").newData(Context.ConnectionId, "OnReconnected"); //Enforce PageUpdate?

            return base.OnReconnected();
        }

    }

}