using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dfulib;

namespace tester
{
    class Program
    {
        static void Main(string[] args)
        {
            TyteraRadio tr = new TyteraRadio(TyteraRadio.RadioModel.RM_MD380);

            tr.TickleTickle();
            UInt32 bof = tr.GetSpiID();
            Console.WriteLine("SPI ID: " + bof.ToString("X"));
            //Console.ReadKey();
            //return;

            if (args[0] == "readcp")
            {
                tr.ReadClodeplug(args[1]);
                tr.Reboot();
                Console.WriteLine("Read codeplug to " + args[1]);
            }
            else if(args[0] == "writecp")
            {
                tr.WriteCodeplug(args[1]);
                tr.Reboot();
                Console.WriteLine("Wrote codeplug to " + args[1]);
            }
            else if (args[0] == "userdb")
            {
                Console.WriteLine("Flashing user database");
                tr.WriteUserDB(args[1]);
                Console.WriteLine("Rebooting");
                tr.Reboot();
                Console.WriteLine("Wrote userDB to " + args[1]);
            }
            else if (args[0] == "flash")
            {
                Console.WriteLine("Flashing firmware");
                tr.WriteFirmware( args[1] );
                Console.WriteLine("Rebooting");
                tr.Reboot();
            }
            Console.WriteLine("Done!");
            //Console.ReadKey();
        }
    }
}
