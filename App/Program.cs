using DLL.Connection;
using System.IO.Ports;

namespace App
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!\n");

            Console.WriteLine("Select Port:");
            string[] ports = SerialPort.GetPortNames();
            int i = 0;
            foreach (string port in ports)
            {
                Console.WriteLine(" {0}: {1}", i++, port);
            }
            while (true)
            {
                Console.Write("Choice (0-{0}): ", ports.Length);
                var key = Console.ReadKey();
                Console.WriteLine(key.KeyChar);
                if (char.IsDigit(key.KeyChar))
                {
                    int portId = key.KeyChar - '0';
                    if (portId < ports.Length)
                    {
                        TestPort(ports[portId]);
                        return;
                    }
                }
                Console.WriteLine("Sorry, invalid port");
            }
        }

        private static void TestPort(string port)
        {
            DeviceConnection connection = new(port);
            if (connection.Open()) {
                Console.WriteLine("\nConnection established! Press any key to terminate.\n");
                Console.ReadKey();
                connection.Close();
            }
        }
    }
}