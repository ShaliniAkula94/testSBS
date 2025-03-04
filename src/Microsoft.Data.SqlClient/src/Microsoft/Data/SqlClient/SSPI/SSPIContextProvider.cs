﻿using System;
using System.Buffers;
using System.Diagnostics;
using Microsoft.Data.Common;

#nullable enable

namespace Microsoft.Data.SqlClient
{
    /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SSPIContextProvider.xml' path='docs/members[@name="SSPIContextProvider"]/SSPIContextProvider/*'/>
    public abstract class SSPIContextProvider
    {
        private TdsParser _parser = null!;
        private ServerInfo _serverInfo = null!;
        private protected TdsParserStateObject _physicalStateObj = null!;

        internal void Initialize(ServerInfo serverInfo, TdsParserStateObject physicalStateObj, TdsParser parser)
        {
            _parser = parser;
            _physicalStateObj = physicalStateObj;
            _serverInfo = serverInfo;

            Initialize();
        }

        private protected virtual void Initialize()
        {
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SSPIContextProvider.xml' path='docs/members[@name="SSPIContextProvider"]/SSPIContextProvider/GenerateSspiClientContext'/>
        protected abstract bool GenerateSspiClientContext(ReadOnlySpan<byte> incomingBlob, IBufferWriter<byte> outgoingBlobWriter, SqlAuthenticationParameters authParams);

        internal void SSPIData(ReadOnlySpan<byte> receivedBuff, IBufferWriter<byte> outgoingBlobWriter, string serverSpn)
        {
            using var _ = TrySNIEventScope.Create(nameof(SSPIContextProvider));

            if (!RunGenerateSspiClientContext(receivedBuff, outgoingBlobWriter, serverSpn))
            {
                // If we've hit here, the SSPI context provider implementation failed to generate the SSPI context.
                SSPIError(SQLMessage.SSPIGenerateError(), TdsEnums.GEN_CLIENT_CONTEXT);
            }
        }

        internal void SSPIData(ReadOnlySpan<byte> receivedBuff, IBufferWriter<byte> outgoingBlobWriter, ReadOnlySpan<string> serverSpns)
        {
            using var _ = TrySNIEventScope.Create(nameof(SSPIContextProvider));

            foreach (var serverSpn in serverSpns)
            {
                if (RunGenerateSspiClientContext(receivedBuff, outgoingBlobWriter, serverSpn))
                {
                    return;
                }
            }

            // If we've hit here, the SSPI context provider implementation failed to generate the SSPI context.
            SSPIError(SQLMessage.SSPIGenerateError(), TdsEnums.GEN_CLIENT_CONTEXT);
        }

        private bool RunGenerateSspiClientContext(ReadOnlySpan<byte> incomingBlob, IBufferWriter<byte> outgoingBlobWriter, string serverSpn)
        {
            var authParams = CreateSqlAuthParams(_parser.Connection, serverSpn);

            try
            {
#if NET8_0_OR_GREATER
                SqlClientEventSource.Log.TryTraceEvent("{0}.{1} | Info | Session Id {2}, SPN={3}", GetType().FullName,
                    nameof(GenerateSspiClientContext), _physicalStateObj.SessionId, serverSpn);
#else
                SqlClientEventSource.Log.TryTraceEvent("{0}.{1} | Info | SPN={1}", GetType().FullName,
                    nameof(GenerateSspiClientContext), serverSpn);
#endif

                return GenerateSspiClientContext(incomingBlob, outgoingBlobWriter, authParams);
            }
            catch (Exception e)
            {
                SSPIError(e.Message + Environment.NewLine + e.StackTrace, TdsEnums.GEN_CLIENT_CONTEXT);
                return false;
            }
        }

        private static SqlAuthenticationParameters CreateSqlAuthParams(SqlInternalConnectionTds connection, string serverSpn)
        {
            var auth = new SqlAuthenticationParameters.Builder(
                authenticationMethod: connection.ConnectionOptions.Authentication,
                resource: null,
                authority: null,
                serverName: serverSpn,
                connection.ConnectionOptions.InitialCatalog);

            if (connection.ConnectionOptions.UserID is { } userId)
            {
                auth.WithUserId(userId);
            }

            if (connection.ConnectionOptions.Password is { } password)
            {
                auth.WithPassword(password);
            }

            return auth;
        }

        private protected void SSPIError(string error, string procedure)
        {
            Debug.Assert(!ADP.IsEmpty(procedure), "TdsParser.SSPIError called with an empty or null procedure string");
            Debug.Assert(!ADP.IsEmpty(error), "TdsParser.SSPIError called with an empty or null error string");

            _physicalStateObj.AddError(new SqlError(0, (byte)0x00, (byte)TdsEnums.MIN_ERROR_CLASS, _serverInfo.ResolvedServerName, error, procedure, 0));
            _parser.ThrowExceptionAndWarning(_physicalStateObj);
        }
    }
}
