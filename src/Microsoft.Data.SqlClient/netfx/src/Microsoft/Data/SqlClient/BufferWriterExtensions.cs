﻿using System;
using System.Buffers;
using System.Text;

namespace Microsoft.Data.SqlClient
{
    internal static class BufferWriterExtensions
    {
        internal static long GetBytes(this Encoding encoding, string str, IBufferWriter<byte> bufferWriter)
        {
            var count = encoding.GetByteCount(str);
            var array = ArrayPool<byte>.Shared.Rent(count);

            try
            {
                var length = encoding.GetBytes(str, 0, str.Length, array, 0);
                bufferWriter.Write(array.AsSpan(0, length));
                return length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }
    }
}
