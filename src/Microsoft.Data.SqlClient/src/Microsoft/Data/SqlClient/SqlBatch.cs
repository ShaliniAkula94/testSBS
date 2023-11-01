﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET6_0_OR_GREATER

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Common;

namespace Microsoft.Data.SqlClient
{
    /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/SqlBatch/*'/>
    public class SqlBatch : DbBatch
    {
        private SqlCommand _batchCommand;
        private List<SqlBatchCommand> _commands;
        private SqlBatchCommandCollection _providerCommands;

        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/ctor1/*'/>
        public SqlBatch()
        {
            _batchCommand = new SqlCommand();
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/ctor2/*'/>
        public SqlBatch(SqlConnection connection = null, SqlTransaction transaction = null)
            : this()
        {
            Connection = connection;
            Transaction = transaction;
        }
        /// <inheritdoc />
        public override int Timeout
        {
            get
            {
                CheckDisposed();
                return _batchCommand.CommandTimeout;
            }
            set
            {
                CheckDisposed();
                _batchCommand.CommandTimeout = value;
            }
        }
        /// <inheritdoc />
        protected override DbBatchCommandCollection DbBatchCommands => BatchCommands;
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/BatchCommands/*'/>
        public new SqlBatchCommandCollection BatchCommands => _providerCommands != null ? _providerCommands : _providerCommands = new SqlBatchCommandCollection(Commands); // Commands call will check disposed
        /// <inheritdoc />
        protected override DbConnection DbConnection
        {
            get
            {
                CheckDisposed();
                return Connection;
            }
            set
            {
                CheckDisposed();
                Connection = (SqlConnection)value;
            }
        }
        /// <inheritdoc />
        protected override DbTransaction DbTransaction
        {
            get
            {
                CheckDisposed();
                return Transaction;
            }
            set
            {
                CheckDisposed();
                Transaction = (SqlTransaction)value;
            }
        }

        /// <inheritdoc />
        public override void Cancel()
        {
            CheckDisposed();
            _batchCommand.Cancel();
        }
        /// <inheritdoc />
        public override int ExecuteNonQuery()
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteNonQuery();
        }
        /// <inheritdoc />
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        /// <inheritdoc />
        public override object ExecuteScalar()
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteScalar();
        }
        /// <inheritdoc />
        public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteScalarBatchAsync(cancellationToken);
        }
        /// <inheritdoc />
        public override void Prepare()
        {
            CheckDisposed();
        }
        /// <inheritdoc />
        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            return Task.CompletedTask;
        }
        /// <inheritdoc />
        public override void Dispose()
        {
            _batchCommand?.Dispose();
            _batchCommand = null;
            _commands?.Clear();
            _commands = null;
            base.Dispose();
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/Commands/*'/>
        public List<SqlBatchCommand> Commands
        {
            get
            {
                CheckDisposed();
                return _commands != null ? _commands : _commands = new List<SqlBatchCommand>();
            }
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/Connection/*'/>
        public new SqlConnection Connection
        {
            get
            {
                CheckDisposed();
                return _batchCommand.Connection;
            }
            set
            {
                CheckDisposed();
                _batchCommand.Connection = value;
            }
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/Transaction/*'/>
        public new SqlTransaction Transaction
        {
            get
            {
                CheckDisposed();
                return _batchCommand.Transaction;
            }
            set
            {
                CheckDisposed();
                _batchCommand.Transaction = value;
            }
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/ExecuteReader/*'/>
        public SqlDataReader ExecuteReader()
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteReader();
        }
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlBatch.xml' path='docs/members[@name="SqlBatch"]/ExecuteReaderAsync/*'/>
        public new Task<SqlDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteReaderAsync(cancellationToken);
        }
        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteReader();
        /// <inheritdoc />
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            CheckDisposed();
            SetupBatchCommandExecute();
            return _batchCommand.ExecuteReaderAsync(cancellationToken)
                .ContinueWith<DbDataReader>((result) =>
                {
                    if (result.IsFaulted)
                    {
                        throw result.Exception.InnerException;
                    }
                    return result.Result;
                }, 
                CancellationToken.None, 
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.NotOnCanceled, 
                TaskScheduler.Default
            );
        }

        private void CheckDisposed()
        {
            if (_batchCommand is null)
            {
                throw ADP.ObjectDisposed(this);
            }
        }

        private void SetupBatchCommandExecute()
        {
            SqlConnection connection = Connection;
            if (connection is null)
            {
                throw ADP.ConnectionRequired(nameof(SetupBatchCommandExecute));
            }
            _batchCommand.Connection = Connection;
            _batchCommand.Transaction = Transaction;
            _batchCommand.SetBatchRPCMode(true, _commands.Count);
            _batchCommand.Parameters.Clear();
            for (int index = 0; index < _commands.Count; index++)
            {
                _batchCommand.AddBatchCommand(_commands[index]);
            }
            _batchCommand.SetBatchRPCModeReadyToExecute();
        }
        /// <inheritdoc />
        protected override DbBatchCommand CreateDbBatchCommand()
        {
            return new SqlBatchCommand();
        }
    }
}

#endif
