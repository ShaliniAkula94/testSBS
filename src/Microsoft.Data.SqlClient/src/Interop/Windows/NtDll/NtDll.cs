﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Interop_TEMP.Windows.NtDll
{
    internal sealed class NtDll
    {
        private const string DllName = "ntdll.dll";

        internal static unsafe (int status, IntPtr handle) CreateFile(
            string path,
            byte[] eaName,
            byte[] eaValue,

            Interop.NtDll.DesiredAccess desiredAccess,
            FileAttributes fileAttributes,
            FileShare shareAccess,
            Interop.NtDll.CreateDisposition createDisposition,
            Interop.NtDll.CreateOptions createOptions

            #if NETFRAMEWORK
            ,Interop.ImpersonationLevel impersonationLevel,
            bool isDynamicTracking,
            bool isEffectiveOnly
            #endif
        )
        {
            // Acquire space for the file extended attribute
            int eaHeaderSize = sizeof(Interop.NtDll.FILE_FULL_EA_INFORMATION);
            int eaBufferSize = eaHeaderSize + eaName.Length + eaValue.Length;
            Span<byte> eaBuffer = stackalloc byte[eaBufferSize];

            // Fix the position of the path and the extended attribute buffer
            fixed (char* pPath = path)
            fixed (byte* pEaBuffer = eaBuffer)
            {
                // Generate a unicode string object from the path
                UnicodeString ucPath = new UnicodeString(pPath, path.Length);

                #if NETFRAMEWORK
                // Generate a Security QOS object
                Interop.SecurityQualityOfService qos = new Interop.SecurityQualityOfService(
                    impersonationLevel,
                    isDynamicTracking,
                    isEffectiveOnly);
                Interop.SecurityQualityOfService* pQos = &qos;
                #else
                Interop.SecurityQualityOfService* pQos = null;
                #endif

                // Generate the object attributes object that defines what we're opening
                ObjectAttributes attributes = new ObjectAttributes(
                    objectName: &ucPath,
                    attributes: ObjectAttributeFlags.OBJ_CASE_INSENSITIVE,
                    rootDirectory: IntPtr.Zero,
                    securityQos: pQos);

                // Set the contents of the extended information
                // NOTE: This chunk of code treats a byte[] as FILE_FULL_EA_INFORMATION. Since we
                //    do not have a direct reference to a FILE_FULL_EA_INFORMATION, we have to use
                //    the -> operator to dereference the object before accessing its members.
                //    However, the byte[] is longer than the FILE_FULL_EA_INFORMATION struct in
                //    order to contain the name and value. Since byte[] are reference types, we
                //    cannot store the name/value directly in the struct (in memory it would be
                //    stored as a pointer). So in the second chunk, we copy the name/value to the
                //    byte[] after the FILE_FULL_EA_INFORMATION struct.
                // Step 1) Write the header
                Interop.NtDll.FILE_FULL_EA_INFORMATION* pEaObj = (Interop.NtDll.FILE_FULL_EA_INFORMATION*)pEaBuffer;
                pEaObj->NextEntryOffset = 0;
                pEaObj->Flags = 0;
                pEaObj->EaNameLength = (byte)(eaName.Length - 1); // Null terminator is not included
                pEaObj->EaValueLength = (ushort)eaValue.Length;

                // Step 2) Write the contents
                eaName.AsSpan().CopyTo(eaBuffer.Slice(eaHeaderSize));
                eaValue.AsSpan().CopyTo(eaBuffer.Slice(eaHeaderSize + eaName.Length));

                // Make the interop call
                int status = NtCreateFile(
                    out IntPtr handle,
                    desiredAccess,
                    ref attributes,
                    IoStatusBlock: out _,
                    AllocationSize: null,
                    fileAttributes,
                    shareAccess,
                    createDisposition,
                    createOptions,
                    pEaBuffer,
                    (uint) eaBufferSize);
                return (status, handle);
            }
        }


        // https://msdn.microsoft.com/en-us/library/bb432380.aspx
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff566424.aspx
        [DllImport(DllName, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern unsafe int NtCreateFile(
            out IntPtr FileHandle,
            Interop.NtDll.DesiredAccess DesiredAccess,
            ref ObjectAttributes ObjectAttributes,
            out Interop.NtDll.IO_STATUS_BLOCK IoStatusBlock,
            long* AllocationSize,
            System.IO.FileAttributes FileAttributes,
            System.IO.FileShare ShareAccess,
            Interop.NtDll.CreateDisposition CreateDisposition,
            Interop.NtDll.CreateOptions CreateOptions,
            void* EaBuffer,
            uint EaLength);
    }
}
