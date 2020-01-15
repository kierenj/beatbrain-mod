using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Linq;

namespace BeatBrain.Mod
{
    /// <summary>
    /// Looks for devices on the LAN which want to listen for note-hit events,
    /// sends packets to them when those events happen: netcode part.
    /// </summary>
    public class FlashCast : IDisposable
    {
        private static readonly IPAddress CastAddress = IPAddress.Parse("226.1.1.1");
        private static readonly int CastPort = 4096;

        private readonly Dictionary<IPEndPoint, DateTime> _targets = new Dictionary<IPEndPoint, DateTime>();
        private readonly IPEndPoint _castEndpoint = new IPEndPoint(CastAddress, CastPort);
        private readonly List<Thread> _threads = new List<Thread>();

        private IPEndPoint _sourceEndpoint;
        private UdpClient _castClient;
        private IPAddress _sourceAddress;

        private enum CastPacketType
        {
            Ping = 0x01,
            Pong = 0x02,
            HitLeft = 0x03,
            HitRight = 0x04
        }

        public bool Initialise()
        {
            var lan = GetLanInterface();
            if (lan == null)
            {
                Logger.log.Error("FlashCast: No suitable network interface found");
                return false;
            }

            var myIp = GetLocalIp(lan);
            if (myIp == null)
            {
                Logger.log.Error("FlashCast: No suitable IPv4 address");
                return false;
            }

            SetupClient(myIp);
            StartThreads();

            return true;
        }

        public void Dispose()
        {
            foreach (var thread in _threads)
            {
                thread.Abort();
            }
        }

        public void HitLeft()
        {
            SendAll(new[] { (byte)CastPacketType.HitLeft });
        }

        public void HitRight()
        {
            SendAll(new[] { (byte)CastPacketType.HitRight });
        }

        private void SendAll(byte[] buffer)
        {
            lock (_targets)
            {
                foreach (var target in _targets.Keys)
                {
                    _castClient.Send(buffer, buffer.Length, target);
                }
            }
        }

        private void StartThreads()
        {
            var serverRecv = new Thread(CastServerReceiveThread);
            serverRecv.IsBackground = true;
            serverRecv.Start();
            _threads.Add(serverRecv);

            var serverPing = new Thread(CastServerPingThread);
            serverPing.IsBackground = true;
            serverPing.Start();
            _threads.Add(serverPing);
        }

        private NetworkInterface GetLanInterface()
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface lan = null;
            foreach (var iface in ifaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                    iface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                lan = iface;
                break;
            }
            return lan;
        }

        private IPAddress GetLocalIp(NetworkInterface lan)
        {
            IPAddress myIp = null;
            foreach (var addr in lan.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                myIp = addr.Address;
                break;
            }
            return myIp;
        }

        private void SetupClient(IPAddress myIp)
        {
            _sourceAddress = myIp;
            _sourceEndpoint = new IPEndPoint(_sourceAddress, 0);
            _castClient = new UdpClient(_sourceEndpoint);
        }

        private void CastServerPingThread()
        {
            while (true)
            {
                Thread.Sleep(1000);
                _castClient.Send(new byte[] { (byte)CastPacketType.Ping }, 1, _castEndpoint);
                var cutoff = DateTime.UtcNow.AddSeconds(-3);
                lock (_targets)
                {
                    foreach (var target in _targets.Keys.ToArray())
                    {
                        if (_targets[target] < cutoff)
                        {
                            Logger.log.Info("Lost contact with unit @ " + target);
                            _targets.Remove(target);
                        }
                    }
                }
            }
        }

        private void CastServerReceiveThread()
        {
            IPEndPoint endPoint = default(IPEndPoint);

            while (true)
            {
                var bytes = _castClient.Receive(ref endPoint);
                if (bytes.Length == 0) continue;

                switch ((CastPacketType)bytes[0])
                {
                    case CastPacketType.Pong:
                        var ep = new IPEndPoint(endPoint.Address, CastPort);
                        lock (_targets)
                        {
                            if (!_targets.ContainsKey(ep))
                            {
                                Logger.log.Info("Discovered new unit @ " + endPoint.Address);
                            }
                            _targets[ep] = DateTime.UtcNow;
                        }
                        break;
                    default:
                        Logger.log.Warn("Unexpected packet type " + bytes[0] + " from " + endPoint + " - ignoring");
                        break;
                }
            }
        }
    }
}
