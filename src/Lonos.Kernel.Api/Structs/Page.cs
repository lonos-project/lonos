﻿// This file is part of Lonos Project, an Operating System written in C#. Web: https://www.lonos.io
// Licensed under the GNU 2.0 license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace Lonos.Kernel.Core
{

    [Flags]
    public enum PageStatus : uint
    {
        Unset = 0,
        Free = 1,
        Used = 2,
        Reserved = 4,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public unsafe struct Page
    {
        public PageStatus Status;
        public ulong Flags;
        public Atomic UsageCount;

        public Addr PhysicalAddress;

        // If this is the head of an allocated block
        public Page* Head;

        // If this is the tail of an allocated block
        public Page* Tail;

        /// <summary>
        /// Number of reserved Page for this allocation. Only set if this is the Head page.
        /// </summary>
        public uint PagesUsed;

        public Page* Next;

        public static USize Size => 4096;

        public uint PageNum => PhysicalAddress / 4096;

        public bool Free => ((uint)Status).IsMaskSet((uint)PageStatus.Free);

        public bool Unset => ((uint)Status).IsMaskSet((uint)PageStatus.Unset);

        public bool Used => ((uint)Status).IsMaskSet((uint)PageStatus.Used);

        public bool Reserved => ((uint)Status).IsMaskSet((uint)PageStatus.Reserved);
    }
}