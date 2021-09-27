// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using Microsoft.Data.Common;

// NOTE: Since Microsoft.VSDesigner only supports SDS and it's not publicly published, it's not allowed to use it, and API scan will complain.
namespace Microsoft.Data.SqlClient
{
    /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/SqlDataAdapter/*' />
    [DefaultEvent("RowUpdated")]
    [DesignerCategory("")]
    // TODO: Add designer and toolbox attribute when Microsoft.VSDesigner.Data.VS.SqlDataAdapterDesigner uses Microsoft.Data.SqlClient
    public sealed class SqlDataAdapter : DbDataAdapter, IDbDataAdapter, ICloneable
    {
        private static readonly object EventRowUpdated = new object();
        private static readonly object EventRowUpdating = new object();

        private SqlCommand _deleteCommand, _insertCommand, _selectCommand, _updateCommand;

        private SqlCommandSet _commandSet;
        private int _updateBatchSize = 1;

        private static int _objectTypeCount; // EventSource Counter
        internal readonly int _objectID = Interlocked.Increment(ref _objectTypeCount);

        internal int ObjectID => _objectID;

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ctor2/*' />
        public SqlDataAdapter() : base()
        {
            GC.SuppressFinalize(this);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ctorSelectCommand/*' />
        public SqlDataAdapter(SqlCommand selectCommand) : this()
        {
            SelectCommand = selectCommand;
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ctorSelectCommandTextSelectConnectionString/*' />
        public SqlDataAdapter(string selectCommandText, string selectConnectionString) : this()
        {
            SqlConnection connection = new SqlConnection(selectConnectionString);
            SelectCommand = new SqlCommand(selectCommandText, connection);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ctorSelectCommandTextSelectConnection/*' />
        public SqlDataAdapter(string selectCommandText, SqlConnection selectConnection) : this()
        {
            SelectCommand = new SqlCommand(selectCommandText, selectConnection);
        }

        private SqlDataAdapter(SqlDataAdapter from) : base(from)
        {   // Clone
            GC.SuppressFinalize(this);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/DeleteCommand/*' />
        [DefaultValue(null)]
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Update)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_DeleteCommand)]
        new public SqlCommand DeleteCommand
        {
            get { return _deleteCommand; }
            set { _deleteCommand = value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/System.Data.IDbDataAdapter.DeleteCommand/*' />
        IDbCommand IDbDataAdapter.DeleteCommand
        {
            get { return _deleteCommand; }
            set { _deleteCommand = (SqlCommand)value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/InsertCommand/*' />
        [DefaultValue(null)]
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Update)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_InsertCommand)]
        new public SqlCommand InsertCommand
        {
            get { return _insertCommand; }
            set { _insertCommand = value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/System.Data.IDbDataAdapter.InsertCommand/*' />
        IDbCommand IDbDataAdapter.InsertCommand
        {
            get { return _insertCommand; }
            set { _insertCommand = (SqlCommand)value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/SelectCommand/*' />
        [DefaultValue(null)]
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Fill)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_SelectCommand)]
        new public SqlCommand SelectCommand
        {
            get { return _selectCommand; }
            set { _selectCommand = value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/System.Data.IDbDataAdapter.SelectCommand/*' />
        IDbCommand IDbDataAdapter.SelectCommand
        {
            get { return _selectCommand; }
            set { _selectCommand = (SqlCommand)value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/UpdateCommand/*' />
        [DefaultValue(null)]
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Update)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_UpdateCommand)]
        new public SqlCommand UpdateCommand
        {
            get { return _updateCommand; }
            set { _updateCommand = value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/System.Data.IDbDataAdapter.UpdateCommand/*' />
        IDbCommand IDbDataAdapter.UpdateCommand
        {
            get { return _updateCommand; }
            set { _updateCommand = (SqlCommand)value; }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/UpdateBatchSize/*' />
        public override int UpdateBatchSize
        {
            get
            {
                return _updateBatchSize;
            }
            set
            {
                if (0 > value)
                {
                    throw ADP.ArgumentOutOfRange(nameof(UpdateBatchSize));
                }
                _updateBatchSize = value;
                SqlClientEventSource.Log.TryTraceEvent("SqlDataAdapter.Set_UpdateBatchSize | API | Object Id {0}, Update Batch Size value {1}", ObjectID, value);
            }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/AddToBatch/*' />
        protected override int AddToBatch(IDbCommand command)
        {
            int commandIdentifier = _commandSet.CommandCount;
            _commandSet.Append((SqlCommand)command);
            return commandIdentifier;
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ClearBatch/*' />
        protected override void ClearBatch()
        {
            _commandSet.Clear();
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/ExecuteBatch/*' />
        protected override int ExecuteBatch()
        {
            Debug.Assert(null != _commandSet && (0 < _commandSet.CommandCount), "no commands");
            SqlClientEventSource.Log.TryCorrelationTraceEvent("SqlDataAdapter.ExecuteBatch | Info | Correlation | Object Id {0}, Activity Id {1}, Command Count {2}", ObjectID, ActivityCorrelator.Current, _commandSet.CommandCount);
            return _commandSet.ExecuteNonQuery();
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/GetBatchedParameter/*' />
        protected override IDataParameter GetBatchedParameter(int commandIdentifier, int parameterIndex)
        {
            Debug.Assert(commandIdentifier < _commandSet.CommandCount, "commandIdentifier out of range");
            Debug.Assert(parameterIndex < _commandSet.GetParameterCount(commandIdentifier), "parameter out of range");
            IDataParameter parameter = _commandSet.GetParameter(commandIdentifier, parameterIndex);
            return parameter;
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/GetBatchedRecordsAffected/*' />
        protected override bool GetBatchedRecordsAffected(int commandIdentifier, out int recordsAffected, out Exception error)
        {
            Debug.Assert(commandIdentifier < _commandSet.CommandCount, "commandIdentifier out of range");
            return _commandSet.GetBatchedAffected(commandIdentifier, out recordsAffected, out error);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/InitializeBatching/*' />
        protected override void InitializeBatching()
        {
            SqlClientEventSource.Log.TryTraceEvent("SqlDataAdapter.InitializeBatching | API | Object Id {0}", ObjectID);
            _commandSet = new SqlCommandSet();
            SqlCommand command = SelectCommand;
            if (null == command)
            {
                command = InsertCommand;
                if (null == command)
                {
                    command = UpdateCommand;
                    if (null == command)
                    {
                        command = DeleteCommand;
                    }
                }
            }
            if (null != command)
            {
                _commandSet.Connection = command.Connection;
                _commandSet.Transaction = command.Transaction;
                _commandSet.CommandTimeout = command.CommandTimeout;
            }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/TerminateBatching/*' />
        protected override void TerminateBatching()
        {
            if (null != _commandSet)
            {
                _commandSet.Dispose();
                _commandSet = null;
            }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/System.ICloneable.Clone/*' />
        object ICloneable.Clone()
        {
            return new SqlDataAdapter(this);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/CreateRowUpdatedEvent/*' />
        protected override RowUpdatedEventArgs CreateRowUpdatedEvent(DataRow dataRow, IDbCommand command, StatementType statementType, DataTableMapping tableMapping)
        {
            return new SqlRowUpdatedEventArgs(dataRow, command, statementType, tableMapping);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/CreateRowUpdatingEvent/*' />
        protected override RowUpdatingEventArgs CreateRowUpdatingEvent(DataRow dataRow, IDbCommand command, StatementType statementType, DataTableMapping tableMapping)
        {
            return new SqlRowUpdatingEventArgs(dataRow, command, statementType, tableMapping);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/RowUpdated/*' />
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Update)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_RowUpdated)]
        public event SqlRowUpdatedEventHandler RowUpdated
        {
            add
            {
                Events.AddHandler(EventRowUpdated, value);
            }
            remove
            {
                Events.RemoveHandler(EventRowUpdated, value);
            }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/RowUpdating/*' />
        [ResCategoryAttribute(StringsHelper.ResourceNames.DataCategory_Update)]
        [ResDescriptionAttribute(StringsHelper.ResourceNames.DbDataAdapter_RowUpdating)]
        public event SqlRowUpdatingEventHandler RowUpdating
        {
            add
            {
                SqlRowUpdatingEventHandler handler = (SqlRowUpdatingEventHandler)Events[EventRowUpdating];

                // Prevent someone from registering two different command builders on the adapter by
                // silently removing the old one.
                if ((null != handler) && (value.Target is DbCommandBuilder))
                {
                    SqlRowUpdatingEventHandler d = (SqlRowUpdatingEventHandler)ADP.FindBuilder(handler);
                    if (null != d)
                    {
                        Events.RemoveHandler(EventRowUpdating, d);
                    }
                }
                Events.AddHandler(EventRowUpdating, value);
            }
            remove
            {
                Events.RemoveHandler(EventRowUpdating, value);
            }
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/OnRowUpdated/*' />
        override protected void OnRowUpdated(RowUpdatedEventArgs value)
        {
            SqlRowUpdatedEventHandler handler = (SqlRowUpdatedEventHandler)Events[EventRowUpdated];
            if ((null != handler) && (value is SqlRowUpdatedEventArgs))
            {
                handler(this, (SqlRowUpdatedEventArgs)value);
            }
            base.OnRowUpdated(value);
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlDataAdapter.xml' path='docs/members[@name="SqlDataAdapter"]/OnRowUpdating/*' />
        override protected void OnRowUpdating(RowUpdatingEventArgs value)
        {
            SqlRowUpdatingEventHandler handler = (SqlRowUpdatingEventHandler)Events[EventRowUpdating];
            if ((null != handler) && (value is SqlRowUpdatingEventArgs))
            {
                handler(this, (SqlRowUpdatingEventArgs)value);
            }
            base.OnRowUpdating(value);
        }
    }
}
