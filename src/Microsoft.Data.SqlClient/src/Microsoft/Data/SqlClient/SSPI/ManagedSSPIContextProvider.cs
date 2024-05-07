﻿#if NET6_0

using Microsoft.Data.SqlClient.SNI;

#nullable enable

namespace Microsoft.Data.SqlClient
{
    internal sealed class ManagedSSPIContextProvider : SSPIContextProvider
    {
        private SspiClientContextStatus? _sspiClientContextStatus;

        internal override void GenerateSspiClientContext(byte[] receivedBuff, uint receivedLength, ref byte[] sendBuff, ref uint sendLength, byte[][] _sniSpnBuffer)
        {
            _sspiClientContextStatus ??= new SspiClientContextStatus();

            SNIProxy.GenSspiClientContext(_sspiClientContextStatus, receivedBuff, ref sendBuff, _sniSpnBuffer);
            SqlClientEventSource.Log.TryTraceEvent("TdsParserStateObjectManaged.GenerateSspiClientContext | Info | Session Id {0}", _physicalStateObj.SessionId);
            sendLength = (uint)(sendBuff != null ? sendBuff.Length : 0);
        }
    }
}
#endif
