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
            var key = Console.ReadKey();
            if (char.IsDigit(key.KeyChar))
            {
                int portId = key.KeyChar - '0';
                if (portId >= ports.Length) {
                    Console.WriteLine("Sorry, invalid port");
                }
                else
                {
                    TestPort(ports[portId]);
                }
            }
        }

        private static void TestPort(string port)
        {
            DeviceConnection connection = new(port);
            if (connection.Open()) {
                Console.WriteLine("\nConnection established! Press any key to terminate.\n");
                Console.ReadKey();
            }
        }
    }
}