﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Common;
using Microsoft.Data.ProviderBase;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClientX.Handlers.Connection.Login;
using Microsoft.Data.SqlClientX.IO;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Microsoft.Data.SqlClientX.Handlers.Connection
{
    internal class LoginHandler : IHandler<ConnectionHandlerContext>
    {
        public IHandler<ConnectionHandlerContext> NextHandler { get; set; }

        public ValueTask Handle(ConnectionHandlerContext context, bool isAsync, CancellationToken ct)
        {
            ValidateIncomingContext(context);

            LoginHandlerContext loginHandlerContext = new LoginHandlerContext(context);
            PrepareLoginDetails(loginHandlerContext);

            void ValidateIncomingContext(ConnectionHandlerContext context)
            {
                if (context.ConnectionString is null)
                {
                    throw new ArgumentNullException(nameof(context.ConnectionString));
                }

                if (context.DataSource is null)
                {
                    throw new ArgumentNullException(nameof(context.DataSource));
                }

                if (context.ConnectionStream is null)
                {
                    throw new ArgumentNullException(nameof(context.ConnectionStream));
                }

                if (context.Error is not null)
                {
                    return;
                }
            }

            return ValueTask.CompletedTask;
        }

        private void PrepareLoginDetails(LoginHandlerContext context)
        {
            SqlLogin login = new SqlLogin();

            PasswordChangeRequest passwordChangeRequest = context.ConnectionContext.PasswordChangeRequest;

            // gather all the settings the user set in the connection string or
            // properties and do the login
            string currentDatabase = context.ServerInfo.ResolvedDatabaseName;

            string currentLanguage = context.ConnectionOptions.CurrentLanguage;

            TimeoutTimer timeout = context.ConnectionContext.TimeoutTimer;

            // If a timeout tick value is specified, compute the timeout based
            // upon the amount of time left in seconds.

            // TODO: Rethink timeout handling.

            int timeoutInSeconds = 0;

            if (!timeout.IsInfinite)
            {
                long t = timeout.MillisecondsRemaining / 1000;

                // This change was done because the timeout 0 being sent to SNI led to infinite timeout.
                // TODO: is this really needed for Managed code? 
                if (t == 0 && LocalAppContextSwitches.UseMinimumLoginTimeout)
                {
                    // Take 1 as the minimum value, since 0 is treated as an infinite timeout
                    // to allow 1 second more for login to complete, since it should take only a few milliseconds.
                    t = 1;
                }

                if (int.MaxValue > t)
                {
                    timeoutInSeconds = (int)t;
                }
            }

            login.authentication = context.ConnectionOptions.Authentication;
            login.timeout = timeoutInSeconds;
            login.userInstance = context.ConnectionOptions.UserInstance;
            login.hostName = context.ConnectionOptions.ObtainWorkstationId();
            login.userName = context.ConnectionOptions.UserID;
            login.password = context.ConnectionOptions.Password;
            login.applicationName = context.ConnectionOptions.ApplicationName;

            login.language = currentLanguage;
            if (!login.userInstance)
            {
                // Do not send attachdbfilename or database to SSE primary instance
                login.database = currentDatabase;
                login.attachDBFilename = context.ConnectionOptions.AttachDBFilename;
            }

            // VSTS#795621 - Ensure ServerName is Sent During TdsLogin To Enable Sql Azure Connectivity.
            // Using server.UserServerName (versus ConnectionOptions.DataSource) since TdsLogin requires
            // serverName to always be non-null.
            login.serverName = context.ServerInfo.UserServerName;

            login.useReplication = context.ConnectionOptions.Replication;
            login.useSSPI = context.ConnectionOptions.IntegratedSecurity  // Treat AD Integrated like Windows integrated when against a non-FedAuth endpoint
                                     || (context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryIntegrated 
                                     && !context.ConnectionContext.FedAuthRequired);
            login.packetSize = context.ConnectionOptions.PacketSize;
            login.newPassword = passwordChangeRequest?.NewPassword;
            login.readOnlyIntent = context.ConnectionOptions.ApplicationIntent == ApplicationIntent.ReadOnly;
            login.credential = passwordChangeRequest?.Credential;
            if (passwordChangeRequest?.NewSecurePassword != null)
            {
                login.newSecurePassword = passwordChangeRequest?.NewSecurePassword;
            }

            TdsEnums.FeatureExtension requestedFeatures = TdsEnums.FeatureExtension.None;
            TdsFeatures features = context.Features;
            if (context.ConnectionOptions.ConnectRetryCount > 0)
            {
                requestedFeatures |= TdsEnums.FeatureExtension.SessionRecovery;
                features.SessionRecoveryRequested = true;
            }

            
            if (ShouldRequestFedAuth(context))
            {
                requestedFeatures |= TdsEnums.FeatureExtension.FedAuth;
                features.FederatedAuthenticationInfoRequested = true;
                features.FedAuthFeatureExtensionData =
                    new FederatedAuthenticationFeatureExtensionData
                    {
                        libraryType = TdsEnums.FedAuthLibrary.MSAL,
                        authentication = context.ConnectionOptions.Authentication,
                        fedAuthRequiredPreLoginResponse = context.ConnectionContext.FedAuthRequired
                    };
            }

            if (context.ConnectionContext.AccessTokenInBytes != null)
            {
                requestedFeatures |= TdsEnums.FeatureExtension.FedAuth;
                features.FedAuthFeatureExtensionData = new FederatedAuthenticationFeatureExtensionData
                {
                    libraryType = TdsEnums.FedAuthLibrary.SecurityToken,
                    fedAuthRequiredPreLoginResponse = context.ConnectionContext.FedAuthRequired,
                    accessToken = context.ConnectionContext.AccessTokenInBytes
                };
                // No need any further info from the server for token based authentication. So set _federatedAuthenticationRequested to true
                features.FederatedAuthenticationRequested = true;
            }

            // The GLOBALTRANSACTIONS, DATACLASSIFICATION, TCE, and UTF8 support features are implicitly requested
            requestedFeatures |= TdsEnums.FeatureExtension.GlobalTransactions | TdsEnums.FeatureExtension.DataClassification | TdsEnums.FeatureExtension.Tce | TdsEnums.FeatureExtension.UTF8Support;

            // The SQLDNSCaching feature is implicitly set
            requestedFeatures |= TdsEnums.FeatureExtension.SQLDNSCaching;

            features.RequestedFeatures = requestedFeatures;
            context.Login = login;
            TdsLogin(context);

            // If the workflow being used is Active Directory Authentication and server's prelogin response
            // for FEDAUTHREQUIRED option indicates Federated Authentication is required, we have to insert FedAuth Feature Extension
            // in Login7, indicating the intent to use Active Directory Authentication for SQL Server.
            static bool ShouldRequestFedAuth(LoginHandlerContext context)
            {
                return context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryPassword
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryInteractive
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryDeviceCodeFlow
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryServicePrincipal
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryMSI
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryDefault
                                || context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity
                                // Since AD Integrated may be acting like Windows integrated, additionally check _fedAuthRequired
                                || (context.ConnectionOptions.Authentication == SqlAuthenticationMethod.ActiveDirectoryIntegrated && context.ConnectionContext.FedAuthRequired)
                                || context.ConnectionContext.AccessTokenCallback != null;
            }
        }

        private void TdsLogin(LoginHandlerContext context)
        {
            // TODO: Set the timeout
            _ = context.Login.timeout;

            // TODO: Add debug asserts.

            // TODO: Add timeout internal details.

            // TODO: Password Change

            context.ConnectionContext.TdsStream.PacketHeaderType = TdsStreamPacketType.Login7;

            // Fixed length of the login record
            int length = TdsEnums.SQL2005_LOG_REC_FIXED_LEN;


            string clientInterfaceName = TdsEnums.SQL_PROVIDER_NAME;
            Debug.Assert(TdsEnums.MAXLEN_CLIENTINTERFACE >= clientInterfaceName.Length, "cchCltIntName can specify at most 128 unicode characters. See Tds spec");

            SqlLogin rec = context.Login;

            // Calculate the fixed length
            checked
            {
                

                length += (rec.hostName.Length + rec.applicationName.Length +
                            rec.serverName.Length + clientInterfaceName.Length +
                            rec.language.Length + rec.database.Length +
                            rec.attachDBFilename.Length) * 2;
                if (context.Features.RequestedFeatures != TdsEnums.FeatureExtension.None)
                {
                    length += 4;
                }
            }

            byte[] rentedSSPIBuff = null;
            byte[] outSSPIBuff = null; // track the rented buffer as a separate variable in case it is updated via the ref parameter
            uint outSSPILength = 0;

            // only add lengths of password and username if not using SSPI or requesting federated authentication info
            if (!rec.useSSPI && !(context.Features.FederatedAuthenticationInfoRequested || context.Features.FederatedAuthenticationRequested))
            {
                checked
                {
                    length += (userName.Length * 2) + encryptedPasswordLengthInBytes
                    + encryptedChangePasswordLengthInBytes;
                }
            }
            else
            {
                if (rec.useSSPI)
                {
                    // now allocate proper length of buffer, and set length
                    outSSPILength = _authenticationProvider.MaxSSPILength;
                    rentedSSPIBuff = ArrayPool<byte>.Shared.Rent((int)outSSPILength);
                    outSSPIBuff = rentedSSPIBuff;

                    // Call helper function for SSPI data and actual length.
                    // Since we don't have SSPI data from the server, send null for the
                    // byte[] buffer and 0 for the int length.
                    Debug.Assert(SniContext.Snix_Login == _physicalStateObj.SniContext, $"Unexpected SniContext. Expecting Snix_Login, actual value is '{_physicalStateObj.SniContext}'");
                    _physicalStateObj.SniContext = SniContext.Snix_LoginSspi;
                    _authenticationProvider.SSPIData(ReadOnlyMemory<byte>.Empty, ref outSSPIBuff, ref outSSPILength, _sniSpnBuffer);

                    if (outSSPILength > int.MaxValue)
                    {
                        throw SQL.InvalidSSPIPacketSize();  // SqlBu 332503
                    }
                    _physicalStateObj.SniContext = SniContext.Snix_Login;

                    checked
                    {
                        length += (int)outSSPILength;
                    }
                }
            }


            int feOffset = length;
            // calculate and reserve the required bytes for the featureEx
            length = ApplyFeatureExData(requestedFeatures, recoverySessionData, fedAuthFeatureExtensionData, useFeatureExt, length);

            WriteLoginData(rec,
                           requestedFeatures,
                           recoverySessionData,
                           fedAuthFeatureExtensionData,
                           encrypt,
                           encryptedPassword,
                           encryptedChangePassword,
                           encryptedPasswordLengthInBytes,
                           encryptedChangePasswordLengthInBytes,
                           useFeatureExt,
                           userName,
                           length,
                           feOffset,
                           clientInterfaceName,
                           outSSPIBuff,
                           outSSPILength);

            if (rentedSSPIBuff != null)
            {
                ArrayPool<byte>.Shared.Return(rentedSSPIBuff, clearArray: true);
            }

            _physicalStateObj.WritePacket(TdsEnums.HARDFLUSH);
            _physicalStateObj.ResetSecurePasswordsInformation();     // Password information is needed only from Login process; done with writing login packet and should clear information
            _physicalStateObj.HasPendingData = true;
            _physicalStateObj._messageStatus = 0;

        }

        private async ValueTask WriteLoginData(SqlLogin rec,
                                     TdsEnums.FeatureExtension requestedFeatures,
                                     SessionData recoverySessionData,
                                     FederatedAuthenticationFeatureExtensionData fedAuthFeatureExtensionData,
                                     SqlConnectionEncryptOption encrypt,
                                     byte[] encryptedPassword,
                                     byte[] encryptedChangePassword,
                                     int encryptedPasswordLengthInBytes,
                                     int encryptedChangePasswordLengthInBytes,
                                     bool useFeatureExt,
                                     string userName,
                                     int length,
                                     int featureExOffset,
                                     string clientInterfaceName,
                                     byte[] outSSPIBuff,
                                     uint outSSPILength,
                                     LoginHandlerContext context,
                                     bool isAsync,
                                     CancellationToken ct)
        {
            try
            {
                TdsWriter writer = context.ConnectionContext.TdsStream.TdsWriter;
                await writer.WriteIntAsync(length, isAsync, ct).ConfigureAwait(false);
                WriteInt(length, _physicalStateObj);
                if (recoverySessionData == null)
                {
                    if (encrypt == SqlConnectionEncryptOption.Strict)
                    {
                        WriteInt((TdsEnums.TDS8_MAJOR << 24) | (TdsEnums.TDS8_INCREMENT << 16) | TdsEnums.TDS8_MINOR, _physicalStateObj);
                    }
                    else
                    {
                        WriteInt((TdsEnums.SQL2012_MAJOR << 24) | (TdsEnums.SQL2012_INCREMENT << 16) | TdsEnums.SQL2012_MINOR, _physicalStateObj);
                    }
                }
                else
                {
                    WriteUnsignedInt(recoverySessionData._tdsVersion, _physicalStateObj);
                }
                WriteInt(rec.packetSize, _physicalStateObj);
                WriteInt(TdsEnums.CLIENT_PROG_VER, _physicalStateObj);
                WriteInt(TdsParserStaticMethods.GetCurrentProcessIdForTdsLoginOnly(), _physicalStateObj);
                WriteInt(0, _physicalStateObj); // connectionID is unused

                // Log7Flags (DWORD)
                int log7Flags = 0;

                /*
                 Current snapshot from TDS spec with the offsets added:
                    0) fByteOrder:1,                // byte order of numeric data types on client
                    1) fCharSet:1,                  // character set on client
                    2) fFloat:2,                    // Type of floating point on client
                    4) fDumpLoad:1,                 // Dump/Load and BCP enable
                    5) fUseDb:1,                    // USE notification
                    6) fDatabase:1,                 // Initial database fatal flag
                    7) fSetLang:1,                  // SET LANGUAGE notification
                    8) fLanguage:1,                 // Initial language fatal flag
                    9) fODBC:1,                     // Set if client is ODBC driver
                   10) fTranBoundary:1,             // Transaction boundary notification
                   11) fDelegatedSec:1,             // Security with delegation is available
                   12) fUserType:3,                 // Type of user
                   15) fIntegratedSecurity:1,       // Set if client is using integrated security
                   16) fSQLType:4,                  // Type of SQL sent from client
                   20) fOLEDB:1,                    // Set if client is OLEDB driver
                   21) fSpare1:3,                   // first bit used for read-only intent, rest unused
                   24) fResetPassword:1,            // set if client wants to reset password
                   25) fNoNBCAndSparse:1,           // set if client does not support NBC and Sparse column
                   26) fUserInstance:1,             // This connection wants to connect to a SQL "user instance"
                   27) fUnknownCollationHandling:1, // This connection can handle unknown collation correctly.
                   28) fExtension:1                 // Extensions are used
                   32 - total
                */

                // first byte
                log7Flags |= TdsEnums.USE_DB_ON << 5;
                log7Flags |= TdsEnums.INIT_DB_FATAL << 6;
                log7Flags |= TdsEnums.SET_LANG_ON << 7;

                // second byte
                log7Flags |= TdsEnums.INIT_LANG_FATAL << 8;
                log7Flags |= TdsEnums.ODBC_ON << 9;
                if (rec.useReplication)
                {
                    log7Flags |= TdsEnums.REPL_ON << 12;
                }
                if (rec.useSSPI)
                {
                    log7Flags |= TdsEnums.SSPI_ON << 15;
                }

                // third byte
                if (rec.readOnlyIntent)
                {
                    log7Flags |= TdsEnums.READONLY_INTENT_ON << 21; // read-only intent flag is a first bit of fSpare1
                }

                // 4th one
                if (!string.IsNullOrEmpty(rec.newPassword) || (rec.newSecurePassword != null && rec.newSecurePassword.Length != 0))
                {
                    log7Flags |= 1 << 24;
                }
                if (rec.userInstance)
                {
                    log7Flags |= 1 << 26;
                }
                if (useFeatureExt)
                {
                    log7Flags |= 1 << 28;
                }

                WriteInt(log7Flags, _physicalStateObj);
                SqlClientEventSource.Log.TryAdvancedTraceEvent("<sc.TdsParser.TdsLogin|ADV> {0}, TDS Login7 flags = {1}:", ObjectID, log7Flags);

                WriteInt(0, _physicalStateObj);  // ClientTimeZone is not used
                WriteInt(0, _physicalStateObj);  // LCID is unused by server

                // Start writing offset and length of variable length portions
                int offset = TdsEnums.SQL2005_LOG_REC_FIXED_LEN;

                // write offset/length pairs

                // note that you must always set ibHostName since it indicates the beginning of the variable length section of the login record
                WriteShort(offset, _physicalStateObj); // host name offset
                WriteShort(rec.hostName.Length, _physicalStateObj);
                offset += rec.hostName.Length * 2;

                // Only send user/password over if not fSSPI...  If both user/password and SSPI are in login
                // rec, only SSPI is used.  Confirmed same behavior as in luxor.
                if (!rec.useSSPI && !(_connHandler._federatedAuthenticationInfoRequested || _connHandler._federatedAuthenticationRequested))
                {
                    WriteShort(offset, _physicalStateObj);  // userName offset
                    WriteShort(userName.Length, _physicalStateObj);
                    offset += userName.Length * 2;

                    // the encrypted password is a byte array - so length computations different than strings
                    WriteShort(offset, _physicalStateObj); // password offset
                    WriteShort(encryptedPasswordLengthInBytes / 2, _physicalStateObj);
                    offset += encryptedPasswordLengthInBytes;
                }
                else
                {
                    // case where user/password data is not used, send over zeros
                    WriteShort(0, _physicalStateObj);  // userName offset
                    WriteShort(0, _physicalStateObj);
                    WriteShort(0, _physicalStateObj);  // password offset
                    WriteShort(0, _physicalStateObj);
                }

                WriteShort(offset, _physicalStateObj); // app name offset
                WriteShort(rec.applicationName.Length, _physicalStateObj);
                offset += rec.applicationName.Length * 2;

                WriteShort(offset, _physicalStateObj); // server name offset
                WriteShort(rec.serverName.Length, _physicalStateObj);
                offset += rec.serverName.Length * 2;

                WriteShort(offset, _physicalStateObj);
                if (useFeatureExt)
                {
                    WriteShort(4, _physicalStateObj); // length of ibFeatgureExtLong (which is a DWORD)
                    offset += 4;
                }
                else
                {
                    WriteShort(0, _physicalStateObj); // unused (was remote password ?)
                }

                WriteShort(offset, _physicalStateObj); // client interface name offset
                WriteShort(clientInterfaceName.Length, _physicalStateObj);
                offset += clientInterfaceName.Length * 2;

                WriteShort(offset, _physicalStateObj); // language name offset
                WriteShort(rec.language.Length, _physicalStateObj);
                offset += rec.language.Length * 2;

                WriteShort(offset, _physicalStateObj); // database name offset
                WriteShort(rec.database.Length, _physicalStateObj);
                offset += rec.database.Length * 2;

                if (null == s_nicAddress)
                    s_nicAddress = TdsParserStaticMethods.GetNetworkPhysicalAddressForTdsLoginOnly();

                _physicalStateObj.WriteByteArray(s_nicAddress, s_nicAddress.Length, 0);

                WriteShort(offset, _physicalStateObj); // ibSSPI offset
                if (rec.useSSPI)
                {
                    WriteShort((int)outSSPILength, _physicalStateObj);
                    offset += (int)outSSPILength;
                }
                else
                {
                    WriteShort(0, _physicalStateObj);
                }

                WriteShort(offset, _physicalStateObj); // DB filename offset
                WriteShort(rec.attachDBFilename.Length, _physicalStateObj);
                offset += rec.attachDBFilename.Length * 2;

                WriteShort(offset, _physicalStateObj); // reset password offset
                WriteShort(encryptedChangePasswordLengthInBytes / 2, _physicalStateObj);

                WriteInt(0, _physicalStateObj);        // reserved for chSSPI

                // write variable length portion
                WriteString(rec.hostName, _physicalStateObj);

                // if we are using SSPI, do not send over username/password, since we will use SSPI instead
                // same behavior as Luxor
                if (!rec.useSSPI && !(_connHandler._federatedAuthenticationInfoRequested || _connHandler._federatedAuthenticationRequested))
                {
                    WriteString(userName, _physicalStateObj);

                    if (rec.credential != null)
                    {
                        _physicalStateObj.WriteSecureString(rec.credential.Password);
                    }
                    else
                    {
                        _physicalStateObj.WriteByteArray(encryptedPassword, encryptedPasswordLengthInBytes, 0);
                    }
                }

                WriteString(rec.applicationName, _physicalStateObj);
                WriteString(rec.serverName, _physicalStateObj);

                // write ibFeatureExtLong
                if (useFeatureExt)
                {
                    if ((requestedFeatures & TdsEnums.FeatureExtension.FedAuth) != 0)
                    {
                        SqlClientEventSource.Log.TryTraceEvent("<sc.TdsParser.TdsLogin|SEC> Sending federated authentication feature request");
                    }

                    WriteInt(featureExOffset, _physicalStateObj);
                }

                WriteString(clientInterfaceName, _physicalStateObj);
                WriteString(rec.language, _physicalStateObj);
                WriteString(rec.database, _physicalStateObj);

                // send over SSPI data if we are using SSPI
                if (rec.useSSPI)
                    _physicalStateObj.WriteByteArray(outSSPIBuff, (int)outSSPILength, 0);

                WriteString(rec.attachDBFilename, _physicalStateObj);
                if (!rec.useSSPI && !(_connHandler._federatedAuthenticationInfoRequested || _connHandler._federatedAuthenticationRequested))
                {
                    if (rec.newSecurePassword != null)
                    {
                        _physicalStateObj.WriteSecureString(rec.newSecurePassword);
                    }
                    else
                    {
                        _physicalStateObj.WriteByteArray(encryptedChangePassword, encryptedChangePasswordLengthInBytes, 0);
                    }
                }

                ApplyFeatureExData(requestedFeatures, recoverySessionData, fedAuthFeatureExtensionData, useFeatureExt, length, true);
            }
            catch (Exception e)
            {
                if (ADP.IsCatchableExceptionType(e))
                {
                    // be sure to wipe out our buffer if we started sending stuff
                    _physicalStateObj.ResetPacketCounters();
                    _physicalStateObj.ResetBuffer();
                }

                throw;
            }
        }
    }

    internal class LoginHandlerContext : HandlerRequest
    {

        public LoginHandlerContext(ConnectionHandlerContext context)
        {
            this.ConnectionContext = context;
            this.ServerInfo = context.ServerInfo;
            this.ConnectionOptions = context.ConnectionString;
        }

        public ConnectionHandlerContext ConnectionContext { get; }
        public ServerInfo ServerInfo { get; }
        public SqlConnectionString ConnectionOptions { get; }

        /// <summary>
        /// Features in the login request.
        /// </summary>
        public TdsFeatures Features { get; internal set; } = new();
        public SqlLogin Login { get; internal set; }
    }
}
