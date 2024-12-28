﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.using System;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Keys;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using Microsoft.Data.SqlClient.ManualTesting.Tests.AlwaysEncrypted.Setup;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests.AlwaysEncrypted
{
    public class SQLSetupStrategyAzureKeyVault : SQLSetupStrategy
    {
        internal static bool IsAKVProviderRegistered = false;

        private readonly List<string> _akvKeyNames;
        private readonly KeyClient _keyClient;

        public Table AKVTestTable { get; private set; }
        public SqlColumnEncryptionAzureKeyVaultProvider AkvStoreProvider;
        public DummyMasterKeyForAKVProvider DummyMasterKey;
        public string AkvKeyUrl { get; private set; }

        public SQLSetupStrategyAzureKeyVault() : base()
        {
            _akvKeyNames = new List<string>();
            _keyClient = new KeyClient(DataTestUtility.AKVBaseUri, DataTestUtility.GetTokenCredential());
            AkvStoreProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());

            if (!IsAKVProviderRegistered)
            {
                RegisterGlobalProviders(AkvStoreProvider);
            }
            SetupAzureKeyVault();
            SetupDatabase();
        }

        public static void RegisterGlobalProviders(SqlColumnEncryptionAzureKeyVaultProvider akvProvider)
        {
            DummyKeyStoreProvider dummyProvider = new DummyKeyStoreProvider();

            Dictionary<string, SqlColumnEncryptionKeyStoreProvider> customKeyStoreProviders =
                new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>(capacity: 2, comparer: StringComparer.OrdinalIgnoreCase)
            {
                    {SqlColumnEncryptionAzureKeyVaultProvider.ProviderName, akvProvider},
                    { DummyKeyStoreProvider.Name, dummyProvider}
            };

            SqlConnection.RegisterColumnEncryptionKeyStoreProviders(customProviders: customKeyStoreProviders);
            IsAKVProviderRegistered = true;
        }

        private void SetupAzureKeyVault()
        {
            JsonWebKey rsaImport = new JsonWebKey(ColumnMasterKeyCertificate.GetRSAPrivateKey(), true);
            string akvKeyName = $"AE-{ColumnMasterKeyCertificate.Thumbprint}";

            _keyClient.ImportKey(akvKeyName, rsaImport);
            _akvKeyNames.Add(akvKeyName);
            AkvKeyUrl = (new Uri(DataTestUtility.AKVBaseUri, $"/keys/{akvKeyName}")).AbsoluteUri;
        }

        internal override void SetupDatabase()
        {
            ColumnMasterKey akvColumnMasterKey = new AkvColumnMasterKey(GenerateUniqueName("AKVCMK"), akvUrl: AkvKeyUrl, AkvStoreProvider, DataTestUtility.EnclaveEnabled);
            DummyMasterKey = new DummyMasterKeyForAKVProvider(GenerateUniqueName("DummyCMK"), AkvKeyUrl, AkvStoreProvider, false);

            databaseObjects.Add(akvColumnMasterKey);
            databaseObjects.Add(DummyMasterKey);

            List<ColumnEncryptionKey> akvColumnEncryptionKeys = CreateColumnEncryptionKeys(akvColumnMasterKey, 2, AkvStoreProvider);
            List<ColumnEncryptionKey> dummyColumnEncryptionKeys = CreateColumnEncryptionKeys(DummyMasterKey, 1, AkvStoreProvider);
            akvColumnEncryptionKeys.AddRange(dummyColumnEncryptionKeys);
            databaseObjects.AddRange(akvColumnEncryptionKeys);

            List<Table> tables = CreateTables(akvColumnEncryptionKeys);
            AKVTestTable = new AKVTestTable(GenerateUniqueName("AKVTestTable"), akvColumnEncryptionKeys[0], akvColumnEncryptionKeys[1]);
            tables.Add(AKVTestTable);
            databaseObjects.AddRange(tables);

            base.SetupDatabase();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            foreach (string keyName in _akvKeyNames)
            {
                try
                {
                    _keyClient.StartDeleteKey(keyName);
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }
    }
}
