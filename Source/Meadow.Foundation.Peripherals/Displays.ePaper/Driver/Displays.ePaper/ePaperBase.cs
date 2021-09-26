﻿using Meadow.Devices;
using Meadow.Hardware;
using System;

namespace Meadow.Foundation.Displays.ePaper
{
    /// <summary>
    ///     Provide an interface for ePaper monochrome displays
    /// </summary>
    public abstract class EpdBase : SpiDisplayBase
    {
        public override DisplayColorMode ColorMode => DisplayColorMode.Format1bpp;

        protected readonly byte[] imageBuffer;

        public override int Width { get; }
        public override int Height { get; }

        private EpdBase()
        { }

        public EpdBase(IMeadowDevice device, ISpiBus spiBus, IPin chipSelectPin, IPin dcPin, IPin resetPin, IPin busyPin,
            int width, int height)
        {
            Width = width;
            Height = height;

            dataCommandPort = device.CreateDigitalOutputPort(dcPin, false);
            resetPort = device.CreateDigitalOutputPort(resetPin, true);
            busyPort = device.CreateDigitalInputPort(busyPin);

            spi = new SpiPeripheral(spiBus, device.CreateDigitalOutputPort(chipSelectPin));

            imageBuffer = new byte[Width * Height / 8];

            for (int i = 0; i < Width * Height / 8; i++)
            {
                imageBuffer[i] = 0xff;
            }

            Initialize();
        }

        protected abstract void Initialize();

        public override void Clear(bool updateDisplay = false)
        {
            Clear(false, updateDisplay);
        }

        /// <summary>
        ///     Clear the display.
        /// </summary>
        /// <param name="color">Color to set the display (not used on ePaper displays)</param>
        /// <param name="updateDisplay">Update the dipslay once the buffer has been cleared when true.</param>
        public void Clear(Color color, bool updateDisplay = false)
        {
            Clear(color.Color1bpp, updateDisplay);
        }

        /// <summary>
        ///     Clear the display.
        /// </summary>
        /// <param name="colored">Set the display dark when true</param>
        /// <param name="updateDisplay">Update the dipslay once the buffer has been cleared when true.</param>
        public void Clear(bool colored, bool updateDisplay = false)
        {
            //   ClearFrameMemory((byte)(colored ? 0 : 0xFF));
            //   DisplayFrame();

            for (int i = 0; i < imageBuffer.Length; i++)
            {
                imageBuffer[i] = colored ? (byte)0 : (byte)255;
            }

            if (updateDisplay)
            {
                Show();
            }
        }

        /// <summary>
        ///     Draw a single pixel 
        /// </summary>
        /// <param name="x">x location </param>
        /// <param name="y">y location</param>
        /// <param name="colored">Turn the pixel on (true) or off (false).</param>
        public override void DrawPixel(int x, int y, bool colored)
        {
            if (colored)
            {
                imageBuffer[(x + y * Width) / 8] &= (byte)~(0x80 >> (x % 8));
            }
            else
            {
                imageBuffer[(x + y * Width) / 8] |= (byte)(0x80 >> (x % 8));
            }
        }

        /// <summary>
        ///     Draw a single pixel 
        /// </summary>
        /// <param name="x">x location </param>
        /// <param name="y">y location</param>
        /// <param name="color">Color of pixel.</param>
        public override void DrawPixel(int x, int y, Color color)
        {
            DrawPixel(x, y, color.Color1bpp);
        }

        public override void InvertPixel(int x, int y)
        {
            imageBuffer[(x + y * Width) / 8] ^= (byte)(0x80 >> (x % 8));
        }

        /// <summary>
        ///     Draw a single pixel 
        /// </summary>
        /// <param name="x">x location </param>
        /// <param name="y">y location</param>
        /// <param name="r">y location</param>
        /// <param name="g">y location</param>
        /// <param name="b">y location</param>
        public void DrawPixel(int x, int y, byte r, byte g, byte b)
        {
            DrawPixel(x, y, r > 0 || g > 0 || b > 0);
        }

        /// <summary>
        ///     Draw the display buffer to screen
        /// </summary>
        public override void Show(int left, int top, int right, int bottom)
        {
            SetFrameMemory(imageBuffer, left, top, right - left, top - bottom);
            DisplayFrame();
        }

        /// <summary>
        ///     Draw the display buffer to screen
        /// </summary>
        public override void Show()
        {
            SetFrameMemory(imageBuffer);
            DisplayFrame();
        }

