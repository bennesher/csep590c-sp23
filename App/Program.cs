using DeviceInterface.Connection;

namespace App
{
    internal class Program
    {
        static void Main(string[] args)
        {
            new Program().AsyncMain().Wait();
        }

        private DeviceConnection? _connection;

        private async Task AsyncMain()
        {
            Console.WriteLine("Select Port:");
            string[] ports = DeviceConnection.AvailablePorts;
            int i = 0;
            foreach (string port in ports)
            {
                Console.WriteLine(" {0}: {1}", i++, port);
            }

            _connection = null;
            while (true)
            {
                Console.Write("Choice (0-{0}): ", ports.Length - 1);
                var key = Console.ReadKey();
                Console.WriteLine();
                if (char.IsDigit(key.KeyChar))
                {
                    int portId = key.KeyChar - '0';
                    if (portId < ports.Length)
                    {
                        _connection = await ConnectToDevice(ports[portId]);
                        if (_connection != null)
                            break;
                    }
                }
                Console.WriteLine("Sorry, invalid port");
            }

            while (true)
            {
                Console.WriteLine("\nS - Start/Stop streaming; Q - Quit. Any other key will repeat this message.");
                var key = Console.ReadKey(true);
                switch (key.KeyChar)
                {
                    case 's':
                    case 'S':
                        if (_connection.IsStreaming)
                        {
                            _connection.StopStreaming();
                        }
                        else
                        {
                            var status = _connection.StartStreaming();
                            if (status != StreamingStatus.Streaming)
                            {
                                Console.WriteLine($"\nCan't start streaming: {status}");
                            }
                        }
                        break;

                    case 'q':
                    case 'Q':
                        _connection.Close();
                        break;

                    default: break;
                }

                if (!_connection.IsConnected)
                {
                    Console.WriteLine("\nConnection is closed. Goodbye!");
                    break;
                }
            }
        }

        private async Task<DeviceConnection?> ConnectToDevice(string port)
        {
            DeviceConnection connection = new(port);
            ConnectionStatus connectResult = await connection.Open();
            if (connectResult == ConnectionStatus.Connected) {
                connection.ConnectionStatusChanged += OnConnectionStatusChanged;
                connection.StreamingData += OnStreamingData;
                return connection;
            } 
            else
            {
                Console.WriteLine($"\nConnection Failed: {connectResult}");
            }
            return null;
        }

        /// <summary>
        ///     Handler invoked when streaming data arrives
        /// </summary>
        /// <param name="sender">The connection</param>
        /// <param name="e">The data</param>
        private void OnStreamingData(object? sender, StreamingData e)
        {
            Console.Write($"\r  {e.Timestamp, 6}: {e.Data, 10:F2}uv; Seizure: {_connection.IsInSeizure,5}, Therapy: Needed? {_connection.IsTherapyNeeded,5} Active? {_connection.IsTherapyActive,5} ");
        }

        /// <summary>
        ///     Handler invoked when the connection status (ie health) changes
        /// </summary>
        /// <param name="sender">The connection</param>
        /// <param name="e">The new status</param>
        private static void OnConnectionStatusChanged(object? sender, ConnectionEventArgs e)
        {
            Console.WriteLine($"\n## Connection is {e.Status}");
        }
    }
}