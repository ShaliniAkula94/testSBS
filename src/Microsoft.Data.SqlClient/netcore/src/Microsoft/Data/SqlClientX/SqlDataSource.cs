﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET8_0_OR_GREATER 

using Microsoft.Data.SqlClient;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Data.SqlClientX
{
    /// <summary>
    /// Represents a data source that can be used to obtain SqlConnections. 
    /// SqlDataSource can also create and open SqlConnectors, which are the internal/physical connections wrapped by SqlConnection.
    /// </summary>
    internal abstract class SqlDataSource : DbDataSource
    {
        private SqlCredential _credential;
        private string _connectionString;
        /// <inheritdoc/>
        public override string ConnectionString => _connectionString;
        internal SqlCredential Credential => _credential;

        /// <summary>
        /// Initializes a new instance of SqlDataSource
        /// </summary>
        /// <param name="connectionString">The connection string this data source should use.</param>
        /// <param name="credential">The credentials this data source should use.</param>
        internal SqlDataSource(string connectionString, SqlCredential credential)
        {
            _connectionString = connectionString;
            _credential = credential;
        }

        //TODO: return SqlConnection after it is updated to wrap SqlConnectionX 
        /// <summary>
        /// Creates a new, unopened SqlConnection.
        /// </summary>
        /// <returns>Returns the newly created SqlConnection</returns>
        protected override SqlConnectionX CreateDbConnection()
        {
            return SqlConnectionX.FromDataSource(this);
        }

        //TODO: rework this so that timeout and cancellationToken are bundled together
        /// <summary>
        /// Returns an opened SqlConnector.
        /// </summary>
        /// <param name="owningConnection">The SqlConnectionX object that will exclusively own and use this connector.</param>
        /// <param name="timeout">The connection timeout for this operation.</param>
        /// <param name="async">Whether this method should be run asynchronously.</param>
        /// <param name="cancellationToken">Cancels an outstanding asynchronous operation.</param>
        /// <returns></returns>
        internal abstract ValueTask<SqlConnector> GetInternalConnection(SqlConnectionX owningConnection, TimeSpan timeout, bool async, CancellationToken cancellationToken);
    }
}

#endif
