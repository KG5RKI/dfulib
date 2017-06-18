using System;
using System.Runtime.InteropServices;

namespace dfulib
{
    static class STDFU
    {
        public const uint STDFU_ERROR_OFFSET = 0x12340000;
        public const uint STDFU_NOERROR = STDFU_ERROR_OFFSET + 0;

        public const byte STATE_IDLE = 0x00;
        public const byte STATE_DETACH = 0x01;
        public const byte STATE_DFU_IDLE = 0x02;
        public const byte STATE_DFU_DOWNLOAD_SYNC = 0x03;
        public const byte STATE_DFU_DOWNLOAD_BUS = 0x04;
        public const byte STATE_DFU_DOWNLOAD_IDLE = 0x05;
        public const byte STATE_DFU_MANIFEST_SYNC = 0x06;
        public const byte STATE_DFU_MANIFEST = 0x07;
        public const byte STATE_DFU_MANIFEST_WAIT_RESET = 0x08;
        public const byte STATE_DFU_UPLOAD_IDLE = 0x09;
        public const byte STATE_DFU_ERROR = 0x0A;
        public const byte STATE_DFU_UPLOAD_SYNC = 0x91;
        public const byte STATE_DFU_UPLOAD_BUSY = 0x92;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct USB_DeviceDescriptor // Taken from usb100.h
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DFU_FunctionalDescriptor
        {
            public byte bLength;
            public byte bDescriptorType; // Should be 0x21
            public byte bmAttributes;
            public UInt16 wDetachTimeOut;
            public UInt16 wTransfertSize;
            public UInt16 bcdDFUVersion;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DFU_Status
        {
            public byte bStatus;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] bwPollTimeout;
            public byte bState;
            public byte iString;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct USB_InterfaceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        };

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Open", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_Open([MarshalAs(UnmanagedType.LPStr)]String szDevicePath, out IntPtr hDevice);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_SelectCurrentConfiguration", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_SelectCurrentConfiguration(ref IntPtr hDevice, UInt32 ConfigIndex, UInt32 InterfaceIndex, UInt32 AlternateSetIndex);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetDeviceDescriptor", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFU_GetDeviceDescriptor(ref IntPtr handle, ref USB_DeviceDescriptor descriptor);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetDFUDescriptor", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFU_GetDFUDescriptor(ref IntPtr handle, ref uint DFUInterfaceNum, ref uint NBOfAlternates, ref DFU_FunctionalDescriptor dfuDescriptor);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetInterfaceDescriptor", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFU_GetInterfaceDescriptor(ref IntPtr handle, UInt32 ConfigIndex, UInt32 InterfaceIndex, UInt32 AlternateIndex, ref USB_InterfaceDescriptor usbDescriptor);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetStringDescriptor", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFU_GetStringDescriptor(ref IntPtr handle, UInt32 Index, IntPtr StringBuffer, UInt32 BufferSize);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Dnload", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_Dnload(ref IntPtr hDevice, [MarshalAs(UnmanagedType.LPArray)]byte[] pBuffer, UInt32 nBytes, UInt16 nBlocks);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Upload", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_Upload(ref IntPtr hDevice, [MarshalAs(UnmanagedType.LPArray)]byte[] pBuffer, UInt32 nBytes, UInt16 nBlocks);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Getstatus", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_GetStatus(ref IntPtr hDevice, ref DFU_Status dfuStatus);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Clrstatus", CharSet = CharSet.Ansi)]
        public static extern UInt32 STDFU_ClrStatus(ref IntPtr hDevice);
    }
}
