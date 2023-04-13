using System.Timers;

namespace DLL.Connection
{
    /// <summary>
    ///     Maintain a watchdog timer to keep the session alive, and restart the
    ///     session if it's interrupted.
    /// </summary>
    internal class Watchdog
    {
        private const int FEEDING_INTERVAL = 4000;

        private readonly DeviceConnection _connection;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;
        private readonly System.Timers.Timer _timer;


        internal Watchdog(DeviceConnection connection)
        {
            _connection = connection;
            _timer = new System.Timers.Timer();
            _timer.Elapsed += Feeder; 
            _timer.Interval = FEEDING_INTERVAL;
            _timer.Start();
        }

        internal void Cancel()
        {
            _timer.Close();
        }

        /// <summary>
        ///     Keep the watchdog fed, and the connection alive
        /// </summary>
        private void Feeder(object? sender, ElapsedEventArgs elapsed)
        {
            if (_connection.Write(0x02))
            {
                Console.WriteLine($"{elapsed.SignalTime} and all is well");
            }
            else
            {
                Console.Error.WriteLine("Lost communication with device; attempting to reconnect...");
                Recover();
            }
        }

        /// <summary>
        ///     Attempt to reestablish the session
        /// </summary>
        private void Recover()
        {
            _timer.Stop();
            try
            {
                if (!_connection.TryConnection())
                {
                    Console.Error.WriteLine("!!! Unable to restore connection to the device !!!");
                    return;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
            _timer.Start();
        }
    }
}
