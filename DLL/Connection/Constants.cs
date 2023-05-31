namespace DeviceInterface.Connection
{
    /// <summary>
    ///     The type of the packet, which identifies the structure of the payload
    /// </summary>
    internal enum PacketType: byte
    {
        Error = 0,
        Command = 1,
        StreamData = 2
    }

    /// <summary>
    ///     The command code, for a <see cref="PacketType.Command" /> packet
    /// </summary>
    internal enum OpCode: byte
    {
        InitialConnection = 0x01,
        WatchdogReset     = 0x02,
        StartStreaming    = 0x03,
        StopStreaming     = 0x04,
        StartTherapy      = 0x05,
        StopTherapy       = 0x06
    }

    /// <summary>
    ///     Possible error codes from device
    /// </summary>
    internal enum DeviceErrorCode: byte
    {
        ERR_BAD_CHECKSUM = 0,
        ERR_PAYLOAD_LENGTH_EXCEEDS_MAX = 1,
        ERR_BAD_PACKET_TYPE = 2,
        ERR_BAD_OPCODE = 3,
        ERR_ALREADY_CONNECTED = 4,
        ERR_ALREADY_STREAMING = 5,
        ERR_ALREADY_STOP_STREAMING = 6,
        ERR_NOT_CONNECTED = 7,
        ERR_ALREADY_DOING_THERAPY = 8,
        ERR_ALREADY_STOP_THERAPY = 9,
        ERR_CANCELLED = 252,
        ERR_NOT_OPEN = 253,
        ERR_TIMEOUT_EXPIRED = 254,
        ERR_COM_FAILED = 255
    }

    /// <summary>
    ///     Constants that can't be declared at namespace scope
    /// </summary>
    internal class Constants
    {
        /// <summary>
        ///     Signature packet prefix for both outgoing and incoming packets
        /// </summary>
        internal static readonly byte[] PACKET_PREFIX = { 0xAA, 0x01, 0x02 };
    }

    /// <summary>
    ///     Possible values returned by the <see cref="DeviceConnection.ConnectionStatusChanged"/> event,
    ///     as well as the <see cref="DeviceConnection.Open"/> method.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        ///    Connection has not been opened, or was previously explicitly closed. 
        /// </summary>
        Unopened,

        /// <summary>
        ///     Connection is alive and well
        /// </summary>
        Connected,

        /// <summary>
        ///     Cannot establish a connection because the connection is already open
        /// </summary>
        AlreadyConnected,

        /// <summary>
        ///     Cannot establish a connection because the port is invalid or has nothing attached
        /// </summary>
        NoDevice,

        /// <summary>
        ///     Device connection has been lost; attempting to reconnect
        /// </summary>
        Disconnected,

        /// <summary>
        ///     Connection has been successfully closed. 
        /// </summary>
        Closed,

        /// <summary>
        ///     Unable to open a connection in the first place for unknown reason
        /// </summary>
        Failed
    }

    /// <summary>
    ///     Status response for <see cref="MyDevice.Connection.DeviceConnection.StartStreaming"/>
    ///     and <see cref="MyDevice.Connection.DeviceConnection.StopStreaming"/>
    /// </summary>
    public enum StreamingStatus
    {
        /// <summary>
        ///     There is no active streaming, or streaming was just cancelled
        /// </summary>
        NotStreaming,

        /// <summary>
        ///     Streaming is active
        /// </summary>
        Streaming,

        /// <summary>
        ///     Streaming cannot be started because it is already active
        /// </summary>
        AlreadyStreaming,

        /// <summary>
        ///     Streaming cannot be started or stopped because the connection is not open
        /// </summary>
        ConnectionNotOpen
    }
}
