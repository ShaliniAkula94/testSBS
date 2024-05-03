﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data.SqlClient.SqlClientX;

namespace simplesqlclient
{
    internal class SqlPhysicalConnection
    {
        private NetworkStream _tcpStream;
        private SslOverTdsStream _sslOverTdsStream;
        private SslStream _sslStream;
        private BufferWriter _bufferWriter;
        private BufferReader _bufferReader;
        private string _hostname;
        private int _port;
        //private readonly string applicationName;
        private ConnectionSettings connectionSettings;
        private bool IsMarsEnabled;
        private bool ServerSupportsFedAuth;
        private readonly AuthenticationOptions authOptions;
        private readonly string database;


        public SqlPhysicalConnection(
            string hostname,
            int port,
            AuthenticationOptions authOptions,
            string database,
            ConnectionSettings connectionSettings)
        {
            this._hostname = hostname;
            this._port = port;
            this.authOptions = authOptions;
            this.database = database;
            this.connectionSettings = connectionSettings;
        }

        public void TcpConnect()
        {
            // Resolve DNS 

            IEnumerable<IPAddress> ipAddresses = Dns.GetHostAddresses(_hostname);
            // Connect to the first IP address
            IPAddress ipToConnect = ipAddresses.First((ipaddr) => ipaddr.AddressFamily == AddressFamily.InterNetwork);

            Socket socket = new Socket(ipToConnect.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false // We want to block until the connection is established
            };

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);

            try
            {
                // Now we have a TCP connection to the server.
                socket.Connect(ipToConnect, _port);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.WouldBlock)
                    throw;
            }

            var write = new List<Socket> { socket };
            var error = new List<Socket> { socket };
            Socket.Select(null, write, error, 30000000); // Wait for 30 seconds 
            if (write.Count > 0)
            {
                // Connection established
                socket.Blocking = true;
            }
            else
            {
                throw new Exception("Connection failed");
            }
            //socket.NoDelay = true;

            this._tcpStream = new NetworkStream(socket, true);

            this._sslOverTdsStream = new SslOverTdsStream(_tcpStream);

