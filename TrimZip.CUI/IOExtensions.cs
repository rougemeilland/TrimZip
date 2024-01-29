using System;
using System.Collections.Generic;
using System.Numerics;
using Palmtree;
using Palmtree.IO;

namespace TrimZip.CUI
{
    internal static class IOExtensions
    {
        public static IEnumerable<(POSITION_T position, byte)> EnumerateBytesInReverseOrder<POSITION_T>(this IRandomInputByteStream<POSITION_T> sourceStream)
            where POSITION_T : IComparable<POSITION_T>, IAdditionOperators<POSITION_T, ulong, POSITION_T>, ISubtractionOperators<POSITION_T, ulong, POSITION_T>, ISubtractionOperators<POSITION_T, POSITION_T, ulong>
        {
            const int _MAX_BUFFER_SIZE = 8 * 1024;

            var buffer = new byte[_MAX_BUFFER_SIZE];
            var pos = sourceStream.EndOfThisStream;
            while (pos.CompareTo(sourceStream.StartOfThisStream) > 0)
            {
                var availableBufferSize = checked((int)checked(pos - sourceStream.StartOfThisStream).Minimum(checked((ulong)_MAX_BUFFER_SIZE)));
                var startPos = checked(pos - (ulong)availableBufferSize);
                sourceStream.Seek(startPos);
                Validation.Assert(sourceStream.ReadBytes(buffer.Slice(0, availableBufferSize)) == availableBufferSize, "sourceStream.ReadBytes(buffer.Slice(availableBufferSize)) == availableBufferSize");
                for (var index = availableBufferSize - 1; index >= 0; --index)
                    yield return (startPos + checked((ulong)index), buffer[index]);
                checked
                {
                    pos -= (ulong)availableBufferSize;
                }
            }
        }
    }
}
