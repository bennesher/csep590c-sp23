using System.IO.Ports;

namespace DLL.Connection
{
    internal class PortListener
    {
        private readonly PacketDispatcher _dispatcher;
        private readonly SerialPort _serialPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        internal PortListener(PacketDispatcher dispatcher, SerialPort serialPort)
        {
            _dispatcher = dispatcher;
            _serialPort = serialPort;

            _task = Task.Run(() => Listen(_cts.Token));
        }

        internal void Cancel()
        {
            _cts.Cancel();
            _task.Wait(TimeSpan.FromSeconds(1));
        }

        private void Listen(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool inPacket = false;
                try
                {
                    int i = 0;
                    byte type = 0;
                    byte packetId = 0;
                    byte size = 0;
                    byte opCode = 0;
                    byte[] data = null;
                    int checksum = 0;
                    int limit = -1;      // We'll know the limit once we get the size

                    int rcvd = _serialPort.ReadByte();
                    if (rcvd == -1)
                    {
                        Console.Error.WriteLine("Unexpected End of Stream received!");
                        return;
                    }

                    switch (i)
                    {
                        case -1:
                            Console.Error.WriteLine("Unexpected byte received at position {0}: {1}", i, rcvd);
                            continue;

                        case 0:
                            if (0xAA != rcvd) goto case -1;
                            break;

                        case 1:
                            if (0x01 != rcvd) goto case -1;
                            break;

                        case 2:
                            if (0x02 != rcvd) goto case -1;
                            break;
                            
                        case 3:
                            if (rcvd > 2) goto case -1;
                            type = (byte)rcvd;
                            break;

                        case 4:
                            packetId = (byte)rcvd;
                            break;

                        case 5:
                            if (rcvd == 0) goto case -1;
                            size = (byte)rcvd;
                            limit = size + 6;
                            if (size > 1)
                            {
                                data = new byte[size-1];
                            }
                            break;

                        case 6:
                            opCode = (byte)rcvd;
                            break;

                        default:
                            if (i < limit)
                            {                                
                                data[i - 7] = (byte)rcvd;
                            }
                            else if (i > limit)
                            {
                                // This shouldn't happen - we could be ignoring the next start byte!
                                Console.Error.WriteLine("*** A read too far! ***");
                                continue;
                            }
                            else
                            {
                                var checksumCheck = (byte)(checksum % (Byte.MaxValue + 1));
                                if (rcvd == checksumCheck)
                                {
                                    // We got a full packet with a valid checksum!
                                    _dispatcher.Handle(new Packet(packetId, opCode, data));
                                }
                                else
                                {
                                    Console.Error.WriteLine("Invalid checksum! Computed: {0}, Received: {1}", checksumCheck, rcvd);
                                }
                                continue;
                            }
                            break;
                    }
                    checksum += rcvd;
                    i++;
                    inPacket = true;
                }
                catch (TimeoutException)
                {
                    if (inPacket)
                    {
                        // We got only part of a packet; that's a problem
                        Console.Error.WriteLine("Incomplete packet received before read timeout");
                    }
                    // otherwise, it could just be silence
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                }
            }
        }
    }
}
