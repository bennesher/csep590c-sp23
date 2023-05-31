using System.Diagnostics;
using System.IO.Ports;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     A continuously running task which handles incoming bytes
    ///     from the device, and passes the packets on to be dispatched.
    /// </summary>
    internal class PortListener
    {
        private readonly PacketDispatcher _dispatcher;
        private readonly SerialPort _serialPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        private static readonly int FIXED_SIZE = 6;

        /// <summary>
        ///     Initialize the listener, and start the listening thread
        /// </summary>
        /// <param name="dispatcher"></param>
        /// <param name="serialPort"></param>
        internal PortListener(PacketDispatcher dispatcher, SerialPort serialPort)
        {
            _dispatcher = dispatcher;
            _serialPort = serialPort;

            _task = Listen(_cts.Token);
        }

        /// <summary>
        ///     Terminate the listening thread
        /// </summary>
        internal void Cancel()
        {
            _cts.Cancel();
            if (_task.Wait(TimeSpan.FromSeconds(1)))
            {
                _task.Dispose();
            }
            else
            {
                Debug.Print("Failed to interrupt listener thread");
                _task.ContinueWith(task => Debug.Print("Listener finally quit"));
            }
        }

        /// <summary>
        ///     The main listener task
        /// </summary>
        /// <param name="cancellationToken">Allows for graceful shutdown</param>
        private async Task Listen(CancellationToken cancellationToken)
        {
            start_over:
            while (!cancellationToken.IsCancellationRequested)
            {
                bool inPacket = false;
                try
                {
                    int i = 0;
                    byte[] buffer = new byte[1];
                    PacketType type = 0;
                    byte packetId = 0;
                    byte size = 0;
                    byte[]? data = null;
                    int checksum = 0;
                    int limit = -1;      // We'll know the limit once we get the size

                    while (!cancellationToken.IsCancellationRequested)
                    {

                        var rcvd = await _serialPort.BaseStream.ReadAsync(buffer, 0, 1, cancellationToken);
                        if (rcvd == 0)
                        {
                            Console.Error.WriteLine("Unexpected End of Stream received!");
                            return;
                        }

                        // Handle the received byte according to its place in the packet
                        var theByte = buffer[0];

                        switch (i)
                        {
                            case -1:
                                if (Constants.PACKET_PREFIX[0] != theByte)
                                {
                                    if (theByte != 0)
                                        Console.Error.WriteLine("Unexpected byte received at position {0}: {1}", i, theByte);
                                    goto start_over;
                                }
                                // If the unexpected byte could be a start byte, treat it as such
                                i = 0;
                                break;

                            case 0:
                                if (Constants.PACKET_PREFIX[0] != theByte) goto case -1;
                                break;

                            case 1:
                                if (Constants.PACKET_PREFIX[1] != theByte) goto case -1;
                                break;

                            case 2:
                                if (Constants.PACKET_PREFIX[2] != theByte) goto case -1;
                                break;

                            case 3:
                                if (theByte > 2) goto case -1;
                                type = (PacketType)theByte;
                                break;

                            case 4:
                                packetId = theByte;
                                break;

                            case 5:
                                if (theByte == 0) goto case -1;
                                size = (byte)theByte;
                                limit = size + FIXED_SIZE;
                                data = new byte[size];
                                break;

                            default:
                                Debug.Assert(data != null);
                                if (i < limit)
                                {
                                    // Additional payload bytes
                                    data[i - FIXED_SIZE] = (byte)theByte;
                                }
                                else if (i > limit)
                                {
                                    // This shouldn't happen - we could be ignoring the next start byte!
                                    Console.Error.WriteLine("*** A read too far! ***");
                                    goto start_over;
                                }
                                else
                                {
                                    // We got the checksum byte; does it match?
                                    var checksumCheck = (byte)(checksum % (Byte.MaxValue + 1));
                                    if (theByte == checksumCheck)
                                    {
                                        // We got a full packet with a valid checksum!
                                        _dispatcher.Handle(new Packet(type, packetId, data));
                                    }
                                    else
                                    {
                                        Console.Error.WriteLine("Invalid checksum! Computed: {0}, Received: {1}", checksumCheck, theByte);
                                    }
                                    goto start_over;
                                }
                                break;
                        }
                        checksum += theByte;
                        i += 1;
                        inPacket = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is TimeoutException || ex.Message is not null && ex.Message.Contains("timeout"))
                    {
                        if (inPacket)
                        {
                            // We got only part of a packet; that's a problem
                            Console.Error.WriteLine("Incomplete packet received before read timeout");
                        }
                        // otherwise, it could just be silence
                    } else
                    {
                        Console.Error.WriteLine(ex.ToString());
                    }
                }
            }
        }
    }
}
