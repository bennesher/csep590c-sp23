namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Notification of a change of the status of the connection
    /// </summary>
    public class ConnectionEventArgs: EventArgs
    {
        /// <summary>
        ///     The new status of the connection
        /// </summary>
        public readonly ConnectionStatus Status;

        public ConnectionEventArgs(ConnectionStatus status)
        {
            Status = status;
        }
    }

    /// <summary>
    ///     A streaming datapoint
    /// </summary>
    public class StreamingDataEventArgs : EventArgs
    {
        /// <summary>
        ///     Event timestamp in device time, in milliseconds
        /// </summary>
        public readonly uint Timestamp;

        /// <summary>
        ///     Reading at specified time, in microvolts
        /// </summary>
        public readonly ushort Data;

        public StreamingDataEventArgs(uint timestamp, ushort data)
        {
            Timestamp = timestamp;
            Data = data;
        }
    }

}
