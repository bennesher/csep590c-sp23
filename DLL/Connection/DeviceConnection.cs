using System.Diagnostics;
using System.IO.Ports;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///    A connection to an INS (Implanted Neural System / simulation) device
    /// </summary>
    public class DeviceConnection
    {
        private const int WRITE_TIMEOUT = 500;
        private const int READ_TIMEOUT = 500;
        private const int CONNECTION_ATTEMPTS = 5;
        private const int BAD_PORT_RETRY_DELAY = 3000;

        private readonly object _writeLock = new();
        private readonly string _port;
        private Watchdog? _watchdog;
        private SerialPort? _serialPort;
        private PacketDispatcher? _packetDispatcher;
        private PortListener? _portListener;
        private StreamingHandler? _streamingHandler;
        private uint _packetCounter = 0;
        private bool _connected = false;
        private bool _streaming = false;

        /// <summary>
        ///     Provides the list of valid values for the constructor
        /// </summary>
        public static string[] AvailablePorts
        {
            get => SerialPort.GetPortNames();
        }

        /// <summary>
        ///     The file to use for logging streaming data; may be set when connection is constructed
        /// </summary>
        public readonly string LogFileName = "DeviceData.csv";

        /// <summary>
        ///     Once opened, a connection will remain connected until it is closed.
        /// </summary>
        public bool IsConnected
        {
            get => _connected;
        }

        /// <summary>
        ///     Has streaming been enabled?
        /// </summary>
        public bool IsStreaming
        {
            get => _streaming;
        }

        /// <summary>
        ///     Expose the Packet Dispatcher to related components. Only valid
        ///     while the connection is Open.
        /// </summary>
        internal PacketDispatcher PacketDispatcher
        {
            get {
                Debug.Assert(_packetDispatcher != null);
                return (PacketDispatcher)_packetDispatcher;
            }
        }

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
            _port = port;
        }

        /// <summary>
        ///     Event raised when connection health is irredeemably failed. 
        ///     This allows the listener to attempt to establish a new connection,
        ///     escalate the alert, or simply fail gracefully.
        /// </summary>
        public event EventHandler<ConnectionEventArgs>? StatusChanged;

        /// <summary>
        ///     Event fired for each data packet received from the device
        /// </summary>
        public event EventHandler<StreamingDataEventArgs>? StreamingData;

        /// <summary>
        ///     Opens the port and establishes a communication sesion
        /// </summary>
        public async Task<ConnectionStatus> Open()
        {
            // Don't reconnect if we're already connected
            if (_connected)
            {
                return ConnectionStatus.AlreadyConnected;
            }

            // The Packet Dispatcher lives from Open to Close
            _packetDispatcher = new PacketDispatcher();

            // Set up the port
            if (!PortSetup())
            {
                return ConnectionStatus.NoDevice;
            }
            Debug.Assert(_serialPort != null);

            // Prepare to listen for incoming data
            _portListener = new(_packetDispatcher, _serialPort);

            // Open the connection
            DeviceErrorCode? result = await TryConnection(CancellationToken.None);
            if (result == null)
            {
                // Now that we have our connection, keep it alive
                _watchdog = new Watchdog(this);
                _connected = true;
                return ConnectionStatus.Connected;
            } else
            {
                // Clean up failed connection
                PortCleanup();
                _packetDispatcher.Cancel();
                _packetDispatcher = null;
            }

            return result == DeviceErrorCode.ERR_ALREADY_CONNECTED ? ConnectionStatus.AlreadyConnected : ConnectionStatus.Failed;
        }

        private void PortCleanup()
        {
            _portListener?.Cancel();
            _portListener = null;
            _serialPort?.Close();
            _serialPort = null;
        }

        /// <summary>
        ///     Set up the underlying serial port
        /// </summary>
        /// <returns><c>true</c> if successful</returns>
        private bool PortSetup()
        {
            try
            {
                _serialPort = new SerialPort(_port, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = READ_TIMEOUT,
                    WriteTimeout = WRITE_TIMEOUT
                };
                _serialPort.Open();
                return true;
            } catch
            {
                return false;
            }
        }

        /// <summary>
        ///     Initiates streaming of device data
        /// </summary>
        /// <returns>
        ///     <see cref="StreamingStatus"/> indicating result of operation. 
        ///     A value of <see cref="StreamingStatus.Streaming"/> indicates success;
        ///     other values are errors.
        /// </returns>
        public StreamingStatus StartStreaming()
        {
            if (!_connected)
            {
                return StreamingStatus.ConnectionNotOpen;
            }

            if (_streaming)
            {
                return StreamingStatus.AlreadyStreaming;
            }

            _streamingHandler = new(this);
            _streaming = true;
            return StreamingStatus.Streaming;
        }

        /// <summary>
        ///     Terminates streaming of device data
        /// </summary>
        public void StopStreaming()
        {
            if (_streaming)
            {
                _streaming = false;
                _streamingHandler?.Cancel();
                _streamingHandler = null;
            }
        }

        /// <summary>
        ///     Attempt to establish a session with the device, retrying up
        ///     to <see cref="CONNECTION_ATTEMPTS"/> before giving up.
        /// </summary>
        /// <returns><c>null</c> if session has been established</returns>
        internal async Task<DeviceErrorCode?> TryConnection(CancellationToken cancellationToken)
        {
            DeviceErrorCode? result = null;
            int attempts = 0;
            while (attempts++ < CONNECTION_ATTEMPTS && !cancellationToken.IsCancellationRequested)
            {
                result = TryOpenConnection();
                if (result == DeviceErrorCode.ERR_TIMEOUT_EXPIRED || result == DeviceErrorCode.ERR_COM_FAILED) 
                {
                    await Task.Delay(WRITE_TIMEOUT, (CancellationToken)cancellationToken);
                }
                else
                {
                    Debug.Assert(result != DeviceErrorCode.ERR_NOT_OPEN);
                    // Status does not warrant retry - success or other error
                    break;
                }
            }

            return cancellationToken.IsCancellationRequested ? DeviceErrorCode.ERR_CANCELLED : result;
        }

        /// <summary>
        ///     Attempt to establish a session with the device
        /// </summary>
        /// <returns><c>null</c> if device acknowledged the new session</returns>
        private DeviceErrorCode? TryOpenConnection() {
            DeviceErrorCode? result = SendCommand(OpCode.InitialConnection);
            if (result == null || result == DeviceErrorCode.ERR_ALREADY_CONNECTED)
            {
                Debug.WriteLine(result == null ? "Connection Successful" : "Already Connected");
                RaiseConnectionEvent(ConnectionStatus.Connected);
                return null;
            }
            else
            {
                Console.Error.WriteLine("Failed to open connection to device [errorCode={0}]", result);
                return result;
            }
        }

        /// <summary>
        ///     Restore an interrupted connection; keep trying until cancelled.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation</param>
        internal async Task RestoreConnection(CancellationToken token)
        {
            // Let client know that connection has been lost
            RaiseConnectionEvent(ConnectionStatus.Disconnected);

            bool reconnected = false;
            while (!reconnected && !token.IsCancellationRequested)
            {
                DeviceErrorCode? errorCode = await TryConnection(token);
                if (errorCode == null)
                {
                    reconnected = true;
                }
                else
                {
                    // Well that didn't work. Try a more drastic restart.
                    PortCleanup();
                    if (!PortSetup())
                    {
                        RaiseConnectionEvent(ConnectionStatus.NoDevice);
                        await Task.Delay(BAD_PORT_RETRY_DELAY, token);
                    }
                    else
                    {
                        // OK, at least the port is good. Prepare to try again.
                        Debug.Assert(_packetDispatcher != null && _serialPort != null);
                        _portListener = new(_packetDispatcher, _serialPort);
                    }
                }
            }
        }

        /// <summary>
        ///     Asynchronosly notify listeners of a change of connection status
        /// </summary>
        /// <param name="status">The new status</param>
        private void RaiseConnectionEvent(ConnectionStatus status)
        {
            Task.Run(() => StatusChanged?.Invoke(this, new ConnectionEventArgs(status)));
        }

        /// <summary>
        ///     Asynchronosly notify listeners of incoming data
        /// </summary>
        /// <param name="args">The received data</param>
        internal void RaiseStreamingEvent(StreamingDataEventArgs args)
        {
            Task.Run(() => StreamingData?.Invoke(this, args));
        }

        /// <summary>
        ///     Terminates the session, and releases the port and supporting resources
        /// </summary>
        public void Close()
        {
            if (_connected)
            {
                StopStreaming();
                _watchdog?.Cancel();
                _watchdog = null;
                _packetDispatcher?.Cancel();
                _packetDispatcher = null;
                PortCleanup();
                _connected = false;
            }
        }

        /// <summary>
        ///     Synchronously write a command to the device, and await acknowledgement
        /// </summary>
        /// <param name="opCode">The code to send</param>
        /// <param name="data">Additional data for the command</param>
        /// <returns>
        ///     <c>null</c> if the operation was acknowledged within the timeout, otherwise
        ///     an instance of <see cref="DeviceErrorCode"/>
        /// </returns>
        internal DeviceErrorCode? SendCommand(OpCode opCode, byte[]? data = null)
        {
            if (_serialPort == null || _packetDispatcher == null)
            {
                return DeviceErrorCode.ERR_NOT_CONNECTED;
            }

            // Prepare the packet
            byte packetId = (byte)(Interlocked.Increment(ref _packetCounter) % (Byte.MaxValue + 1));
            byte[] packet = BuildPacket(PacketType.Command, packetId, opCode, data);

            // Listen for the acknowledgement
            ManualResetEvent acknowledged = new(false);
            bool confirmed = false;
            DeviceErrorCode? errorCode = null;

            bool listener(Packet packet)
            {
                if (packet.id == packetId)
                {
                    if (packet.type == PacketType.Error)
                    {
                        errorCode = ((DeviceErrorCode)packet.data[0]);
                        Console.Error.WriteLine("Received error response {0} for command {1}", errorCode.ToString(), opCode.ToString());
                    } else
                    {
                        confirmed = true;
                    }
                    acknowledged.Set();
                    return true;
                }
                return false;
            }
            _packetDispatcher.Register(PacketType.Command, listener, true);

            // Send the packet
            var result = false;
            lock (_writeLock)
            {
                try
                {
                    _serialPort.Write(packet, 0, packet.Length);
                    result = true;
                }
                catch (Exception e) { 
                    Console.Error.WriteLine("Unexpected exception writing to device: {0}", e.ToString());
                    errorCode = DeviceErrorCode.ERR_COM_FAILED;
                }
            }

            // Wait for a reply 
            if (result)
            {
                result = acknowledged.WaitOne(WRITE_TIMEOUT);
                if (!result)
                {
                    // Timeout waiting for reply
                    errorCode = DeviceErrorCode.ERR_TIMEOUT_EXPIRED;
                }
            }

            if (!result)
            {
                Debug.Assert(errorCode != null);

                // Clean up if no reply received
                if (_connected)
                {
                    _packetDispatcher.Unregister(PacketType.Command, listener);
                }
            }

            return result && confirmed ? null : errorCode;
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
        private static byte[] BuildPacket(PacketType type, byte packetId, OpCode opCode, byte[]? data)
        {
            int payloadSize = 1 + (data?.Length ?? 0);
            int packetSize = 7 + payloadSize;
            byte[] packet = new byte[packetSize];
            packet[0] = Constants.PACKET_PREFIX[0];
            packet[1] = Constants.PACKET_PREFIX[1];
            packet[2] = Constants.PACKET_PREFIX[2];
            packet[3] = (byte)type;
            packet[4] = packetId;
            packet[5] = (byte)payloadSize;
            packet[6] = (byte)opCode;
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
    }
}