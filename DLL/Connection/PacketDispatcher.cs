using System.Collections.Concurrent;

namespace DLL.Connection
{
    /// <summary>
    ///     A <c>PacketListener</c> handles incoming packets from the device
    /// </summary>
    /// <param name="id">The sequence id of the packet</param>
    /// <param name="opCode">The command code from the device</param>
    /// <param name="data">Additional data in the packet, if any</param>
    /// <returns>
    ///     <c>true</c> if the packet has been handled
    /// </returns>
    public delegate bool PacketListener(byte id, byte opCode, byte[]? data);

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
    internal class PacketDispatcher: IDisposable
    {
        private readonly ConcurrentDictionary<byte, List<RegisteredListener>> _listeners = new();
        private readonly BlockingCollection<Packet> _packets = new(new ConcurrentQueue<Packet>());
        private readonly Task _dispatchTask;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool disposedValue;

        internal PacketDispatcher()
        {
            _dispatchTask = Task.Run(() => Dispatcher(_cts.Token));
        }

        /// <summary>
        ///     Register a listener
        /// </summary>
        /// <param name="opCode">The opCode to listen for</param>
        /// <param name="listener">The listener</param>
        /// <param name="oneShot">If <c>true</c>, the listener will be unregistered after handling one packet</param>
        internal void Register(byte opCode, PacketListener listener, bool oneShot = false)
        {
            if (!_listeners.ContainsKey(opCode))
            {
                _listeners[opCode] = new List<RegisteredListener>();
            }
            _listeners[opCode].Add(new RegisteredListener(listener, oneShot));
        }

        /// <summary>
        ///     Unregister a listener
        /// </summary>
        /// <param name="opCode">The opCode from which to unregister</param>
        /// <param name="listener">The listener to unregister</param>
        /// <exception cref="Exception">If the specified listener is not active</exception>
        internal void Unregister(byte opCode, PacketListener listener)
        {
            if (_listeners.ContainsKey(opCode))
            {
                for (int i = 0; i < _listeners[opCode].Count; i++)
                {
                    RegisteredListener item = _listeners[opCode][i];
                    if (item.listener == listener)
                    {
                        _listeners[opCode].RemoveAt(i);
                        return;
                    }
                }
            }
            throw new Exception("Listener not found for opcode " + opCode);
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
        private void Dispatcher(CancellationToken token)
        {
            main_loop:
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                Packet packet = _packets.Take(token);
                List<RegisteredListener> listeners = _listeners[packet.opCode];
                if (listeners != null)
                {
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        RegisteredListener listener = listeners[i];
                        if (listener.listener(packet.id, packet.opCode, packet.data)) 
                        {
                            if (listener.oneShot)
                            {
                                listeners.RemoveAt(i);
                            }
                            goto main_loop;
                        }
                    }
                }
                Console.Error.WriteLine("Unhandled packet {0}", packet);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                _cts.Cancel();
                _dispatchTask.Wait(TimeSpan.FromSeconds(1));

                if (disposing)
                {
                    _dispatchTask.Dispose();
                }

                disposedValue = true;
            }
        }

        ~PacketDispatcher()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        void IDisposable.Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
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

    /// <summary>
    ///     A received packet
    /// </summary>
    /// <param name="id">The packet sequence id</param>
    /// <param name="opCode">The packet opCode</param>
    /// <param name="data">The additional packet data, if any</param>
    internal record struct Packet(byte id, byte opCode, byte[]? data)
    {
        public static implicit operator (byte id, byte opCode, byte[]? data)(Packet value)
        {
            return (value.id, value.opCode, value.data);
        }

        public static implicit operator Packet((byte id, byte opCode, byte[]? data) value)
        {
            return new Packet(value.id, value.opCode, value.data);
        }
    }
}
