using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;

namespace dfulib
{
    /* DFULIB
     * golden is a nasty ass programmer!
     * - read/write codeplug
     * - write userdatabase
     * - write firmware
     * - read/write calibration data
     * - read screenbuffer
     * - reboot
     */

    public class TyteraRadio : Win32Usb
    {
        // VID/PID for MD380/MD390/MD2017
        private const UInt16 MD_VID = 0x0483;
        private const UInt16 MD_PID = 0xDF11;
        private static IntPtr INVALID_HANDLE_VALUE = (IntPtr)(-1);
        private const UInt16 MAX_WRITE_BLOCK_SIZE = 1024;

        private static Guid GUID_DFU = new Guid(0x3FE809AB, 0xFB91, 0x4CB5, 0xA6, 0x43, 0x69, 0x67, 0x0D, 0x52, 0x36, 0x6E);

        // variables
        public enum RadioModel
        {
            RM_MD380 = 1,
            RM_MD390,
            RM_MD2017
        };
        private RadioModel radioModel;
        private IntPtr hDevice;

        public float progress = 0;

        public float GetCurrentOperationProgress()
        {
            return progress;
        }

        public TyteraRadio(RadioModel radioModel = RadioModel.RM_MD380)
        {
            this.radioModel = radioModel;

            // should be same for all radios
            try
            {
                this.hDevice = OpenDFUDevice();
            }
            catch
            {
                this.hDevice = INVALID_HANDLE_VALUE;
            }
        }

        private IntPtr OpenDFUDevice()
        {
            Guid GUID = GUID_DFU;
            DeviceInterfaceData ifData = new DeviceInterfaceData();
            ifData.Size = Marshal.SizeOf(ifData);
            DeviceInterfaceDetailData ifDetail = new DeviceInterfaceDetailData();

            UInt32 Size = 0;
            IntPtr hInfoSet = SetupDiGetClassDevs(ref GUID, null, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);  // this gets a list of all DFU devices currently connected to the computer (InfoSet)
            IntPtr hDevice = IntPtr.Zero;

            if (hInfoSet == INVALID_HANDLE_VALUE)
            {
                throw new Exception("Could not open DFU device!");
                //throw new Exception("SetupDiGetClassDevs returned error=" + Marshal.GetLastWin32Error().ToString());
            }

            // Loop ten times hoping to find exactly one DFU device
            int i = 10;
            uint Index = 0;
            while (i-- > 0)
            {
                Index = 0;
                while (SetupDiEnumDeviceInterfaces(hInfoSet, 0, ref GUID, Index, ref ifData))
                {
                    Index++;
                }
                if (0 == Index)
                {
                    Thread.Sleep(500);
                }
                else
                {
                    break;
                }
            }

            if (1 == Index)
            {
                SetupDiEnumDeviceInterfaces(hInfoSet, 0, ref GUID, 0, ref ifData);
                SetupDiGetDeviceInterfaceDetail(hInfoSet, ref ifData, IntPtr.Zero, 0, ref Size, IntPtr.Zero);

                if (IntPtr.Size == 8)   // If we are compiled as 64bit
                {
                    ifDetail.Size = 8;
                }
                else if (IntPtr.Size == 4) // If we are compiled as 32 bit
                {
                    ifDetail.Size = 5;
                }

                if (Marshal.SizeOf(ifDetail) < Size)
                {
                    throw new Exception("Could not open DFU device!");
                    //throw new Exception("ifDetail too small");
                }

                if (true == SetupDiGetDeviceInterfaceDetail(hInfoSet, ref ifData, ref ifDetail, Size, ref Size, IntPtr.Zero))
                {
                    string DevicePath = ifDetail.DevicePath.ToUpper();
                    if (STDFU.STDFU_Open(DevicePath, out hDevice) != STDFU.STDFU_NOERROR)
                    {
                        throw new Exception("Could not open DFU device!");
                    }
                }
            }
            else
            {
                throw new Exception("There must be exactly one DFU device attached to the computer!");
            }

            SetupDiDestroyDeviceInfoList(hInfoSet);

            return hDevice;
        }

