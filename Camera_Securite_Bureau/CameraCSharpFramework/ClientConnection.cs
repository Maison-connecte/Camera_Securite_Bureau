using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CameraCSharpFramework
{
    public class ClientConnection
    {
        public Socket ClientSocket { get; set; }
        public Thread ClientThread { get; set; }
        public string ClientType { get; set; }
    }
}
