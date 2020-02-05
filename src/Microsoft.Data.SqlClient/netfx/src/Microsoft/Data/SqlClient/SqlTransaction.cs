// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Data.Common;


namespace Microsoft.Data.SqlClient
{
    /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/SqlTransaction/*' />
    public sealed class SqlTransaction : DbTransaction
    {
        private static int _objectTypeCount; // Bid counter
        internal readonly int _objectID = System.Threading.Interlocked.Increment(ref _objectTypeCount);
        internal readonly IsolationLevel _isolationLevel = IsolationLevel.ReadCommitted;

        private SqlInternalTransaction _internalTransaction;
        private SqlConnection _connection;

        private bool _isFromAPI;

        internal SqlTransaction(SqlInternalConnection internalConnection, SqlConnection con,
                                IsolationLevel iso, SqlInternalTransaction internalTransaction)
        {
            SqlConnection.VerifyExecutePermission();

            _isolationLevel = iso;
            _connection = con;

            if (internalTransaction == null)
            {
                _internalTransaction = new SqlInternalTransaction(internalConnection, TransactionType.LocalFromAPI, this);
            }
            else
            {
                Debug.Assert(internalConnection.CurrentTransaction == internalTransaction, "Unexpected Parser.CurrentTransaction state!");
                _internalTransaction = internalTransaction;
                _internalTransaction.InitParent(this);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // PROPERTIES
        ////////////////////////////////////////////////////////////////////////////////////////

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Connection/*' />
        new public SqlConnection Connection
        { // MDAC 66655
            get
            {
                if (IsZombied)
                {
                    return null;
                }
                else
                {
                    return _connection;
                }
            }
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/DbConnection/*' />
        override protected DbConnection DbConnection
        {
            get
            {
                return Connection;
            }
        }

        internal SqlInternalTransaction InternalTransaction
        {
            get
            {
                return _internalTransaction;
            }
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/IsolationLevel/*' />
        override public IsolationLevel IsolationLevel
        {
            get
            {
                ZombieCheck();
                return _isolationLevel;
            }
        }

        private bool IsYukonPartialZombie
        {
            get
            {
                return (null != _internalTransaction && _internalTransaction.IsCompleted);
            }
        }

        internal bool IsZombied
        {
            get
            {
                return (null == _internalTransaction || _internalTransaction.IsCompleted);
            }
        }

        internal int ObjectID
        {
            get
            {
                return _objectID;
            }
        }

        internal SqlStatistics Statistics
        {
            get
            {
                if (null != _connection)
                {
                    if (_connection.StatisticsEnabled)
                    {
                        return _connection.Statistics;
                    }
                }
                return null;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // PUBLIC METHODS
        ////////////////////////////////////////////////////////////////////////////////////////

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Commit/*' />
        override public void Commit()
        {
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476

            ZombieCheck();

            SqlStatistics statistics = null;

            long scopeID = 0;
            if (SqlClientEventSource.Log.IsEnabled())
                scopeID = SqlClientEventSource.Log.ScopeEnter($"<sc.SqlTransaction.Commit|API> {ObjectID}#");

            if (SqlClientEventSource.Log.IsCorrelationEnabled())
                SqlClientEventSource.Log.CorrelationTrace($"<sc.SqlTransaction.Commit|API|Correlation> ObjectID{ObjectID}#, ActivityID {SqlClientEventSource.Log.Guid}");

            TdsParser bestEffortCleanupTarget = null;
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
#if DEBUG
                TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();

                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    tdsReliabilitySection.Start();
#else
                {
#endif //DEBUG
                    bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                    statistics = SqlStatistics.StartTimer(Statistics);

                    _isFromAPI = true;

                    _internalTransaction.Commit();
                }
#if DEBUG
                finally
                {
                    tdsReliabilitySection.Stop();
                }
#endif //DEBUG
            }
            catch (System.OutOfMemoryException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.StackOverflowException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.Threading.ThreadAbortException e)
            {
                _connection.Abort(e);
                SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                throw;
            }
            catch (SqlException e)
            {
                // GitHub Issue #130 - When a timeout exception has occurred on transaction completion request, 
                // this connection may not be in reusable state.
                // We will abort this connection and make sure it does not go back to the pool.
                var innerException = e.InnerException as Win32Exception;
                if (innerException != null && innerException.NativeErrorCode == TdsEnums.SNI_WAIT_TIMEOUT)
                {
                    _connection.Abort(e);
                }
                throw;
            }
            finally
            {
                _isFromAPI = false;

                SqlStatistics.StopTimer(statistics);
                SqlClientEventSource.Log.ScopeLeave(scopeID);
            }
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/DisposeDisposing/*' />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TdsParser bestEffortCleanupTarget = null;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
#if DEBUG
                    TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();

                    RuntimeHelpers.PrepareConstrainedRegions();
                    try
                    {
                        tdsReliabilitySection.Start();
#else
                    {
#endif //DEBUG
                        bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                        if (!IsZombied && !IsYukonPartialZombie)
                        {
                            _internalTransaction.Dispose();
                        }
                    }
#if DEBUG
                    finally
                    {
                        tdsReliabilitySection.Stop();
                    }
#endif //DEBUG
                }
                catch (System.OutOfMemoryException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (System.StackOverflowException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (System.Threading.ThreadAbortException e)
                {
                    _connection.Abort(e);
                    SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                    throw;
                }
            }
            base.Dispose(disposing);
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Rollback2/*' />
        override public void Rollback()
        {
            if (IsYukonPartialZombie)
            {
                if (SqlClientEventSource.Log.IsAdvanceTraceOn())
                {
                    // Put something in the trace in case a customer has an issue
                    SqlClientEventSource.Log.Trace($"<sc.SqlTransaction.Rollback|ADV> {ObjectID}# partial zombie no rollback required");
                }

                _internalTransaction = null; // yukon zombification
            }
            else
            {
                ZombieCheck();

                SqlStatistics statistics = null;

                long scopeID = 0;
                if (SqlClientEventSource.Log.IsEnabled())
                    scopeID = SqlClientEventSource.Log.ScopeEnter($"<sc.SqlTransaction.Rollback|API> {ObjectID}#");

                if (SqlClientEventSource.Log.IsCorrelationEnabled())
                    SqlClientEventSource.Log.CorrelationTrace($"<sc.SqlTransaction.Rollback|API|Correlation> ObjectID{ObjectID}#, ActivityID {SqlClientEventSource.Log.Guid}");

                TdsParser bestEffortCleanupTarget = null;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
#if DEBUG
                    TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();

                    RuntimeHelpers.PrepareConstrainedRegions();
                    try
                    {
                        tdsReliabilitySection.Start();
#else
                    {
#endif //DEBUG
                        bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                        statistics = SqlStatistics.StartTimer(Statistics);

                        _isFromAPI = true;

                        _internalTransaction.Rollback();
                    }
#if DEBUG
                    finally
                    {
                        tdsReliabilitySection.Stop();
                    }
#endif //DEBUG
                }
                catch (System.OutOfMemoryException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (System.StackOverflowException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (System.Threading.ThreadAbortException e)
                {
                    _connection.Abort(e);
                    SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                    throw;
                }
                finally
                {
                    _isFromAPI = false;

                    SqlStatistics.StopTimer(statistics);
                    SqlClientEventSource.Log.ScopeLeave(scopeID);
                }
            }
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/RollbackTransactionName/*' />
        public void Rollback(string transactionName)
        {
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476

            ZombieCheck();

            SqlStatistics statistics = null;

            long scopeID = 0;
            if (SqlClientEventSource.Log.IsEnabled())
                scopeID = SqlClientEventSource.Log.ScopeEnter($"<sc.SqlTransaction.Rollback|API> {ObjectID}# transactionName='{transactionName}'");

            TdsParser bestEffortCleanupTarget = null;
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
#if DEBUG
                TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();

                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    tdsReliabilitySection.Start();
#else
                {
#endif //DEBUG
                    bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                    statistics = SqlStatistics.StartTimer(Statistics);

                    _isFromAPI = true;

                    _internalTransaction.Rollback(transactionName);
                }
#if DEBUG
                finally
                {
                    tdsReliabilitySection.Stop();
                }
#endif //DEBUG
            }
            catch (System.OutOfMemoryException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.StackOverflowException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.Threading.ThreadAbortException e)
            {
                _connection.Abort(e);
                SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                throw;
            }
            finally
            {
                _isFromAPI = false;

                SqlStatistics.StopTimer(statistics);
                SqlClientEventSource.Log.ScopeLeave(scopeID);
            }
        }

        /// <include file='..\..\..\..\..\..\..\doc\snippets\Microsoft.Data.SqlClient\SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Save/*' />
        public void Save(string savePointName)
        {
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476

            ZombieCheck();

            SqlStatistics statistics = null;
            
            long scopeID = 0;
            if (SqlClientEventSource.Log.IsEnabled())
                scopeID = SqlClientEventSource.Log.ScopeEnter($"<sc.SqlTransaction.Save|API> {ObjectID}# savePointName='{savePointName}'");

            TdsParser bestEffortCleanupTarget = null;
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
#if DEBUG
                TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();

                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    tdsReliabilitySection.Start();
#else
                {
#endif //DEBUG
                    bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                    statistics = SqlStatistics.StartTimer(Statistics);

                    _internalTransaction.Save(savePointName);
                }
#if DEBUG
                finally
                {
                    tdsReliabilitySection.Stop();
                }
#endif //DEBUG
            }
            catch (System.OutOfMemoryException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.StackOverflowException e)
            {
                _connection.Abort(e);
                throw;
            }
            catch (System.Threading.ThreadAbortException e)
            {
                _connection.Abort(e);
                SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                throw;
            }
            finally
            {
                SqlStatistics.StopTimer(statistics);
                SqlClientEventSource.Log.ScopeLeave(scopeID);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // INTERNAL METHODS
        ////////////////////////////////////////////////////////////////////////////////////////

        internal void Zombie()
        {
            // SQLBUDT #402544 For Yukon, we have to defer "zombification" until
            //                 we get past the users' next rollback, else we'll
            //                 throw an exception there that is a breaking change.
            //                 Of course, if the connection is aready closed, 
            //                 then we're free to zombify...
            SqlInternalConnection internalConnection = (_connection.InnerConnection as SqlInternalConnection);

            if (null != internalConnection && internalConnection.IsYukonOrNewer && !_isFromAPI)
            {
                if (SqlClientEventSource.Log.IsAdvanceTraceOn())
                {
                    SqlClientEventSource.Log.Trace($"<sc.SqlTransaction.Zombie|ADV> {ObjectID}# yukon deferred zombie");
                }
            }
            else
            {
                _internalTransaction = null; // pre-yukon zombification
            }

        }

        ////////////////////////////////////////////////////////////////////////////////////////
        // PRIVATE METHODS
        ////////////////////////////////////////////////////////////////////////////////////////

        private void ZombieCheck()
        {
            // If this transaction has been completed, throw exception since it is unusable.
            if (IsZombied)
            {

                if (IsYukonPartialZombie)
                {
                    _internalTransaction = null; // yukon zombification
                }

                throw ADP.TransactionZombied(this);
            }
        }
    }
}

