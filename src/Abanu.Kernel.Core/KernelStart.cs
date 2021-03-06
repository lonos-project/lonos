﻿// This file is part of Abanu, an Operating System written in C#. Web: https://www.abanu.org
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using Abanu.Kernel.Core.Api;
using Abanu.Kernel.Core.Boot;
using Abanu.Kernel.Core.Collections;
using Abanu.Kernel.Core.Devices;
using Abanu.Kernel.Core.Diagnostics;
using Abanu.Kernel.Core.Elf;
using Abanu.Kernel.Core.External;
using Abanu.Kernel.Core.Interrupts;
using Abanu.Kernel.Core.MemoryManagement;
using Abanu.Kernel.Core.PageManagement;
using Abanu.Kernel.Core.Processes;
using Abanu.Kernel.Core.Scheduling;
using Abanu.Kernel.Core.SysCalls;
using Abanu.Kernel.Core.Tasks;
using Mosa.Runtime;
using Mosa.Runtime.x86;

#pragma warning disable CA2000 // Dispose objects before losing scope

namespace Abanu.Kernel.Core
{

    public static class KernelStart
    {

        public static unsafe void Main()
        {
            try
            {

                ManagedMemory.InitializeGCMemory();

                // Initialize static fields
                StartUp.InitializeAssembly();

                KMath.Init();

                BootInfo.SetupStage1();

                // Write protect all possible pages and disallow execution on all non-code pages.
                Memory.InitialKernelProtect();

                ApiContext.Current = new ApiHost();
                Assert.Setup(AssertError);

                // Setup some pseudo devices
                DeviceManager.InitStage1();

                //Setup Output and Debug devices
                DeviceManager.InitStage2();

                // Write first output
                KernelMessage.WriteLine("<KERNEL:CONSOLE:BEGIN>");
                PerformanceCounter.Setup(BootInfo.Header->KernelBootStartCycles);
                KernelMessage.WriteLine("Starting Abanu Kernel...");

                KernelMessage.WriteLine("KConfig.UseKernelMemoryProtection: {0}", KConfig.UseKernelMemoryProtection);
                KernelMessage.WriteLine("KConfig.UsePAE: {0}", KConfig.UsePAE);
                KernelMessage.WriteLine("Apply PageTableType: {0}", (uint)BootInfo.Header->PageTableType);
                KernelMessage.WriteLine("GCInitialMemory: {0:X8}-{1:X8}", Address.GCInitialMemory, Address.GCInitialMemory + Address.GCInitialMemorySize - 1);

                CodeTests.Run();

                // Detect environment (Memory Maps, Video Mode, etc.)
                BootInfo.SetupStage2();

                KernelMemoryMapManager.Setup();
                //KernelMemoryMapManager.Allocate(0x1000 * 1000, BootInfoMemoryType.PageDirectory);

                // Read own ELF-Headers and Sections
                KernelElf.Setup();

                // Initialize the embedded code (actually only a little proof of concept code)
                NativeCalls.Setup();

                PhysicalPageManager.Setup();

                NonThreadTests.TestPhysicalPageAllocation();

                VirtualPageManager.Setup();

                // Setup final Memory Allocator
                Memory.Setup();

                // Now Memory Sub System is working. At this point it's valid
                // to allocate memory dynamically

                // If we have a Framebuffer, lets try to initialize it.
                DeviceManager.InitFrameBuffer();

                // Setup Programmable Interrupt Table
                PIC.Setup();

                // Setup Interrupt Descriptor Table
                // Important Note: IDT depends on GDT. Never setup IDT before GDT.
                IDTManager.Setup();

                InitializeUserMode();
                SysCallManager.Setup();

                KernelMessage.WriteLine("Initialize Runtime Metadata");
                StartUp.InitializeRuntimeMetadata();

                KernelMessage.WriteLine("Performing some Non-Thread Tests");
                NonThreadTests.RunTests();
            }
            catch (Exception ex)
            {
                Panic.Error(ex.Message);
            }

            if (KConfig.SingleThread)
                StartupStage2();
            else
                ProcessManager.Setup(StartupStage2);
        }

        private static ProcessService Serv;
        private static ProcessService FileServ;

