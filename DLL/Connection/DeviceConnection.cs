using System.IO.Ports;
using System.Threading;

namespace DLL.Connection
{
    /// <summary>
    /// A connection to an INS (Implanted Neural System / simulation) device
    /// </summary>
    public class DeviceConnection
    {
        private const int WRITE_TIMEOUT = 500;
        private const int READ_TIMEOUT = 500;

        private readonly object _writeLock = new();
        private readonly string _port;
        private Watchdog? _watchdog;
        private SerialPort? _serialPort;
        private PacketDispatcher? _packetDispatcher;
        private uint _packetCount = 0;

        /// <summary>
        ///     Create a connection to the INS over the specified port
        /// </summary>
        /// 
        /// <remarks>
        ///     The connection is created but not initialized. To initialize the connection,
        ///     call <see cref="Open"/>.
        /// </remarks>
        /// <param name="port">The port over which to communicate</param>
        public DeviceConnection(string port)
        {
            this._port = port;
        }

        /// <summary>
        ///     Opens the port and establishes a communication sesion
        /// </summary>
        public bool Open()
        {
            // Set up the port
            _serialPort = new SerialPort(_port, 115200, Parity.None, 8, StopBits.One);
            _serialPort.ReadTimeout = READ_TIMEOUT;
            _serialPort.WriteTimeout = WRITE_TIMEOUT;
            _serialPort.Open();

            // Prepare to listen for incoming data
            _packetDispatcher = new PacketDispatcher();

            // Open the connection
            bool opened = false;
            int attempts = 0;
            while (!opened && attempts++ < 10)
            {
                opened = TryOpenConnection();
                if (!opened)
                {
                    Thread.Sleep(WRITE_TIMEOUT);
                }
            }
            if (opened)
            {
                // Now that we have our connection, keep it alive
                _watchdog = new Watchdog(this);
            }
            return opened;
        }

        /// <summary>
        ///     Attempt to establish a session with the device
        /// </summary>
        /// <returns><c>true</c> if device acknowledged the new session</returns>
        internal bool TryOpenConnection() {
            bool success = Write(0x01);
            if (success)
            {
                Console.WriteLine("Connection Successful");
            }
            else
            {
                Console.Error.WriteLine("Timout opening connection to device!");
            }
            return success;
        }

        /// <summary>
        ///     Terminates the session, and releases the port and supporting resources
        /// </summary>
        public void Close()
        {
            if (_serialPort != null)
            {
                _watchdog?.Cancel();
                _packetDispatcher?.Cancel();

                _serialPort.Close();
                _serialPort = null;
            }
        }

        /// <summary>
        ///     Synchronously write a command to the device, and await acknowledgement
        /// </summary>
        /// <param name="opCode">The code to send</param>
        /// <param name="data">Additional data for the command</param>
        /// <returns>
        ///     <c>true</c> if the operation was acknowledged within the timeout
        /// </returns>
        public bool Write(byte opCode, byte[]? data = null)
        {
            if (_serialPort == null || _packetDispatcher == null)
            {
                throw new InvalidOperationException("Connection isn't open");
            }

            // Prepare the packet
            byte packetId = (byte)(Interlocked.Increment(ref _packetCount) % (Byte.MaxValue + 1));
            byte[] packet = BuildPacket(0x01, packetId, opCode, data);

            // Listen for the acknowledgement
            ManualResetEvent acknowledged = new(false);

            bool listener(byte id, byte opCode, byte[]? data)
            {
                if (id == packetId)
                {
                    acknowledged.Set();
                    return true;
                }
                return false;
            }
            _packetDispatcher.Register(opCode, listener, true);

            // Send the packet
            var result = true;
            lock (_writeLock)
            {
                try
                {
                    _serialPort.Write(packet, 0, packet.Length);
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("Timeout while writing to device!");
                    result = false;
                }
                catch (Exception e) { 
                    Console.Error.WriteLine("Unexpected exception writing to device: {0}", e.ToString()); 
                    result = false;
                }
            }

            // Wait for a reply 
            if (result)
            {
                result = acknowledged.WaitOne(WRITE_TIMEOUT);
            }

            if (!result)
            {
                // Clean up if no reply received
                _packetDispatcher.Unregister(opCode, listener);
            }
            return result;
        }

        /// <summary>
        ///     Build a packet for transmission to the device
        /// </summary>
        /// <param name="type">The packet type</param>
        /// <param name="packetId">Packet sequence id</param>
        /// <param name="opCode">The operation</param>
        /// <param name="data">Additional data buffer (optional)</param>
        /// <returns>
        ///     The fully populated packet
        /// </returns>
        private static byte[] BuildPacket(byte type, byte packetId, byte opCode, byte[]? data)
        {
            int payloadSize = 1 + (data?.Length ?? 0);
            int packetSize = 7 + payloadSize;
            byte[] packet = new byte[packetSize];
            packet[0] = 0xAA;
            packet[1] = 0x01;
            packet[2] = 0x02;
            packet[3] = type;
            packet[4] = packetId;
            packet[5] = (byte)payloadSize;
            packet[6] = opCode;
            if (data != null) {
                Buffer.BlockCopy(data, 0, packet, 7, data.Length);
            }
            int checksum = 0;
            for (int i = 0; i < packetSize; i++)
            {
                checksum += (byte)packet[i];
            }
            packet[packetSize - 1] = (byte)(checksum % (Byte.MaxValue + 1));

            return packet;
        }

        /// <summary>
        ///     Register a packet listener for a specific opcode
        /// </summary>
        /// <remarks>
        ///     The same listener can be registered for multiple opcodes, and multiple
        ///     listeners can be registered for a given opcode. When a packet is received,
        ///     listeners for the opcode will be notified in the order they were registered.
        /// </remarks>
        /// <param name="opCode">The code to listen for</param>
        /// <param name="listener">The handler</param>
        /// <param name="oneShot">
        ///     If <tt>true</tt>, the listener is deregistered immediately 
        ///     after it handles a packet
        /// </param>
        public void Listen(byte opCode, PacketListener listener, bool oneShot = false)
        {
            if (_packetDispatcher == null)
            {
                throw new InvalidOperationException("Connection isn't open");
            }
            _packetDispatcher.Register(opCode, listener, oneShot);
        }
    }
}