            this._sslStream = new SslStream(_sslOverTdsStream, true, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        #region Prelogin
        internal void SendPrelogin()
        {
            // 5 bytes for each option (1 byte length, 2 byte offset, 2 byte payload length)
            int preloginOptionsCount = 7;
            int offset = 36; // 7 * 5 + 1 add 1 to start after the first 40 bytes
            // The payload is the bytes for all the options and the maximum length of the payload
            byte[] payload = new byte[preloginOptionsCount * 5 + TdsConstants.MAX_PRELOGIN_PAYLOAD_LENGTH];
            int payLoadIndex = 0;
            _bufferWriter = new BufferWriter(TdsConstants.DEFAULT_LOGIN_PACKET_SIZE, _tcpStream);
            _bufferWriter.PacketType = PacketType.PRELOGIN;

            for (int option = 0; option < preloginOptionsCount; option++)
            {
                int optionDataSize = 0;

                // Fill in the option
                _bufferWriter.WriteByte((byte)option);

                // Fill in the offset of the option data
                _bufferWriter.WriteByte((byte)((offset & 0xff00) >> 8)); // send upper order byte
                _bufferWriter.WriteByte((byte)(offset & 0x00ff)); // send lower order byte

                switch (option)
                {
                    case 0:
                        Version.TryParse("6.0.0.0", out Version systemDataVersion);

                        // Major and minor
                        payload[payLoadIndex++] = (byte)(systemDataVersion.Major & 0xff);
                        payload[payLoadIndex++] = (byte)(systemDataVersion.Minor & 0xff);

                        // Build (Big Endian)
                        payload[payLoadIndex++] = (byte)((systemDataVersion.Build & 0xff00) >> 8);
                        payload[payLoadIndex++] = (byte)(systemDataVersion.Build & 0xff);

                        // Sub-build (Little Endian)
                        payload[payLoadIndex++] = (byte)(systemDataVersion.Revision & 0xff);
                        payload[payLoadIndex++] = (byte)((systemDataVersion.Revision & 0xff00) >> 8);
                        offset += 6;
                        optionDataSize = 6;
                        break;

                    case 1:

                        // Assume that the encryption is off
                        payload[payLoadIndex] = (byte)0;

                        payLoadIndex += 1;
                        offset += 1;
                        optionDataSize = 1;
                        break;

                    case 2:
                        int i = 0;
                        // Assume we dont need to send the instance name.
                        byte[] instanceName = new byte[1];
                        while (instanceName[i] != 0)
                        {
                            payload[payLoadIndex] = instanceName[i];
                            payLoadIndex++;
                            i++;
                        }

                        payload[payLoadIndex] = 0; // null terminate
                        payLoadIndex++;
                        i++;

                        offset += i;
                        optionDataSize = i;
                        break;

                    case (int)PreLoginOptions.THREADID:
                        int threadID = 1234; // Hard code some thread on the client side.

                        payload[payLoadIndex++] = (byte)((0xff000000 & threadID) >> 24);
                        payload[payLoadIndex++] = (byte)((0x00ff0000 & threadID) >> 16);
                        payload[payLoadIndex++] = (byte)((0x0000ff00 & threadID) >> 8);
                        payload[payLoadIndex++] = (byte)(0x000000ff & threadID);
                        offset += 4;
                        optionDataSize = 4;
                        break;

                    case (int)PreLoginOptions.MARS:
                        payload[payLoadIndex++] = (byte)(0); // Turn off MARS
                        offset += 1;
                        optionDataSize += 1;
                        break;

                    case (int)PreLoginOptions.TRACEID:
                        Guid connectionId = new Guid();
                        connectionId.TryWriteBytes(payload.AsSpan(payLoadIndex, TdsConstants.GUID_SIZE)); // 16 is the size of a GUID
                        payLoadIndex += TdsConstants.GUID_SIZE;
                        offset += TdsConstants.GUID_SIZE;
                        optionDataSize = TdsConstants.GUID_SIZE;

                        Guid activityId = new Guid();
                        uint sequence = 123;
                        activityId.TryWriteBytes(payload.AsSpan(payLoadIndex, 16)); // 16 is the size of a GUID
                        payLoadIndex += TdsConstants.GUID_SIZE;
                        payload[payLoadIndex++] = (byte)(0x000000ff & sequence);
                        payload[payLoadIndex++] = (byte)((0x0000ff00 & sequence) >> 8);
                        payload[payLoadIndex++] = (byte)((0x00ff0000 & sequence) >> 16);
                        payload[payLoadIndex++] = (byte)((0xff000000 & sequence) >> 24);
                        int actIdSize = TdsConstants.GUID_SIZE + sizeof(uint);
                        offset += actIdSize;
                        optionDataSize += actIdSize;
                        break;

                    case (int)PreLoginOptions.FEDAUTHREQUIRED:
                        payload[payLoadIndex++] = 0x01;
                        offset += 1;
                        optionDataSize += 1;
                        break;

                    default:
                        Debug.Fail("UNKNOWN option in SendPreLoginHandshake");
                        break;
                }

                // Write data length
                _bufferWriter.WriteByte((byte)((optionDataSize & 0xff00) >> 8));
                _bufferWriter.WriteByte((byte)(optionDataSize & 0x00ff));
            }
            _bufferWriter.WriteByte((byte)255);
            _bufferWriter.WriteByteArray(payload.AsSpan(0, payLoadIndex));
            _bufferWriter.FlushPacket(PacketFlushMode.HARDFLUSH);

        }


        internal void EnableSsl()
        {
            _sslStream.AuthenticateAsClient(this._hostname, null, System.Security.Authentication.SslProtocols.None, false);
            if (_sslOverTdsStream is not null)
            {
                _sslOverTdsStream.FinishHandshake();
            }

            _bufferWriter.UpdateStream(_sslStream);
            _bufferReader.UpdateStream(_sslStream);
        }

        private void DisableSsl()
        {
            _sslStream?.Dispose();
            _sslOverTdsStream?.Dispose();
            _sslStream = null;
            _sslOverTdsStream = null;
            _bufferWriter.UpdateStream(_tcpStream);
            _bufferReader.UpdateStream(_tcpStream);
        }

        internal bool TryConsumePrelogin()
        {
            byte[] payload = new byte[TdsConstants.DEFAULT_LOGIN_PACKET_SIZE];
            if (_bufferReader == null)
            {
                _bufferReader = new BufferReader(_tcpStream);
            }
            TdsPacketHeader header = _bufferReader.ProcessPacketHeader();
            Debug.Assert(header.PacketType == (byte)PacketType.SERVERSTREAM);

            Span<PreLoginOption> options = stackalloc PreLoginOption[7];
            for (int i = 0; i < 8; i++)
            {
                PreLoginOption option;
                option.Option = _bufferReader.ReadByte();
                if (option.Option == (int)PreLoginOptions.LASTOPT)
                {
                    break;
                }
                option.Offset = _bufferReader.ReadByte() << 8 | _bufferReader.ReadByte() - 36;
                option.Length = _bufferReader.ReadByte() << 8 | _bufferReader.ReadByte();
                options[i] = option;
            }

            int optionsDataLength = 0;
            foreach (PreLoginOption option in options)
            {
                optionsDataLength += option.Length;
            }

            Span<byte> preLoginPacket = stackalloc byte[optionsDataLength];
            _bufferReader.ReadByteArray(preLoginPacket);

            for (int i = 0; i < 7; i++)
            {
                PreLoginOption currentOption = options[i];
                switch (currentOption.Option)
                {
                    case (int)PreLoginOptions.VERSION:
                        byte major = preLoginPacket[currentOption.Offset];
                        byte minor = preLoginPacket[currentOption.Offset + 1];
                        ushort build = (ushort)(preLoginPacket[currentOption.Offset + 2] << 8 | preLoginPacket[currentOption.Offset + 3]);
                        ushort revision = (ushort)(preLoginPacket[currentOption.Offset + 4] << 8 | preLoginPacket[currentOption.Offset + 5]);
                        break;
                    case (int)PreLoginOptions.ENCRYPT:
                        byte encrypt = preLoginPacket[currentOption.Offset];
                        if ((SqlEncryptionOptions)encrypt == SqlEncryptionOptions.NOT_SUP)
                        {
                            throw new Exception("SErver does not support encryption, cannot go ahead with connection.");
                        }
                        break;
                    case (int)PreLoginOptions.INSTANCE:
                        // Ignore this 
                        Span<byte> instance = stackalloc byte[currentOption.Length];

                        break;
                    case (int)PreLoginOptions.THREADID:
                        // Ignore 
                        break;
                    case (int)PreLoginOptions.MARS:
                        IsMarsEnabled = preLoginPacket[currentOption.Offset] == 1;
                        break;
                    case (int)PreLoginOptions.TRACEID:
                        // Ignore
                        break;
                    case (int)PreLoginOptions.FEDAUTHREQUIRED:
                        ServerSupportsFedAuth = preLoginPacket[currentOption.Offset] == 1;
                        break;
                    default:
                        Debug.Fail("Unknown option");
                        break;
                }
            }

            return true;
        }
        #endregion

        public void Connect()
        {
            //Console.WriteLine("Connecting to {0}:{1} with user {2} and database {3}", hostname, port, database);
            // Establish TCP connection
            TcpConnect();
            // Send prelogin
            SendPrelogin();

            if (!TryConsumePrelogin())
            {
                throw new Exception("Failed to consume prelogin");
            }

            EnableSsl();
            // Send login
            SendLogin();

            // Process packet for login.
            ProcessPacket();
        }

        public void ProcessPacket()
        {
            this._bufferReader.ResetPacket();
            TdsPacketHeader packetHeader = this._bufferReader.ProcessPacketHeader();
            if (packetHeader.PacketType != (byte)PacketType.SERVERSTREAM)
            {
                throw new Exception("Expected a server stream packet");
            }

            // Read a 1 byte token
            TdsToken token = this._bufferReader.ProcessToken();

            SqlEnvChange envChange = null;
            switch (token.TokenType)
            {
                case TdsTokens.SQLENVCHANGE:
                    byte envType = this._bufferReader.ReadByte();
                    switch (envType)
                    {
                        case TdsEnums.ENV_DATABASE:
                        case TdsEnums.ENV_LANG:
                            envChange = ReadTwoStrings();
                            break;
                        case TdsEnums.ENV_PACKETSIZE:
                            envChange = ReadTwoStrings();
                            // Read 
                            break;
                        case TdsEnums.ENV_COLLATION:
                            _ = this._bufferReader.ReadByte();
                            _ = this._bufferReader.ReadInt32();
                            _ = this._bufferReader.ReadByte();

                            _ = this._bufferReader.ReadByte();
                            _ = this._bufferReader.ReadInt32();
                            _ = this._bufferReader.ReadByte();
                            break;

                    }
                    break;
                case TdsTokens.SQLERROR:
                // TODO : Process error


                case TdsTokens.SQLINFO:
                    simplesqlclient.SqlError error = this._bufferReader.ProcessError(token);
                    if (token.TokenType == TdsTokens.SQLERROR)
                    {
                        throw new Exception("Error received from server " + error.Message);
                    }
                    if (token.TokenType == TdsTokens.SQLINFO)
                    {
                        // TODO: Accumulate the information packet to be dispatched later
                        // to SqlConnection.
                    }
                    break;
                case TdsTokens.SQLLOGINACK:
                    // TODO: Login ack needs to be processed to have some server side information 
                    // readily 
                    // Right now simply read it and ignore it.
                    // First byte skip
                    this._bufferReader.ReadByte();
                    // TdsEnums.Version_size skip
                    this._bufferReader.ReadByteArray(stackalloc byte[4]);
                    // One byte length skip
                    byte lenSkip = this._bufferReader.ReadByte();
                    // skip length * 2 bytes
                    this._bufferReader.ReadByteArray(stackalloc byte[lenSkip * 2]);
                    // skip major version byte
                    this._bufferReader.ReadByte();
                    // skip minor version byte
                    this._bufferReader.ReadByte();
                    // skip build version byte
                    this._bufferReader.ReadByte();
                    // skip sub build version byte
                    this._bufferReader.ReadByte();
                    // Do nothing.
                    break;
                case TdsTokens.SQLDONE:
                    ushort status = this._bufferReader.ReadUInt16();
                    ushort curCmd = this._bufferReader.ReadUInt16();
                    long longCount = this._bufferReader.ReadInt64();
                    int count = (int)longCount;

                    if (TdsEnums.DONE_MORE != (status & TdsEnums.DONE_MORE))
                    {
                    }
                    break;
                case TdsTokens.SQLCOLMETADATA:
                    if (token.Length != TdsEnums.VARNULL) // TODO: What does this mean? 
                    {

                    }
                    throw new NotImplementedException();
                case TdsTokens.SQLROW:
                    throw new NotImplementedException();
                default:
                    throw new NotImplementedException("The token type is not implemented. " + token.TokenType);
            }
        }

        private SqlEnvChange ReadTwoStrings()
        {
            SqlEnvChange env = new SqlEnvChange();
            // Used by ProcessEnvChangeToken
            byte newLength = this._bufferReader.ReadByte();
            string newValue = this._bufferReader.ReadString(newLength);
            byte oldLength = this._bufferReader.ReadByte();
            string oldValue = this._bufferReader.ReadString(oldLength);

            env._newLength = newLength;
            env._newValue = newValue;
            env._oldLength = oldLength;
            env._oldValue = oldValue;

            // env.length includes 1 byte type token
            env._length = 3 + env._newLength * 2 + env._oldLength * 2;
            return env;
        }

        public void SendLogin()
        {
            LoginPacket packet = new LoginPacket();
            packet.ApplicationName = this.connectionSettings.ApplicationName;
            packet.ClientHostName = this.connectionSettings.WorkstationId;
            packet.ServerHostname = this._hostname;
            packet.ClientInterfaceName = TdsEnums.SQL_PROVIDER_NAME;
            packet.Database = this.database;
            packet.PacketSize = this.connectionSettings.PacketSize;
            packet.ProcessIdForTdsLogin = Utilities.GetCurrentProcessIdForTdsLoginOnly();
            packet.UserName = this.authOptions.AuthDetails.UserName;
            packet.ObfuscatedPassword = this.authOptions.AuthDetails.EncryptedPassword;
            packet.Login7Flags = 0;
            packet.IsIntegratedSecurity = false;
            packet.UserInstance = string.Empty;
            packet.NewPassword = new byte[0];
            packet.FeatureExtensionData = new FeatureExtensionsData();
            packet.FeatureExtensionData.fedAuthFeature.AccessToken = null;
            packet.FeatureExtensionData.fedAuthFeature.FedAuthLibrary = default;


            TdsEnums.FeatureExtension requestedFeatures = TdsEnums.FeatureExtension.None;
            requestedFeatures |= TdsEnums.FeatureExtension.GlobalTransactions
                | TdsEnums.FeatureExtension.DataClassification
                | TdsEnums.FeatureExtension.Tce
                | TdsEnums.FeatureExtension.UTF8Support
                | TdsEnums.FeatureExtension.SQLDNSCaching;

            packet.RequestedFeatures = requestedFeatures;
            packet.FeatureExtensionData.requestedFeatures = requestedFeatures;

            this._bufferWriter.PacketType = PacketType.LOGIN;
            int length = packet.Length;
            _bufferWriter.WriteInt(length);
            // Write TDS Version. We support 7.4
            _bufferWriter.WriteInt(packet.ProtocolVersion);
            // Negotiate the packet size.
            _bufferWriter.WriteInt(packet.PacketSize);
            // Client Prog Version
            _bufferWriter.WriteInt(packet.ClientProgramVersion);
            // Current Process Id
            _bufferWriter.WriteInt(packet.ProcessIdForTdsLogin);
            // Unused Connection Id 
            _bufferWriter.WriteInt(0);

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

            // No SSPI usage
            if (this.connectionSettings.UseSSPI)
            {
                log7Flags |= TdsEnums.SSPI_ON << 15;
            }

            // third byte
            if (this.connectionSettings.ReadOnlyIntent)
            {
                log7Flags |= TdsEnums.READONLY_INTENT_ON << 21; // read-only intent flag is a first bit of fSpare1
            }

            // Always say that we are using Feature extensions
            log7Flags |= 1 << 28;

            _bufferWriter.WriteInt(log7Flags);
            // Time Zone
            _bufferWriter.WriteInt(0);

            // LCID
            _bufferWriter.WriteInt(0);

            int offset = TdsEnums.SQL2005_LOG_REC_FIXED_LEN;

            _bufferWriter.WriteShort(offset);

            _bufferWriter.WriteShort(packet.ClientHostName.Length);

            offset += packet.ClientHostName.Length * 2;

            // Support User name and password
            if (authOptions.AuthenticationType == AuthenticationType.SQLAUTH)
            {
                _bufferWriter.WriteShort(offset);
                _bufferWriter.WriteShort(this.authOptions.AuthDetails.UserName.Length);
                offset += this.authOptions.AuthDetails.UserName.Length * 2;

                _bufferWriter.WriteShort(offset);
                _bufferWriter.WriteShort(this.authOptions.AuthDetails.EncryptedPassword.Length / 2);
                offset += this.authOptions.AuthDetails.EncryptedPassword.Length;
            }
            else
            {
                _bufferWriter.WriteShort(0);  // userName offset
                _bufferWriter.WriteShort(0);
                _bufferWriter.WriteShort(0);  // password offset
                _bufferWriter.WriteShort(0);
            }

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(this.connectionSettings.ApplicationName.Length);
            offset += this.connectionSettings.ApplicationName.Length * 2;

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(this._hostname.Length);
            offset += this._hostname.Length * 2;

            _bufferWriter.WriteShort(offset);
            // Feature extension being used 
            _bufferWriter.WriteShort(4);

            offset += 4;

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(packet.ClientInterfaceName.Length);
            offset += packet.ClientInterfaceName.Length * 2;

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(packet.Language.Length);
            offset += packet.Language.Length * 2;

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(packet.Database.Length);
            offset += packet.Database.Length * 2;

            byte[] nicAddress = new byte[TdsEnums.MAX_NIC_SIZE];
            Random random = new Random();
            random.NextBytes(nicAddress);
            _bufferWriter.WriteByteArray(nicAddress);

            _bufferWriter.WriteShort(offset);

            // No Integrated Auth
            _bufferWriter.WriteShort(0);

            // Attach DB Filename
            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(string.Empty.Length);
            offset += string.Empty.Length * 2;

            _bufferWriter.WriteShort(offset);
            _bufferWriter.WriteShort(packet.NewPassword.Length / 2);

            // reserved for chSSPI
            _bufferWriter.WriteInt(0);

            _bufferWriter.WriteString(packet.ClientHostName);

            // Consider User Name auth only
            _bufferWriter.WriteString(packet.UserName);
            _bufferWriter.WriteByteArray(packet.ObfuscatedPassword);

            _bufferWriter.WriteString(packet.ApplicationName);
            _bufferWriter.WriteString(packet.ServerHostname);

            _bufferWriter.WriteInt(packet.Length - packet.FeatureExtensionData.Length);
            _bufferWriter.WriteString(packet.ClientInterfaceName);
            _bufferWriter.WriteString(packet.Language);
            _bufferWriter.WriteString(packet.Database);
            // Attach DB File Name
            _bufferWriter.WriteString(string.Empty);

            _bufferWriter.WriteByteArray(packet.NewPassword);
            // Apply feature extension data


            FeatureExtensionsData featureExtensionData = packet.FeatureExtensionData;

            Span<byte> tceData = stackalloc byte[5];
            featureExtensionData.colEncryptionData.FillData(tceData);
            _bufferWriter.WriteByte((byte)featureExtensionData.colEncryptionData.FeatureExtensionFlag);
            _bufferWriter.WriteByteArray(tceData);


            Span<byte> globalTransaction = stackalloc byte[4];
            featureExtensionData.globalTransactionsFeature.FillData(globalTransaction);
            _bufferWriter.WriteByte((byte)featureExtensionData.globalTransactionsFeature.FeatureExtensionFlag);
            _bufferWriter.WriteByteArray(globalTransaction);

            Span<byte> dataClassificationFeatureData = stackalloc byte[5];
            packet.FeatureExtensionData.dataClassificationFeature.FillData(dataClassificationFeatureData);
            _bufferWriter.WriteByte((byte)packet.FeatureExtensionData.dataClassificationFeature.FeatureExtensionFlag);
            _bufferWriter.WriteByteArray(dataClassificationFeatureData);

            Span<byte> utf8SupportData = stackalloc byte[4];
            packet.FeatureExtensionData.uTF8SupportFeature.FillData(utf8SupportData);
            _bufferWriter.WriteByte((byte)packet.FeatureExtensionData.uTF8SupportFeature.FeatureExtensionFlag);
            _bufferWriter.WriteByteArray(utf8SupportData);

            Span<byte> dnsCaching = stackalloc byte[4];
            packet.FeatureExtensionData.sQLDNSCaching.FillData(dnsCaching);
            _bufferWriter.WriteByte((byte)packet.FeatureExtensionData.sQLDNSCaching.FeatureExtensionFlag);
            _bufferWriter.WriteByteArray(dnsCaching);

            _bufferWriter.WriteByte(0xFF);
            _bufferWriter.FlushPacket(PacketFlushMode.HARDFLUSH);

            DisableSsl();
        }

        public void SendQuery(string query)
        {
            this._bufferReader.ResetPacket();
            int marsHeaderSize = 18;
            int notificationHeaderSize = 0; // TODO: Needed for sql notifications feature. Not implemetned yet
            int totalHeaderLength = 4 + marsHeaderSize + notificationHeaderSize;
            this._bufferWriter.WriteInt(totalHeaderLength);

            this._bufferWriter.WriteInt(marsHeaderSize);

            // Write the MARS header data. 
            this._bufferWriter.WriteShort(TdsEnums.HEADERTYPE_MARS);
            int transactionId = 0; // TODO: Needed for txn support
            this._bufferWriter.WriteLong(transactionId);

            int resultCount = 0;
            // TODO Increment and add the open results count per connection.
            this._bufferWriter.WriteInt(++resultCount);
            _bufferWriter.PacketType = PacketType.MT_SQL;

            // TODO: Add the enclave support. The server doesnt support Enclaves yet.

            _bufferWriter.WriteString(query);
            this._bufferWriter.FlushPacket(PacketFlushMode.HARDFLUSH);
        }

        internal void ProcessQueryResults()
        {
            ProcessPacket();
        }
    }
}
