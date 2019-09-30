﻿using System;
using System.Threading;
using Meadow;
using Meadow.Devices;
using Meadow.Foundation.Displays.ePaper;
using Meadow.Foundation.Graphics;
using Meadow.Hardware;

namespace WaveShare_ePaper_Sample
{
    public class WaveShareEPaperApp : App<F7Micro, WaveShareEPaperApp>
    {
        EPD2i9b display;
        ISpiBus spiBus;

        public WaveShareEPaperApp()
        {
            Console.WriteLine("ePaper sample");
            Console.WriteLine("Create Spi bus");

            spiBus = Device.CreateSpiBus();// Device.Pins.SCK, Device.Pins.MOSI, Device.Pins.MISO, 2000);


            Console.WriteLine("Create display driver instance");
            display = new EPD2i9b(device: Device, spiBus: spiBus,
                chipSelectPin: Device.Pins.D02,
                dcPin: Device.Pins.D01,
                resetPin: Device.Pins.D00,
                busyPin: Device.Pins.D03);


            var graphics = new GraphicsLibrary(display);

            //any color but black will show the ePaper alternate color 
            graphics.DrawRectangle(1, 1, 126, 32, Meadow.Foundation.Color.Red, false);

            graphics.CurrentFont = new Font8x12();
            graphics.DrawText(2, 2, ".NET Conf 2019");
            graphics.DrawText(2, 20, "Meadow F7");

            int ySpacing = 6;

            for (int i = 0; i < 3; i++)
            {
                graphics.DrawLine(2, 70 + ySpacing * i, 22, 50 + ySpacing * i);
                graphics.DrawLine(22, 50 + ySpacing * i, 42, 70 + ySpacing * i);
                graphics.DrawLine(44, 70 + ySpacing * i, 64, 50 + ySpacing * i);
                graphics.DrawLine(64, 50 + ySpacing * i, 84, 70 + ySpacing * i);
                graphics.DrawLine(86, 70 + ySpacing * i, 106, 50 + ySpacing * i);
                graphics.DrawLine(106, 50 + ySpacing * i, 126, 70 + ySpacing * i);
            }

            Console.WriteLine("Show");

            graphics.Show();
        }
    }
}