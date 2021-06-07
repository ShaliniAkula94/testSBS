﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public class KerberosTests
    {
        [PlatformSpecific(TestPlatforms.AnyUnix)]
        [Theory]
        //[ConditionalTheory(typeof(DataTestUtility), nameof(DataTestUtility.IsKerberosTest))]
        [ClassData(typeof(ConnectionStringsProvider))]
        public void FailsToConnectWithNoTicketIssued(string cnn)
        {
            using var conn = new SqlConnection(cnn);
            Assert.Throws<SqlException>(() => conn.Open());
        }

        [PlatformSpecific(TestPlatforms.AnyUnix)]
        //[ConditionalTheory(typeof(DataTestUtility), nameof(DataTestUtility.IsKerberosTest))]
        [Theory]
        [ClassData(typeof(ConnectionStringsProvider))]
        [ClassData(typeof(DomainProvider))]
        public void IsKerBerosSetupTest(string connection, string domain)
        {
            Task t = Task.Run(() => KerberosTicketManagemnt.Init(domain)).ContinueWith((i) =>
            {
                using var conn = new SqlConnection(connection);
                try
                {
                    conn.Open();
                    using var command = new SqlCommand("SELECT auth_scheme from sys.dm_exec_connections where session_id = @@spid", conn);
                    using SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Assert.Equal("KERBEROS", reader.GetString(0));
                    }
                }
                catch (SqlException ex)
                {
                    Console.WriteLine(ex.Message);
                    Assert.False(true);
                }
            });
        }

        [PlatformSpecific(TestPlatforms.AnyUnix)]
        [Theory]
        //[ConditionalTheory(typeof(DataTestUtility), nameof(DataTestUtility.IsKerberosTest))]
        [ClassData(typeof(ConnectionStringsProvider))]
        public void ExpiredTicketTest(string connection)
        {
            Task t = Task.Run(() => KerberosTicketManagemnt.Destroy()).ContinueWith((i) =>
            {
                using var conn = new SqlConnection(connection);
                Assert.Throws<SqlException>(() => conn.Open());
            });
        }

        public class ConnectionStringsProvider : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                foreach (var cnnString in DataTestUtility.ConnectionStrings)
                {
                    yield return new object[] { cnnString, false };
                    yield return new object[] { cnnString, true };
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public class DomainProvider : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                foreach (var provider in DataTestUtility.DomainProviderNames)
                {
                    yield return new object[] { provider };
                }
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

