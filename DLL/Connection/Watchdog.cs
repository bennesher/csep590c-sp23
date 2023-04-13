using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLL.Connection
{
    internal class Watchdog
    {
        private readonly DeviceConnection connection;

        public Watchdog(DeviceConnection connection)
        {
            this.connection = connection;
        }

        internal void Cancel()
        {
            throw new NotImplementedException();
        }
    }
}
