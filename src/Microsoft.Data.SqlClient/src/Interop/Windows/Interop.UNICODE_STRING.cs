// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    // https://msdn.microsoft.com/en-us/library/windows/desktop/aa380518.aspx
    // https://msdn.microsoft.com/en-us/library/windows/hardware/ff564879.aspx
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct UNICODE_STRING
    {
        /// <summary>
        /// Length, in bytes, not including the the null, if any.
        /// </summary>
        internal ushort Length;

        /// <summary>
        /// Max size of the buffer in bytes
        /// </summary>
        internal ushort MaximumLength;

        /// <summary>
        /// Pointer to the buffer used to contain the wide characters of the string.
        /// </summary>
        internal char* Buffer;

        public UNICODE_STRING(char* buffer, int length)
        {
            Length = checked((ushort)(length * sizeof(char)));
            MaximumLength = checked((ushort)(length * sizeof(char)));
            Buffer = buffer;
        }
    }
}
