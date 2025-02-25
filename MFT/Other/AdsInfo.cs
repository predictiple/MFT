﻿using MFT.Attributes;

namespace MFT.Other
{
    public class AdsInfo
    {
        public AdsInfo(string name, ulong size, ResidentData residentData, NonResidentData nonResidentData)
        {
            Name = name;
            Size = size;
            ResidentData = residentData;
            NonResidentData = nonResidentData;
        }

        public string Name { get; }
        public ulong Size { get; }

        public ResidentData ResidentData { get; }
        public NonResidentData NonResidentData { get; }

        public override string ToString()
        {
            return $"Name: {Name} Size: 0x{Size:X}";
        }
    }
}