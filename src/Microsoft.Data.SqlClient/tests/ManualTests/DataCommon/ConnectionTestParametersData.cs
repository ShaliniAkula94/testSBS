// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using Microsoft.SqlServer.TDS.PreLogin;

// For brevity.
using ServerEnc = Microsoft.SqlServer.TDS.PreLogin.TDSPreLoginTokenEncryptionType;
using ClientEnc = Microsoft.Data.SqlClient.SqlConnectionEncryptOption;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests.DataCommon
{
    public class ConnectionTestParametersData
    {
        private string _empty = string.Empty;
        // It was advised to store the client certificate in its own folder.
        private static readonly string s_fullPathToCer = Path.Combine(Directory.GetCurrentDirectory(), "clientcert", "localhostcert.cer");
        private static readonly string s_mismatchedcert = Path.Combine(Directory.GetCurrentDirectory(), "clientcert", "mismatchedcert.cer");

        private static readonly string s_hostName = System.Net.Dns.GetHostName();
        public static ConnectionTestParametersData Data { get; } = new ConnectionTestParametersData();
        public List<ConnectionTestParameters> ConnectionTestParametersList { get; set; }

        public static IEnumerable<object[]> GetConnectionTestParameters()
        {
            foreach (var item in Data.ConnectionTestParametersList)
            {
                yield return new object[] { item };
            }
        }

        public ConnectionTestParametersData()
        {
            // Test cases possible field values for connection parameters.
            ConnectionTestParametersList = new List<ConnectionTestParameters>
            {
                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                // Not Sup.   | Optional (Off) |  true             | valid        | match    | yes
                // Not Sup.   | Optional (Off) |  true             | valid        | no match | yes
                // Not Sup.   | Optional (Off) |  true             | self-signed  | match    | yes
                // Not Sup.   | Optional (Off) |  true             | self-signed  | no match | yes
                // Not Sup.   | Optional (Off) |  false            | valid        | match    | yes
                // Not Sup.   | Optional (Off) |  false            | valid        | no match | yes
                // Not Sup.   | Optional (Off) |  false            | self-signed  | match    | yes
                // Not Sup.   | Optional (Off) |  false            | self-signed  | no match | yes
                //
                new(ServerEnc.NotSupported, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, false, s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.NotSupported, ClientEnc.Optional, false, s_mismatchedcert, s_hostName + "foo", true),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                // Not Sup.   | Mandatory (On) |  true             | valid        | match    | no
                // Not Sup.   | Mandatory (On) |  true             | valid        | no match | no
                // Not Sup.   | Mandatory (On) |  true             | self-signed  | match    | no
                // Not Sup.   | Mandatory (On) |  true             | self-signed  | no match | no
                // Not Sup.   | Mandatory (On) |  false            | valid        | match    | no
                // Not Sup.   | Mandatory (On) |  false            | valid        | no match | no
                // Not Sup.   | Mandatory (On) |  false            | self-signed  | match    | no
                // Not Sup.   | Mandatory (On) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName,         false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName + "foo", false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName,         false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.NotSupported, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName + "foo", false),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                // Off        | Optional (Off) |  true             | valid        | match    | yes
                // Off        | Optional (Off) |  true             | valid        | no match | yes
                // Off        | Optional (Off) |  true             | self-signed  | match    | yes
                // Off        | Optional (Off) |  true             | self-signed  | no match | yes
                // Off        | Optional (Off) |  false            | valid        | match    | yes
                // Off        | Optional (Off) |  false            | valid        | no match | yes
                // Off        | Optional (Off) |  false            | self-signed  | match    | yes
                // Off        | Optional (Off) |  false            | self-signed  | no match | yes
                //
                new(ServerEnc.Off, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.Off, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.Off, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.Off, ClientEnc.Optional, false, s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Optional, false, s_mismatchedcert, s_hostName + "foo", true),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                //  Off       | Mandatory (On) |  true             | valid        | match    | yes
                //  Off       | Mandatory (On) |  true             | valid        | no match | yes
                //  Off       | Mandatory (On) |  true             | self-signed  | match    | yes
                //  Off       | Mandatory (On) |  true             | self-signed  | no match | yes
                //  Off       | Mandatory (On) |  false            | valid        | match    | yes
                //  Off       | Mandatory (On) |  false            | valid        | no match | no
                //  Off       | Mandatory (On) |  false            | self-signed  | match    | no
                //  Off       | Mandatory (On) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.Off, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.Off, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.Off, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Off, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.Off, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.Off, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName + "foo", false),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                //  On        | Optional (Off) |  true             | valid        | match    | yes
                //  On        | Optional (Off) |  true             | valid        | no match | yes
                //  On        | Optional (Off) |  true             | self-signed  | match    | yes
                //  On        | Optional (Off) |  true             | self-signed  | no match | yes
                //  On        | Optional (Off) |  false            | valid        | match    | yes
                //  On        | Optional (Off) |  false            | valid        | no match | no
                //  On        | Optional (Off) |  false            | self-signed  | match    | no
                //  On        | Optional (Off) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.On, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.On, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.On, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.On, ClientEnc.Optional, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.On, ClientEnc.Optional, false, s_mismatchedcert, s_hostName + "foo", false),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                //  On        | Mandatory (On) |  true             | valid        | match    | yes
                //  On        | Mandatory (On) |  true             | valid        | no match | yes
                //  On        | Mandatory (On) |  true             | self-signed  | match    | yes
                //  On        | Mandatory (On) |  true             | self-signed  | no match | yes
                //  On        | Mandatory (On) |  false            | valid        | match    | yes
                //  On        | Mandatory (On) |  false            | valid        | no match | no
                //  On        | Mandatory (On) |  false            | self-signed  | match    | no
                //  On        | Mandatory (On) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.On, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.On, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.On, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.On, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.On, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.On, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName + "foo", false),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                //  Required  | Optional (Off) |  true             | valid        | match    | yes
                //  Required  | Optional (Off) |  true             | valid        | no match | yes
                //  Required  | Optional (Off) |  true             | self-signed  | match    | yes
                //  Required  | Optional (Off) |  true             | self-signed  | no match | yes
                //  Required  | Optional (Off) |  false            | valid        | match    | yes
                //  Required  | Optional (Off) |  false            | valid        | no match | no
                //  Required  | Optional (Off) |  false            | self-signed  | match    | no
                //  Required  | Optional (Off) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.Required, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Optional, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.Required, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Optional, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.Required, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Optional, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.Required, ClientEnc.Optional, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.Required, ClientEnc.Optional, false, s_mismatchedcert, s_hostName + "foo", false),

                // Server Enc | Client Enc     | Trust Server Cert | Certificate  | HNIC     | Expect Connect?
                // -------------------------------------------------------------------------------------------
                //  Required  | Mandatory (On) |  true             | valid        | match    | yes
                //  Required  | Mandatory (On) |  true             | valid        | no match | yes
                //  Required  | Mandatory (On) |  true             | self-signed  | match    | yes
                //  Required  | Mandatory (On) |  true             | self-signed  | no match | yes
                //  Required  | Mandatory (On) |  false            | valid        | match    | yes
                //  Required  | Mandatory (On) |  false            | valid        | no match | no
                //  Required  | Mandatory (On) |  false            | self-signed  | match    | no
                //  Required  | Mandatory (On) |  false            | self-signed  | no match | no
                //
                new(ServerEnc.Required, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Mandatory, true,  s_fullPathToCer,  s_hostName + "foo", true),
                new(ServerEnc.Required, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Mandatory, true,  s_mismatchedcert, s_hostName + "foo", true),
                new(ServerEnc.Required, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName,         true),
                new(ServerEnc.Required, ClientEnc.Mandatory, false, s_fullPathToCer,  s_hostName + "foo", false),
                new(ServerEnc.Required, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName,         false),
                new(ServerEnc.Required, ClientEnc.Mandatory, false, s_mismatchedcert, s_hostName + "foo", false),

                // TDSPreLoginTokenEncryptionType.Off
                // new(ServerEnc.Off, ClientEnc.Optional, false, _empty, _empty, true),
                // new(ServerEnc.Off, ClientEnc.Mandatory, false, _empty, _empty, false),
                // new(ServerEnc.Off, ClientEnc.Optional, true, _empty, _empty, true),
                // new(ServerEnc.Off, ClientEnc.Mandatory, true, _empty, _empty, true),
                // new(ServerEnc.Off, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, true),
                // new(ServerEnc.Off, ClientEnc.Mandatory, true, s_fullPathToCer, _empty, true),
                // new(ServerEnc.Off, ClientEnc.Mandatory, false, _empty, s_hostName, false),
                // new(ServerEnc.Off, ClientEnc.Mandatory, true, _empty, s_hostName, true),

                // // TDSPreLoginTokenEncryptionType.On
                // new(ServerEnc.On, ClientEnc.Optional, false, _empty, _empty, false),
                // new(ServerEnc.On, ClientEnc.Mandatory, false, _empty, _empty, false),
                // new(ServerEnc.On, ClientEnc.Optional, true, _empty, _empty, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, true, _empty, _empty, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, true, s_fullPathToCer, _empty, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, false, _empty, s_hostName, false),
                // new(ServerEnc.On, ClientEnc.Mandatory, true, _empty, s_hostName, true),

                // // TDSPreLoginTokenEncryptionType.Required
                // new(ServerEnc.Required, ClientEnc.Optional, false, _empty, _empty, false),
                // new(ServerEnc.Required, ClientEnc.Mandatory, false, _empty, _empty, false),
                // new(ServerEnc.Required, ClientEnc.Optional, true, _empty, _empty, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, true, _empty, _empty, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, true, s_fullPathToCer, _empty, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, false, _empty, s_hostName, false),
                // new(ServerEnc.Required, ClientEnc.Mandatory, true, _empty, s_hostName, true),

                // // Mismatched certificate test
                // new(ServerEnc.Off, ClientEnc.Mandatory, false, s_mismatchedcert, _empty, false),
                // new(ServerEnc.Off, ClientEnc.Mandatory, true, s_mismatchedcert, _empty, false),
                // new(ServerEnc.Off, ClientEnc.Mandatory, true, s_mismatchedcert, _empty, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, false, s_mismatchedcert, _empty, false),
                // new(ServerEnc.On, ClientEnc.Mandatory, true, s_mismatchedcert, _empty, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, false, s_mismatchedcert, _empty, false),
                // new(ServerEnc.Required, ClientEnc.Mandatory, true, s_mismatchedcert, _empty, true),

                // Multiple SSL protocols test
#pragma warning disable CA5397 // Do not use deprecated SslProtocols values
#pragma warning disable CA5398 // Avoid hardcoded SslProtocols values
#if NET
#pragma warning disable SYSLIB0039 // Type or member is obsolete: TLS 1.0 & 1.1 are deprecated
#endif
                // new(ServerEnc.Off, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, SslProtocols.Tls | SslProtocols.Tls11, true),
                // new(ServerEnc.On, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, SslProtocols.Tls | SslProtocols.Tls11, true),
                // new(ServerEnc.Required, ClientEnc.Mandatory, false, s_fullPathToCer, _empty, SslProtocols.Tls | SslProtocols.Tls11, true),
#if NET
#pragma warning restore SYSLIB0039 // Type or member is obsolete: TLS 1.0 & 1.1 are deprecated
#endif
#pragma warning restore CA5397 // Do not use deprecated SslProtocols values
#pragma warning restore CA5398 // Avoid hardcoded SslProtocols values
            };
        }
    }
}
