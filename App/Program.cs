using DeviceInterface.Connection;

namespace App
{
    internal class Program
    {
        static void Main(string[] args)
        {
            AsyncMain().Wait();
        }

        private static async Task AsyncMain()
        {
            Console.WriteLine("Select Port:");
            string[] ports = DeviceConnection.AvailablePorts;
            int i = 0;
            foreach (string port in ports)
            {
                Console.WriteLine(" {0}: {1}", i++, port);
            }

            DeviceConnection? connection = null;
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
                        connection = await ConnectToDevice(ports[portId]);
                        if (connection != null)
                            break;
                    }
                }
                Console.WriteLine("Sorry, invalid port");
            }

            while (true)
            {
                Console.WriteLine("S - Start/Stop streaming; Q - Quit. Any other key will repeat this message.");
                var key = Console.ReadKey(true);
                switch (key.KeyChar)
                {
                    case 's':
                    case 'S':
                        if (connection.IsStreaming)
                        {
                            connection.StopStreaming();
                        }
                        else
                        {
                            var status = connection.StartStreaming();
                            if (status != StreamingStatus.Streaming)
                            {
                                Console.WriteLine($"Can't start streaming: {status}");
                            }
                        }
                        break;

                    case 'q':
                    case 'Q':
                        connection.Close();
                        break;

                    default: break;
                }

                if (!connection.IsConnected)
                {
                    Console.WriteLine("Connection is closed. Goodbye!");
                    break;
                }
            }
        }

        private static async Task<DeviceConnection?> ConnectToDevice(string port)
        {
            DeviceConnection connection = new(port);
            ConnectionStatus connectResult = await connection.Open();
            if (connectResult == ConnectionStatus.Connected) {
                connection.StatusChanged += OnConnectionStatusChanged;
                connection.StreamingData += OnStreamingData;
                return connection;
            } 
            else
            {
                Console.WriteLine($"Connection Failed: {connectResult}");
            }
            return null;
        }

        /// <summary>
        ///     Handler invoked when streaming data arrives
        /// </summary>
        /// <param name="sender">The connection</param>
        /// <param name="e">The data</param>
        private static void OnStreamingData(object? sender, StreamingDataEventArgs e)
        {
            Console.WriteLine($"- {e.Timestamp}: {e.Data}");
        }

        /// <summary>
        ///     Handler invoked when the connection status (ie health) changes
        /// </summary>
        /// <param name="sender">The connection</param>
        /// <param name="e">The new status</param>
        private static void OnConnectionStatusChanged(object? sender, ConnectionEventArgs e)
        {
            Console.WriteLine($"## Connection is {e.Status}");
        }
    }
}