        private void WaitUntilIdle()
        {
            // wait 5 seconds max
            long start = DateTime.Now.Millisecond;

            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STDFU.STATE_DFU_IDLE && ((DateTime.Now.Millisecond - start) < 5000))
            {
                STDFU.STDFU_ClrStatus(ref hDevice);
                STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }
        }

        private void SetAddressPointer(UInt32 address)
        {
            WaitUntilIdle();

            byte[] cmd = new byte[5];
            cmd[0] = 0x21;
            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            STDFU.STDFU_Dnload(ref hDevice, cmd, 5, 0);

            WaitUntilIdle();
        }

        private void EraseSector(UInt32 address)
        {
            UInt32 result = 0;
            byte[] cmd = { 0x41, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            if ((result = STDFU.STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 0)) == STDFU.STDFU_NOERROR)
            {
                WaitUntilIdle();

                STDFU.STDFU_Dnload(ref hDevice, cmd, 5, 0);

                WaitUntilIdle();
            }
        }

        private bool WriteBlock(byte[] data, UInt32 BlockNumber)
        {
            if(this.hDevice == INVALID_HANDLE_VALUE || data.Length > MAX_WRITE_BLOCK_SIZE)
            {
                return false;
            }

            WaitUntilIdle();

            STDFU.STDFU_Dnload(ref hDevice, data, (UInt32)data.Length, (UInt16)BlockNumber);

            WaitUntilIdle();

            return true;
        }

        private byte[] ReadBlock(UInt32 BlockNumber, UInt32 BlockSize)
        {
            WaitUntilIdle();

            byte[] data = new byte[BlockSize];
            STDFU.STDFU_Upload(ref hDevice, data, BlockSize, (UInt16)BlockNumber);

            WaitUntilIdle();

            return data;
        }

        private void EraseSPI64kBlock(UInt32 address)
        {
            WaitUntilIdle();

            byte[] cmd = new byte[5];
            cmd[0] = 0x03; // SPIFLASHWRITE_NEW

            // address
            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            STDFU.STDFU_Dnload(ref hDevice, cmd, (uint)cmd.Length, 1);

            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            Thread.Sleep(50);
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
           // WaitUntilIdle();

            Console.WriteLine(address);

            byte[] bytes = new byte[1024];
            STDFU.STDFU_Upload(ref hDevice, bytes, 1024, 1);
        }

        private void WriteSPIFlash(UInt32 address, UInt32 size, byte[] data)
        {
            
            //Console.WriteLine("Flashin " + data.Length);
            byte[] cmd = new byte[9 + data.Length];
            cmd[0] = 0x04; // SPIFLASHWRITE_NEW

            // address
            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            // size
            cmd[5] = (byte)(size & 0xFF);
            cmd[6] = (byte)((size >> 8) & 0xFF);
            cmd[7] = (byte)((size >> 16) & 0xFF);
            cmd[8] = (byte)((size >> 24) & 0xFF);

            // copy
            Array.Copy(data, 0, cmd, 9, data.Length);

            uint res = STDFU.STDFU_Dnload(ref hDevice, cmd, (uint)cmd.Length, 1);
            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            //Thread.Sleep(50);
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            WaitUntilIdle();

            byte[] bytes = new byte[data.Length];
            res = STDFU.STDFU_Upload(ref hDevice, bytes, (uint)data.Length, 1);
            //Console.WriteLine("WriteSPIFlash: " + res);
        }

        private void CustomCommand(byte a, byte b)
        {
            WaitUntilIdle();

            STDFU.STDFU_Dnload(ref hDevice, new byte[] { a, b }, 2, 0);

            WaitUntilIdle();
        }

        public void Reboot()
        {
            progress = 0;
            WaitUntilIdle();
            STDFU.STDFU_Dnload(ref hDevice, new byte[] { 0x91, 0x05 }, 2, 0);
            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
        }

        // codeplug
        public void WriteCodeplug(string filename)
        {
            WriteCodeplug(File.ReadAllBytes(filename));
        }
        public void WriteCodeplug(byte[] data)
        {
            // programming mode
            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);

            EraseSector(0x00000000);
            EraseSector(0x00010000);
            EraseSector(0x00020000);
            EraseSector(0x00030000);

            SetAddressPointer(0x00000000);

