﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Data.SqlClientX.IO;

namespace Microsoft.Data.SqlClientX.Tds.Tokens.EnvChange
{
    /// <summary>
    /// Commit transaction token.
    /// </summary>
    internal sealed class TransactionEnvChangeToken : EnvChangeToken<ByteBuffer>
    {
        private readonly EnvChangeTokenSubType _subtype;
        /// <summary>
        /// EnvChange token sub type.
        /// </summary>
        public override EnvChangeTokenSubType SubType => _subtype;

        /// <summary>
        /// Create a new instance of this token.
        /// </summary>
        /// <param name="subtype">Subtype of this token</param>
        /// <param name="oldValue">Old value./</param>
        /// <param name="newValue">New value.</param>
        public TransactionEnvChangeToken(EnvChangeTokenSubType subtype, ByteBuffer oldValue, ByteBuffer newValue) : base(oldValue, newValue)
        {
            _subtype = subtype;
        }

    }
}
