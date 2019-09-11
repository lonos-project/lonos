﻿// Copyright (c) MOSA Project. Licensed under the New BSD License.

using Mosa.Compiler.Framework.Linker;
using Mosa.Compiler.MosaTypeSystem;
using Mosa.Utility.BootImage;
using Mosa.Utility.Launcher;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;

using Mosa.Compiler.Framework.Linker.Elf;
using Mosa.Compiler.Common;

using Lonos.Kernel.Core;

namespace Lonos.Build
{
    public class LonosBuilder_Kernel : LonosBuilder
    {

        public LonosBuilder_Kernel(string inputAssembly) : base(inputAssembly)
        {
        }

        public override void Configure()
        {
            InputAssembly = BuildUtility.GetEnv("LONOS_EXE");

            Options = new LauncherOptions()
            {
                EnableSSA = true,
                EnableIROptimizations = true,
                EnableSparseConditionalConstantPropagation = true,
                EnableInlinedMethods = true,
                EnableLongExpansion = false, // see LonosBuilder_Loader
                EnableValueNumbering = true,
                TwoPassOptimizations = true,
                //EnableBitTracker = true,

                Emulator = EmulatorType.Bochs,
                ImageFormat = ImageFormat.IMG,
                //BootFormat = BootFormat.Multiboot_0_7,
                MultiBootV1 = true,
                PlatformType = PlatformType.x86,
                LinkerFormatType = LinkerFormatType.Elf32,
                EmulatorMemoryInMB = 128,
                DestinationDirectory = BuildUtility.GetEnv("LONOS_OSDIR"),
                FileSystem = FileSystem.FAT16,

                //UseMultiThreadingCompiler = false,
                EnableMultiThreading = false,

                InlinedIRMaximum = 12,
                BootLoader = BootLoader.Syslinux_3_72,
                VBEVideo = false,
                Width = 640,
                Height = 480,
                Depth = 32,
                //BaseAddress = 0x00500000,
                BaseAddress = Address.KernelBaseVirt,
                //EmitRelocations = false,
                //EmitSymbols = false,
                //Emitx86IRQMethods = true,
                //SerialConnectionOption = SerialConnectionOption.Pipe,
                // EmitAllSymbols = true,
                // EnableQemuGDB = true,
                SerialConnectionPort = 9999,
                SerialConnectionHost = "127.0.0.1",
                SerialPipeName = "MOSA",
                ExitOnLaunch = true,
                GenerateNASMFile = false,
                GenerateASMFile = false,
                GenerateMapFile = false,
                GenerateDebugFile = false,
                PlugKorlib = true,
                HuntForCorLib = true
            };

            //Options.GenerateNASMFile = true;
            Options.GenerateASMFile = true;
            Options.GenerateMapFile = true;
            Options.GenerateDebugFile = true;
            //Options.EmitRelocations = true;
            //Options.EmitSymbols = true; // Kernel Loader needs to resolve Adress of Start Method
            //Options.Emitx86IRQMethods = true;
            Options.EmitAllSymbols = true;


            Options.EnableSSA = false;
            Options.EnableIROptimizations = false;
            Options.EnableSparseConditionalConstantPropagation = false;
            Options.EnableInlinedMethods = false;
            Options.EnableLongExpansion = false;
            Options.EnableValueNumbering = false;
            Options.TwoPassOptimizations = false;
            //Options.EnableMethodScanner = true;

            //Options.VBEVideo = true;

            Section sect = null;
            Options.CreateExtraSections = () =>
            {
                return new List<Section>
                {
                    new Section
                    {
                        Name = "native",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(BuildUtility.GetEnv("LONOS_NATIVE_FILES"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "consolefont.regular",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(Path.Combine(BuildUtility.GetEnv("LONOS_PROJDIR"),"tools","consolefonts","Uni2-Terminus14.psf"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "consolefont.bold",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(Path.Combine(BuildUtility.GetEnv("LONOS_PROJDIR"),"tools","consolefonts","Uni2-TerminusBold14.psf"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "App.HelloKernel",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(Path.Combine(BuildUtility.GetEnv("LONOS_PROJDIR"),"os","App.HelloKernel.bin"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "App.HelloService",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(Path.Combine(BuildUtility.GetEnv("LONOS_PROJDIR"),"os","App.HelloService.bin"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "Service.Basic",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            var data = File.ReadAllBytes(Path.Combine(BuildUtility.GetEnv("LONOS_PROJDIR"),"os","Lonos.Service.Basic.bin"));
                            writer.Write(data);
                            section.Size = (uint)data.Length;
                        }
                    },
                    new Section
                    {
                        Name = "elf.header",
                        Type = SectionType.ProgBits,
                        AddressAlignment = 0x1000,
                        Address = 0x4FF000,
                        Size=0x1000,
                        EmitMethod = (section, writer) =>
                        {
                            sect = section; //TODO: Could set outsite
                            writer.Write(new byte[]{1,2,3,4,5,6,7,8,9});
                            section.Size=0x1000;
                        }
                    }
                };
            };

            Options.CreateExtraProgramHeaders = () =>
            {
                return new List<ProgramHeader>
                 {
                    new ProgramHeader
                    {
                        Alignment = sect.AddressAlignment,
                        Offset = sect.Offset,
                        VirtualAddress = sect.Address,
                        PhysicalAddress = sect.Address,
                        FileSize = 0x1000,
                        MemorySize = 0x1000,
                        Type = ProgramHeaderType.Load,
                        Flags = ProgramHeaderFlags.Read
                    }
                 };
            };
        }

    }
}
