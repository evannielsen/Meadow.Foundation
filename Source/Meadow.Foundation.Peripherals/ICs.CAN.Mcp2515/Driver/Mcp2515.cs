using Meadow.Hardware;
using Meadow.Logging;
using System;
using System.Threading;

namespace Meadow.Foundation.ICs.CAN;

/// <summary>
/// Encapsulation for the Microchip MCP2515 CAN controller
/// </summary>
public partial class Mcp2515 : ICanController
{
    public const SpiClockConfiguration.Mode DefaultSpiMode = SpiClockConfiguration.Mode.Mode0;

    private byte BRP_Default = 0x01;
    private byte SJW_Default = 0x01;
    private byte SAM_1x = 0x00;
    private byte SAM_3x = 0x40;
    private byte PHASE_SEG1_Default = 0x04;// = 0x01;
    private byte PHASE_SEG2_Default = 0x03;//0x02;
    private byte PROP_SEG_Default = 0x02;// 0x01;

    private ICanBus? _busInstance;
    private CanOscillator _oscillator;

    private ISpiBus SpiBus { get; }
    private IDigitalOutputPort ChipSelect { get; }
    private Logger? Logger { get; }
    private IDigitalInterruptPort? InterruptPort { get; }

    public Mcp2515(
        ISpiBus bus,
        IDigitalOutputPort chipSelect,
        CanOscillator oscillator = CanOscillator.Osc_8MHz,
        IDigitalInterruptPort? interruptPort = null,
        Logger? logger = null)
    {
        if (interruptPort != null)
        {
            if (interruptPort.InterruptMode != InterruptMode.EdgeFalling)
            {
                throw new ArgumentException("InterruptPort must be a falling-edge interrupt");
            }
        }

        SpiBus = bus;
        ChipSelect = chipSelect;
        Logger = logger;
        InterruptPort = interruptPort;
        _oscillator = oscillator;
    }

    /// <inheritdoc/>
    public ICanBus CreateCanBus(CanBitrate bitrate, int busNumber = 0)
    {
        if (_busInstance == null)
        {
            Initialize(bitrate, _oscillator);

            _busInstance = new Mcp2515CanBus(this);
        }

        return _busInstance;
    }

    private void Initialize(CanBitrate bitrate, CanOscillator oscillator)
    {
        Reset();

        Thread.Sleep(10);

        // put the chip into config mode
        var mode = GetMode();
        if (mode != Mode.Configure)
        {
            SetMode(Mode.Configure);
        }

        ClearFiltersAndMasks();

        ClearControlBuffers();

        if (InterruptPort != null)
        {
            // TODO: add error condition handling
            //ConfigureInterrupts(InterruptEnable.RXB0 | InterruptEnable.RXB1 | InterruptEnable.ERR | InterruptEnable.MSG_ERR);
            ConfigureInterrupts(InterruptEnable.RXB0 | InterruptEnable.RXB1);
            ClearInterrupt((InterruptFlag)0xff);
        }
        else
        {
            ConfigureInterrupts(InterruptEnable.DisableAll);
        }

        ModifyRegister(Register.RXB0CTRL,
            0x60 | 0x04 | 0x07,
            0x00 | 0x04 | 0x00);
        ModifyRegister(Register.RXB1CTRL,
            0x60 | 0x07,
            0x00 | 0x01);

        DisableFilters();

        LogRegisters(Register.RXF0SIDH, 14);
        LogRegisters(Register.CANSTAT, 2);
        LogRegisters(Register.RXF3SIDH, 14);
        LogRegisters(Register.RXM0SIDH, 8);
        LogRegisters(Register.CNF3, 6);

        var cfg = GetConfigForOscillatorAndBitrate(oscillator, bitrate);
        WriteRegister(Register.CNF1, cfg.CFG1);
        WriteRegister(Register.CNF2, cfg.CFG2);
        WriteRegister(Register.CNF3, cfg.CFG3);
        LogRegisters(Register.CNF3, 3);

        SetMode(Mode.Normal);
    }

    private void DisableFilters()
    {
        ModifyRegister(Register.RXB0CTRL,
            0x60,
            0x60);
        ModifyRegister(Register.RXB1CTRL,
            0x60,
            0x60);
    }

