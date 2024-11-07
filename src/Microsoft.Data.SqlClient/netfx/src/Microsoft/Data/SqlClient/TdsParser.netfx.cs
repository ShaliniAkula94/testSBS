﻿using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using System.Net;
using System.Threading;

namespace Microsoft.Data.SqlClient
{
    internal sealed partial class TdsParser
    {
        // ReliabilitySection Usage:
        //
        // #if DEBUG
        //        TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();
        //
        //        RuntimeHelpers.PrepareConstrainedRegions();
        //        try {
        //            tdsReliabilitySection.Start();
        // #else
        //        {
        // #endif //DEBUG
        //
        //        // code that requires reliability
        //
        //        }
        // #if DEBUG
        //        finally {
        //            tdsReliabilitySection.Stop();
        //        }
        //  #endif //DEBUG

        internal struct ReliabilitySection
        {
#if DEBUG
            // do not allocate TLS data in RETAIL bits
            [ThreadStatic]
            private static int s_reliabilityCount; // initialized to 0 by CLR

            private bool m_started;  // initialized to false (not started) by CLR
#endif //DEBUG

            [Conditional("DEBUG")]
            internal void Start()
            {
#if DEBUG
                Debug.Assert(!m_started);

                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                }
                finally
                {
                    ++s_reliabilityCount;
                    m_started = true;
                }
#endif //DEBUG
            }

            [Conditional("DEBUG")]
            internal void Stop()
            {
#if DEBUG
                // cannot assert m_started - ThreadAbortException can be raised before Start is called

                if (m_started)
                {
                    Debug.Assert(s_reliabilityCount > 0);

                    RuntimeHelpers.PrepareConstrainedRegions();
                    try
                    {
                    }
                    finally
                    {
                        --s_reliabilityCount;
                        m_started = false;
                    }
                }
#endif //DEBUG
            }

            // you need to setup for a thread abort somewhere before you call this method
            [Conditional("DEBUG")]
            internal static void Assert(string message)
            {
#if DEBUG
                Debug.Assert(s_reliabilityCount > 0, message);
#endif //DEBUG
            }
        }

        // This is called from a ThreadAbort - ensure that it can be run from a CER Catch
        internal void BestEffortCleanup()
        {
            _state = TdsParserState.Broken;

            var stateObj = _physicalStateObj;
            if (stateObj != null)
            {
                var stateObjHandle = stateObj.Handle;
                if (stateObjHandle != null)
                {
                    stateObjHandle.Dispose();
                }
            }

            if (_fMARS)
            {
                var sessionPool = _sessionPool;
                if (sessionPool != null)
                {
                    sessionPool.BestEffortCleanup();
                }

                var marsStateObj = _pMarsPhysicalConObj;
                if (marsStateObj != null)
                {
                    var marsStateObjHandle = marsStateObj.Handle;
                    if (marsStateObjHandle != null)
                    {
                        marsStateObjHandle.Dispose();
                    }
                }
            }
        }

        // Retrieve the IP and port number from native SNI for TCP protocol. The IP information is stored temporarily in the
        // pendingSQLDNSObject but not in the DNS Cache at this point. We only add items to the DNS Cache after we receive the
        // IsSupported flag as true in the feature ext ack from server.
        internal void AssignPendingDNSInfo(string userProtocol, string DNSCacheKey)
        {
            uint result;
            ushort portFromSNI = 0;
            string IPStringFromSNI = string.Empty;
            IPAddress IPFromSNI;
            isTcpProtocol = false;
            SNINativeMethodWrapper.ProviderEnum providerNumber = SNINativeMethodWrapper.ProviderEnum.INVALID_PROV;

            if (string.IsNullOrEmpty(userProtocol))
            {

                result = SNINativeMethodWrapper.SniGetProviderNumber(_physicalStateObj.Handle, ref providerNumber);
                Debug.Assert(result == TdsEnums.SNI_SUCCESS, "Unexpected failure state upon calling SniGetProviderNumber");
                isTcpProtocol = (providerNumber == SNINativeMethodWrapper.ProviderEnum.TCP_PROV);
            }
            else if (userProtocol == TdsEnums.TCP)
            {
                isTcpProtocol = true;
            }

            // serverInfo.UserProtocol could be empty
            if (isTcpProtocol)
            {
                result = SNINativeMethodWrapper.SniGetConnectionPort(_physicalStateObj.Handle, ref portFromSNI);
                Debug.Assert(result == TdsEnums.SNI_SUCCESS, "Unexpected failure state upon calling SniGetConnectionPort");


                result = SNINativeMethodWrapper.SniGetConnectionIPString(_physicalStateObj.Handle, ref IPStringFromSNI);
                Debug.Assert(result == TdsEnums.SNI_SUCCESS, "Unexpected failure state upon calling SniGetConnectionIPString");

                _connHandler.pendingSQLDNSObject = new SQLDNSInfo(DNSCacheKey, null, null, portFromSNI.ToString());

                if (IPAddress.TryParse(IPStringFromSNI, out IPFromSNI))
                {
                    if (System.Net.Sockets.AddressFamily.InterNetwork == IPFromSNI.AddressFamily)
                    {
                        _connHandler.pendingSQLDNSObject.AddrIPv4 = IPStringFromSNI;
                    }
                    else if (System.Net.Sockets.AddressFamily.InterNetworkV6 == IPFromSNI.AddressFamily)
                    {
                        _connHandler.pendingSQLDNSObject.AddrIPv6 = IPStringFromSNI;
                    }
                }
            }
            else
            {
                _connHandler.pendingSQLDNSObject = null;
            }
        }

        internal bool RunReliably(RunBehavior runBehavior, SqlCommand cmdHandler, SqlDataReader dataStream, BulkCopySimpleResultSet bulkCopyHandler, TdsParserStateObject stateObj)
        {
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
#if DEBUG
                TdsParser.ReliabilitySection tdsReliabilitySection = new TdsParser.ReliabilitySection();
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    tdsReliabilitySection.Start();
#endif //DEBUG
                    return Run(runBehavior, cmdHandler, dataStream, bulkCopyHandler, stateObj);
#if DEBUG
                }
                finally
                {
                    tdsReliabilitySection.Stop();
                }
#endif //DEBUG
            }
            catch (OutOfMemoryException)
            {
                _connHandler.DoomThisConnection();
                throw;
            }
            catch (StackOverflowException)
            {
                _connHandler.DoomThisConnection();
                throw;
            }
            catch (ThreadAbortException)
            {
                _connHandler.DoomThisConnection();
                throw;
            }
        }
    }
}
