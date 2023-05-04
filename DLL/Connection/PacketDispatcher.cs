using System.Collections.Concurrent;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     A received packet
    /// </summary>
    /// <param name="type">The packet type</param>
    /// <param name="id">The packet sequence id</param>
    /// <param name="data">The additional packet data, if any</param>
    internal record struct Packet(PacketType type, byte id, byte[] data)
    {
        public static implicit operator (PacketType type, byte id, byte[] data)(Packet value)
        {
            return (value.type, value.id, value.data);
        }

        public static implicit operator Packet((PacketType type, byte id, byte[] data) value)
        {
            return new Packet(value.type, value.id, value.data);
        }
    }

    /// <summary>
    ///     A <c>PacketListener</c> handles incoming packets from the device
    /// </summary>
    /// <param name="packet">The received packet</param>
    /// <returns>
    ///     <c>true</c> if the packet has been handled
    /// </returns>
    internal delegate bool PacketListener(Packet packet);

    /// <summary>
    ///     Maintains the list of registered <see cref="PacketListener"/>s, and handles dispatch
    ///     of incoming packets.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     <see cref="PacketListener"/>s are invoked only for the packet opCode specified 
    ///     when they are registered. A single listener instance may be registered for multiple
    ///     opCodes. Listeners for a given opCode are invoked in the order registered until
    ///     a listener returns <c>true</c>. If a packet is not handled by any registered
    ///     listener, a warning is logged.
    /// </para>
    /// <para>
    ///     The dispatcher maintains a worker task which handles the dispatch to the
    ///     listeners. The listeners are invoked synchronously, so any compute-intensive
    ///     or blocking work should be fire-and-forget. 
    /// </para>
    /// </remarks>
    internal class PacketDispatcher
    {
        private readonly ConcurrentDictionary<PacketType, List<RegisteredListener>> _listeners = new();
        private readonly BlockingCollection<Packet> _packets = new(new ConcurrentQueue<Packet>());
        private readonly Task _dispatchTask;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _canceled;

        internal PacketDispatcher()
        {
            _dispatchTask = Task.Run(() => Dispatcher(_cts.Token));
            Register(PacketType.Error, ErrorPacketHandler);
        }

        /// <summary>
        ///     Register a listener
        /// </summary>
        /// <param name="type">The packet type listen for</param>
        /// <param name="listener">The listener</param>
        /// <param name="oneShot">If <c>true</c>, the listener will be unregistered after handling one packet</param>
        internal void Register(PacketType type, PacketListener listener, bool oneShot = false)
        {
            if (!_listeners.ContainsKey(type))
            {
                _listeners[type] = new List<RegisteredListener>();
            }
            _listeners[type].Add(new RegisteredListener(listener, oneShot));
        }

        /// <summary>
        ///     Unregister a listener
        /// </summary>
        /// <param name="type">The type from which to unregister</param>
        /// <param name="listener">The listener to unregister</param>
        /// <exception cref="Exception">If the specified listener is not active</exception>
        internal void Unregister(PacketType type, PacketListener listener)
        {
            if (_listeners.TryGetValue(type, out var value))
            {
                for (int i = 0; i < value.Count; i++)
                {
                    RegisteredListener item = _listeners[type][i];
                    if (item.listener == listener)
                    {
                        _listeners[type].RemoveAt(i);
                        return;
                    }
                }
            }
            throw new Exception("Specified listener not found for type " + type);
        }

        /// <summary>
        ///     Enqueue a packet for handling
        /// </summary>
        /// <param name="packet"></param>
        internal void Handle(Packet packet)
        {
            _packets.Add(packet);
        }

        /// <summary>
        ///     The worker method that dispatches packets to listeners
        /// </summary>
        /// <param name="token">The cancellation token</param>
        // TODO: Determine best way to dispatch error packets
        private void Dispatcher(CancellationToken token)
        {
            main_loop:
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                try
                {
                    Packet packet = _packets.Take(token);
                    if (InvokeListeners(packet.type, packet))
                    {
                        goto main_loop;
                    }
                    Console.Error.WriteLine("Unhandled packet {0}", packet);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Exception in packet dispatcher: {0}", e.ToString());
                }
            }
        }

        /// <summary>
        ///     Sequentially invoke the listeners registered for the specified packet type
        /// </summary>
        /// <param name="type">The type to consider</param>
        /// <param name="packet">The packet received</param>
        /// <returns><c>true</c> if the packet was handled</returns>
        private bool InvokeListeners(PacketType type, Packet packet) 
        {
            if (_listeners.TryGetValue(type, out var listeners))
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    RegisteredListener listener = listeners[i];
                    try
                    {
                        if (listener.listener(packet))
                        {
                            if (listener.oneShot)
                            {
                                listeners.RemoveAt(i);
                            }
                            return true;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Exception in packet listener: {0}", e.ToString());
                    }
                }
            }
            return false;
        }

        /// <summary>
        ///     The error packet handler of last resort: try dispatching the packet
        ///     assuming it's an error reply to a command. Logs the error packet if
        ///     no handler acknowledged it.
        /// </summary>
        /// <param name="packet">The error packet</param>
        /// <returns><c>true</c> because one way or another, it's handled here.</returns>
        private bool ErrorPacketHandler(Packet packet)
        {
            if (!InvokeListeners(PacketType.Command, packet))
            {
                Console.Error.WriteLine($"Received unhandled error packet: id={packet.id}, data={packet.data}");
            }
            return true;
        }

        /// <summary>
        ///     Cancel the dispatch task and clean up.
        /// </summary>
        internal void Cancel()
        {
            if (!_canceled)
            {
                _cts.Cancel();
                _dispatchTask.Wait(TimeSpan.FromSeconds(1));

                _canceled = true;
            }
        }

        /// <summary>
        ///     A <see cref="PacketListener"/>, as registered
        /// </summary>
        /// <param name="listener">The listener</param>
        /// <param name="oneShot">If <c>true</c>, deregistered after it handles one packet</param>
        private record struct RegisteredListener(PacketListener listener, bool oneShot)
        {
            /// <summary>
            ///     Implicit unpacking operator
            /// </summary>
            /// <param name="value">The instance to unpack</param>
            public static implicit operator (PacketListener listener, bool oneShot)(RegisteredListener value)
            {
                return (value.listener, value.oneShot);
            }
        }
    }
}
