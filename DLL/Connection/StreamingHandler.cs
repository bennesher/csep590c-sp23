using System.Diagnostics;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Handles all streaming related device communication, including
    ///     dispatching events, logging stream data, and ensuring the stream 
    ///     remains active after reconnects. An instance of this class handles
    ///     a logical streaming session, from explicit stream initiation to
    ///     explicit stream termination.
    /// </summary>
    internal class StreamingHandler
    {
        private const int RETRY_DELAY = 500;
        private const int CANCEL_RETRY_LIMIT = 3;

        private readonly DeviceConnection _connection;
        private readonly CancellationTokenSource _cts = new();
        private bool _initStreamInProgress = false;

        /// <summary>
        ///     Initialize this handler, which synchronously initializes the session.
        /// </summary>
        /// <param name="connection">The connection this handler is supporting</param>
        internal StreamingHandler(DeviceConnection connection)
        {
            _connection = connection;

            // Register packet listener
            connection.PacketDispatcher.Register(PacketType.StreamData, PacketHandler);

            // Register connection event listener
            connection.StatusChanged += OnConnectionStatusChange;

            // Initialize the stream
            InitStream().Wait();
        }

        /// <summary>
        ///     Communicate with the device to initialize the stream. This operation
        ///     continues until the stream is successfully started or this handler is
        ///     cancelled.
        /// </summary>
        /// <returns>A <see cref="Task"/> for synchronization</returns>
        private async Task InitStream()
        {
            bool success = false;
            _initStreamInProgress = true;
            CancellationToken cancellationToken = _cts.Token;

            while (!success && !cancellationToken.IsCancellationRequested)
            {
                DeviceErrorCode? cmdResult = _connection.SendCommand(OpCode.StartStreaming);
                if (cmdResult == null || cmdResult == DeviceErrorCode.ERR_ALREADY_STREAMING)
                {
                    // Either this request was successfully acknowledged, or the device is already
                    // in streaming mode. We're good!
                    success = true;
                    Debug.WriteLine("Streaming started!");
                }
                else
                {
                    // Something went wrong, so wait a bit & try again
                    await Task.Delay(RETRY_DELAY, cancellationToken);
                }
            }

            if (success)
            {
                _cts.TryReset();
            }
            _initStreamInProgress = false;
        }

        /// <summary>
        ///     Cancel this streaming session. Once a session has been terminated, it
        ///     cannot be restarted.
        /// </summary>
        internal void Cancel()
        {
            // Abort any reconnection attempt that may be in progress
            if (_initStreamInProgress)
            {
                try { _cts.Cancel(); } 
                catch (Exception ex) {
                    Console.Error.WriteLine(ex);
                }
            }

            // Attempt to cancel stream at device
            int retryCount = 0;
            while (retryCount++ < CANCEL_RETRY_LIMIT)
            {
                DeviceErrorCode? cmdResult = _connection.SendCommand(OpCode.StopStreaming);
                if (cmdResult != DeviceErrorCode.ERR_BAD_CHECKSUM) {
                    break;
                }
            }

            Debug.WriteLine("Streaming Cancelled.");

            // Deregister listeners
            _connection.PacketDispatcher.Unregister(PacketType.StreamData, PacketHandler);
            _connection.StatusChanged -= OnConnectionStatusChange;
        }

        /// <summary>
        ///     Handle incoming streaming data packets
        /// </summary>
        /// <param name="packet">The received packet</param>
        /// <returns><c>true</c> because the packet will always be handled</returns>
        private bool PacketHandler(Packet packet)
        {
            // Make sure it's actually a data packet
            if (packet.type == PacketType.StreamData)
            {
                // Decode the packet
                using MemoryStream dataStream = new(packet.data);
                using BinaryReader reader = new(dataStream);

                uint timestamp = reader.ReadUInt32();
                ushort reading = reader.ReadUInt16();

                StreamingDataEventArgs evt = new(timestamp, reading);

                // Fire the event
                _connection.RaiseStreamingEvent(evt);
            }

            // Regardless, we handled it
            return true;
        }

        /// <summary>
        ///     Handle changes of connection status
        /// </summary>
        /// <param name="sender">The connection; ignored</param>
        /// <param name="evtArgs">Information about the change</param>
        private void OnConnectionStatusChange(object? sender, ConnectionEventArgs evtArgs)
        {
            switch (evtArgs.Status)
            {
                // We've (re-)connected - (re-)start streaming
                case ConnectionStatus.Connected:
                    if (!_initStreamInProgress)
                    {
                        // Let the reconnection proceed asynchronously
                        _ = InitStream();
                    }
                    break;

                // Connection has been lost. Just a logging opportunity.
                case ConnectionStatus.Disconnected:
                    Debug.WriteLine("Device disconnected; streaming will resume when connection is restored");
                    break;
            }
        }
    }
}
