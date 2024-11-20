// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Data.Common;
using System.Data.SqlClient;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Data.Common;

namespace Microsoft.Data.SqlClient
{
    /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/SqlTransaction/*' />
    public sealed partial class SqlTransaction : DbTransaction
    {
        //netcore private static readonly SqlDiagnosticListener s_diagnosticListener = new(SqlClientDiagnosticListenerExtensions.DiagnosticListenerName);

        ////////////////////////////////////////////////////////////////////////////////////////
        // PUBLIC METHODS
        ////////////////////////////////////////////////////////////////////////////////////////

        //netcore---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Commit/*' />
        public override void Commit()
        {
            using (DiagnosticTransactionScope diagnosticScope = s_diagnosticListener.CreateTransactionCommitScope(_isolationLevel, _connection, InternalTransaction))
            {
                ZombieCheck();

                using (TryEventScope.Create("SqlTransaction.Commit | API | Object Id {0}", ObjectID))
                {
                    SqlStatistics statistics = null;
                    SqlClientEventSource.Log.TryCorrelationTraceEvent("SqlTransaction.Commit | API | Correlation | Object Id {0}, Activity Id {1}, Client Connection Id {2}", ObjectID, ActivityCorrelator.Current, Connection?.ClientConnectionId);
                    try
                    {
                        statistics = SqlStatistics.StartTimer(Statistics);

                        _isFromAPI = true;

                        _internalTransaction.Commit();
                    }
                    catch (SqlException ex)
                    {
                        diagnosticScope.SetException(ex);
                        // GitHub Issue #130 - When a timeout exception has occurred on transaction completion request,
                        // this connection may not be in reusable state.
                        // We will abort this connection and make sure it does not go back to the pool.
                        if (ex.InnerException is Win32Exception innerException && innerException.NativeErrorCode == TdsEnums.SNI_WAIT_TIMEOUT)
                        {
                            _connection.Abort(ex);
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        diagnosticScope.SetException(ex);
                        throw;
                    }
                    finally
                    {
                        SqlStatistics.StopTimer(statistics);
                        _isFromAPI = false;
                    }
                }
            }
        }
        //---netcore
        //netfx---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Commit/*' />
        public override void Commit()
        {
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476

            ZombieCheck();

            SqlStatistics statistics = null;
            using (TryEventScope.Create("<sc.SqlTransaction.Commit|API> {0}", ObjectID))
            {
                SqlClientEventSource.Log.TryCorrelationTraceEvent("<sc.SqlTransaction.Commit|API|Correlation> ObjectID {0}, ActivityID {1}", ObjectID, ActivityCorrelator.Current);

                TdsParser bestEffortCleanupTarget = null;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
#if DEBUG
                    TdsParser.ReliabilitySection tdsReliabilitySection = new();

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
                    if (e.InnerException is Win32Exception innerException && innerException.NativeErrorCode == TdsEnums.SNI_WAIT_TIMEOUT)
                    {
                        _connection.Abort(e);
                    }
                    throw;
                }
                finally
                {
                    _isFromAPI = false;

                    SqlStatistics.StopTimer(statistics);
                }
            }
        }
        //---netfx

        //netcore---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/DisposeDisposing/*' />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!IsZombied && !Is2005PartialZombie)
                {
                    _internalTransaction.Dispose();
                }
            }
            base.Dispose(disposing);
        }
        //---netcore
        //netfx---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/DisposeDisposing/*' />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TdsParser bestEffortCleanupTarget = null;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
#if DEBUG
                    TdsParser.ReliabilitySection tdsReliabilitySection = new();

                    RuntimeHelpers.PrepareConstrainedRegions();
                    try
                    {
                        tdsReliabilitySection.Start();
#else
                    {
#endif //DEBUG
                        bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                        if (!IsZombied && !Is2005PartialZombie)
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
        //---netfx

        //netcore---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Rollback2/*' />
        public override void Rollback()
        {
            using (DiagnosticTransactionScope diagnosticScope = s_diagnosticListener.CreateTransactionRollbackScope(_isolationLevel, _connection, InternalTransaction, null))
            {
                if (Is2005PartialZombie)
                {
                    // Put something in the trace in case a customer has an issue
                    SqlClientEventSource.Log.TryAdvancedTraceEvent("SqlTransaction.Rollback | ADV | Object Id {0}, partial zombie no rollback required", ObjectID);
                    _internalTransaction = null; // 2005 zombification
                }
                else
                {
                    ZombieCheck();

                    SqlStatistics statistics = null;
                    using (TryEventScope.Create("SqlTransaction.Rollback | API | Object Id {0}", ObjectID))
                    {
                        SqlClientEventSource.Log.TryCorrelationTraceEvent("SqlTransaction.Rollback | API | Correlation | Object Id {0}, ActivityID {1}, Client Connection Id {2}", ObjectID, ActivityCorrelator.Current, Connection?.ClientConnectionId);
                        try
                        {
                            statistics = SqlStatistics.StartTimer(Statistics);

                            _isFromAPI = true;

                            _internalTransaction.Rollback();
                        }
                        catch (Exception ex)
                        {
                            diagnosticScope.SetException(ex);
                            throw;
                        }
                        finally
                        {
                            SqlStatistics.StopTimer(statistics);
                            _isFromAPI = false;
                        }
                    }
                }
            }
        }
        //---netcore
        //netfx---
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Rollback2/*' />
        public override void Rollback()
        {
            if (Is2005PartialZombie)
            {
                // Put something in the trace in case a customer has an issue
                SqlClientEventSource.Log.TryAdvancedTraceEvent("<sc.SqlTransaction.Rollback|ADV> {0} partial zombie no rollback required", ObjectID);

                _internalTransaction = null; // 2005 zombification
            }
            else
            {
                ZombieCheck();

                SqlStatistics statistics = null;
                using (TryEventScope.Create("<sc.SqlTransaction.Rollback|API> {0}", ObjectID))
                {
                    SqlClientEventSource.Log.TryCorrelationTraceEvent("<sc.SqlTransaction.Rollback|API|Correlation> ObjectID {0}, ActivityID {1}", ObjectID, ActivityCorrelator.Current);

                    TdsParser bestEffortCleanupTarget = null;
                    RuntimeHelpers.PrepareConstrainedRegions();
                    try
                    {
#if DEBUG
                        TdsParser.ReliabilitySection tdsReliabilitySection = new();

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
                    }
                }
            }
        }
        //---netfx

        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/RollbackTransactionName/*' />
        public override void Rollback(string transactionName)
        {
            #if NETFRAMEWORK
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476
            const string eventScopeMessage = "SqlTransaction.Rollback | API | Object Id {0}, Transaction Name='{1}', ActivityID {2}, Client Connection Id {3}";
            #else
            const string eventScopeMessage = <sc.SqlTransaction.Rollback|API> {0} transactionName='{1}', activityId='{2}', clientConnectionId='{3}'";
            #endif
            
            #if NET
            using (DiagnosticTransactionScope diagnosticScope = s_diagnosticListener.CreateTransactionRollbackScope(_isolationLevel, _connection, InternalTransaction, transactionName))
            {
            #endif

                ZombieCheck();
                SqlStatistics statistics = null;

                using (TryEventScope.Create(SqlClientEventSource.Log.TryScopeEnterEvent(eventScopeMessage, ObjectID, transactionName, ActivityCorrelator.Current, Connection?.ClientConnectionId)))
                {
                    #if NETFRAMEWORK
                    TdsParser bestEffortCleanupTarget = null;
                    RuntimeHelpers.PrepareConstrainedRegions();
                    #endif

                    try
                    {
                        void RollbackInternal()
                        {
                            statistics = SqlStatistics.StartTimer(Statistics);
                            _isFromAPI = true;

                            _internalTransaction.Rollback(transactionName);
                        }

                        #if NETFRAMEWORK && DEBUG
                        ExecuteInReliabilitySection(RollbackInternal);
                        #else
                        RollbackInternal();
                        #endif
                    }
                    #if NETFRAMEWORK
                    catch (OutOfMemoryException e)
                    {
                        _connection.Abort(e);
                        throw;
                    }
                    catch (StackOverflowException e)
                    {
                        _connection.Abort(e);
                        throw;
                    }
                    catch (ThreadAbortException e)
                    {
                        _connection.Abort(e);
                        SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                        throw;
                    }
                    #else
                    catch (Exception ex)
                    {
                        diagnosticScope.SetException(ex);
                        throw;
                    }
                    #endif
                    finally
                    {
                        _isFromAPI = false;
                        SqlStatistics.StopTimer(statistics);
                    }
                }
            #if NET
            } // Close diagnostic scope using block
            #endif
        }
        
        /// <include file='../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlTransaction.xml' path='docs/members[@name="SqlTransaction"]/Save/*' />
        public override void Save(string savePointName)
        {
            #if NETFRAMEWORK
            SqlConnection.ExecutePermission.Demand(); // MDAC 81476
            const string eventScopeMessage = "<sc.SqlTransaction.Save|API> {0} savePointName='{1}'";
            #else
            const string eventScopeMessage = "SqlTransaction.Save | API | Object Id {0} | Save Point Name '{1}'"
            #endif
            
            ZombieCheck();
            SqlStatistics statistics = null;
            
            using (TryEventScope.Create(eventScopeMessage, ObjectID, savePointName))
            {
                #if NETFRAMEWORK
                TdsParser bestEffortCleanupTarget = null;
                RuntimeHelpers.PrepareConstrainedRegions();
                #endif
                
                try
                {
                    void SaveInternal()
                    {
                        statistics = SqlStatistics.StartTimer(Statistics);
                        _internalTransaction.Save(savePointName);
                    }

                    #if NETFRAMEWORK && DEBUG
                    bestEffortCleanupTarget = SqlInternalConnection.GetBestEffortCleanupTarget(_connection);
                    ExecuteInReliabilitySection(SaveInternal);
                    #else
                    SaveInternal();
                    #endif
                }
                #if NETFRAMEWORK
                catch (OutOfMemoryException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (StackOverflowException e)
                {
                    _connection.Abort(e);
                    throw;
                }
                catch (ThreadAbortException e)
                {
                    _connection.Abort(e);
                    SqlInternalConnection.BestEffortCleanup(bestEffortCleanupTarget);
                    throw;
                }
                #endif
                finally
                {
                    SqlStatistics.StopTimer(statistics);
                }
            }
        }

        #if NETFRAMEWORK && DEBUG
        private static void ExecuteInReliabilitySection(Action action)
        {
            TdsParser.ReliabilitySection tdsReliabilitySection = new();
            
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                tdsReliabilitySection.Start();

                action();
            }
            finally
            {
                tdsReliabilitySection.Stop();
            }
        }
        #endif
    }
}
