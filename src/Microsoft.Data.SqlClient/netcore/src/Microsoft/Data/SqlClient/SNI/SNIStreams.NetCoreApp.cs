﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Security;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System;

namespace Microsoft.Data.SqlClient.SNI
{

    internal sealed partial class SNISslStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValueTask<int> valueTask = ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
            if (valueTask.IsCompletedSuccessfully)
            {
                return Task.FromResult(valueTask.Result);
            }
            else
            {
                return valueTask.AsTask();
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _readAsyncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _readAsyncSemaphore.Release();
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValueTask valueTask = WriteAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
            if (valueTask.IsCompletedSuccessfully)
            {
                return Task.CompletedTask;
            }
            else
            {
                return valueTask.AsTask();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writeAsyncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeAsyncSemaphore.Release();
            }
        }
    }


    internal sealed partial class SNINetworkStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValueTask<int> valueTask = ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
            if (valueTask.IsCompletedSuccessfully)
            {
                return Task.FromResult(valueTask.Result);
            }
            else
            {
                return valueTask.AsTask();
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _readAsyncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _readAsyncSemaphore.Release();
            }
        }

        // Prevent the WriteAsync collisions by running the task in a Semaphore Slim
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValueTask valueTask = WriteAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
            if (valueTask.IsCompletedSuccessfully)
            {
                return Task.CompletedTask;
            }
            else
            {
                return valueTask.AsTask();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writeAsyncSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeAsyncSemaphore.Release();
            }
        }
    }
}