    private void ClearInterrupt(InterruptFlag flag)
    {
        ModifyRegister(Register.CANINTF, (byte)flag, 0);

        LogRegisters(Register.CANINTF, 1);
    }

    private void WriteFrame(ICanFrame frame, int bufferNumber)
    {
        if (frame is DataFrame df)
        {
            var ctrl_reg = bufferNumber switch
            {
                0 => Register.TXB0CTRL,
                1 => Register.TXB1CTRL,
                2 => Register.TXB2CTRL,
                _ => throw new ArgumentOutOfRangeException()
            };

            if (frame is ExtendedDataFrame edf)
            {
                var eid0 = (byte)(edf.ID & 0xff);
                var eid8 = (byte)(edf.ID >> 8);
                var id = edf.ID >> 16;
                var sidh = (byte)(id >> 5);
                var sidl = (byte)(id & 3);
                sidl += (byte)((id & 0x1c) << 3);
                sidl |= TXB_EXIDE_MASK;

                WriteRegister(ctrl_reg + 1, sidh);
                WriteRegister(ctrl_reg + 2, sidl);
                WriteRegister(ctrl_reg + 3, eid8);
                WriteRegister(ctrl_reg + 4, eid0);
            }
            else if (frame is StandardDataFrame sdf)
            {
                // put the frame data into a buffer (0-2)
                var sidh = (byte)(sdf.ID >> 3);
                var sidl = (byte)(sdf.ID << 5 & 0xe0);
                WriteRegister(ctrl_reg + 1, sidh);
                WriteRegister(ctrl_reg + 2, sidl);
            }
            // TODO: handle RTR

            WriteRegister(ctrl_reg + 5, (byte)df.Payload.Length);
            byte i = 0;
            foreach (var b in df.Payload)
            {
                WriteRegister(ctrl_reg + 6 + i, b);
                i++;
            }

            // transmit the buffer
            WriteRegister(ctrl_reg, 0x08);
        }
        else
        {
            throw new NotSupportedException($"Sending frames of type {frame.GetType().Name} is not supported");
        }
    }

    private void Reset()
    {
        Span<byte> tx = stackalloc byte[1];
        Span<byte> rx = stackalloc byte[1];

        tx[0] = (byte)Command.Reset;

        SpiBus.Exchange(ChipSelect, tx, rx);
    }

    private void LogRegisters(Register start, byte count)
    {
        var values = ReadRegister(start, count);

        Resolver.Log.Info($"{(byte)start:X2} ({start}): {BitConverter.ToString(values)}");
    }

    private Mode GetMode()
    {
        return (Mode)(ReadRegister(Register.CANSTAT)[0] | 0xE0);
    }

    private void SetMode(Mode mode)
    {
        ModifyRegister(Register.CANCTRL, (byte)Control.REQOP, (byte)mode);
    }

    private Status GetStatus()
    {
        Span<byte> tx = stackalloc byte[2];
        Span<byte> rx = stackalloc byte[2];

        tx[0] = (byte)Command.ReadStatus;
        tx[1] = 0xff;

        SpiBus.Exchange(ChipSelect, tx, rx);

        return (Status)rx[1];
    }

    private void WriteRegister(Register register, byte value)
    {
        Span<byte> tx = stackalloc byte[3];
        Span<byte> rx = stackalloc byte[3];

        tx[0] = (byte)Command.Write;
        tx[1] = (byte)register;
        tx[2] = value;

        SpiBus.Exchange(ChipSelect, tx, rx);
    }

    private void WriteRegister(Register register, Span<byte> data)
    {
        Span<byte> tx = stackalloc byte[data.Length + 2];
        Span<byte> rx = stackalloc byte[data.Length + 2];

        tx[0] = (byte)Command.Write;
        tx[1] = (byte)register;
        data.CopyTo(tx.Slice(2));

        SpiBus.Exchange(ChipSelect, tx, rx);
    }

    private byte[] ReadRegister(Register register, byte length = 1)
    {
        Span<byte> tx = stackalloc byte[2 + length];
        Span<byte> rx = stackalloc byte[2 + length];

        tx[0] = (byte)Command.Read;
        tx[1] = (byte)register;

        SpiBus.Exchange(ChipSelect, tx, rx);

        return rx.Slice(2).ToArray();
    }

