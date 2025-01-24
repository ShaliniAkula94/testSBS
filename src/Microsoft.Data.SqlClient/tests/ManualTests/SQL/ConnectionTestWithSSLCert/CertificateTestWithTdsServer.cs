// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Data.SqlClient.ManualTesting.Tests.DataCommon;
using Microsoft.Win32;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public sealed class CertificateTestWithTdsServer : IDisposable
    {
        private static readonly string s_fullPathToPowershellScript = Path.Combine(Directory.GetCurrentDirectory(), "makepfxcert.ps1");
        private static readonly string s_fullPathToCleanupPowershellScript = Path.Combine(Directory.GetCurrentDirectory(), "removecert.ps1");
        private static readonly string s_fullPathToPfx = Path.Combine(Directory.GetCurrentDirectory(), "localhostcert.pfx");
        private static readonly string s_fullPathTothumbprint = Path.Combine(Directory.GetCurrentDirectory(), "thumbprint.txt");
        private static readonly string s_fullPathToClientCert = Path.Combine(Directory.GetCurrentDirectory(), "clientcert");
        private const string LocalHost = "localhost";

        public CertificateTestWithTdsServer()
        {
            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);
            Assert.True(DataTestUtility.ParseDataSource(builder.DataSource, out string hostname, out _, out string instanceName));

            if (!Directory.Exists(s_fullPathToClientCert))
            {
                Directory.CreateDirectory(s_fullPathToClientCert);
            }

            RunPowershellScript(s_fullPathToPowershellScript);
        }

        private static bool IsLocalHost()
        {
            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);
            Assert.True(DataTestUtility.ParseDataSource(builder.DataSource, out string hostname, out _, out _));
            return LocalHost.Equals(hostname, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreConnStringsSetup() => DataTestUtility.AreConnStringsSetup();
        private static bool IsNotAzureServer() => DataTestUtility.IsNotAzureServer();
        private static bool UseManagedSNIOnWindows() => DataTestUtility.UseManagedSNIOnWindows;

        [ConditionalTheory(
            nameof(AreConnStringsSetup),
            nameof(IsNotAzureServer),
            nameof(IsLocalHost))]
        [MemberData(
            nameof(ConnectionTestParametersData.GetConnectionTestParameters),
            MemberType = typeof(ConnectionTestParametersData))]
        public void ConnectionTest(ConnectionTestParameters connectionTestParameters)
        {
            // Some of the certificate operations require elevated privileges on
            // Windows.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                if (! principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    Assert.Fail(
                        "This test requires Administrator role; current user " +
                        $"{identity.User} lacks this role");
                }
            }

            SqlConnectionStringBuilder builder = new(DataTestUtility.TCPConnectionString);

            // The TestTdsServer does not validate the user name and password, so we can use any value if they are not defined.
            string userId = string.IsNullOrWhiteSpace(builder.UserID) ? "user" : builder.UserID;
            string password = string.IsNullOrWhiteSpace(builder.Password) ? "password" : builder.Password;

            using TestTdsServer server =
                TestTdsServer.StartTestServer(
                    encryptionCertificate:
#if NET
                        X509CertificateLoader.LoadPkcs12FromFile(
                            s_fullPathToPfx,
                            "nopassword",
                            X509KeyStorageFlags.UserKeySet),
#else
                        new X509Certificate2(
                            s_fullPathToPfx,
                            "nopassword",
                            X509KeyStorageFlags.UserKeySet),
#endif
                    encryptionProtocols: connectionTestParameters.EncryptionProtocols,
                    encryptionType: connectionTestParameters.TdsEncryptionType);

            builder = new(server.ConnectionString)
            {
                UserID = userId,
                Password = password,
                TrustServerCertificate = connectionTestParameters.TrustServerCertificate,
                Encrypt = connectionTestParameters.Encrypt
            };

            if (!string.IsNullOrEmpty(connectionTestParameters.Certificate))
            {
                builder.ServerCertificate = connectionTestParameters.Certificate;
            }

            if (!string.IsNullOrEmpty(connectionTestParameters.HostNameInCertificate))
            {
                builder.HostNameInCertificate = connectionTestParameters.HostNameInCertificate;
            }

            using SqlConnection connection = new(builder.ConnectionString);
            try
            {
                connection.Open();
            }
            catch (SqlException ex)
            {
                // When Open() throws, we expect a single error code of 20,
                // which means encryption negotiation failed.
                //
                // However, on Windows we seem to get code 10054
                // (SNI_WSAECONNRESET) instead.
                //
                Assert.Single(ex.Errors);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.Equal(10054, ex.Errors[0].Number);
                }
                else
                {
                    Assert.Equal(20, ex.Errors[0].Number);
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to open connection: {ex}");
            }

            // Are we expecting the connection to be open?
            if (connectionTestParameters.TestResult)
            {
                // Yes, so verify that it's open.
                Assert.Equal(ConnectionState.Open, connection.State);
            }
            else
            {
                // No, so verify that it isn't open.
                Assert.NotEqual(ConnectionState.Open, connection.State);
            }
        }

        private static void RunPowershellScript(string script)
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            string powerShellCommand = "powershell.exe";
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                powerShellCommand = "pwsh";
            }

            if (File.Exists(script))
            {
                StringBuilder output = new();
                Process proc = new()
                {
                    StartInfo =
                    {
                        FileName = powerShellCommand,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        Arguments = $"{script} -OutDir {currentDirectory} > result.txt",
                        CreateNoWindow = false,
                        Verb = "runas"
                    }
                };

                proc.EnableRaisingEvents = true;

                proc.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                    }
                });

                proc.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                    }
                });

                proc.Start();

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(60000))
                {
                    proc.Kill();
                    proc.WaitForExit(2000);
                    throw new Exception($"Could not generate certificate. Error output: {output}");
                }
            }
            else
            {
                throw new Exception($"Could not find makepfxcert.ps1");
            }
        }

        private void RemoveCertificate()
        {
            string thumbprint = File.ReadAllText(s_fullPathTothumbprint);
            using X509Store certStore = new(StoreName.Root, StoreLocation.LocalMachine);
            certStore.Open(OpenFlags.ReadWrite);
            X509Certificate2Collection certCollection = certStore.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (certCollection.Count > 0)
            {
                certStore.Remove(certCollection[0]);
            }
            certStore.Close();

            File.Delete(s_fullPathTothumbprint);
            Directory.Delete(s_fullPathToClientCert, true);
        }

        public void Dispose()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!string.IsNullOrEmpty(s_fullPathTothumbprint))
                {
                    RemoveCertificate();
                }
            }
            else
            {
                RunPowershellScript(s_fullPathToCleanupPowershellScript);
            }
        }
    }
}
