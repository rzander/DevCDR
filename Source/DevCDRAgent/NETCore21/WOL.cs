using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DevCDRAgent.Modules
{
    /// <summary>
    /// Wake ON LAN Class
    /// </summary>
    public class WOL
    {
        public static void WakeUp(string MAC_ADDRESS)
        {
            WakeUp(new IPAddress(0xffffffff), 0x2fff, MAC_ADDRESS);
        }

        /// <summary>
        /// Send a WakeOnLan command 
        /// </summary>
        /// <param name="IPAddr">Destination IP Address (e.g. 255.255.255.255 = broadcast)</param>
        /// <param name="Port">UDP Port</param>
        /// <param name="MAC_ADDRESS">MAC Address to wakeup</param>
        public static void WakeUp(IPAddress IPAddr, int Port, string MAC_ADDRESS)
        {
            try
            {
                WOLClass client = new WOLClass();
                Regex oRegex = new Regex("[^a-fA-F0-9]");
                MAC_ADDRESS = oRegex.Replace(MAC_ADDRESS, "");
                client.Connect(IPAddr,  //255.255.255.255  i.e broadcast
                   Port); // port=12287 let's use this one 
                client.SetClientToBrodcastMode();
                //set sending bites
                int counter = 0;
                //buffer to be send
                byte[] bytes = new byte[1024];   // more than enough :-)
                                                 //first 6 bytes should be 0xFF
                for (int y = 0; y < 6; y++)
                    bytes[counter++] = 0xFF;
                //now repeate MAC 16 times
                for (int y = 0; y < 16; y++)
                {
                    int i = 0;
                    for (int z = 0; z < 6; z++)
                    {
                        bytes[counter++] =
                            byte.Parse(MAC_ADDRESS.Substring(i, 2),
                            NumberStyles.HexNumber);
                        i += 2;
                    }
                }

                //now send wake up packet
                int reterned_value = client.Send(bytes, 1024);
            }
            catch { }
        }
    }

    internal class WOLClass : UdpClient
    {
        internal WOLClass()
            : base()
        { }
        //this is needed to send broadcast packet
        internal void SetClientToBrodcastMode()
        {
            if (this.Active)
                this.Client.SetSocketOption(SocketOptionLevel.Socket,
                                          SocketOptionName.Broadcast, 0);
        }
    }
}
