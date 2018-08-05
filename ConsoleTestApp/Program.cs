﻿using System;
using System.Threading;
using Ymf825;
using Ymf825.Driver;
using Ymf825.IO;

namespace TestConsoleApp
{
    internal class Program
    {
        private static void Main()
        {
            Console.WriteLine("Image type: {0}bit", Environment.Is64BitProcess ? "64" : "32");

            if (D2XxSpi.DeviceCount < 1)
                return;

            using (var ymf825 = SelectInterface())
            {
                var driver = new Ymf825Driver(ymf825);
                driver.EnableSectionMode();

                Console.WriteLine("Software Reset");
                driver.ResetSoftware();

                {
                    Console.WriteLine("Tone Init");
                    var tones = new ToneParameterCollection { [0] = ToneParameter.GetSine() };

                    driver.Section(() =>
                    {
                        driver.WriteContentsData(tones, 0);
                        driver.SetSequencerSetting(SequencerSetting.AllKeyOff | SequencerSetting.AllMute | SequencerSetting.AllEgReset |
                                                   SequencerSetting.R_FIFOR | SequencerSetting.R_SEQ | SequencerSetting.R_FIFO);
                    }, 1);

                    driver.Section(() =>
                    {
                        driver.SetSequencerSetting(SequencerSetting.Reset);

                        driver.SetToneFlag(0, false, true, true);
                        driver.SetChannelVolume(31, true);
                        driver.SetVibratoModuration(0);
                        driver.SetFrequencyMultiplier(1, 0);
                    });
                }

                var noteon = new Action<int>(key =>
                {
                    Ymf825Driver.GetFnumAndBlock(key, out var fnum, out var block, out var correction);
                    Ymf825Driver.ConvertForFrequencyMultiplier(correction, out var integer, out var fraction);
                    var freq = Ymf825Driver.CalcFrequency(fnum, block);
                    Console.WriteLine("key: {0}, freq: {4:f1} Hz, fnum: {5:f0}, block: {6}, correction: {1:f3}, integer: {2}, fraction: {3}", key, correction, integer, fraction, freq, fnum, block);

                    driver.Section(() =>
                    {
                        driver.SetVoiceNumber(0);
                        driver.SetVoiceVolume(15);
                        driver.SetFrequencyMultiplier(integer, fraction);
                        driver.SetFnumAndBlock((int)Math.Round(fnum), block);
                        driver.SetToneFlag(0, true, false, false);
                    });
                });

                var noteoff = new Action(() =>
                {
                    driver.Section(() => driver.SetToneFlag(0, false, false, false));
                });

                var index = 0;
                var score = new[]
                {
                    60, 62, 64, 65, 67, 69, 71, 72,
                    72, 74, 76, 77, 79, 81, 83, 84,
                    84, 83, 81, 79, 77, 76, 74, 72,
                    72, 71, 69, 67, 65, 64, 62, 60
                };
                while (true)
                {
                    const int noteOnTime = 250;
                    const int sleepTime = 0;

                    noteon(score[index]);
                    Thread.Sleep(noteOnTime);
                    noteoff();

                    Thread.Sleep(sleepTime);

                    if (Console.KeyAvailable)
                        break;

                    index++;
                    if (index >= score.Length)
                        index = 0;
                }

                ymf825.InvokeHardwareReset();
            }
        }

        private static IYmf825 SelectInterface()
        {
            var deviceInfoList = D2XxSpi.GetDeviceInfoList();
            var deviceIndex = 0;
            int interfaceIndex;

            if (deviceInfoList.Length > 1)
            {
                Console.WriteLine();

                while (true)
                {
                    for (var i = 0; i < deviceInfoList.Length; i++)
                        Console.WriteLine($"  {i}: {deviceInfoList[i].Description} ({deviceInfoList[i].SerialNumber})");

                    Console.WriteLine();
                    Console.Write("Select device [0]: ");

                    var index = Console.ReadLine();
                    if (index == null)
                        Environment.Exit(0);

                    if (int.TryParse(index, out deviceIndex) && deviceIndex >= 0 && deviceIndex < deviceInfoList.Length)
                        break;

                    Console.WriteLine();
                }
            }

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("  0: AE-FT232HL (for CBW-YMF825-BB)");
                Console.WriteLine("  1: Adafruit FT232H Breakout");
                Console.WriteLine();
                Console.Write("Select interface [0]: ");

                var index = Console.ReadLine();
                if (index == null)
                    Environment.Exit(0);

                if (int.TryParse(index, out interfaceIndex) && interfaceIndex >= 0 && interfaceIndex < 2)
                    break;

                Console.WriteLine();
            }

            if (interfaceIndex == 0)
                return new AeFt232HInterface(deviceIndex);

            return new AdafruitFt232HInterface(deviceIndex);
        }
    }
}
