// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;

namespace Microsoft.SqlServer.Server
{
    /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/Format.xml' path='Type[@Name="Format"]/Docs/*' />
    public enum Format
    {
        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/Format.xml' path='Type[@Name="Format"]/Members/Member[@MemberName="Unknown"]/Docs/*' />
        Unknown = 0,
        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/Format.xml' path='Type[@Name="Format"]/Members/Member[@MemberName="Native"]/Docs/*' />
        Native = 1,
        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/Format.xml' path='Type[@Name="Format"]/Members/Member[@MemberName="UserDefined"]/Docs/*' />
        UserDefined = 2,
    }

    /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Docs/*' />
    // This custom attribute indicates that the given type is
    // a SqlServer udt. The properties on the attribute reflect the
    // physical attributes that will be used when the type is registered
    // with SqlServer.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public sealed class SqlUserDefinedTypeAttribute : Attribute
    {
        private int _maxByteSize;
        private bool _isFixedLength;
        private bool _isByteOrdered;
        private Format _format;
        private string _name;

        // The maximum value for the maxbytesize field, in bytes.
        internal const int YukonMaxByteSizeValue = 8000;
        private string _validationMethodName = null;

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName=".ctor"]/Docs/*' />
        // A required attribute on all udts, used to indicate that the
        // given type is a udt, and its storage format.
        public SqlUserDefinedTypeAttribute(Format format)
        {
            switch (format)
            {
                case Format.Unknown:
                    throw new ArgumentOutOfRangeException(typeof(Format).Name, string.Format(Strings.ADP_NotSupportedEnumerationValue, typeof(Format).Name, ((int)format).ToString(), nameof(format)));
                case Format.Native:
                case Format.UserDefined:
                    _format = format;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(typeof(Format).Name, string.Format(Strings.ADP_InvalidEnumerationValue, typeof(Format).Name, ((int)format).ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="MaxByteSize"]/Docs/*' />
        // The maximum size of this instance, in bytes. Does not have to be
        // specified for Native serialization. The maximum value
        // for this property is specified by MaxByteSizeValue.
        public int MaxByteSize
        {
            get
            {
                return _maxByteSize;
            }
            set
            {
                if (value < -1)
                {
                    throw new ArgumentOutOfRangeException(value.ToString(), StringsHelper.GetString(Strings.SQLUDT_MaxByteSizeValue), nameof(MaxByteSize));
                }
                _maxByteSize = value;
            }
        }

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="IsFixedLength"]/Docs/*' />
        // Are all instances of this udt the same size on disk?
        public bool IsFixedLength
        {
            get
            {
                return _isFixedLength;
            }
            set
            {
                _isFixedLength = value;
            }
        }

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="IsByteOrdered"]/Docs/*' />
        // Is this type byte ordered, i.e. is the on disk representation
        // consistent with the ordering semantics for this type?
        // If true, the binary representation of the type will be used
        // in comparison by SqlServer. This property enables indexing on the
        // udt and faster comparisons.
        public bool IsByteOrdered
        {
            get
            {
                return _isByteOrdered;
            }
            set
            {
                _isByteOrdered = value;
            }
        }

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="Format"]/Docs/*' />
        // The on-disk format for this type.
        public Format Format => _format;

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="ValidationMethodName"]/Docs/*' />
        // An Optional method used to validate this UDT
        public string ValidationMethodName
        {
            get
            {
                return _validationMethodName;
            }
            set
            {
                _validationMethodName = value;
            }
        }

        /// <include file='../../doc/snippets/Microsoft.SqlServer.Server/SqlUserDefinedTypeAttribute.xml' path='Type[@Name="SqlUserDefinedTypeAttribute"]/Members/Member[@MemberName="Name"]/Docs/*' />
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
            }
        }
    }
}
