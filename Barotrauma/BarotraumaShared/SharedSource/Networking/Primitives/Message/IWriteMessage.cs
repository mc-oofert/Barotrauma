﻿using System;

namespace Barotrauma.Networking
{
    public interface IWriteMessage
    {
        void Write(bool val);
        void WritePadBits();
        void Write(byte val);
        void Write(Int16 val);
        void Write(UInt16 val);
        void Write(Int32 val);
        void Write(UInt32 val);
        void Write(Int64 val);
        void Write(UInt64 val);
        void Write(Single val);
        void Write(Double val);
        void WriteColorR8G8B8(Microsoft.Xna.Framework.Color val);
        void WriteColorR8G8B8A8(Microsoft.Xna.Framework.Color val);
        void WriteVariableUInt32(UInt32 val);
        void Write(string val);
        void WriteRangedInteger(int val, int min, int max);
        void WriteRangedSingle(Single val, Single min, Single max, int bitCount);
        void Write(byte[] val, int startIndex, int length);

        void PrepareForSending(ref byte[] outBuf, out bool isCompressed, out int outLength);

        int BitPosition { get; set; }
        int BytePosition { get; }
        byte[] Buffer { get; }
        int LengthBits { get; set; }
        int LengthBytes { get; }
    }
}
