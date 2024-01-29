// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.ProviderBase;

namespace Microsoft.Data.SqlClient.SNI
{
    /// <summary>
    /// SNI Asynchronous callback
    /// </summary>
    /// <param name="packet">SNI packet</param>
    /// <param name="sniErrorCode">SNI error code</param>
    internal delegate void SNIAsyncCallback(SNIPacket packet, uint sniErrorCode);

    /// <summary>
    /// SNI provider identifiers
    /// </summary>
    internal enum SNIProviders
    {
        HTTP_PROV = 0, // HTTP Provider
        NP_PROV = 1, // Named Pipes Provider
        SESSION_PROV = 2, // Session Provider
        SIGN_PROV = 3, // Sign Provider
        SM_PROV = 4, // Shared Memory Provider
        SMUX_PROV = 5, // SMUX Provider
        SSL_PROV = 6, // SSL Provider
        TCP_PROV = 7, // TCP Provider
        VIA_PROV = 8, // Virtual Interface Architecture Provider
        CTAIP_PROV = 9,
        MAX_PROVS = 10, // Number of providers
        INVALID_PROV = 11 // SQL Network Interfaces
    }

    /// <summary>
    /// SMUX packet header
    /// </summary>
    internal sealed class SNISMUXHeader
    {
        public const int HEADER_LENGTH = 16;

        public byte SMID;
        public byte flags;
        public ushort sessionId;
        public uint length;
        public uint sequenceNumber;
        public uint highwater;

