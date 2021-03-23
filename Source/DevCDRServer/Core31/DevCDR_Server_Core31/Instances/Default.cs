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
using DevCDR.Extensions;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

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
        public static string wwwrootPath;
        public static string RootName= (Environment.GetEnvironmentVariable("rootKey") ?? "DeviceCommander");

        public static int ClientCount { get { return lClients.Distinct().Count(); } }

        public async Task Init(string name)
        {
            //string sEndPoint = Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];

            name = name.ToLower();
            _connections.Remove(name, ""); //Remove existing Name
            _connections.Add(name, Context.ConnectionId); //Add Name

            if (string.IsNullOrEmpty(IP2LocationURL))
            {
                IP2LocationURL = Environment.GetEnvironmentVariable("IP2LocationURL") ?? "";
            }

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

        public async Task InitCert(string name, string signature)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                X509AgentCert oSig = new X509AgentCert(signature);
                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid)
                {
                    name = name.ToLower();
                    _connections.Remove(name, ""); //Remove existing Name
                    _connections.Add(name, Context.ConnectionId); //Add Name

                    IP2LocationURL = Environment.GetEnvironmentVariable("IP2LocationURL") ?? "";

                    lClients.Remove(name);
                    lClients.Add(name);
                    lClients.Remove("");

                    if (!string.IsNullOrEmpty(name))
                    {
                        await JoinGroup("Devices");
                    }

                    string groupName = oSig.IssuingCA;

                    if (!string.IsNullOrEmpty(groupName))
                    {
                        if (!lGroups.Contains(groupName))
                        {
                            lGroups.Add(groupName);
                        }
                    }

                    await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                    //Request Status
                    await Clients.Client(Context.ConnectionId).SendAsync("status", name);
                }
                else
                {

                    //Just for the case that something is wrong with the certificates...
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AllowAll")))
                    {
                        name = name.ToLower();
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
                    else
                    {
                        await Clients.Client(Context.ConnectionId).SendAsync("setAgentSignature", "");
                    }
                }
            }
            else
            {
                //No external CertAuthority defined... start classic mode:
                name = name.ToLower();
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
        }

        public async Task UpdateCompliance(string CustomerID, string DeviceId, string ComplianceResult)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                await setComplianceAsync(CustomerID, DeviceId, ComplianceResult);
            }
        }

        public async Task UpdateComplianceCert(string CustomerID, string DeviceId, string ComplianceResult, string signature)
        {
            X509AgentCert oSig = new X509AgentCert(signature);

            try
            {
                if (X509AgentCert.publicCertificates.Count == 0)
                    X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                    X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                oSig.ValidateChain(X509AgentCert.publicCertificates);
            }
            catch { }

            if (oSig.Exists && oSig.Valid)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    await setComplianceAsync(CustomerID, DeviceId, ComplianceResult);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    await Clients.Client(Context.ConnectionId).SendAsync("setAgentSignature", "");
                }
            }
        }

        public async Task UpdateComplianceCert2(string ComplianceResult, string signature)
        {
            X509AgentCert oSig = new X509AgentCert(signature);

            try
            {
                if (X509AgentCert.publicCertificates.Count == 0)
                    X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                    X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                oSig.ValidateChain(X509AgentCert.publicCertificates);
            }
            catch { }

            if (oSig.Exists && oSig.Valid)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    await setComplianceAsync(oSig.CustomerID, oSig.DeviceID, ComplianceResult);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    await Clients.Client(Context.ConnectionId).SendAsync("setAgentSignature", "");
                }
            }
        }

        public async Task GetCert(string customer, bool useKey = true)
        {
            string orgCustomer = customer;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                if(customer == "DEMO")
                {
                    string sIssuingCA = getPublicCertAsync(customer, false).Result;
                    await Clients.Client(Context.ConnectionId).SendAsync("setCert", sIssuingCA);
                }
                else
                {
                    string sIssuingCA = getPublicCertAsync(customer, useKey).Result;
                    await Clients.Client(Context.ConnectionId).SendAsync("setCert", sIssuingCA);
                }
            }
        }

        public async Task GetMachineCert(string customer, string deviceID)
        {
            try
            {
                if (!string.IsNullOrEmpty(customer + deviceID))
                {
                    string sCert = getMachineCertAsync(deviceID, customer).Result;
                    await Clients.Client(Context.ConnectionId).SendAsync("setCert", sCert);
                }
            }
            catch
            {
            }
        }

        public void HealthCheck(string name)
        {
            string regName = Environment.GetEnvironmentVariable("ComputernameRegex") ?? "(.*?)";
            string complianceFile = Environment.GetEnvironmentVariable("ScriptCompliance") ?? "Compliance_default.ps1";
            Match m = Regex.Match(name, regName, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string sEndPoint = Environment.GetEnvironmentVariable("DevCDRUrl") ?? Context.GetHttpContext().Request.GetEncodedUrl().ToLower().Split("/chat")[0];
                Clients.Client(Context.ConnectionId).SendAsync("returnPSAsync", "Invoke-RestMethod -Uri '" + sEndPoint + "/jaindb/getps?filename=" + complianceFile + "' | IEX;''", "Host");

                //string spath = Path.Combine(wwwrootPath, "jaindb", complianceFile);
                //string sPS = File.ReadAllText(spath);

                //Clients.Client(Context.ConnectionId).SendAsync("checkComplianceAsync", sPS);
            }
        }

        public void HealthCheckCert(string name, string signature, string customerid)
        {
            string regName = Environment.GetEnvironmentVariable("ComputernameRegex") ?? "(.*?)";
            Match m = Regex.Match(name, regName, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid)
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        string sScript = getScriptAsync(customerid, "compliance.ps1").Result;
                        if(string.IsNullOrEmpty(sScript))
                            sScript = getScriptAsync("DEMO", "compliance.ps1").Result;

                        if(!string.IsNullOrEmpty(sScript))
                            Clients.Client(Context.ConnectionId).SendAsync("checkComplianceAsync", sScript);
                    }
                }

            }
        }

        public void HealthCheckCert2(string signature)
        {
            X509AgentCert oSig = new X509AgentCert(signature);

            try
            {
                if (X509AgentCert.publicCertificates.Count == 0)
                    X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                    X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                oSig.ValidateChain(X509AgentCert.publicCertificates);
            }
            catch { }

            if (oSig.Exists && oSig.Valid)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sScript = getScriptAsync(oSig.CustomerID, "compliance.ps1").Result;
                    if (string.IsNullOrEmpty(sScript))
                        sScript = getScriptAsync("DEMO", "compliance.ps1").Result;

                    if (!string.IsNullOrEmpty(sScript))
                        Clients.Client(Context.ConnectionId).SendAsync("checkComplianceAsync", sScript);
                }
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
            if (groupName != "web" && groupName != "Devices" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                if (!lGroups.Contains("unknown"))
                {
                    lGroups.Add("unknown");
                }
                return Groups.AddToGroupAsync(Context.ConnectionId, "unknown");
            }
            
            if (!lGroups.Contains(groupName))
            {
                lGroups.Add(groupName);
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public Task JoinGroupCert(string name, string signature)
        {
            string groupName = "unknown";
            try
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid && !string.IsNullOrEmpty(oSig.IssuingCA))
                {
                    groupName = oSig.IssuingCA;

                    if (!string.IsNullOrEmpty(groupName))
                    {
                        if (!lGroups.Contains(groupName))
                        {
                            lGroups.Add(groupName);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                ex.Message.ToString();
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public Task JoinGroupCert2(string signature)
        {
            string groupName = "unknown";
            try
            {
                X509AgentCert oSig = new X509AgentCert(signature);

                try
                {
                    if (X509AgentCert.publicCertificates.Count == 0)
                        X509AgentCert.publicCertificates.Add(new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(RootName, false).Result))); //root

                    var xIssuing = new X509Certificate2(Convert.FromBase64String(getPublicCertAsync(oSig.IssuingCA, false).Result));
                    if (!X509AgentCert.publicCertificates.Contains(xIssuing))
                        X509AgentCert.publicCertificates.Add(xIssuing); //Issuing

                    oSig.ValidateChain(X509AgentCert.publicCertificates);
                }
                catch { }

                if (oSig.Exists && oSig.Valid && !string.IsNullOrEmpty(oSig.IssuingCA))
                {
                    groupName = oSig.IssuingCA;

                    if (!string.IsNullOrEmpty(groupName))
                    {
                        if (!lGroups.Contains(groupName))
                        {
                            lGroups.Add(groupName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async void Status(string name, string Status)
        {
            name = name.ToLower();

            if (string.IsNullOrEmpty(AzureLog.WorkspaceId))
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-WorkspaceID")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Log-SharedKey")))
                {
                    AzureLog = new DevCDR.Extensions.AzureLogAnalytics(Environment.GetEnvironmentVariable("Log-WorkspaceID"), Environment.GetEnvironmentVariable("Log-SharedKey"), "DevCDR_" + (Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default"));
                }
            }

            var J1 = JObject.Parse(Status);

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
            {
                J1["Groups"] = "unknown";
            }

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

            if (J1["Customer"] == null)
                J1.Add("Customer", "");

            bool bChange = false;
            try
            {
                if (string.IsNullOrEmpty(J1.GetValue("Hostname").Value<string>()))
                    return; 

                if (jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]").Count() == 0) //Prevent Duplicates
                {
                    J1.Add("ConnectionId", Context.ConnectionId);

                    lock (jData)
                    {
                        jData.Add(J1);
                    }
                    bChange = true;
                    _connections.Add(name, Context.ConnectionId);

                    await Clients.Group("web").SendAsync("newData", "add", ""); //Enforce PageUpdate

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        try
                        {
                            await setStatusAsync(Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString(), J1.ToString());
                        }
                        catch { }
                    }

                    AzureLog.PostAsync(new { Computer = J1.GetValue("Hostname"), EventID = 3000, Description = J1.GetValue("ScriptResult").ToString() });
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

                            //if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                            //{
                            //    J1.Add("ConnectionId", Context.ConnectionId);
                            //    setStatusAsync(Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString(), J1.ToString());
                            //}
                        }
                        else
                        {
                            //if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                            //{
                            //    string sJSON = "{\"ConnectionID\":\"" + Context.ConnectionId + "\"}";
                            //    setStatusAsync(Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("Hostname").ToString(), sJSON);
                            //}
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                bChange = true;
                ex.Message.ToString();
            }

            if (bChange)
            {
                try
                {
                    await Clients.Group("web").SendAsync("newData", name, ""); //Enforce PageUpdate
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                }
            }
        }

        public async void Status2(string name, string Status, string signature)
        {
            name = name.ToLower();
            X509AgentCert oSig = new X509AgentCert(signature, true); //do not validate signature for performance 

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
                    if (J1["IP"] == null)
                        J1.Add(new JProperty("IP", ClientIP));
                    else
                        J1["IP"] = ClientIP;
                }
                catch { }
            }

            if (J1["Customer"] == null)
                J1.Add("Customer", "");

            bool bChange = false;
            try
            {
                if (string.IsNullOrEmpty(J1.GetValue("Hostname").Value<string>()))
                    return;

                if (string.IsNullOrEmpty(J1.GetValue("id").Value<string>()))
                    return;

                if (jData.SelectTokens("[?(@.id == '" + J1.GetValue("id") + "')]").Count() == 0) //Prevent Duplicates
                {
                    J1.Add("ConnectionId", Context.ConnectionId);
                    lock (jData)
                    {
                        jData.Add(J1);
                    }
                    bChange = true;
                    _connections.Add(name, Context.ConnectionId); //Add Device
                    await Clients.Group("web").SendAsync("newData", "add", ""); //Enforce PageUpdate
                    AzureLog.PostAsync(new { Computer = J1.GetValue("Hostname"), EventID = 3000, Description = J1.GetValue("ScriptResult").ToString() });
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        if (!string.IsNullOrEmpty(oSig.CustomerID))
                            await setStatusAsync(oSig.CustomerID, oSig.DeviceID, J1.ToString());
                        else
                            await setStatusAsync(Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString(), J1.ToString());
                    }
                }
                else
                {
                    var jTemp = JObject.Parse(jData.SelectTokens("[?(@.id == '" + J1.GetValue("id") + "')]", false).First().ToString());

                    await Clients.Group("web").SendAsync("newData", jTemp.ToString(), ""); //Enforce PageUpdate
                    //Changes ?
                    if ((jTemp["ScriptResult"].Value<string>().ToLower() != J1["ScriptResult"].Value<string>().ToLower()) || (jTemp["Version"].Value<string>().ToLower() != J1["Version"].Value<string>().ToLower()))
                    {
                        lock (jData)
                        {
                            jData.SelectTokens("[?(@.id == '" + J1.GetValue("id") + "')]", false).First().Replace(J1);
                            bChange = true;
                        }
                    }

                    //if (jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]").First().ToString(Formatting.None).ToLower() != J1.ToString(Formatting.None).ToLower())
                    //{
                    //    await Clients.Group("web").SendAsync("newData", "change", jData.ToString()); //Enforce PageUpdate
                    //    lock (jData)
                    //    {
                    //        jData.SelectTokens("[?(@.Hostname == '" + J1.GetValue("Hostname") + "')]", false).First().Replace(J1);
                    //        bChange = true;
                    //    }
                    //    await Clients.Group("web").SendAsync("newData", "done", jData.ToString()); //Enforce PageUpdate
                    //}

                    if (bChange)
                    {
                        _ = Task.Run(() =>
                          {
                              AzureLog.PostAsync(new { Computer = J1.GetValue("Hostname"), EventID = 3000, Description = J1.GetValue("ScriptResult").ToString() });

                              if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                              {
                                  if (!string.IsNullOrEmpty(oSig.CustomerID))
                                      setStatusAsync(oSig.CustomerID, J1.GetValue("id").ToString(), J1.ToString()).Wait(2000);
                                  else
                                      setStatusAsync(Environment.GetEnvironmentVariable("INSTANCENAME") ?? "Default", J1.GetValue("id").ToString(), J1.ToString()).Wait(2000);
                              }
                          });

                       
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
                bChange = true; //update on error...
            }

            if (bChange)
            {
                try
                {
                    await Clients.Group("web").SendAsync("newData", name, ""); //Enforce PageUpdate
                }
                catch (Exception ex)
                {
                    ex.Message.ToString();
                }
            }
        }

        public void RunPS(string who, string message)
        {
            foreach (var connectionId in _connections.GetConnections(who.ToLower()))
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

            foreach (var connectionId in _connections.GetConnections(who.ToLower()))
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

            foreach (var connectionId in _connections.GetConnections(who.ToLower()))
            {
                Clients.Client(connectionId).SendAsync("version", "");
            }
        }

        public static string GetID(string who)
        {
            foreach (var connectionId in _connections.GetConnections(who.ToLower()))
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
            await Clients.Group("web").SendAsync("newData", Context.ConnectionId, "OnConnected"); //Enforce PageUpdate
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            //string name = Context.User.Identity.Name;

            _connections.Remove("", Context.ConnectionId);

            lClients = _connections.GetNames();

            try
            {
                if (lClients.Count > 0)
                {
                    foreach (var oObj in jData.Children().ToArray())
                    {
                        try
                        {
                            if (!lClients.Contains(oObj.Value<string>("Hostname").ToLower()))
                            {
                                int ix = jData.IndexOf(jData.SelectToken("[?(@.Hostname == '" + ((dynamic)oObj).Hostname + "')]"));
                                jData.RemoveAt(ix);
                            }
                        }
                        catch { }
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

        private static async Task<string> getPublicCertAsync(string CertName, bool useKey = true)
        {
            if (string.IsNullOrEmpty(CertName))
                return "";

            if (_cache == null)
                _cache = new MemoryCache(new MemoryCacheOptions());

            bool bCached = false;
            _cache.TryGetValue(CertName, out string Cert);
            if (string.IsNullOrEmpty(Cert))
            {
                try
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                    {
                        string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                        sURL = sURL.Replace("{fn}", "fnGetPublicCert");

                        if (useKey)
                        {
                            var stringTask = client.GetStringAsync($"{sURL}&key={CertName}");
                            Cert = await stringTask;
                        }
                        else
                        {
                            var stringTask = client.GetStringAsync($"{sURL}&name={CertName}");
                            Cert = await stringTask;
                        }
                    }
                }
                catch { return Cert; }
            }
            else
            {
                bCached = true;
            }

            if (!string.IsNullOrEmpty(Cert))
            {
                if (!bCached)
                {
                    var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(15)); //cache hash for x Seconds
                    _cache.Set(CertName, Cert, cacheEntryOptions);
                }
            }

            return Cert;
        }

        private static async Task<string> getMachineCertAsync(string deviceid, string key)
        {
            string Cert = "";
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnRequestAgentCert");

                    var stringTask = client.GetStringAsync($"{sURL}&deviceid={deviceid}&key={key}");
                    Cert = await stringTask;

                }
            }
            catch(Exception ex)
            {
                return ex.Message; 
            }


            return Cert;
        }

        private static async Task<string> getScriptAsync(string customerid, string filename)
        {
            string Res = "";
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnGetFile");

                    var stringTask = client.GetStringAsync($"{sURL}&customerid={customerid}&file={filename}");
                    Res = await stringTask;

                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
                return Res;
            }


            return Res;
        }

        private static async Task setComplianceAsync(string customerid, string deviceid, string compliancedata)
        {
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnUpdateCompliance");

                    StringContent oData = new StringContent(compliancedata, Encoding.UTF8, "application/json");
                    await client.PostAsync($"{sURL}&deviceid={deviceid}&customerid={customerid}", oData);
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }
        }

        private static async Task setStatusAsync(string customerid, string deviceid, string compliancedata)
        {
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnUpdateStatus");

                    StringContent oData = new StringContent(compliancedata, Encoding.UTF8, "application/json");
                    await client.PostAsync($"{sURL}&deviceid={deviceid}&customerid={customerid}", oData);
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }
        }

        private static async Task putFileAsync(string customerid, string deviceid, string data)
        {
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("fnDevCDR")))
                {
                    string sURL = Environment.GetEnvironmentVariable("fnDevCDR");
                    sURL = sURL.Replace("{fn}", "fnUploadFile");

                    StringContent oData = new StringContent(data, Encoding.UTF8, "application/json");
                    await client.PostAsync($"{sURL}&deviceid={deviceid}&customerid={customerid}", oData);
                }
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }
        }
    }
}