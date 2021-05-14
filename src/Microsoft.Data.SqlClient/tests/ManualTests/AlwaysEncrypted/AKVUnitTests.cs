﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using Azure.Identity;
using Xunit;
using Azure.Security.KeyVault.Keys;
using Azure.Core;
using System.Reflection;
using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests.AlwaysEncrypted
{
    public static class AKVUnitTests
    {
        const string EncryptionAlgorithm = "RSA_OAEP";
        public static readonly byte[] s_columnEncryptionKey = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void LegacyAuthenticationCallbackTest()
        {
            // SqlClientCustomTokenCredential implements legacy authentication callback to request access token at client-side.
            SqlColumnEncryptionAzureKeyVaultProvider akvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());
            byte[] encryptedCek = akvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, s_columnEncryptionKey);
            byte[] decryptedCek = akvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, encryptedCek);

            Assert.Equal(s_columnEncryptionKey, decryptedCek);
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void TokenCredentialTest()
        {
            ClientSecretCredential clientSecretCredential = new ClientSecretCredential(DataTestUtility.AKVTenantId, DataTestUtility.AKVClientId, DataTestUtility.AKVClientSecret);
            SqlColumnEncryptionAzureKeyVaultProvider akvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(clientSecretCredential);
            byte[] encryptedCek = akvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, s_columnEncryptionKey);
            byte[] decryptedCek = akvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, encryptedCek);

            Assert.Equal(s_columnEncryptionKey, decryptedCek);
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void TokenCredentialRotationTest()
        {
            // SqlClientCustomTokenCredential implements a legacy authentication callback to request the access token from the client-side.
            SqlColumnEncryptionAzureKeyVaultProvider oldAkvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());

            ClientSecretCredential clientSecretCredential = new ClientSecretCredential(DataTestUtility.AKVTenantId, DataTestUtility.AKVClientId, DataTestUtility.AKVClientSecret);
            SqlColumnEncryptionAzureKeyVaultProvider newAkvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(clientSecretCredential);

            byte[] encryptedCekWithNewProvider = newAkvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, s_columnEncryptionKey);
            byte[] decryptedCekWithOldProvider = oldAkvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, encryptedCekWithNewProvider);
            Assert.Equal(s_columnEncryptionKey, decryptedCekWithOldProvider);

            byte[] encryptedCekWithOldProvider = oldAkvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, s_columnEncryptionKey);
            byte[] decryptedCekWithNewProvider = newAkvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, EncryptionAlgorithm, encryptedCekWithOldProvider);
            Assert.Equal(s_columnEncryptionKey, decryptedCekWithNewProvider);
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void ReturnSpecifiedVersionOfKeyWhenItIsNotTheMostRecentVersion()
        {
            Uri keyPathUri = new Uri(DataTestUtility.AKVOriginalUrl);
            Uri vaultUri = new Uri(keyPathUri.GetLeftPart(UriPartial.Authority));

            //If key version is not specified then we cannot test.
            if (KeyIsVersioned(keyPathUri))
            {
                string keyName = keyPathUri.Segments[2];
                string keyVersion = keyPathUri.Segments[3];
                ClientSecretCredential clientSecretCredential = new ClientSecretCredential(DataTestUtility.AKVTenantId, DataTestUtility.AKVClientId, DataTestUtility.AKVClientSecret);
                KeyClient keyClient = new KeyClient(vaultUri, clientSecretCredential);
                KeyVaultKey currentVersionKey = keyClient.GetKey(keyName);
                KeyVaultKey specifiedVersionKey = keyClient.GetKey(keyName, keyVersion);

                //If specified versioned key is the most recent version of the key then we cannot test.
                if (!KeyIsLatestVersion(specifiedVersionKey, currentVersionKey))
                {
                    SqlColumnEncryptionAzureKeyVaultProvider azureKeyProvider = new SqlColumnEncryptionAzureKeyVaultProvider(clientSecretCredential);
                    // Perform an operation to initialize the internal caches
                    azureKeyProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVOriginalUrl, EncryptionAlgorithm, s_columnEncryptionKey);

                    PropertyInfo keyCryptographerProperty = azureKeyProvider.GetType().GetProperty("KeyCryptographer", BindingFlags.NonPublic | BindingFlags.Instance);
                    var keyCryptographer = keyCryptographerProperty.GetValue(azureKeyProvider);
                    MethodInfo getKeyMethod = keyCryptographer.GetType().GetMethod("GetKey", BindingFlags.NonPublic | BindingFlags.Instance);
                    KeyVaultKey key = (KeyVaultKey)getKeyMethod.Invoke(keyCryptographer, new[] { DataTestUtility.AKVOriginalUrl });

                    Assert.Equal(keyVersion, key.Properties.Version);
                }
            }
        }

        static bool KeyIsVersioned(Uri keyPath) => keyPath.Segments.Length > 3;
        static bool KeyIsLatestVersion(KeyVaultKey specifiedVersionKey, KeyVaultKey currentVersionKey) => currentVersionKey.Properties.Version == specifiedVersionKey.Properties.Version;

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void ThrowWhenUrlHasLessThanThreeSegments()
        {
            SqlColumnEncryptionAzureKeyVaultProvider azureKeyProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());
            string invalidKeyPath = "https://my-key-vault.vault.azure.net/keys";
            Exception ex1 = Assert.Throws<ArgumentException>(() => azureKeyProvider.EncryptColumnEncryptionKey(invalidKeyPath, EncryptionAlgorithm, s_columnEncryptionKey));
            Assert.Contains($"Invalid url specified: '{invalidKeyPath}'", ex1.Message);
            Exception ex2 = Assert.Throws<ArgumentException>(() => azureKeyProvider.DecryptColumnEncryptionKey(invalidKeyPath, EncryptionAlgorithm, s_columnEncryptionKey));
            Assert.Contains($"Invalid url specified: '{invalidKeyPath}'", ex2.Message);
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void DecryptedCekIsCachedDuringDecryption()
        {
            SqlColumnEncryptionAzureKeyVaultProvider akvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());
            string cacheName = "_columnEncryptionKeyCache";
            byte[] plaintextKey1 = { 1, 2, 3 };
            byte[] plaintextKey2 = { 1, 2, 3 };
            byte[] plaintextKey3 = { 0, 1, 2, 3 };
            byte[] encryptedKey1 = akvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", plaintextKey1);
            byte[] encryptedKey2 = akvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", plaintextKey2);
            byte[] encryptedKey3 = akvProvider.EncryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", plaintextKey3);

            byte[] decryptedKey1 = akvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", encryptedKey1);
            Assert.Equal(1, GetCacheCount(cacheName, akvProvider));
            Assert.True(decryptedKey1.SequenceEqual(plaintextKey1));

            byte[] decryptedKey2 = akvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", encryptedKey2);
            Assert.Equal(2, GetCacheCount(cacheName, akvProvider));
            Assert.True(decryptedKey2.SequenceEqual(plaintextKey2));

            byte[] decryptedKey3 = akvProvider.DecryptColumnEncryptionKey(DataTestUtility.AKVUrl, "RSA_OAEP", encryptedKey3);
            Assert.Equal(3, GetCacheCount(cacheName, akvProvider));
            Assert.True(decryptedKey3.SequenceEqual(plaintextKey3));
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsAKVSetupAvailable))]
        public static void SignatureVerificationResultIsCachedDuringVerification()
        {
            SqlColumnEncryptionAzureKeyVaultProvider akvProvider = new SqlColumnEncryptionAzureKeyVaultProvider(new SqlClientCustomTokenCredential());
            string cacheName = "_columnMasterKeyMetadataSignatureVerificationCache";
            byte[] signature = akvProvider.SignColumnMasterKeyMetadata(DataTestUtility.AKVUrl, true);
            byte[] signature2 = akvProvider.SignColumnMasterKeyMetadata(DataTestUtility.AKVUrl, true);
            byte[] signatureWithoutEnclave = akvProvider.SignColumnMasterKeyMetadata(DataTestUtility.AKVUrl, false);

            Assert.True(akvProvider.VerifyColumnMasterKeyMetadata(DataTestUtility.AKVUrl, true, signature));
            Assert.Equal(1, GetCacheCount(cacheName, akvProvider));

            Assert.True(akvProvider.VerifyColumnMasterKeyMetadata(DataTestUtility.AKVUrl, true, signature));
            Assert.Equal(1, GetCacheCount(cacheName, akvProvider));

            Assert.True(akvProvider.VerifyColumnMasterKeyMetadata(DataTestUtility.AKVUrl, true, signature2));
            Assert.Equal(1, GetCacheCount(cacheName, akvProvider));

            Assert.True(akvProvider.VerifyColumnMasterKeyMetadata(DataTestUtility.AKVUrl, false, signatureWithoutEnclave));
            Assert.Equal(2, GetCacheCount(cacheName, akvProvider));
        }

        private static int GetCacheCount(string nameOfCache, SqlColumnEncryptionAzureKeyVaultProvider akvProvider)
        {
            Assembly akvProviderAssembly = typeof(SqlColumnEncryptionAzureKeyVaultProvider).Assembly;
            Type akvProviderType = akvProviderAssembly.GetType(
                "Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider.SqlColumnEncryptionAzureKeyVaultProvider");
            FieldInfo cacheField = akvProviderType.GetField(nameOfCache, BindingFlags.Instance | BindingFlags.NonPublic);
            var cacheInstance = cacheField.GetValue(akvProvider);
            Type cacheType = cacheInstance.GetType();
            var countProperty = cacheType.GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic);
            var countValue = countProperty.GetValue(cacheInstance);
            return (int)countValue;
        }
    }
}