            byte[] tmpBlk = new byte[MAX_WRITE_BLOCK_SIZE];

            uint blockNumber = 2;
            for (int i = 0; i < data.Length; i += MAX_WRITE_BLOCK_SIZE)
            {
                Array.Copy(data, i, tmpBlk, 0, MAX_WRITE_BLOCK_SIZE);
                WriteBlock(tmpBlk, blockNumber);
                WaitUntilIdle();
                blockNumber++;
                progress = (int)(((float)i / (float)data.Length) * 100.0);
            }
        }

        public void WriteFirmware(string filename)
        {
            WriteFirmware(File.ReadAllBytes(filename));
        }
        public void WriteFirmware(byte[] data)
        {
            IntPtr strinng = Marshal.AllocHGlobal(255);
            
            STDFU.STDFU_GetStringDescriptor(ref hDevice, 1, strinng, 255);

            byte[] bytes = new byte[255];
            Marshal.Copy(strinng, bytes, 0, 255);

                
            String device = Encoding.ASCII.GetString(bytes).Trim();
            Console.WriteLine("Device: " + device);
            
            if(device.Equals("AnyRoad Technology"))
            {
                Console.WriteLine("Radio not in DFU Mode!");
                return;
            }
            
            byte[] tmp = new byte[14];
            Array.Copy(data, 0, tmp, 0, 14);
            if (Encoding.GetEncoding("ASCII").GetString(tmp) == "OutSecurityBin")
            {
                 byte[] newdata = new byte[data.Length - 0x100];
                 Array.Copy(data, 0x100, newdata, 0, newdata.Length);
                 data = newdata;
            }

            uint[] addresses = {
                 0x0800c000,
                 0x08010000,
                 0x08020000,
                 0x08040000,
                 0x08060000,
                 0x08080000,
                 0x080a0000,
                 0x080c0000,
                 0x080e0000
            };

            uint[] sizes = {
                 0x4000,   // 0c
                 0x10000,  // 1
                 0x20000,  // 2
                 0x20000,  // 4
                 0x20000,  // 6
                 0x20000,  // 8
                 0x20000,  // a
                 0x20000,  // c
                 0x20000   // e
            };

            uint[] block_ends = { 0x11, 0x41, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81 };

            WaitUntilIdle();

            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x31);

            int jj = 0;
            foreach(uint addr in addresses)
            {
                progress = (int)(((float)jj++ / (float)addresses.Length) * 100.0);
                EraseSector(addr);
            }

            uint block_size = 1024;
            uint block_start = 2;
            uint address_idx = 0;
            uint datawritten = 0;

            for (uint i=0; i<addresses.Length && datawritten < data.Length; i++)
            {
                Console.WriteLine("\r" + (address_idx * 100 / addresses.Length) + "% complete");
                SetAddressPointer(addresses[i]);

                
                uint block_number = block_start;
                byte[] buffa = new byte[block_size];

                uint blk_data_len = 0;

                while(blk_data_len < sizes[i] && datawritten < data.Length)
                {
                    if (block_number > block_ends[address_idx])
                    {
                       // Console.WriteLine("Something bad happenin!");
                        return;
                    }

                    for(int b=0; b<buffa.Length; b++) buffa[b] = 0xFF;

                    uint dat_siz = (uint)data.Length - datawritten;
                    Array.Copy(data, datawritten, buffa, 0, (dat_siz < block_size ? dat_siz : block_size));
                    datawritten += (dat_siz < block_size ? dat_siz : block_size);
                    blk_data_len += (dat_siz < block_size ? dat_siz : block_size);

                    WriteBlock(buffa, block_number);
                    WaitUntilIdle();

                    block_number++;
                    int stuff = (int)(((float)datawritten / (float)data.Length) * 100.0);
                    progress = (stuff == 0 ? 1 : stuff);
                }
                address_idx += 1;
            }
        }
        

        public void ReadClodeplug(string filename)
        {
            File.WriteAllBytes(filename, ReadClodeplug());
        }
        public byte[] ReadClodeplug()
        {
            byte[] codeplug = new byte[1024 * 256];

            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);

            SetAddressPointer(0x00000000);

            // 256kb
            long offset = 0;
            for(uint blockNumber = 2; blockNumber < 0x102; blockNumber++)
            {
                progress = (int)(((float)blockNumber / (float)0x102) * 100.0);
                byte[] data = ReadBlock(blockNumber, MAX_WRITE_BLOCK_SIZE);
                Array.Copy(data, 0, codeplug, offset, MAX_WRITE_BLOCK_SIZE);

                offset += MAX_WRITE_BLOCK_SIZE;
            }

            return codeplug;
        }

        // userdb
        public void WriteUserDB(string filename)
        {
            WriteUserDB(File.ReadAllBytes(filename));
        }

        public void TickleTickle()
        {
            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            UInt32 Result = STDFU.STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 0);
            Console.WriteLine("SelectConfig Res: " + Result);
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            STDFU.STDFU_ClrStatus(ref hDevice);
           // Console.WriteLine("DFU iString: " + dfuStatus.iString);
           // Console.WriteLine("DFU State: " + dfuStatus.bState);
           // Console.WriteLine("DFU STATUS: " + dfuStatus.bStatus);
            IntPtr strinng = Marshal.AllocHGlobal(256);
            Result = STDFU.STDFU_GetStringDescriptor(ref hDevice, 1, strinng, 256);
           // Console.WriteLine("StringDesc Res: " + Result);
            
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            STDFU.STDFU_ClrStatus(ref hDevice);
           // Console.WriteLine("DFU iString: " + dfuStatus.iString);
            //Console.WriteLine("DFU State: " + dfuStatus.bState);
            //Console.WriteLine("DFU STATUS: " + dfuStatus.bStatus);
            byte[] bytes = new byte[256];
            Marshal.Copy(strinng, bytes, 0, 256); 
            Marshal.FreeHGlobal(strinng);
            Console.WriteLine(Encoding.ASCII.GetString(bytes));
        }

        public UInt32 GetSpiID()
        {

            WaitUntilIdle();

            byte[] cmd = new byte[1];
            cmd[0] = 0x05;
            
            
            STDFU.STDFU_Dnload(ref hDevice, cmd, (uint)1, 1);

            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            //Console.WriteLine("DFU iString: " + dfuStatus.iString);
            //Console.WriteLine("DFU State: " + dfuStatus.bState);
            //Console.WriteLine("DFU STATUS: " + dfuStatus.bStatus);

            WaitUntilIdle();

            byte[] bytes = new byte[4];
            STDFU.STDFU_Upload(ref hDevice, bytes, 4, 1);

            return BitConverter.ToUInt32(bytes, 0);
        }

        public void WriteUserDB(byte[] data)
        {
            CustomCommand(0x91, 0x01);

            // todo: change to userdb address
            uint address = 0x100000;

            Console.WriteLine("Erasing..");
            // erase
            for (uint i = address; i < (address + data.Length + 1); i += 0x1000)
            {
                progress = (int)(((float)(i) / (float)((address + data.Length+1))) * 100.0);
                EraseSPI64kBlock(i);
            }

            Console.WriteLine("Writing..");
            // write
            uint fullparts = (uint)data.Length / 1024;
            if(fullparts > 0)
            {
                for(uint i = 0; i < fullparts; i++)
                {
                    byte[] tmp = new byte[1024];
                    Array.Copy(data, i * 1024, tmp, 0, 1024);
                    WriteSPIFlash(address + (i * 1024), 1024, tmp);
                    //WaitUntilIdle();
                    int prog = (int)(((float)(i) / (float)(fullparts)) * 100.0);
                    progress = (prog == 0 ? 1 : prog);
                    Console.WriteLine("Writing addr: " + ((i * 1024)));
                }
            }

            // last part
            uint lastpartsize = (uint)data.Length - (fullparts * 1024);
            if(lastpartsize > 0)
            {
                byte[] tmp = new byte[1024];
                for (int i = 0; i < 1024; i++) tmp[i] = 0;
                Array.Copy(data, fullparts * 1024, tmp, 0, lastpartsize);
                WriteSPIFlash(address + (fullparts * 1024), 1024, tmp);
                Console.WriteLine("Writing addr: " + (fullparts * 1024));
            }
            progress = 100;
        }

    }
}