        private static void StartupStage2()
        {
            try
            {
                if (!KConfig.SingleThread)
                {
                    Scheduler.CreateThread(ProcessManager.System, new ThreadStartOptions(BackgroundWorker.ThreadMain) { DebugName = "BackgroundWorker", Priority = -5 }).Start();

                    ThreadTests.StartTestThreads();

                    // Start some applications

                    var fileProc = ProcessManager.CreateProcess("Service.Basic");
                    FileServ = fileProc.Service;
                    fileProc.Start();

                    KernelMessage.WriteLine("Waiting for Service");
                    while (FileServ.Status != ServiceStatus.Ready)
                    {
                        Scheduler.Sleep(0);
                    }
                    KernelMessage.WriteLine("Service Ready");

                    var conProc = ProcessManager.CreateProcess("Service.ConsoleServer");
                    conProc.Start();
                    var conServ = conProc.Service;
                    KernelMessage.WriteLine("Waiting for ConsoleServer");
                    while (conServ.Status != ServiceStatus.Ready)
                    {
                        Scheduler.Sleep(0);
                    }
                    KernelMessage.WriteLine("ConsoleServer Ready");

                    //var buf = Abanu.Runtime.SysCalls.RequestMessageBuffer(4096, FileServ.Process.ProcessID);
                    //var kb = Abanu.Runtime.SysCalls.OpenFile(buf, "/dev/keyboard");
                    //KernelMessage.Write("kb Handle: {0:X8}", kb);
                    //buf.Size = 4;
                    //Abanu.Runtime.SysCalls.WriteFile(kb, buf);
                    //Abanu.Runtime.SysCalls.ReadFile(kb, buf);

                    //var procHostCommunication = ProcessManager.StartProcess("Service.HostCommunication");
                    //ServHostCommunication = new Service(procHostCommunication);
                    //// TODO: Optimize Registration
                    //SysCallManager.SetCommandProcess(SysCallTarget.HostCommunication_CreateProcess, procHostCommunication);

                    var proc = ProcessManager.CreateProcess("App.HelloService");
                    Serv = proc.Service;
                    proc.Start();

                    var p2 = ProcessManager.CreateProcess("App.HelloKernel");
                    p2.Start();
                    //p2.Threads[0].SetArgument(0, 0x90);
                    //p2.Threads[0].SetArgument(4, 0x94);
                    //p2.Threads[0].SetArgument(8, 0x98);
                    p2.Threads[0].Debug = true;

                    var p3 = ProcessManager.CreateProcess("App.Shell");
                    p3.Start();

                    ProcessManager.System.Threads[0].Status = ThreadStatus.Terminated;
                }
                VirtualPageManager.SetTraceOptions(new PageFrameAllocatorTraceOptions { Enabled = true, MinPages = 1 });

                KernelMessage.WriteLine("Enter Main Loop");
                AppMain();
            }
            catch (Exception ex)
            {
                Panic.Error(ex.Message);
            }
        }

        internal static Addr TssAddr = null;

        private static unsafe void InitializeUserMode()
        {
            if (!KConfig.UseUserMode)
                return;

            if (KConfig.UseTaskStateSegment)
            {
                TssAddr = VirtualPageManager.AllocatePages(1);
                PageTable.KernelTable.SetWritable(TssAddr, 4096);
                KernelMemoryMapManager.Header->Used.Add(new KernelMemoryMap(TssAddr, 4096, BootInfoMemoryType.TSS, AddressSpaceKind.Virtual));
            }

            // Disabling Interrupts here is very important, otherwise we will get randomly an Invalid TSS Exception.
            Uninterruptible.Execute(() =>
            {
                GDT.SetupUserMode(TssAddr);
            });
        }

        private static void AppMain()
        {
            KernelMessage.WriteLine("Kernel ready");

            ThreadTests.TriggerTestPassed();

            // We have nothing to do (yet). So let's stop.
            Debug.Break();
        }

        private static void Dummy()
        {
            // This is a dummy call, that get never executed.
            // Its required, because we need a real reference to Mosa.Runtime.x86
            // Without that, the .NET compiler will optimize that reference away
            // if its nowhere used. Than the Compiler doesn't know about that reference
            // and the Compilation will fail
            Mosa.Runtime.x86.Internal.ExceptionHandler();
        }

        private static void AssertError(string message, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0)
        {
            var sb = new StringBuffer();
            sb.Append(message, arg1, arg2, arg3);
            Panic.Error(sb.CreateString());
        }

    }

}
