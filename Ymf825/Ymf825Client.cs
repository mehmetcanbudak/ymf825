﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.ServiceModel;
using System.Text;

namespace Ymf825
{
    [ServiceContract]
    public interface IYmf825Client
    {
        #region -- Methods --

        [OperationContract]
        bool CheckAvailable();

        [OperationContract]
        void ResetHardware();

        [OperationContract]
        void ResetSoftware();

        [OperationContract]
        void Write(byte address, byte data);

        [OperationContract]
        void WriteBuffer(byte[] buffer, int offset, int count);

        [OperationContract]
        void BurstWriteBytes(byte address, params byte[] data);

        [OperationContract]
        byte Read(TargetDevice device, byte address);

        [OperationContract]
        void SetTarget(TargetDevice device);

        #endregion
    }

    [Flags]
    public enum TargetDevice : byte
    {
        None = 0x00,

        Ymf825Board0 = 0x01,
        Ymf825Board1 = 0x02
    }

    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.Single,
        ConcurrencyMode = ConcurrencyMode.Single,
        UseSynchronizationContext = false)]
    public class Ymf825Client : IYmf825Client
    {
        #region -- Public Fields --

        public const string ServiceUri = "net.pipe://localhost/nanase.cc";
        public const string ServiceName = "YMF825Board_SPI_Server";
        public const string ServiceAddress = ServiceUri + "/" + ServiceName;

        #endregion

        #region -- Private Fields --

        private readonly SerialPort port;

        private static readonly byte[] DataHardwareReset = { 0xfe };
        private static readonly byte[] DataVersion = { 0xff };
        private const string VersionString = "V1YMF825";
        #endregion

        #region -- Public Properties --

        public long WriteBytesTotal { get; private set; }

        public long BurstWriteBytesTotal { get; private set; }

        public long ReadBytesTotal { get; private set; }

        public long WriteCommandsTotal { get; private set; }

        public long BurstWriteCommandsTotal { get; private set; }

        public long ReadCommandsTotal { get; private set; }

        public long FailedWriteBytesTotal { get; private set; }

        public long FailedBurstWriteBytesTotal { get; private set; }

        public long FailedReadBytesTotal { get; private set; }

        public long WriteErrorTotal { get; private set; }

        public long BurstWriteErrorTotal { get; private set; }

        public long ReadErrorTotal { get; private set; }

        #endregion

        #region -- Public Events --

        public event EventHandler<SpiServiceTransferedEventArgs> DataWrote;

        public event EventHandler<SpiServiceBurstWriteEventArgs> DataBurstWrote;

        public event EventHandler<SpiServiceTransferedEventArgs> DataRead;

        #endregion

        #region -- Constructors --

        public Ymf825Client(SerialPort port)
        {
            this.port = port;
        }

        #endregion

        #region -- Public Methods --

        public bool CheckAvailable()
        {
            var readBuffer = new byte[8];
            port.Write(DataVersion, 0, DataVersion.Length);
            port.Read(readBuffer, 0, 8);

            return Encoding.ASCII.GetString(readBuffer) == VersionString;
        }

        public void ResetHardware()
        {
            port.Write(DataHardwareReset, 0, DataHardwareReset.Length);
        }

        public void ResetSoftware()
        {
            new Ymf825Driver(this).ResetSoftware();
        }

        public void Write(byte address, byte data)
        {
            address &= 0b01111111;

            try
            {
                port.Write(new byte[] { 0x00, address, data }, 0, 3);
                WriteBytesTotal += 2;
                WriteCommandsTotal++;

                DataWrote?.Invoke(this, new SpiServiceTransferedEventArgs(address, data));
            }
            catch (InvalidOperationException)
            {
                FailedWriteBytesTotal += 2;
                WriteErrorTotal++;
            }
        }

        public void WriteBuffer(byte[] buffer, int offset, int count)
        {
            if (count < 1)
                return;

            if (count == 1)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 2)
            {
                Write(buffer[offset], buffer[offset + 1]);
                return;
            }

            var newBuffer = new byte[count - 1];
            Array.Copy(buffer, offset, newBuffer, 0, count - 1);
            BurstWriteBytes(buffer[offset], newBuffer);
        }

        public void BurstWriteBytes(byte address, params byte[] data)
        {
            address &= 0b01111111;

            if (data.Length > 512)
                throw new ArgumentOutOfRangeException(nameof(data));

            var size = BitConverter.GetBytes((short)data.Length);

            try
            {
                port.Write(new byte[] { 0x01 }, 0, 1);
                port.Write(size, 0, 2);
                port.Write(new[] { address }, 0, 1);
                port.Write(data, 0, data.Length);

                BurstWriteBytesTotal += 3 + data.Length;
                BurstWriteCommandsTotal++;
                DataBurstWrote?.Invoke(this, new SpiServiceBurstWriteEventArgs(address, data));
            }
            catch (InvalidOperationException)
            {
                FailedBurstWriteBytesTotal += 3 + data.Length;
                BurstWriteErrorTotal++;
            }
        }

        public byte Read(TargetDevice device, byte address)
        {
            address |= 0b10000000;
            var readBuffer = new byte[1];

            try
            {
                port.Write(new byte[] { 0x20, (byte)device, address }, 0, 3);
                port.Read(readBuffer, 0, 1);
                ReadBytesTotal++;
                WriteBytesTotal++;
                ReadCommandsTotal++;
                WriteCommandsTotal++;
                DataRead?.Invoke(this, new SpiServiceTransferedEventArgs(address, readBuffer[0]));

                return readBuffer[0];
            }
            catch (InvalidOperationException)
            {
                FailedWriteBytesTotal++;
                FailedReadBytesTotal++;
                ReadErrorTotal++;
                WriteErrorTotal++;

                return 0;
            }
        }

        public void SetTarget(TargetDevice device)
        {
            port.Write(new byte[] { 0x40, (byte)device }, 0, 2);
        }

        public static bool IsAvailable()
        {
            var spiServiceChannel = new ChannelFactory<IYmf825Client>(
                new NetNamedPipeBinding(),
                new EndpointAddress(ServiceAddress));
            var spiService = spiServiceChannel.CreateChannel();

            try
            {
                spiService.WriteBuffer(new byte[0], 0, 0);
                spiServiceChannel.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static IYmf825Client GetClient()
        {
            var spiServiceChannel = new ChannelFactory<IYmf825Client>(
                new NetNamedPipeBinding(),
                new EndpointAddress(ServiceAddress));
            var spiService = spiServiceChannel.CreateChannel();

            try
            {
                // spiService.SendReset();
                spiService.WriteBuffer(new byte[0], 0, 0);
            }
            catch (EndpointNotFoundException e)
            {
                throw new InvalidOperationException("Ymf825Server が起動していません。クライアントを実行する前に、サーバを起動してください。", e);
            }

            return spiService;
        }

        #endregion
    }

    public class SpiServiceTransferedEventArgs : EventArgs
    {
        #region -- Public Properties --

        public byte Address { get; }

        public byte Data { get; }

        #endregion

        #region -- Constructors --

        public SpiServiceTransferedEventArgs(byte address, byte data)
        {
            Address = address;
            Data = data;
        }

        #endregion
    }

    public class SpiServiceBurstWriteEventArgs : EventArgs
    {
        #region -- Public Properties --

        public byte Address { get; }

        public IReadOnlyList<byte> Data { get; }

        #endregion

        #region -- Constructors --

        public SpiServiceBurstWriteEventArgs(byte address, IReadOnlyList<byte> data)
        {
            Address = address;
            Data = data;
        }

        #endregion
    }
}
