using System.Diagnostics;
using System.Timers;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Maintain a watchdog timer to keep the session alive, and restart the
    ///     session if it's interrupted.
    /// </summary>
    internal class Watchdog
    {
        private const int FEEDING_INTERVAL = 4000;
        private const int WATCHDOG_ATTEMPTS = 5;

        private readonly DeviceConnection _connection;
        private readonly CancellationTokenSource _cts = new();
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
            try { _cts.Cancel(); }
            catch (Exception ex) { 
                Console.Error.WriteLine(ex.ToString());
            }
            _timer.Stop();
            _timer.Close();
        }

        /// <summary>
        ///     Keep the watchdog fed, and the connection alive
        /// </summary>
        private async void Feeder(object? sender, ElapsedEventArgs elapsed)
        {
            bool success = false;
            int attempts = 0;
            while (!success && attempts++ < WATCHDOG_ATTEMPTS)
            {
                DeviceErrorCode? result = _connection.SendCommand(OpCode.WatchdogReset);
                if (result == null)
                {
                    Debug.WriteLine($"{elapsed.SignalTime} and all is well");
                    success = true;
                }
                else if (result == DeviceErrorCode.ERR_NOT_CONNECTED || result == DeviceErrorCode.ERR_NOT_OPEN)
                {
                    Console.Error.WriteLine("Can't feed if it's not connected!");
                }
            }
            
            if (!success) { 
                Console.Error.WriteLine("Lost communication with device; attempting to reconnect...");
                await Recover();
            }
        }

        /// <summary>
        ///     Attempt to reestablish the session
        /// </summary>
        private async Task Recover()
        {
            bool restartOk = true;
            _timer.Stop();
            try
            {
                await _connection.RestoreConnection(_cts.Token);
                restartOk = _cts.TryReset();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
            if (restartOk)
            {
                // Restart if we weren't cancelled
                _timer.Start();
            }
        }
    }
}
