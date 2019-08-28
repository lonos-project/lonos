﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lonos.kernel.core
{
    public enum X86_EFlags : uint
    {
        CarryFlag = BitMask.Bit0,
        Reserved1 = BitMask.Bit1,
        ParityFlag = BitMask.Bit2,
        AuxiliaryCarryFlag = BitMask.Bit3,
        Reserved4 = BitMask.Bit4,
        Reserved5 = BitMask.Bit5,
        ZeroFlag = BitMask.Bit6,
        SignFlag = BitMask.Bit7,
        TrapFlag = BitMask.Bit8,
        InterruptEnableFlag = BitMask.Bit9,
        DirectionFlag = BitMask.Bit10,
        OverflowFlag = BitMask.Bit11,
        IOPrivilegeLevel = BitMask.Bit12 | BitMask.Bit13,
        NestedTask = BitMask.Bit14,
        Reserved15 = BitMask.Bit15,
        ResumeFlag = BitMask.Bit16,
        Virtual8086Mode = BitMask.Bit17,
        AlignmentCheck = BitMask.Bit18,
        VirtualInterruptFlag = BitMask.Bit19,
        VirtualInterruptPending = BitMask.Bit20,
        IDFlag = BitMask.Bit21,
    }
}
