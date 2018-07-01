using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dfulib;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace tester
{
    class Program
    {


        static void Main(string[] args)
        {
            TyteraRadio tr = new TyteraRadio(TyteraRadio.RadioModel.RM_MD380);

             tr.TickleTickle();
            //if (!tr.checkSpiID())
            {
             //   Console.WriteLine("USB Connection Failed. Reboot radio.");
            //    Console.ReadKey();
            //    return;
            }
            //Console.ReadKey();
            //return;

           /* Console.WriteLine("Dumping frame buffers for screen shot");
            //byte[] shot = tr.ScreenShotBMP();
            //File.WriteAllBytes("screenshot.bmp", shot);
            int width2 = 160;
            int height2 = 128;

            byte[] BMPDATA = null;
            while (BMPDATA == null) { BMPDATA = tr.ScreenShotBMP(); }
            //File.WriteAllBytes("bmpdata.bin", BMPDATA);
            if (BMPDATA != null)
            {

                Bitmap bmp = new Bitmap(width2, height2, PixelFormat.Format24bppRgb);

                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width2, height2), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                IntPtr ptr = bmpData.Scan0;
                Marshal.Copy(BMPDATA, 0, ptr, height2 * width2 * 3);
                bmp.UnlockBits(bmpData);
                bmp.Save("screenshot.bmp", System.Drawing.Imaging.ImageFormat.Bmp);

                Console.WriteLine("Done.");
                Console.ReadKey();
            }
            return;*/

            if (args[0] == "readcp")
            {
                byte[] data = tr.ReadClodeplug();
                //byte[] FFF = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                //Array.Copy(FFF, 0, data, 0xA0, 8);
                //tr.ReadClodeplug(args[1]);
                File.WriteAllBytes(args[1], data);
                tr.Reboot();
                Console.WriteLine("Read codeplug to " + args[1]);
            }
            else if (args[0] == "dumpspiflash")
            {
                byte[] data = tr.ReadSPIFlash(0, 1024 * 1000);
                //byte[] FFF = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                //Array.Copy(FFF, 0, data, 0xA0, 8);
                //tr.ReadClodeplug(args[1]);
                File.WriteAllBytes(args[1], data);
                tr.Reboot();
                Console.WriteLine("dump spiflash to " + args[1]);
            }
            else if (args[0] == "fixpass")
            {
                tr.FixPassword();
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
            else if (args[0] == "userdb2017")
            {
                Console.WriteLine("Flashing csv");
                tr.WriteCSV_RT82(args[1]);
                Console.WriteLine("Rebooting");
                tr.Reboot();
            }
            else if(args[0] == "screenshot")
            {
                Console.WriteLine("Dumping frame buffers for screen shot");
                //byte[] shot = tr.ScreenShotBMP();
                //File.WriteAllBytes("screenshot.bmp", shot);
                int width = 160;
                int height = 128;

                byte[] BMPDATA2 = tr.ScreenShotBMP();
                //File.WriteAllBytes("bmpdata.bin", BMPDATA);
                if (BMPDATA2 != null)
                {

                    Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    
                     BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                     IntPtr ptr = bmpData.Scan0;
                     Marshal.Copy(BMPDATA2, 0, ptr, height * width * 3);
                    bmp.UnlockBits(bmpData);
                    bmp.Save("screenshot.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
                }
            }else if(args[0] == "test")
            {
                Console.WriteLine("Flashing firmware");
                tr.WriteFirmwareTest(File.ReadAllBytes(args[1]));
                Console.WriteLine("Rebooting");
                tr.Reboot();

                /*if (tr.isInDFU())
                {
                    Console.WriteLine("DFU mode, rebooting to normal mode");
                    tr.Reboot();
                    Thread.Sleep(7000);
                    tr = new TyteraRadio(TyteraRadio.RadioModel.RM_MD380);

                    tr.TickleTickle();
                    if (!tr.checkSpiID())
                    {
                        Console.WriteLine("USB Connection Failed. Reboot radio.");
                        Console.ReadKey();
                        return;
                    }
                    if (!tr.isInDFU())
                    {
                        Console.WriteLine("Success! Radio in normal mode.");
                    }

                }
                else
                {
                    Console.WriteLine("Normal mode");
                }*/
            }
            else
            {
                Console.WriteLine("   readcp [file]");
                Console.WriteLine("   writecp [file]");
                Console.WriteLine("   userdb [file]");
                Console.WriteLine("   flash [file]");
                Console.WriteLine("   userdb2017");

            }
            Console.WriteLine("Done!");
            Console.ReadKey();
        }
    }
}