    private void ModifyRegister(Register register, byte mask, byte value)
    {
        Span<byte> tx = stackalloc byte[4];
        Span<byte> rx = stackalloc byte[4];

        tx[0] = (byte)Command.Bitmod;
        tx[1] = (byte)register;
        tx[2] = mask;
        tx[3] = value;

        SpiBus.Exchange(ChipSelect, tx, rx);
    }

    private void EnableMasksAndFilters(bool enable)
    {
        if (enable)
        {
            ModifyRegister(Register.RXB0CTRL, 0x64, 0x00);
            ModifyRegister(Register.RXB1CTRL, 0x60, 0x00);
        }
        else
        {
            ModifyRegister(Register.RXB0CTRL, 0x64, 0x60);
            ModifyRegister(Register.RXB1CTRL, 0x60, 0x60);
        }
    }

    private void ConfigureInterrupts(InterruptEnable interrupts)
    {
        WriteRegister(Register.CANINTE, (byte)interrupts);
    }

    private void ClearFiltersAndMasks()
    {
        Span<byte> zeros12 = stackalloc byte[12];
        WriteRegister(Register.RXF0SIDH, zeros12);
        WriteRegister(Register.RXF3SIDH, zeros12);

        Span<byte> zeros8 = stackalloc byte[8];
        WriteRegister(Register.RXM0SIDH, zeros8);
    }

    private void ClearControlBuffers()
    {
        Span<byte> zeros14 = stackalloc byte[14];
        WriteRegister(Register.TXB0CTRL, zeros14);
        WriteRegister(Register.TXB1CTRL, zeros14);
        WriteRegister(Register.TXB2CTRL, zeros14);

        WriteRegister(Register.RXB0CTRL, 0);
        WriteRegister(Register.RXB1CTRL, 0);
    }

    private DataFrame ReadDataFrame(RxBufferNumber bufferNumber)
    {
        var sidh_reg = bufferNumber == RxBufferNumber.RXB0 ? Register.RXB0SIDH : Register.RXB1SIDH;
        var ctrl_reg = bufferNumber == RxBufferNumber.RXB0 ? Register.RXB0CTRL : Register.RXB1CTRL;
        var data_reg = bufferNumber == RxBufferNumber.RXB0 ? Register.RXB0DATA : Register.RXB1DATA;
        var int_flag = bufferNumber == RxBufferNumber.RXB0 ? InterruptFlag.RX0IF : InterruptFlag.RX1IF;

        // read 5 bytes
        var buffer = ReadRegister(sidh_reg, 5);

        int id = (buffer[MCP_SIDH] << 3) + (buffer[MCP_SIDL] >> 5);

        bool isExtended = false;

        // check to see if it's an extended ID
        if ((buffer[MCP_SIDL] & TXB_EXIDE_MASK) == TXB_EXIDE_MASK)
        {
            id = (id << 2) + (buffer[MCP_SIDL] & 0x03);
            id = (id << 8) + buffer[MCP_EID8];
            id = (id << 8) + buffer[MCP_EID0];
            isExtended = true;
        }

        byte dataLengthCode = (byte)(buffer[MCP_DLC] & DLC_MASK);
        if (dataLengthCode > 8) throw new Exception($"DLC of {dataLengthCode} is > 8 bytes");

        // see if it's a remote transmission request
        var isRemoteTransmitRequest = false;
        var ctrl = ReadRegister(ctrl_reg)[0];
        if ((ctrl & RXBnCTRL_RTR) == RXBnCTRL_RTR)
        {
            isRemoteTransmitRequest = true;
        }

        // create the frame
        DataFrame frame;

        if (isExtended)
        {
            if (isRemoteTransmitRequest)
            {
                frame = new ExtendedRtrFrame
                {
                    ID = id,
                };
            }
            else
            {
                frame = new ExtendedDataFrame
                {
                    ID = id,
                };
            }
        }
        else
        {
            if (isRemoteTransmitRequest)
            {
                frame = new StandardRtrFrame
                {
                    ID = id,
                };
            }
            else
            {
                frame = new StandardDataFrame
                {
                    ID = id,
                };
            }
        }

        // read the frame data
        frame.Payload = ReadRegister(data_reg, dataLengthCode);

        // clear the interrupt flag
        if (InterruptPort != null)
        {
            ModifyRegister(Register.CANINTF, (byte)int_flag, 0);
        }

        return frame;
    }
}