        public void Read(byte[] bytes)
        {
            SMID = bytes[0];
            flags = bytes[1];
            Span<byte> span = bytes.AsSpan();
            sessionId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2));
            length = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)) - SNISMUXHeader.HEADER_LENGTH;
            sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));
            highwater = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12));
        }

        public void Write(Span<byte> bytes)
        {
            uint value = highwater;
            // access the highest element first to cause the largest range check in the jit, then fill in the rest of the value and carry on as normal
            bytes[15] = (byte)((value >> 24) & 0xff);
            bytes[12] = (byte)(value & 0xff); // BitConverter.GetBytes(_currentHeader.highwater).CopyTo(headerBytes, 12);
            bytes[13] = (byte)((value >> 8) & 0xff);
            bytes[14] = (byte)((value >> 16) & 0xff);

            bytes[0] = SMID; // BitConverter.GetBytes(_currentHeader.SMID).CopyTo(headerBytes, 0);
            bytes[1] = flags; // BitConverter.GetBytes(_currentHeader.flags).CopyTo(headerBytes, 1);

            value = sessionId;
            bytes[2] = (byte)(value & 0xff); // BitConverter.GetBytes(_currentHeader.sessionId).CopyTo(headerBytes, 2);
            bytes[3] = (byte)((value >> 8) & 0xff);

            value = length;
            bytes[4] = (byte)(value & 0xff); // BitConverter.GetBytes(_currentHeader.length).CopyTo(headerBytes, 4);
            bytes[5] = (byte)((value >> 8) & 0xff);
            bytes[6] = (byte)((value >> 16) & 0xff);
            bytes[7] = (byte)((value >> 24) & 0xff);

            value = sequenceNumber;
            bytes[8] = (byte)(value & 0xff); // BitConverter.GetBytes(_currentHeader.sequenceNumber).CopyTo(headerBytes, 8);
            bytes[9] = (byte)((value >> 8) & 0xff);
            bytes[10] = (byte)((value >> 16) & 0xff);
            bytes[11] = (byte)((value >> 24) & 0xff);

        }
    }

    /// <summary>
    /// SMUX packet flags
    /// </summary>
    [Flags]
    internal enum SNISMUXFlags
    {
        SMUX_SYN = 1,       // Begin SMUX connection
        SMUX_ACK = 2,       // Acknowledge SMUX packets
        SMUX_FIN = 4,       // End SMUX connection
        SMUX_DATA = 8       // SMUX data packet
    }

    internal class SNICommon
    {
        // Each error number maps to SNI_ERROR_* in String.resx
        internal const int ConnTerminatedError = 2;
        internal const int InvalidParameterError = 5;
        internal const int ProtocolNotSupportedError = 8;
        internal const int ConnTimeoutError = 11;
        internal const int ConnNotUsableError = 19;
        internal const int InvalidConnStringError = 25;
        internal const int ErrorLocatingServerInstance = 26;
        internal const int HandshakeFailureError = 31;
        internal const int InternalExceptionError = 35;
        internal const int ConnOpenFailedError = 40;
        internal const int ErrorSpnLookup = 44;
        internal const int LocalDBErrorCode = 50;
        internal const int MultiSubnetFailoverWithMoreThan64IPs = 47;
        internal const int MultiSubnetFailoverWithInstanceSpecified = 48;
        internal const int MultiSubnetFailoverWithNonTcpProtocol = 49;
        internal const int MaxErrorValue = 50157;
        internal const int LocalDBNoInstanceName = 51;
        internal const int LocalDBNoInstallation = 52;
        internal const int LocalDBInvalidConfig = 53;
        internal const int LocalDBNoSqlUserInstanceDllPath = 54;
        internal const int LocalDBInvalidSqlUserInstanceDllPath = 55;
        internal const int LocalDBFailedToLoadDll = 56;
        internal const int LocalDBBadRuntime = 57;

        /// <summary>
        /// We only validate Server name in Certificate to match with "targetServerName".
        /// Certificate validation and chain trust validations are done by SSLStream class [System.Net.Security.SecureChannel.VerifyRemoteCertificate method]
        /// This method is called as a result of callback for SSL Stream Certificate validation.
        /// </summary>
        /// <param name="targetServerName">Server that client is expecting to connect to</param>
        /// <param name="cert">X.509 certificate</param>
        /// <param name="policyErrors">Policy errors</param>
        /// <returns>True if certificate is valid</returns>
        internal static bool ValidateSslServerCertificate(string targetServerName, X509Certificate cert, SslPolicyErrors policyErrors)
        {
            using (TrySNIEventScope.Create("SNICommon.ValidateSslServerCertificate | SNI | SCOPE | INFO | Entering Scope {0} "))
            {
                if (policyErrors == SslPolicyErrors.None)
                {
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO, "targetServerName {0}, SSL Server certificate not validated as PolicyErrors set to None.", args0: targetServerName);
                    return true;
                }

                // If we get to this point then there is a ssl policy flag.
                StringBuilder messageBuilder = new();
                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                {
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, SslPolicyError {1}, SSL Policy certificate chain has errors.", args0: targetServerName, args1: policyErrors);

                    // get the chain status from the certificate
                    X509Certificate2 cert2 = cert as X509Certificate2;
                    X509Chain chain = new();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
                    StringBuilder chainStatusInformation = new();
                    bool chainIsValid = chain.Build(cert2);
                    Debug.Assert(!chainIsValid, "RemoteCertificateChainError flag is detected, but certificate chain is valid.");
                    if (!chainIsValid)
                    {
                        foreach (X509ChainStatus chainStatus in chain.ChainStatus)
                        {
                            chainStatusInformation.Append($"{chainStatus.StatusInformation}, [Status: {chainStatus.Status}]");
                            chainStatusInformation.AppendLine();
                        }
                    }
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, SslPolicyError {1}, SSL Policy certificate chain has errors. ChainStatus {2}", args0: targetServerName, args1: policyErrors, args2: chainStatusInformation);
                    messageBuilder.AppendFormat(Strings.SQL_RemoteCertificateChainErrors, chainStatusInformation);
                    messageBuilder.AppendLine();
                }

                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, SSL Policy invalidated certificate.", args0: targetServerName);
                    messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNotAvailable);
                }

                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
#if NET7_0_OR_GREATER
                    X509Certificate2 cert2 = cert as X509Certificate2;
                    if (!cert2.MatchesHostname(targetServerName))
                    {
                        SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, Target Server name or HNIC does not match the Subject/SAN in Certificate.", args0: targetServerName);
                        messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                    }
