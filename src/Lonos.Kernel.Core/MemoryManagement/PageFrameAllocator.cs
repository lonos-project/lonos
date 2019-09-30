﻿// This file is part of Lonos Project, an Operating System written in C#. Web: https://www.lonos.io
// Licensed under the GNU 2.0 license. See LICENSE.txt file in the project root for full license information.

using System;
using Lonos.Kernel.Core.Boot;
using Lonos.Kernel.Core.Devices;
using Lonos.Kernel.Core.Diagnostics;
using Lonos.Kernel.Core.PageManagement;
using Mosa.Runtime;

//using Mosa.Kernel.x86;

namespace Lonos.Kernel.Core.MemoryManagement
{

    public unsafe class PageFrameAllocator : BasePageFrameAllocator
    {
    }

    /// <summary>
    /// A physical page allocator.
    /// </summary>
    public unsafe class BasePageFrameAllocator : IPageFrameAllocator
    {

        public Page* AllocatePages(uint pages, AllocatePageOptions options = AllocatePageOptions.Default)
        {
            return Allocate(pages);
        }

        public Page* AllocatePage(AllocatePageOptions options = AllocatePageOptions.Default)
        {
            var p = Allocate(1);
            //if (p->PhysicalAddress == 0x01CA4000)
            //    Panic.Error("DEBUG-MARKER");
            return p;
        }

        public void Free(Page* page)
        {
            Free(page->Address);
        }

        private uint _FreePages;

        private Page* PageArray;
        private uint _TotalPages;

        private KernelMemoryMap kmap;

        /// <summary>
        /// Setup the physical page manager
        /// </summary>
        public void Setup(MemoryRegion region, AddressSpaceKind addrKind)
        {
            _AddressSpaceKind = addrKind;
            _Region = region;
            _TotalPages = region.Size / PageSize;
            kmap = KernelMemoryMapManager.Allocate(_TotalPages * (uint)sizeof(Page), BootInfoMemoryType.PageFrameAllocator);
            PageArray = (Page*)kmap.Start;
            lastAllocatedPage = PageArray;

            var firstSelfPageNum = KMath.DivFloor(kmap.Start, 4096);
            var selfPages = KMath.DivFloor(kmap.Size, 4096);

            KernelMessage.WriteLine("Page Frame Array allocated {0} pages, beginning with page {1}", selfPages, firstSelfPageNum);

            PageTableExtensions.SetWritable(PageTable.KernelTable, kmap.Start, kmap.Size);
            MemoryOperation.Clear4(kmap.Start, kmap.Size);

            for (uint i = 0; i < _TotalPages; i++)
            {
                PageArray[i].Address = i * PageSize;
                if (i != 0)
                    PageArray[i - 1].Next = &PageArray[i];
            }

            SetupFreeMemory();
        }

        /// <summary>
        /// Setups the free memory.
        /// </summary>
        private unsafe void SetupFreeMemory()
        {
            if (!BootInfo.Present)
                return;

            for (uint i = 0; i < _TotalPages; i++)
                PageArray[i].Status = PageStatus.Reserved;

            SetInitialPageStatus(&KernelMemoryMapManager.Header->SystemUsable, PageStatus.Free);
            SetInitialPageStatus(&KernelMemoryMapManager.Header->Used, PageStatus.Used);

            _FreePages = 0;
            for (uint i = 0; i < _TotalPages; i++)
                if (PageArray[i].Status == PageStatus.Free)
                    _FreePages++;

            //if (GetPhysPage(0x01CA3000)->Status != PageStatus.Used)
            //{
            //    Panic.Error("01CA3000!!!");
            //}
            //KernelMessage.WriteLine("DEBUG-MARKER");
            //DumpPage(GetPhysPage(0x01CA3000));

            KernelMessage.WriteLine("Pages Free: {0}", FreePages);
        }

        private void SetInitialPageStatus(KernelMemoryMapArray* maps, PageStatus status)
        {
            for (var i = 0; i < maps->Count; i++)
            {
                var map = maps->Items[i];
                if (map.Start >= BootInfo.Header->InstalledPhysicalMemory)
                    continue;

                var mapPages = KMath.DivCeil(map.Size, 4096);
                var fistPageNum = KMath.DivFloor(map.Start, 4096);
                KernelMessage.WriteLine("Mark Pages from {0:X8}, Size {1:X8}, Type {2}, FirstPage {3}, Pages {4}, Status {5}", map.Start, map.Size, (uint)map.Type, (uint)fistPageNum, mapPages, (uint)status);

                for (var p = fistPageNum; p < fistPageNum + mapPages; p++)
                {
                    var addr = p * 4096;
                    if (addr >= BootInfo.Header->InstalledPhysicalMemory)
                    {
                        KernelMessage.WriteLine("addr >= BootInfo.Header->InstalledPhysicalMemory");
                        break;
                    }
                    var page = GetPageByNum(p);
                    page->Status = status;
                }
            }
        }

        private void DumpPage(Page* p)
        {
            KernelMessage.WriteLine("pNum {0}, phys {1:X8} status {2} struct {3:X8} structPage {4}", p->PageNum, p->Address, (uint)p->Status, (uint)p, (uint)p / 4096);
        }