        public virtual void SetFrameMemory(byte[] image_buffer,
                                int x,
                                int y,
                                int image_width,
                                int image_height)
        {
            int x_end;
            int y_end;

            if (image_buffer == null ||
                x < 0 || image_width < 0 ||
                y < 0 || image_height < 0)
            {
                return;
            }

            /* x point must be the multiple of 8 or the last 3 bits will be ignored */
            x &= 0xF8;
            image_width &= 0xF8;
            if (x + image_width >= Width)
            {
                x_end = (int)Width - 1;
            }
            else
            {
                x_end = x + image_width - 1;
            }

            if (y + image_height >= Height)
            {
                y_end = (int)Height - 1;
            }
            else
            {
                y_end = y + image_height - 1;
            }

            SetMemoryArea(x, y, x_end, y_end);
            SetMemoryPointer(x, y);
            SendCommand(Command.WRITE_RAM);
            /* send the image data */
            for (int j = 0; j < y_end - y + 1; j++)
            {
                for (int i = 0; i < (x_end - x + 1) / 8; i++)
                {
                    SendData(image_buffer[i + j * (image_width / 8)]);
                }
            }
        }

        public virtual void SetFrameMemory(byte[] image_buffer)
        {
            SetMemoryArea(0, 0, (int)Width - 1, (int)Height - 1);
            SetMemoryPointer(0, 0);
            SendCommand(Command.WRITE_RAM);
            /* send the image data */
            for (int i = 0; i < Width / 8 * Height; i++)
            {
                SendData(image_buffer[i]);
            }
        }

        public virtual void ClearFrameMemory(byte color)
        {
            SetMemoryArea(0, 0, (int)Width - 1, (int)Height - 1);
            SetMemoryPointer(0, 0);
            SendCommand(Command.WRITE_RAM);
            /* send the color data */
            for (int i = 0; i < Width / 8 * Height; i++)
            {
                SendData(color);
            }
        }

        public virtual void DisplayFrame()
        {
            SendCommand(Command.DISPLAY_UPDATE_CONTROL_2);
            SendData(0xC4);
            SendCommand(Command.MASTER_ACTIVATION);
            SendCommand(Command.TERMINATE_FRAME_READ_WRITE);
            WaitUntilIdle();
        }

        void SetMemoryArea(int x_start, int y_start, int x_end, int y_end)
        {
            SendCommand(Command.SET_RAM_X_ADDRESS_START_END_POSITION);
            /* x point must be the multiple of 8 or the last 3 bits will be ignored */
            SendData((x_start >> 3) & 0xFF);
            SendData((x_end >> 3) & 0xFF);
            SendCommand(Command.SET_RAM_Y_ADDRESS_START_END_POSITION);
            SendData(y_start & 0xFF);
            SendData((y_start >> 8) & 0xFF);
            SendData(y_end & 0xFF);
            SendData((y_end >> 8) & 0xFF);
        }

        void SetMemoryPointer(int x, int y)
        {
            SendCommand(Command.SET_RAM_X_ADDRESS_COUNTER);
            /* x point must be the multiple of 8 or the last 3 bits will be ignored */
            SendData((x >> 3) & 0xFF);
            SendCommand(Command.SET_RAM_Y_ADDRESS_COUNTER);
            SendData(y & 0xFF);
            SendData((y >> 8) & 0xFF);
            WaitUntilIdle();
        }

        protected void Sleep()
        {
            SendCommand(Command.DEEP_SLEEP_MODE);
            WaitUntilIdle();
        }

        protected void SendCommand(Command command)
        {
            SendCommand((byte)command);
        }

        public enum Command : byte
        {
            DRIVER_OUTPUT_CONTROL = 0x01,
            BOOSTER_SOFT_START_CONTROL = 0x0C,
            GATE_SCAN_START_POSITION = 0x0F,
            DEEP_SLEEP_MODE = 0x10,
            DATA_ENTRY_MODE_SETTING = 0x11,
            SW_RESET = 0x12,
            TEMPERATURE_SENSOR_CONTROL = 0x1A,
            MASTER_ACTIVATION = 0x20,
            DISPLAY_UPDATE_CONTROL_1 = 0x21,
            DISPLAY_UPDATE_CONTROL_2 = 0x22,
            WRITE_RAM = 0x24,
            WRITE_VCOM_REGISTER = 0x2C,
            WRITE_LUT_REGISTER = 0x32,
            SET_DUMMY_LINE_PERIOD = 0x3A,
            SET_GATE_TIME = 0x3B,
            BORDER_WAVEFORM_CONTROL = 0x3C,
            SET_RAM_X_ADDRESS_START_END_POSITION = 0x44,
            SET_RAM_Y_ADDRESS_START_END_POSITION = 0x45,
            SET_RAM_X_ADDRESS_COUNTER = 0x4E,
            SET_RAM_Y_ADDRESS_COUNTER = 0x4F,
            TERMINATE_FRAME_READ_WRITE = 0xFF,
        }
    }

}