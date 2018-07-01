using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

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

            //STDFU.STDFU_ClrStatus(ref hDevice);
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while ((dfuStatus.bState != STDFU.STATE_DFU_IDLE && dfuStatus.bState != STDFU.STATE_DFU_DOWNLOAD_IDLE && dfuStatus.bState != STDFU.STATE_DFU_UPLOAD_IDLE) && ((DateTime.Now.Millisecond - start) < 5000))
            {
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

            //if ((result = STDFU.STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 0)) == STDFU.STDFU_NOERROR)
            {
                //WaitUntilIdle();

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

            //WaitUntilIdle();

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

        public void EraseSPI64kBlock(UInt32 address)
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

        public void WriteSPIFlash(UInt32 address, UInt32 size, byte[] data)
        {

            //CustomCommand(0x91, 0x01);

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

        public byte[] ReadSPIFlash(UInt32 address, UInt32 size )
        {
            byte[] retBytes = new byte[size];
            for (int i = 0; i < size; i++) { retBytes[i] = 0; }

            byte[] cmd = new byte[5];
            
            byte[] bytes = new byte[MAX_WRITE_BLOCK_SIZE];
            for(int i=0; i< MAX_WRITE_BLOCK_SIZE; i++) { bytes[i] = 0; }

            for (uint offset = 0; offset < size; offset+= MAX_WRITE_BLOCK_SIZE)
            {
                progress = (int)(((float)offset / (float)size) * 100.0) + 1;

                cmd[0] = 0x01; // SPIFLASHWRITE_NEW

                // address
                cmd[1] = (byte)(address & 0xFF);
                cmd[2] = (byte)((address >> 8) & 0xFF);
                cmd[3] = (byte)((address >> 16) & 0xFF);
                cmd[4] = (byte)((address >> 24) & 0xFF);

                uint res = STDFU.STDFU_Dnload(ref hDevice, cmd, (uint)cmd.Length, 1);
                STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
                STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
                //Thread.Sleep(3);
                STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
                WaitUntilIdle();

                uint lenn = (uint)(size - offset < MAX_WRITE_BLOCK_SIZE ? (size - offset) : MAX_WRITE_BLOCK_SIZE);
                res = STDFU.STDFU_Upload(ref hDevice, bytes, lenn, 1);
                Array.Copy(bytes, 0, retBytes, offset, lenn);
                address += MAX_WRITE_BLOCK_SIZE;
            }

            return retBytes;
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

        public void ReadCSV_RT82(string filename)
        {
            File.WriteAllBytes(filename, ReadCSV_RT82());
        }

        public byte[] ReadCSV_RT82()
        {
            long length = 0xF00000;
            long offset = 0;

            byte[] codeplug = new byte[length];

            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);

            SetAddressPointer(0x00000000);

            // 256kb
            
            for (int blockNumber = 2; blockNumber < 0x2 + (length / 1024); blockNumber++)
            {
                progress = (int)(((float)(blockNumber) / (float)(0x2 + (length / 1024))) * 100.0)+1;
                byte[] data = ReadBlock((uint)blockNumber, 1024);
                Array.Copy(data, 0, codeplug, offset, 1024);
                offset += 1024;
            }

            return codeplug;
        }

        public void WriteSpiFlash_RT82(byte[] data)
        {
            uint blockNumber = 0x2;
            long length = 0xF00000; 
            if (data==null || data.Length!= length)
            {
                return;
            }
            
            
            // programming mode
            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);


            Console.WriteLine("Erasing..");

            for (uint i = 0; i < length; i += 0x10000)
            {
                EraseSector(i);
                progress = (int)((((float)(i)) / (float)(length)) * 100.0)+1;
            }

            SetAddressPointer(0);

            byte[] tmpBlk = new byte[MAX_WRITE_BLOCK_SIZE];
            for (int b = 0; b < tmpBlk.Length; b++)
            {
                tmpBlk[b] = 0xFF;
            }
            Console.WriteLine("Writing..");


            for (int i = 0; i < length; i += MAX_WRITE_BLOCK_SIZE)
            {
                Array.Copy(data, i, tmpBlk, 0, (data.Length - i < MAX_WRITE_BLOCK_SIZE) ? data.Length - i : MAX_WRITE_BLOCK_SIZE);
                WriteBlock(tmpBlk, blockNumber);
                WaitUntilIdle();
                blockNumber++;
                progress = (int)(((float)i / (float)length) * 100.0);
            }
        }

        public byte[] ReadCSV_Header_RT82()
        {
            long length = 0x4000;
            long offset = 0;

            byte[] codeplug = new byte[length];

            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);

            SetAddressPointer(0x00000000);

            // 256kb
            uint blockNumber = 0x810 + 0x2 - 0x10;
            for (; offset < length; blockNumber++)
            {
                progress = (int)(((float)offset / (float)length + 2.0f) * 100.0) + 1;
                byte[] data = ReadBlock((uint)blockNumber, 1024);
                Array.Copy(data, 0, codeplug, offset, 1024);
                offset += 1024;
            }

            return codeplug;
        }

        public byte[] GenerateHeader_RT82(byte[] data)
        {

            byte[] retdata = new byte[0x4003];
            for (int i = 0; i < retdata.Length; i++) { retdata[i] = 0xFF; }

            byte[] temp = new byte[4];
           
            List<int> prefixList = new List<int>();
            List<int> indexList = new List<int>();
            
            int maxSize = 0;

            for (int i = 0; i < 100000; i++)
            {
                for (int b = 0; b < temp.Length; b++) { temp[b] = 0; }
                Array.Copy(data, 3 + (i * 0x78), temp, 0, 3);
                //Array.Reverse(temp);
                //Console.WriteLine("ID: " + BitConverter.ToInt32(temp, 0) + "-" + BitConverter.ToInt32(temp, 0).ToString("X"));
                int dmrid = ((BitConverter.ToInt32(temp, 0) >> 12) & 0xFFF);

                if (dmrid == 0xFFF)
                {
                    maxSize = i;
                    //Console.WriteLine("maxsize: " + maxSize);
                    break;
                }
                if (!prefixList.Contains(dmrid))
                {

                    prefixList.Add(dmrid);
                    int index = ((i + 1) & 0xFFFFF);
                    indexList.Add(index);
                    //Console.WriteLine("Prefix: " + prefixList[prefixList.Count - 1].ToString("X") + " Index: " + indexList[indexList.Count - 1].ToString("X"));
                }
            }
            int[] headerArr = new int[indexList.Count];
            for (int i = 0; i < headerArr.Length; i++)
            {
                headerArr[i] = ((int)(indexList[i] & 0xFFFFF) | (int)((prefixList[i] << 20) & 0xFFF00000));
                //Console.WriteLine(headerArr[i].ToString("X"));
            }
            byte[] intBytes = BitConverter.GetBytes(maxSize);
            Array.Reverse(intBytes);
            Array.Copy(intBytes, 1, retdata, 0, 3);

           // Console.WriteLine("MaxData: " + maxSize.ToString("X"));
            for (int i = 0; i < headerArr.Length; i++)
            {
                byte[] tmpbytes = BitConverter.GetBytes(headerArr[i]);
                Array.Reverse(tmpbytes);
                Array.Copy(tmpbytes, 0, retdata, (i * 4) + 3, 4);
            }

            return retdata;
        }


        public void WriteCSV_RT82(string filename)
        {

            byte[] csvData = getCSVData(filename);
            WriteCSV_RT82(csvData);
        }

        private byte[] getCSVData(string filename)
        {
            string[] userdblines = File.ReadAllLines(filename);

            byte[] userdbdata = new byte[(0x78 * (userdblines.Length-1)) + 3];

            for (int h = 0; h < userdbdata.Length; h++)
            {
                userdbdata[h] = (byte)(0xFF);
            }

            for (int i = 1; i < userdblines.Length && ((i - 1) * 0x78 < 0x78 * (userdblines.Length-1)); i++)
            {
                try
                {
                    string[] userparts = userdblines[i].Split(',');

                    if (userparts.Length < 2)
                    {
                        continue;
                    }

                    for (int j = 0; j < userparts.Length - 2; j++) {
                        try
                        {
                            if (userparts[j].Length < 2) { continue; }

                        }
                                catch
                        {
                            continue;
                        }
                    }

                    UInt32 userID = Convert.ToUInt32(userparts[0]) | 0xFF000000;
                    //skip talkgroups
                    /*if (userID < 1000000)
                    {
                        continue;
                    }*/

                    byte[] idbytes = BitConverter.GetBytes(userID);
                    Array.Copy(idbytes, 0, userdbdata, (0x78 * (i - 1)), 4);

                    //callsign
                    for (int h = 0; h < userparts[1].Length+1; h++)
                    {
                        userdbdata[(0x78 * (i - 1)) + 4 + h] = (byte)(0);
                    }
                    byte[] toBytes = Encoding.UTF8.GetBytes(userparts[1]);
                    Array.Copy(toBytes, 0, userdbdata, (((0x78) * (i - 1)) + 4), toBytes.Length);
                    //rest of data
                    for (int h = 0; h < 0x64; h++)
                    {
                        userdbdata[(0x78 * (i - 1)) + 0x14 + h] = (byte)(0xFF);
                    }
                    string newcsv = "";
                    for (int h = 2; h < userparts.Length; h++)
                    {
                        if (userparts[h].Length < 2)
                        {
                            newcsv += ",";
                            continue;
                        }

                        if (h == 2)
                        {
                            string[] tttt = userparts[h].Split(' ');
                            if (tttt.Length == 3 && tttt[1].Length == 1)
                            {
                                newcsv += tttt[0] + " " + tttt[2];
                                continue;
                            }
                        }

                        newcsv += userparts[h];
                        if (h != userparts.Length - 1)
                        {
                            newcsv += ",";
                        }
                    }
                    newcsv += ",,,";
                    toBytes = Encoding.UTF8.GetBytes(newcsv);
                    int lenn = (toBytes.Length >= 0x64 ? 0x63 : toBytes.Length);
                    Array.Copy(toBytes, 0, userdbdata, (((0x78) * (i - 1)) + 0x14), lenn);
                    userdbdata[(((0x78) * (i - 1)) + 0x14) + lenn] = 0x0;
                }
                catch
                {      
                    continue;
                }
            }
            //Array.Copy(userdbdata, 0, userdbdata, 3, userdbdata.Length - 3);
            byte[] ret = new byte[userdbdata.Length & 0xFFFFFFF0];
            for(int i=0; i < ret.Length; i++)
            {
                ret[i] = 0xFF;
            }
            Array.Copy(userdbdata, 0x78, ret, 3, (ret.Length - 0x78) );
            //File.WriteAllBytes("userdb2017.bin", ret);
            return ret;
        }

        public void WriteCSV_RT82(byte[] datain)
        {
            byte[] data = null;
            uint blockNumber = 0x810 + 0x2 - 0x10;

            //check if in rdf container..

            byte[] headerData = GenerateHeader_RT82(datain);
            //this.Reboot();
            //File.WriteAllBytes("userdb2017_header.bin", headerData);
            if (headerData==null || headerData.Length != 0x4003)
            {
                return;
            }

            data = new byte[datain.Length+ headerData.Length];
            Array.Copy(headerData, 0, data, 0, headerData.Length);
            Array.Copy(datain, 0, data, headerData.Length-3, datain.Length);
            

            //progress = 10f;
            //Thread.Sleep(10000);
            //this.hDevice = OpenDFUDevice();
            //Thread.Sleep(1000);

            //progress = 20f;
            // programming mode
            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x01);

            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x02);
            CustomCommand(0xA2, 0x03);
            CustomCommand(0xA2, 0x04);
            CustomCommand(0xA2, 0x07);


            Console.WriteLine("Erasing..");

            for (uint i = 0x200000; i < 0x200000+(data.Length); i+=0x10000)
            {
                EraseSector(i);
                progress = (int)((((float)(i- 0x200000)) /(float)(data.Length)) * 100.0)+1;
            }

            SetAddressPointer(0);

            byte[] tmpBlk = new byte[MAX_WRITE_BLOCK_SIZE];
            for (int b = 0; b < tmpBlk.Length; b++)
            {
                tmpBlk[b] = 0xFF;
            }
            Console.WriteLine("Writing..");


            for (int i = 0; i < data.Length; i += MAX_WRITE_BLOCK_SIZE)
            {
                Array.Copy(data, i, tmpBlk, 0, (data.Length - i < MAX_WRITE_BLOCK_SIZE) ? data.Length - i : MAX_WRITE_BLOCK_SIZE);
                WriteBlock(tmpBlk, blockNumber);
                WaitUntilIdle();
                blockNumber++;
                progress = (int)(((float)i / (float)data.Length) * 100.0)+1;
            }
            progress = 0;
            Console.WriteLine("Final Block #: " + (blockNumber * 1024));
        }


        // codeplug
        public void WriteCodeplug(string filename)
        {
            WriteCodeplug(File.ReadAllBytes(filename));
        }
        public void WriteCodeplug(byte[] datain)
        {
            byte[] data = null;

            //check if in rdf container..
            if(datain.Length == 262709 && datain[0]=='D' && datain[2] == 'u')
            {
                byte[] tmpData = new byte[datain.Length - 549 - 16];
                Array.Copy(datain, 549, tmpData, 0, datain.Length - 549 - 16);
                data = new byte[datain.Length - 549 - 16];
                Array.Copy(tmpData, data, tmpData.Length);
            }
            else
            {
                data = new byte[datain.Length];
                Array.Copy(datain, data, data.Length);
            }

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
                Array.Copy(data, i, tmpBlk, 0, (data.Length - i < MAX_WRITE_BLOCK_SIZE) ? data.Length - i : MAX_WRITE_BLOCK_SIZE);
                WriteBlock(tmpBlk, blockNumber);
                WaitUntilIdle();
                blockNumber++;
                progress = (int)(((float)i / (float)data.Length) * 100.0);
            }
        }

        public void FixPassword()
        {
            Console.WriteLine("Reading data from radio..");
            byte[] data = ReadClodeplug();
            byte[] FFF = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            Array.Copy(FFF, 0, data, 0x20A0, 8);
            Array.Copy(FFF, 0, data, 0x20A8, 8);
            //tr.ReadClodeplug(args[1]);
            File.WriteAllBytes("fixpass.bin", data);
            Console.WriteLine("Rebooting radio, do not disconnect, not done yet..");

            //

            Reboot();

            Thread.Sleep(7000);
            this.hDevice = OpenDFUDevice();

            TickleTickle();
            GetSpiID();

            Thread.Sleep(2000);
            
            Console.WriteLine("Writing fixed data back to radio..");

            //WriteUserDB(data, 0);
            WriteCodeplug(data);

            //WriteSPIFlash(0, 0x4000, data);
            Console.WriteLine("Done! Rebooting radio.");
            Reboot();
            Console.WriteLine("Fixed.. hopefully?");

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
                progress = (int)((float)jj++ / (addresses.Length+1) * 50.0)+1;
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
                    int stuff = (int)((float)datawritten / (float)(data.Length) * 50.0)+50;
                    progress = (stuff == 0 ? 1 : stuff);
                }
                address_idx += 1;
            }
        }


        public void WriteFirmware_2017(byte[] data)
        {
            IntPtr strinng = Marshal.AllocHGlobal(255);

            STDFU.STDFU_GetStringDescriptor(ref hDevice, 1, strinng, 255);

            byte[] bytes = new byte[255];
            Marshal.Copy(strinng, bytes, 0, 255);


            String device = Encoding.ASCII.GetString(bytes).Trim();
            Console.WriteLine("Device: " + device);

            if (device.Equals("AnyRoad Technology"))
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

            //byte[] unkData = File.ReadAllBytes("C:\\TYT\\FW_2017_f.bin");

            uint[] addresses1 = {
                    0x00060000,
                    0x00070000,
                    0x00080000,
                    0x00090000,
                    0x000a0000,
                    0x000b0000,
                    0x000c0000,
                    0x000d0000,
                    0x000e0000,
                    0x000f0000,

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

            uint[] block_ends = { 0x11, 0x41, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81 };

            WaitUntilIdle();

            CustomCommand(0x91, 0x01);
            CustomCommand(0x91, 0x31);

            //CustomCommand(0xA2, 0x01);
            //CustomCommand(0xA2, 0x31);
            //CustomCommand(0xA2, 0x02);
            //CustomCommand(0xA2, 0x03);
            //CustomCommand(0xA2, 0x04);
            //CustomCommand(0xA2, 0x07);
            //CustomCommand(0x91, 0x31);

            

            int jj = 0;
            foreach (uint addr in addresses1)
            {
                progress = (int)((float)jj++ / (float)0.0) + 1;
                EraseSector(addr);
            }

            SetAddressPointer(0);

            uint block_size = 1024;
            uint block_start = 2;
            uint address_idx = 0;
            uint datawritten = 0;

            for (; datawritten < data.Length; )
            {
                Console.WriteLine("\r" + "% complete");
                SetAddressPointer(addresses1[i]);


                uint block_number = block_start;
                byte[] buffa = new byte[block_size];

                uint blk_data_len = 0;

                if(block_number == 0x42)
                {
                    

                }else
                {
                    

                    for (int b = 0; b < buffa.Length; b++) buffa[b] = 0xFF;

                    uint dat_siz = (uint)data.Length - datawritten;
                    Array.Copy(data, datawritten, buffa, 0, (dat_siz < block_size ? dat_siz : block_size));
                    datawritten += (dat_siz < block_size ? dat_siz : block_size);
                    blk_data_len += (dat_siz < block_size ? dat_siz : block_size);

                    WriteBlock(buffa, block_number);
                    WaitUntilIdle();

                    block_number++;
                    int stuff = (int)((float)datawritten / (float)(data.Length) * 50.0) + 50;
                    progress = (stuff == 0 ? 1 : stuff);
                }
                address_idx += 1;
            }
        }

        public void WriteFirmwareTest(byte[] data)
        {
            /*IntPtr strinng = Marshal.AllocHGlobal(255);

            STDFU.STDFU_GetStringDescriptor(ref hDevice, 1, strinng, 255);

            byte[] bytes = new byte[255];
            Marshal.Copy(strinng, bytes, 0, 255);


            String device = Encoding.ASCII.GetString(bytes).Trim();
            Console.WriteLine("Device: " + device);

            if (device.Equals("AnyRoad Technology"))
            {
                Console.WriteLine("Radio not in DFU Mode!");
                return;
            }*/

            byte[] tmp = new byte[14];
            Array.Copy(data, 0, tmp, 0, 14);
            if (Encoding.GetEncoding("ASCII").GetString(tmp) == "OutSecurityBin")
            {
                byte[] newdata = new byte[data.Length - 0x200];
                Array.Copy(data, 0x100, newdata, 0, newdata.Length);
                data = newdata;
            }

           
            //WaitUntilIdle();

            
           
                uint blockNumber = 0x2;

               
              


                //progress = 10f;
                //Thread.Sleep(10000);
                //this.hDevice = OpenDFUDevice();
                //Thread.Sleep(1000);

                //progress = 20f;
                // programming mode
                //CustomCommand(0x91, 0x01);
                //CustomCommand(0x91, 0x01);

                //CustomCommand(0xA2, 0x01);
                CustomCommand(0xA2, 0x31);
                //CustomCommand(0xA2, 0x03);
                //CustomCommand(0xA2, 0x07);
                CustomCommand(0x91, 0x31);
            

                Console.WriteLine("Erasing..");

                for (uint i = 0x600; i <= 0x1000; i += 0x100)
                {
                    EraseSector(i);
                    progress = (int)((((float)(i - 0x600)) / (float)(data.Length)+1) * 100.0) + 1;
                    Thread.Sleep(1500);
                }
            //WaitUntilIdle();

            SetAddressPointer(0x600);

           // WaitUntilIdle();

            byte[] tmpBlk = new byte[MAX_WRITE_BLOCK_SIZE];
                for (int b = 0; b < tmpBlk.Length; b++)
                {
                    tmpBlk[b] = 0xFF;
                }
                Console.WriteLine("Writing..");


                for (int i = 0; i < data.Length; i += MAX_WRITE_BLOCK_SIZE)
                {
                    Array.Copy(data, i, tmpBlk, 0, (data.Length - i < MAX_WRITE_BLOCK_SIZE) ? data.Length - i : MAX_WRITE_BLOCK_SIZE);
                    WriteBlock(tmpBlk, blockNumber);
                    //WaitUntilIdle();
                Thread.Sleep(20);
                blockNumber++;
                    progress = (int)(((float)i / (float)data.Length) * 100.0) + 1;
                }
                progress = 0;
                Console.WriteLine("Final Block #: " + (blockNumber));
            
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
                progress = (int)(((float)blockNumber / (float)0x102) * 100.0)+1;
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

        public bool isInDFU()
        {
            string desc1 = getDeviceDescriptor(1).Substring(0, 18);
            string desc2 = getDeviceDescriptor(2).Substring(0, 13);
            if (desc1 == "AnyRoad Technology")
            {
                Console.WriteLine("Is in DFU Mode");
                return true;
            }
            else if (desc2 == "Digital Radio in USB mode" || desc2 == "Patched MD380")
            {
                Console.WriteLine("In normal mode");
                return false;
            }
            return false;
        }

        public string getDeviceDescriptor(uint index=1)
        {
            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            UInt32 Result = STDFU.STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 0);
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            STDFU.STDFU_ClrStatus(ref hDevice);

            IntPtr strinng = Marshal.AllocHGlobal(256);
            STDFU.STDFU_GetStringDescriptor(ref hDevice, index, strinng, 256);

            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
            STDFU.STDFU_ClrStatus(ref hDevice);

            byte[] bytes = new byte[256];
            Marshal.Copy(strinng, bytes, 0, 256);
            Marshal.FreeHGlobal(strinng);
            return Encoding.ASCII.GetString(bytes);
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

        public bool checkSpiID()
        {
            UInt32 spi = GetSpiID();
            return (spi == 2602057967);
        }

        public void WriteUserDB(byte[] data, uint address=0x100000)
        {
            CustomCommand(0x91, 0x01);

            // todo: change to userdb address
            //uint address = 0x100000;

            Console.WriteLine("Erasing..");
            // erase
            for (uint i = address; i < (address + data.Length); i += 0x1000)
            {
                progress = (int)(((float)(i) / (float)((address + (data.Length*2)+1))) * 100.0)+1;
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
                    int prog = (int)(((float)(fullparts+i) / (float)(fullparts*2)) * 100.0)+1;
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

        /*public bool DrawText(string msg, int a, int b)
        {
            
            byte[] cmd = new byte[msg.Length + 3];
            cmd[0] = 0x80;

            a &= 0xFF;
            b &= 0xFF;

            WaitUntilIdle();
            
            cmd[1] = (byte)(address & 0xFF);
            cmd[2] = (byte)((address >> 8) & 0xFF);
            cmd[3] = (byte)((address >> 16) & 0xFF);
            cmd[4] = (byte)((address >> 24) & 0xFF);

            STDFU.STDFU_Dnload(ref hDevice, cmd, 5, 0);
            STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
            STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);

            WaitUntilIdle();

            STDFU.STDFU_Dnload(ref hDevice, new byte[] { 0x84, 0,  }, 2, 0);

            WaitUntilIdle();

           


            return true;
        }*/

        public byte[] ReadFrameLine(byte y)
        {
            int majorTries = 0;
            retryStart:

            WaitUntilIdle();

            for (int i = 0; i < 2; i++)
            {
                

                byte[] cmd = new byte[5];
                cmd[0] = 0x84; // SPIFLASHWRITE_NEW

                // address
                cmd[1] = (byte)(0);
                cmd[2] = (byte)(y & 0xFF);
                cmd[3] = (byte)(159 & 0xFF);
                cmd[4] = (byte)(y & 0xFF);

                STDFU.STDFU_Dnload(ref hDevice, cmd, (uint)cmd.Length, 1);

                STDFU.DFU_Status dfuStatus = new STDFU.DFU_Status();
                STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
                //Thread.Sleep(40);
                STDFU.STDFU_GetStatus(ref hDevice, ref dfuStatus);
                // WaitUntilIdle();

                byte[] bytes = new byte[(160 * 3) + 5];
                STDFU.STDFU_Upload(ref hDevice, bytes, (160 * 3) + 5, 1);
                if(bytes[4] == y)
                {
                    byte[] ret = new byte[160 * 3];
                    Array.Copy(bytes, 5, ret, 0, 160 * 3);
                    return ret;
                }
                else { Thread.Sleep(3); }
                //Thread.Sleep(25);
            }

            return null;
            /*
            majorTries++;
            Console.WriteLine("ERR: Frame line " + y);

            if (majorTries > 100) return null;
            else
            {

                TickleTickle();
                GetSpiID();

                goto retryStart;
            }*/
            
        }

        public byte[] GetHeaderBMP()
        {
            byte[] header = {0x42, 0x4D, 0x36, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00, 0x28, 0x00,
                    0x00, 0x00, 0xA0, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x01, 0x00, 0x18, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0xA0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            return header;
        }

        public byte[] GetFrameBuffer()
        {
            byte[] bmpdata = new byte[128 * 160 * 3];
            
            for (byte y = 0; y < 128; y++)
            {
                byte[] framedata = ReadFrameLine(y);
                if (framedata == null)
                {
                    //return null;
                    //Thread.Sleep(10);
                    Console.WriteLine("FAIL");
                    return null;
                    //continue;
                }
                else
                {
                    Array.Copy(framedata, 0, bmpdata, ((y) * (160 * 3)), 160 * 3);
                }
                //for (int i = 0; i < 160 * 3; i++)
               // {
                    //Console.Write(framedata[i].ToString("X"));
                    //if (i % 16 == 0)
                    //{
                    //    Console.Write("\n");
                    //}
               // }
            }
            return bmpdata;
        }

        
        public byte[] ScreenShotBMP()
        {
            //byte[] bmpdata = new byte[0xF036];
            //byte[] header = GetHeaderBMP();
            //Array.Copy(header, 0, bmpdata, 0, header.Length);

            byte[] frames = GetFrameBuffer();
            byte[] frames2 = GetFrameBuffer();

            bool isGood = false;
            while (!isGood) {
                isGood = true;
                for (int i = 0; i < 128; i++)
                {
                    for (int x = 0; x < 160; x++)
                    {
                        if (frames[(i * 160 * 3) + x] != frames2[(i * 160 * 3) + x])
                        {
                            byte[] buf = ReadFrameLine((byte)i);
                            Array.Copy(buf, 0, frames, (i * 160 * 3), 160 * 3);
                            byte[] buf2 = ReadFrameLine((byte)i);
                            Array.Copy(buf2, 0, frames2, (i * 160 * 3), 160 * 3);
                            isGood = false;
                            continue;
                        }
                    }
                }
            }
            ///Array.Copy(frames, 0, bmpdata, header.Length, frames.Length);

            return frames;
        }



    }
}