        public void Dump()
        {
            var sb = new StringBuffer();

            for (uint i = 0; i < _TotalPages; i++)
            {
                var p = &PageArray[i];
                if (i % 64 == 0)
                {
                    sb.Append("\nIndex={0} Page {1} at {2:X8}, PageStructAddr={3:X8}: ", i, p->PageNum, p->Address, (uint)p);
                    sb.WriteTo(DeviceManager.Serial1);
                    sb.Clear();
                }
                sb.Append((int)p->Status);
                sb.WriteTo(DeviceManager.Serial1);
                sb.Clear();
            }
        }

        public Page* GetPageByAddress(Addr physAddr)
        {
            return GetPageByNum((uint)physAddr / PageSize);
        }

        public Page* GetPageByNum(uint pageNum)
        {
            if (pageNum > _TotalPages)
                return null;
            return &PageArray[pageNum];
        }

        //static uint _nextAllocacationSearchIndex;
        private static Page* lastAllocatedPage;

        /// <summary>
        /// Allocate a physical page from the free list
        /// </summary>
        /// <returns>The page</returns>
        private Page* Allocate(uint num)
        {
            lock (this)
            {
                if (num == 0)
                {
                    KernelMessage.WriteLine("Requesting zero pages");
                    return null;
                }
                else if (num > 1 && KConfig.TracePageAllocation)
                {
                    KernelMessage.WriteLine("Requesting {0} pages", num);
                }

                //KernelMessage.WriteLine("Request {0} pages...", num);

                uint statBlocks = 0;
                uint statFreeBlocks = 0;
                int statMaxBlockPages = 0;
                uint statRangeChecks = 0;

                uint cnt = 0;

                if (lastAllocatedPage == null)
                    lastAllocatedPage = PageArray;

                Page* p = lastAllocatedPage->Next;
                while (true)
                {
                    statBlocks++;

                    if (p == null)
                        p = PageArray;

                    if (p->Status == PageStatus.Free)
                    {
                        statFreeBlocks++;
                        var head = p;

                        // Found free Page. Check now free range.
                        for (var i = 0; i < num; i++)
                        {
                            statRangeChecks++;
                            statMaxBlockPages = Math.Max(statMaxBlockPages, i);

                            if (p == null)
                                break; // Reached end. SorRange is incomplete
                            if (p->Status != PageStatus.Free) // Used -> so we can abort the searach
                                break;

                            if (i == num - 1)
                            { // all loops successful. So we found our range.

                                head->Tail = p;
                                head->PagesUsed = num;
                                p = head;
                                for (var n = 0; n < num; n++)
                                {
                                    if (p->Status != PageStatus.Free)
                                        Panic.Error("Page is not Free. PageFrame Array corrupted?");

                                    p->Status = PageStatus.Used;
                                    p->Head = head;
                                    p->Tail = head->Tail;
                                    p = p->Next;
                                    _FreePages--;
                                }
                                lastAllocatedPage = p;

                                //KernelMessage.WriteLine("Allocated from {0:X8} to {1:X8}", (uint)head->PhysicalAddress, (uint)head->Tail->PhysicalAddress + 4096 - 1);

                                //if (head->PhysicalAddress == 0x01CA4000)
                                //{
                                //    KernelMessage.WriteLine("DEBUG-MARKER 2");
                                //    DumpPage(head);
                                //}

                                return head;
                            }

                            p = p->Next;
                        }

                    }

                    if (p->Tail != null)
                        p = p->Tail;

                    p = p->Next;
                    if (++cnt > _TotalPages)
                        break;
                }

                KernelMessage.WriteLine("Blocks={0} FreeBlocks={1} MaxBlockPages={2} RangeChecks={3} cnt={4}", statBlocks, statFreeBlocks, (uint)statMaxBlockPages, statRangeChecks, cnt);
                Dump();
                Panic.Error("PageFrameAllocator: Could not allocate " + num + " Pages.");
                return null;
            }
        }

        /// <summary>
        /// Releases a page to the free list
        /// </summary>
        private void Free(Addr address)
        {
            lock (this)
            {
                var p = GetPageByAddress(address);
                if (p->Free)
                    return;

                var num = p->PagesUsed;

                for (var n = 0; n < num; n++)
                {
                    p->Status = PageStatus.Used;
                    p->PagesUsed = 0;
                    p->Head = null;
                    p->Tail = null;
                    p = p->Next;
                    _FreePages++;
                }
            }
        }

        public uint GetAddress(Page* page)
        {
            return page->Address;
        }

        public uint GetPageNum(Page* page)
        {
            return page->PageNum;
        }

        public Page* GetPageByIndex(uint pageIndex)
        {
            return &PageArray[pageIndex];
        }

        public Page* NextPage(Page* page)
        {
            return page->Next;
        }

        public Page* NextCompoundPage(Page* page)
        {
            if (page == null)
                return null;

            var next = page->Next;
            if (next == null)
                return null;

            return next;
        }

        public uint GetPageIndex(Page* page)
        {
            return page->PageNum;
        }

        public uint GetPageIndex(Addr addr)
        {
            return addr / 4096;
        }

        public bool ContainsPage(Page* page)
        {
            return _Region.Contains(page->Address);
        }

        /// <summary>
        /// Gets the size of a single memory page.
        /// </summary>
        public static uint PageSize => 4096;

        public uint PagesAvailable
        {
            get
            {
                return _FreePages;
            }
        }

        public uint TotalPages => _TotalPages;

        private MemoryRegion _Region;
        public MemoryRegion Region => _Region;

        private AddressSpaceKind _AddressSpaceKind;
        public AddressSpaceKind AddressSpaceKind => _AddressSpaceKind;

        public uint FreePages => _FreePages;
    }
}