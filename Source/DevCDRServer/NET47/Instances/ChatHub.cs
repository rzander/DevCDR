using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevCDRServer
{
    public class ConnectionMapping<T>
    {
        private readonly Dictionary<T, HashSet<string>> _connections = new Dictionary<T, HashSet<string>>();

        public int Count
        {
            get
            {
                return _connections.Count;
            }
        }

        public void Add(T key, string connectionId)
        {
            lock (_connections)
            {
                HashSet<string> connections;
                if (!_connections.TryGetValue(key, out connections))
                {
                    connections = new HashSet<string>();
                    _connections.Add(key, connections);
                }

                lock (connections)
                {
                    connections.Add(connectionId);
                }
            }
        }

        public IEnumerable<string> GetConnections(T key)
        {
            HashSet<string> connections;
            if (_connections.TryGetValue(key, out connections))
            {
                return connections;
            }

            return Enumerable.Empty<string>();
        }

        public List<string> GetNames()
        {
            List<string> lResult = new List<string>();
            foreach (var oItem in _connections.Keys)
            {
                lResult.Add(oItem.ToString());
            }

            return lResult;
        }

        public void Remove(T key, string connectionId)
        {
            lock (_connections)
            {
                if (string.IsNullOrEmpty(key as string))
                {
                    try
                    {
                        var oItem = _connections.FirstOrDefault(t => t.Value.Contains(connectionId));
                        if (oItem.Key != null)
                            key = oItem.Key;
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(key as string))
                {
                    try
                    {
                        _connections.Remove(key);
                        return;
                    }
                    catch { }
                }

            }
        }

        public void Clean()
        {
            lock (_connections)
            {
                _connections.Clear();
            }
        }
    }
}