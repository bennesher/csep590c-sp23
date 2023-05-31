namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Used for reporting the current classification state
    /// </summary>
    public class SeizureStatusClassification : EventArgs
    {
        public readonly bool Classification;
        public readonly float Confidence;
        public readonly double[] SpectralPowerDensity;

        internal SeizureStatusClassification(bool classification, float confidence, double[] spectralPowerDensity)
        {
            Classification = classification;
            Confidence = confidence;
            SpectralPowerDensity = spectralPowerDensity;
        }
    }

    /// <summary>
    ///     Notification of a change of the status of the connection
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        /// <summary>
        ///     The new status of the connection
        /// </summary>
        public readonly ConnectionStatus Status;

        internal ConnectionEventArgs(ConnectionStatus status)
        {
            Status = status;
        }
    }

    /// <summary>
    ///     A streaming datapoint
    /// </summary>
    public class StreamingData : EventArgs
    {
        /// <summary>
        ///     Event timestamp in device time, in milliseconds
        /// </summary>
        public readonly uint Timestamp;

        /// <summary>
        ///     Reading at specified time, in millivolts
        /// </summary>
        public readonly double Data;

        internal StreamingData(uint timestamp, double data)
        {
            Timestamp = timestamp;
            Data = data;
        }
    }
}