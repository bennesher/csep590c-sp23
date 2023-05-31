using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Storage;

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

        private const double DYNAMIC_RANGE = 3932.0;
        private const double X_MIN = -1885.0032958984373;

        private readonly DeviceConnection _connection;
        private readonly CancellationTokenSource _ctsReset = new();
        private readonly CancellationTokenSource _ctsLog = new();
        private bool _initStreamInProgress = false;
        private readonly BlockingCollection<StreamingData> _datapoints = new(new ConcurrentQueue<StreamingData>());

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
            connection.ConnectionStatusChanged += OnConnectionStatusChange;

            // Set up the log file
            _ = Task.Run(async () => await LogFileWriter(connection.LogFileName));

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
            CancellationToken cancellationToken = _ctsReset.Token;

            while (!success && !cancellationToken.IsCancellationRequested)
            {
                DeviceErrorCode? cmdResult = _connection.SendCommand(OpCode.StartStreaming);
                if (cmdResult is null or DeviceErrorCode.ERR_ALREADY_STREAMING)
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
                _ctsReset.TryReset();
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
                try { _ctsReset.Cancel(); } 
                catch (Exception ex) {
                    Console.Error.WriteLine(ex);
                }
            }

            // Attempt to cancel stream at device
            int retryCount = 0;
            while (retryCount++ < CANCEL_RETRY_LIMIT)
            {
                var cmdResult = _connection.SendCommand(OpCode.StopStreaming);
                if (cmdResult != DeviceErrorCode.ERR_BAD_CHECKSUM && cmdResult != DeviceErrorCode.ERR_TIMEOUT_EXPIRED) {
                    break;
                }
            }

            Debug.WriteLine("Streaming Cancelled.");

            // Deregister listeners
            _connection.PacketDispatcher.Unregister(PacketType.StreamData, PacketHandler);
            _connection.ConnectionStatusChanged -= OnConnectionStatusChange;

            // And cancel the log writer
            try { _ctsLog.Cancel(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        /// <summary>
        ///     Handle incoming streaming data packets
        /// </summary>
        /// <param name="packet">The received packet</param>
        /// <returns><c>true</c> because the packet will always be handled</returns>
        private bool PacketHandler(Packet packet)
        {
            // Make sure it's actually a data packet (we shouldn't be getting called if it isn't)
            if (packet.type != PacketType.StreamData) return true;

            // Decode the packet
            using MemoryStream dataStream = new(packet.data);
            using BinaryReader reader = new(dataStream);

            var timestamp = reader.ReadUInt32();
            var reading = reader.ReadUInt16();
            var mv = reading / 65536.0 * DYNAMIC_RANGE + X_MIN;

            StreamingData evt = new(timestamp, mv);

            // Fire the event
            _connection.RaiseStreamingEvent(evt);

            // And log the packet
            _datapoints.Add(evt);

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
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
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

        /// <summary>
        ///     Writes streaming datapoints to the log file
        /// </summary>
        /// <param name="fileName">The base filename to use</param>
        private async Task LogFileWriter(string fileName)
        {
            var cToken = _ctsLog.Token;
            try
            {
                var logFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                Console.WriteLine($"Logging to {logFile.Path}");
                await using StreamWriter logStream = new(logFile.Path);
                await logStream.WriteLineAsync("'Timestamp','Value','InSeizure','TherapyState'");
                while (!cToken.IsCancellationRequested)
                {
                    try
                    {
                        var data = _datapoints.Take(cToken);
                        await logStream.WriteLineAsync($"{data.Timestamp},{data.Data},{_connection.IsInSeizure},{_connection.IsTherapyNeeded}");
                    }
                    catch (OperationCanceledException) 
                    {
                        break;
                    }
                    catch (Exception e) 
                    {
                        Console.Error.WriteLine($"Error writing to log: {e}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Unable to open log file: {e}");
            }
        }

    }
}
