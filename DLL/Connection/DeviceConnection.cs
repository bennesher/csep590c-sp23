using System.IO.Ports;

namespace DLL.Connection
{
    /// <summary>
    /// A connection to an INS (Implanted Neural System / simulation) device
    /// </summary>
    public class DeviceConnection
    {
        private readonly string port;
        private Watchdog? _watchdog;
        private SerialPort? serialPort;
        private PacketDispatcher packetDispatcher = new PacketDispatcher();

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
            this.port = port;
        }

        /// <summary>
        ///     Opens the port and establishes a communication sesion
        /// </summary>
        public void Open()
        {

        }

        /// <summary>
        ///     Terminates the session, and releases the port
        /// </summary>
        public void Close()
        {

        }

        /// <summary>
        ///     Synchronously write a command to the device, and await acknowledgement
        /// </summary>
        /// <param name="opCode">The code to send</param>
        /// <param name="data">Additional data for the command</param>
        public Task Write(byte opCode, byte[]? data)
        {

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
        ///     prior to being invoked
        /// </param>
        public void Listen(byte opCode, PacketListener listener, bool oneShot = false)
        {
            packetDispatcher.Register(opCode, listener, oneShot);
        }
    }
}