#else
                    // To Do: include certificate SAN (Subject Alternative Name) check.
                    string certServerName = cert.Subject.Substring(cert.Subject.IndexOf('=') + 1);

                    // Verify that target server name matches subject in the certificate
                    if (targetServerName.Length > certServerName.Length)
                    {
                        SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, Target Server name is of greater length than Subject in Certificate.", args0: targetServerName);
                        messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                    }
                    else if (targetServerName.Length == certServerName.Length)
                    {
                        // Both strings have the same length, so targetServerName must be a FQDN
                        if (!targetServerName.Equals(certServerName, StringComparison.OrdinalIgnoreCase))
                        {
                            SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, Target Server name does not match Subject in Certificate.", args0: targetServerName);
                            messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                        }
                    }
                    else
                    {
                        if (string.Compare(targetServerName, 0, certServerName, 0, targetServerName.Length, StringComparison.OrdinalIgnoreCase) != 0)
                        {
                            SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, Target Server name does not match Subject in Certificate.", args0: targetServerName);
                            messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                        }

                        // Server name matches cert name for its whole length, so ensure that the
                        // character following the server name is a '.'. This will avoid
                        // having server name "ab" match "abc.corp.company.com"
                        // (Names have different lengths, so the target server can't be a FQDN.)
                        if (certServerName[targetServerName.Length] != '.')
                        {
                            SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "targetServerName {0}, Target Server name does not match Subject in Certificate.", args0: targetServerName);
                            messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                        }
                    }
#endif
                }

                if (messageBuilder.Length > 0)
                {
                    throw ADP.SSLCertificateAuthenticationException(messageBuilder.ToString());
                }

                SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO, " Remote certificate with subject: {0}, validated successfully.", args0: cert.Subject);
                return true;
            }
        }

        /// <summary>
        /// We validate the provided certificate provided by the client with the one from the server to see if it matches.
        /// Certificate validation and chain trust validations are done by SSLStream class [System.Net.Security.SecureChannel.VerifyRemoteCertificate method]
        /// This method is called as a result of callback for SSL Stream Certificate validation.
        /// </summary>
        /// <param name="clientCert">X.509 certificate provided by the client</param>
        /// <param name="serverCert">X.509 certificate provided by the server</param>
        /// <param name="policyErrors">Policy errors</param>
        /// <returns>True if certificate is valid</returns>
        internal static bool ValidateSslServerCertificate(X509Certificate clientCert, X509Certificate serverCert, SslPolicyErrors policyErrors)
        {
            using (TrySNIEventScope.Create("SNICommon.ValidateSslServerCertificate | SNI | SCOPE | INFO | Entering Scope {0} "))
            {
                if (policyErrors == SslPolicyErrors.None)
                {
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO, "serverCert {0}, SSL Server certificate not validated as PolicyErrors set to None.", args0: clientCert.Subject);
                    return true;
                }

                StringBuilder messageBuilder = new();
                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "serverCert {0}, SSL Server certificate not validated as PolicyErrors set to RemoteCertificateNotAvailable.", args0: clientCert.Subject);
                    messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNotAvailable);
                }

                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                {
                    // get the chain status from the server certificate
                    X509Certificate2 cert2 = serverCert as X509Certificate2;
                    X509Chain chain = new();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
                    StringBuilder chainStatusInformation = new();
                    bool chainIsValid = chain.Build(cert2);
                    Debug.Assert(!chainIsValid, "RemoteCertificateChainError flag is detected, but certificate chain is valid.");
                    if (!chainIsValid)
                    {
                        foreach (X509ChainStatus chainStatus in chain.ChainStatus)
                        {
                            chainStatusInformation.Append($"{chainStatus.StatusInformation}, [Status: {chainStatus.Status}]");
                            chainStatusInformation.AppendLine();
                        }
                    }
                    SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "certificate subject from server is {0}, and does not match with the certificate provided client.", args0: cert2.SubjectName.Name);
                    messageBuilder.AppendFormat(Strings.SQL_RemoteCertificateChainErrors, chainStatusInformation);
                    messageBuilder.AppendLine();
                }

                if (policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
#if NET7_0_OR_GREATER
                    X509Certificate2 s_cert = serverCert as X509Certificate2;
                    X509Certificate2 c_cert = clientCert as X509Certificate2;

                    if (!s_cert.MatchesHostname(c_cert.SubjectName.Name))
                    {
                        SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "certificate from server does not match with the certificate provided client.", args0: s_cert.Subject);
                        messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                    }
