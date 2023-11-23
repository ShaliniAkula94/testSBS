// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public static class InstanceNameTest
    {
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsNotAzureServer), nameof(DataTestUtility.IsNotAzureSynapse), nameof(DataTestUtility.AreConnStringsSetup))]
        public static void ConnectToSQLWithInstanceNameTest()
        {
            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);

            bool proceed = true;
            string dataSourceStr = builder.DataSource.Replace("tcp:", "");
            string[] serverNamePartsByBackSlash = dataSourceStr.Split('\\');
            string hostname = serverNamePartsByBackSlash[0];
            if (!dataSourceStr.Contains(",") && serverNamePartsByBackSlash.Length == 2)
            {
                proceed = !string.IsNullOrWhiteSpace(hostname) && IsBrowserAlive(hostname);
            }

            if (proceed)
            {
                using SqlConnection connection = new(builder.ConnectionString);
                connection.Open();
                connection.Close();

                // We can only connect via IP address if we aren't doing remote Kerberos or strict TLS
                if (builder.Encrypt != SqlConnectionEncryptOption.Strict &&
                        (!builder.IntegratedSecurity || hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                         hostname.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Exercise the IP address-specific code in SSRP
                    IPAddress[] addresses = Dns.GetHostAddresses(hostname);
                    builder.DataSource = builder.DataSource.Replace(hostname, addresses[0].ToString());
                    builder.TrustServerCertificate = true;
                    using SqlConnection connection2 = new(builder.ConnectionString);
                    connection2.Open();
                    connection2.Close();
                }
            }
        }

        [ConditionalTheory(typeof(DataTestUtility), nameof(DataTestUtility.IsNotAzureServer), nameof(DataTestUtility.IsNotAzureSynapse), nameof(DataTestUtility.AreConnStringsSetup))]
        [InlineData(true, SqlConnectionIPAddressPreference.IPv4First)]
        [InlineData(true, SqlConnectionIPAddressPreference.IPv6First)]
        [InlineData(true, SqlConnectionIPAddressPreference.UsePlatformDefault)]
        [InlineData(false, SqlConnectionIPAddressPreference.IPv4First)]
        [InlineData(false, SqlConnectionIPAddressPreference.IPv6First)]
        [InlineData(false, SqlConnectionIPAddressPreference.UsePlatformDefault)]
        public static void ConnectManagedWithInstanceNameTest(bool useMultiSubnetFailover, SqlConnectionIPAddressPreference ipPreference)
        {
            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);
            builder.MultiSubnetFailover = useMultiSubnetFailover;
            builder.IPAddressPreference = ipPreference;

            Assert.True(DataTestUtility.ParseDataSource(builder.DataSource, out string hostname, out _, out string instanceName));

            if (IsBrowserAlive(hostname) && IsValidInstance(hostname, instanceName))
            {
                builder.DataSource = hostname + "\\" + instanceName;
                using SqlConnection connection = new(builder.ConnectionString);
                connection.Open();
            }

            builder.ConnectTimeout = 2;
            instanceName = "invalidinstance3456";
            if (!IsValidInstance(hostname, instanceName))
            {
                builder.DataSource = hostname + "\\" + instanceName;

                using SqlConnection connection = new(builder.ConnectionString);
                SqlException ex = Assert.Throws<SqlException>(() => connection.Open());
                Assert.Contains("Error Locating Server/Instance Specified", ex.Message);
            }
        }

        // Note: This Unit test was tested in a VM within the sqldrv.ad domain. i.e. from server sqldrv-win22 and
        //       is connecting to a Sql Server using Kerberos at sqldrv-sql22 server in the same domain.
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsManagedSNI), nameof(DataTestUtility.IsNotLocalhost), nameof(DataTestUtility.IsKerberosTest), nameof(DataTestUtility.IsNotAzureServer), nameof(DataTestUtility.IsNotAzureSynapse))]
        public static void PortNumberInSPNTest()
        {
            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);

            Assert.True(DataTestUtility.ParseDataSource(builder.DataSource, out string hostname, out _, out string instanceName));

            if (IsBrowserAlive(hostname) && IsValidInstance(hostname, instanceName))
            {
                using SqlConnection connection = new(builder.ConnectionString);
                connection.Open();
                using SqlCommand command = new("SELECT auth_scheme, local_tcp_port from sys.dm_exec_connections where session_id = @@spid", connection);
                using SqlDataReader reader = command.ExecuteReader();
                Assert.True(reader.Read(), "Expected to receive one row data");
                Assert.Equal("KERBEROS", reader.GetString(0));
                int Port = reader.GetInt32(1);

                int port = -1;
                string spnInfo = GetSPNInfo(builder.DataSource, out port);

                // sample output to validate = MSSQLSvc/sqldrv-sql22.sqldrv.ad:1433"
                Assert.Contains($"MSSQLSvc/{hostname}", spnInfo);
                // the local_tcp_port Port is the same as the inferred port from instance name
                Assert.Equal(Port, port);
            }
        }

        private static string GetSPNInfo(string datasource, out int out_port)
        {
            Assembly systemData = Assembly.GetAssembly(typeof(SqlConnection));

            // Get all required types using reflection
            Type SniProxy = systemData.GetType("Microsoft.Data.SqlClient.SNI.SNIProxy");
            Type SSRP = systemData.GetType("Microsoft.Data.SqlClient.SNI.SSRP");
            Type DataSource = systemData.GetType("Microsoft.Data.SqlClient.SNI.DataSource");
            Type TimeoutTimer = systemData.GetType("Microsoft.Data.ProviderBase.TimeoutTimer");

            // Used in Datasource constructor param type array 
            Type[] types = new Type[1];
            types[0] = typeof(string);

            // Used in GetSqlServerSPNs function param types array
            Type[] types2 = new Type[2];
            types2[0] = DataSource;
            types2[1] = typeof(string);

            // GetPortByInstanceName parameters array
            Type[] types3 = new Type[5];
            types3[0] = typeof(string);
            types3[1] = typeof(string);
            types3[2] = TimeoutTimer;
            types3[3] = typeof(bool);
            types3[4] = typeof(Microsoft.Data.SqlClient.SqlConnectionIPAddressPreference);

            // TimeoutTimer.StartSecondsTimeout params
            Type[] types4 = new Type[1];
            types4[0] = typeof(int);

            // Get all types constructors
            ConstructorInfo sniProxyCtor = SniProxy.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, Type.EmptyTypes, null);
            ConstructorInfo SSRPCtor = SSRP.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, Type.EmptyTypes, null);
            ConstructorInfo datasSourceCtor = DataSource.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, types , null);
            ConstructorInfo timeoutTimerCtor = TimeoutTimer.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, Type.EmptyTypes, null);

            // Instantiate SNIProxy
            var sniProxy =  sniProxyCtor.Invoke(new object[] { });

            // Instatntiate datasource 
            var details = datasSourceCtor.Invoke(new object[] { datasource });

            // Instantiate SSRP
            var ssrp = SSRPCtor.Invoke(new object[] { });    

            // Instantiate TimeoutTimer
            var timeoutTimer = timeoutTimerCtor.Invoke(new object[] { });

            // Get TimeoutTimer.StartSecondsTimeout Method
            MethodInfo startSecondsTimeout = timeoutTimer.GetType().GetMethod("StartSecondsTimeout", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, types4, null);
            // Create a timeoutTimer that expires in 30 seconds
            timeoutTimer = startSecondsTimeout.Invoke(details, new object[] { 30 });

            // Parse the datasource to separate the server name and instance name
            MethodInfo ParseServerName = details.GetType().GetMethod("ParseServerName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, types, null);
            var dataSrcInfo = ParseServerName.Invoke(details, new object[] { datasource });

            // Get the GetPortByInstanceName method of SSRP
            MethodInfo getPortByInstanceName = ssrp.GetType().GetMethod("GetPortByInstanceName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, types3, null);

            // Get the server name
            PropertyInfo serverInfo = dataSrcInfo.GetType().GetProperty("ServerName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var serverName = serverInfo.GetValue(dataSrcInfo, null).ToString();

            // Get the instance name
            PropertyInfo instanceNameInfo = dataSrcInfo.GetType().GetProperty("InstanceName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var instanceName = instanceNameInfo.GetValue(dataSrcInfo, null).ToString();

            // Get the port number using the GetPortByInstanceName method of SSRP
            var port = getPortByInstanceName.Invoke(ssrp, parameters: new object[] { serverName, instanceName, timeoutTimer, false, 0 } );

            // Set the resolved port property of datasource
            PropertyInfo resolvedPortInfo = dataSrcInfo.GetType().GetProperty("ResolvedPort", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            resolvedPortInfo.SetValue(dataSrcInfo, (int)port, null);

            // Prepare the GetSqlServerSPNs method
            string serverSPN = "";
            MethodInfo getSqlServerSPNs = sniProxy.GetType().GetMethod("GetSqlServerSPNs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, types2, null);

            // Finally call GetSqlServerSPNs
            dynamic result = getSqlServerSPNs.Invoke(sniProxy, new object[] { dataSrcInfo, serverSPN });

            // MSSQLSvc/sqldrv-sql22.sqldrv.ad:1433"
            var spnInfo = System.Text.Encoding.Unicode.GetString(result[0]);

            out_port = (int)port;

            return spnInfo;
        }

        private static bool IsBrowserAlive(string browserHostname)
        {
            const byte ClntUcastEx = 0x03;

            byte[] responsePacket = QueryBrowser(browserHostname, new byte[] { ClntUcastEx });
            return responsePacket != null && responsePacket.Length > 0;
        }

        private static bool IsValidInstance(string browserHostName, string instanceName)
        {
            byte[] request = CreateInstanceInfoRequest(instanceName);
            byte[] response = QueryBrowser(browserHostName, request);
            return response != null && response.Length > 0;
        }

        private static byte[] QueryBrowser(string browserHostname, byte[] requestPacket)
        {
            const int DefaultBrowserPort = 1434;
            const int sendTimeout = 1000;
            const int receiveTimeout = 1000;
            byte[] responsePacket = null;
            using (UdpClient client = new(AddressFamily.InterNetwork))
            {
                try
                {
                    Task<int> sendTask = client.SendAsync(requestPacket, requestPacket.Length, browserHostname, DefaultBrowserPort);
                    Task<UdpReceiveResult> receiveTask = null;
                    if (sendTask.Wait(sendTimeout) && (receiveTask = client.ReceiveAsync()).Wait(receiveTimeout))
                    {
                        responsePacket = receiveTask.Result.Buffer;
                    }
                }
                catch { }
            }

            return responsePacket;
        }

        private static byte[] CreateInstanceInfoRequest(string instanceName)
        {
            const byte ClntUcastInst = 0x04;
            instanceName += char.MinValue;
            int byteCount = Encoding.ASCII.GetByteCount(instanceName);

            byte[] requestPacket = new byte[byteCount + 1];
            requestPacket[0] = ClntUcastInst;
            Encoding.ASCII.GetBytes(instanceName, 0, instanceName.Length, requestPacket, 1);

            return requestPacket;
        }
    }
}