#else
                    // Verify that subject name matches
                    if (serverCert.Subject != clientCert.Subject)
                    {
                        SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "certificate subject from server is {0}, and does not match with the certificate provided client.", args0: serverCert.Subject);
                        messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                    }

                    if (!serverCert.Equals(clientCert))
                    {
                        SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "certificate from server does not match with the certificate provided client.", args0: serverCert.Subject);
                        messageBuilder.AppendLine(Strings.SQL_RemoteCertificateNameMismatch);
                    }
#endif
                }

                if (messageBuilder.Length > 0)
                {
                    throw ADP.SSLCertificateAuthenticationException(messageBuilder.ToString());
                }

                SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO, "certificate subject {0}, Client certificate validated successfully.", args0: clientCert.Subject);
                return true;
            }
        }

        internal static IPAddress[] GetDnsIpAddresses(string serverName, TimeoutTimer timeout)
        {
            using (TrySNIEventScope.Create(nameof(GetDnsIpAddresses)))
            {
                int remainingTimeout = timeout.MillisecondsRemainingInt;
                SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO,
                                                          "Getting DNS host entries for serverName {0} within {1} milliseconds.",
                                                          args0: serverName,
                                                          args1: remainingTimeout);
                using CancellationTokenSource cts = new CancellationTokenSource(remainingTimeout);
                // using this overload to support netstandard
                //TODO Refactor?
                Task<IPAddress[]> task = Dns.GetHostAddressesAsync(serverName);
                task.ConfigureAwait(false);
                task.Wait(cts.Token);
                return task.Result;
            }
        }

        internal static IPAddress[] GetDnsIpAddresses(string serverName)
        {
            using (TrySNIEventScope.Create(nameof(GetDnsIpAddresses)))
            {
                SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.INFO, "Getting DNS host entries for serverName {0}.", args0: serverName);
                return Dns.GetHostAddresses(serverName);
            }
        }

        /// <summary>
        /// Sets last error encountered for SNI
        /// </summary>
        /// <param name="provider">SNI provider</param>
        /// <param name="nativeError">Native error code</param>
        /// <param name="sniError">SNI error code</param>
        /// <param name="errorMessage">Error message</param>
        /// <returns></returns>
        internal static uint ReportSNIError(SNIProviders provider, uint nativeError, uint sniError, string errorMessage)
        {
            SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "Provider = {0}, native Error = {1}, SNI Error = {2}, Error Message = {3}", args0: provider, args1: nativeError, args2: sniError, args3: errorMessage);
            return ReportSNIError(new SNIError(provider, nativeError, sniError, errorMessage));
        }

        /// <summary>
        /// Sets last error encountered for SNI
        /// </summary>
        /// <param name="provider">SNI provider</param>
        /// <param name="sniError">SNI error code</param>
        /// <param name="sniException">SNI Exception</param>
        /// <param name="nativeErrorCode">Native SNI error code</param>
        /// <returns></returns>
        internal static uint ReportSNIError(SNIProviders provider, uint sniError, Exception sniException, uint nativeErrorCode = 0)
        {
            SqlClientEventSource.Log.TrySNITraceEvent(nameof(SNICommon), EventType.ERR, "Provider = {0}, SNI Error = {1}, Exception = {2}", args0: provider, args1: sniError, args2: sniException?.Message);
            return ReportSNIError(new SNIError(provider, sniError, sniException, nativeErrorCode));
        }

        /// <summary>
        /// Sets last error encountered for SNI
        /// </summary>
        /// <param name="error">SNI error</param>
        /// <returns></returns>
        internal static uint ReportSNIError(SNIError error)
        {
            SNILoadHandle.SingletonInstance.LastError = error;
            return TdsEnums.SNI_ERROR;
        }
    }
}
