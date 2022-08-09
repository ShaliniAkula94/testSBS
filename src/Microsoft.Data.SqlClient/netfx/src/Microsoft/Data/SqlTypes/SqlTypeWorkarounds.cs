// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Xml;

namespace Microsoft.Data.SqlTypes
{
    /// <summary>
    /// This type provides workarounds for the separation between Microsoft.Data.Common
    /// and Microsoft.Data.SqlClient.  The latter wants to access internal members of the former, and
    /// this class provides ways to do that.  We must review and update this implementation any time the
    /// implementation of the corresponding types in Microsoft.Data.Common change.
    /// </summary>
    internal static class SqlTypeWorkarounds
    {
        #region Work around inability to access SqlXml.CreateSqlXmlReader
        private static readonly XmlReaderSettings s_defaultXmlReaderSettings = new XmlReaderSettings() { ConformanceLevel = ConformanceLevel.Fragment };
        private static readonly XmlReaderSettings s_defaultXmlReaderSettingsCloseInput = new XmlReaderSettings() { ConformanceLevel = ConformanceLevel.Fragment, CloseInput = true };
        private static readonly XmlReaderSettings s_defaultXmlReaderSettingsAsyncCloseInput = new XmlReaderSettings() { Async = true, ConformanceLevel = ConformanceLevel.Fragment, CloseInput = true };

        internal const SqlCompareOptions SqlStringValidSqlCompareOptionMask =
            SqlCompareOptions.IgnoreCase | SqlCompareOptions.IgnoreWidth |
            SqlCompareOptions.IgnoreNonSpace | SqlCompareOptions.IgnoreKanaType |
            SqlCompareOptions.BinarySort | SqlCompareOptions.BinarySort2;

        internal static XmlReader SqlXmlCreateSqlXmlReader(Stream stream, bool closeInput = false, bool async = false)
        {
            Debug.Assert(closeInput || !async, "Currently we do not have pre-created settings for !closeInput+async");

            XmlReaderSettings settingsToUse = closeInput ?
                (async ? s_defaultXmlReaderSettingsAsyncCloseInput : s_defaultXmlReaderSettingsCloseInput) :
                s_defaultXmlReaderSettings;

            return XmlReader.Create(stream, settingsToUse);
        }

        internal static XmlReader SqlXmlCreateSqlXmlReader(TextReader textReader, bool closeInput = false, bool async = false)
        {
            Debug.Assert(closeInput || !async, "Currently we do not have pre-created settings for !closeInput+async");

            XmlReaderSettings settingsToUse = closeInput ?
               (async ? s_defaultXmlReaderSettingsAsyncCloseInput : s_defaultXmlReaderSettingsCloseInput) :
               s_defaultXmlReaderSettings;

            return XmlReader.Create(textReader, settingsToUse);
        }
        #endregion

        #region Work around inability to access SqlDateTime.ToDateTime
        internal static DateTime SqlDateTimeToDateTime(int daypart, int timepart)
        {
            // Values need to match those from SqlDateTime
            const double SQLTicksPerMillisecond = 0.3;
            const int SQLTicksPerSecond = 300;
            const int SQLTicksPerMinute = SQLTicksPerSecond * 60;
            const int SQLTicksPerHour = SQLTicksPerMinute * 60;
            const int SQLTicksPerDay = SQLTicksPerHour * 24;
            const int MinDay = -53690;                // Jan 1 1753
            const int MaxDay = 2958463;               // Dec 31 9999 is this many days from Jan 1 1900
            const int MinTime = 0;                    // 00:00:0:000PM
            const int MaxTime = SQLTicksPerDay - 1; // = 25919999,  11:59:59:997PM

            if (daypart < MinDay || daypart > MaxDay || timepart < MinTime || timepart > MaxTime)
            {
                throw new OverflowException(SQLResource.DateTimeOverflowMessage);
            }

            long baseDateTicks = new DateTime(1900, 1, 1).Ticks;
            long dayticks = daypart * TimeSpan.TicksPerDay;
            long timeticks = ((long)(timepart / SQLTicksPerMillisecond + 0.5)) * TimeSpan.TicksPerMillisecond;

            return new DateTime(baseDateTicks + dayticks + timeticks);
        }
        #endregion

        #region Work around inability to access SqlMoney.ctor(long, int) and SqlMoney.ToSqlInternalRepresentation
        private static readonly Func<long, SqlMoney> s_sqlMoneyfactory = CtorHelper.CreateFactory<SqlMoney, long, int>(); // binds to SqlMoney..ctor(long, int) if it exists

        /// <summary>
        /// Constructs a SqlMoney from a long value without scaling. The ignored parameter exists
        /// only to distinguish this constructor from the constructor that takes a long.
        /// Used only internally.
        /// </summary>
        internal static SqlMoney SqlMoneyCtor(long value, int ignored)
        {
            SqlMoney val;
            if (s_sqlMoneyfactory is not null)
            {
                val = s_sqlMoneyfactory(value);
            }
            else
            {
                // SqlMoney is a long internally. Dividing by 10,000 gives us the decimal representation
                val = new SqlMoney(((decimal)value) / 10000);
            }

            return val;
        }

        internal static long SqlMoneyToSqlInternalRepresentation(SqlMoney money)
        {
            return SqlMoneyHelper.s_sqlMoneyToLong(ref money);
        }

        private static class SqlMoneyHelper
        {
            internal delegate long SqlMoneyToLongDelegate(ref SqlMoney @this);
            internal static readonly SqlMoneyToLongDelegate s_sqlMoneyToLong = GetSqlMoneyToLong();

            internal static SqlMoneyToLongDelegate GetSqlMoneyToLong()
            {
                SqlMoneyToLongDelegate del = null;
                    try
                    {
                        del = GetFastSqlMoneyToLong();
                    }
                    catch
                    {
                        // If an exception occurs for any reason, swallow & use the fallback code path.
                    }

                return del ?? FallbackSqlMoneyToLong;
            }

            private static SqlMoneyToLongDelegate GetFastSqlMoneyToLong()
            {
                MethodInfo toSqlInternalRepresentation = typeof(SqlMoney).GetMethod("ToSqlInternalRepresentation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.ExactBinding,
                    null, CallingConventions.Any, new Type[] { }, null);

                if (toSqlInternalRepresentation is not null && toSqlInternalRepresentation.ReturnType == typeof(long))
                {
                    // On Full Framework, invoking the MethodInfo first before wrapping
                    // a delegate around it will produce better codegen. We don't need
                    // to inspect the return value; we just need to call the method.

                    _ = toSqlInternalRepresentation.Invoke(new SqlMoney(0), new object[0]);

                    // Now create the delegate. This is an open delegate, meaning the
                    // "this" parameter will be provided as arg0 on each call.

                    var del = (SqlMoneyToLongDelegate)toSqlInternalRepresentation.CreateDelegate(typeof(SqlMoneyToLongDelegate), target: null);

                    // Now we can cache the delegate and invoke it over and over again.
                    // Note: the first parameter to the delegate is provided *byref*.

                    return del;
                }

                return null; // missing the expected method - cannot use fast path
            }

            // Used in case we can't use a [Serializable]-like mechanism.
            private static long FallbackSqlMoneyToLong(ref SqlMoney value)
            {
                if (value.IsNull)
                {
                    return default;
                }
                else
                {
                    decimal data = value.ToDecimal();
                    return (long)(data * 10000);
                }
            }
        }
        #endregion

        #region Work around inability to access SqlDecimal._data1/2/3/4
        internal static void SqlDecimalExtractData(SqlDecimal d, out uint data1, out uint data2, out uint data3, out uint data4)
        {
            SqlDecimalHelper.s_decompose(d, out data1, out data2, out data3, out data4);
        }

        private static class SqlDecimalHelper
        {
            internal delegate void Decomposer(SqlDecimal value, out uint data1, out uint data2, out uint data3, out uint data4);
            internal static readonly Decomposer s_decompose = GetDecomposer();

            private static Decomposer GetDecomposer()
            {
                Decomposer decomposer = null;
                try
                {
                    decomposer = GetFastDecomposer();
                }
                catch
                {
                    // If an exception occurs for any reason, swallow & use the fallback code path.
                }

                return decomposer ?? FallbackDecomposer;
            }

            private static Decomposer GetFastDecomposer()
            {
                // This takes advantage of the fact that for [Serializable] types, the member fields are implicitly
                // part of the type's serialization contract. This includes the fields' names and types. By default,
                // [Serializable]-compliant serializers will read all the member fields and shove the data into a
                // SerializationInfo dictionary. We mimic this behavior in a manner consistent with the [Serializable]
                // pattern, but much more efficiently.
                //
                // In order to make sure we're staying compliant, we need to gate our checks to fulfill some core
                // assumptions. Importantly, the type must be [Serializable] but cannot be ISerializable, as the
                // presence of the interface means that the type wants to be responsible for its own serialization,
                // and that member fields are not guaranteed to be part of the serialization contract. Additionally,
                // we need to check for [OnSerializing] and [OnDeserializing] methods, because we cannot account
                // for any logic which might be present within them.

                if (!typeof(SqlDecimal).IsSerializable)
                {
                    return null; // type is not serializable - cannot use fast path assumptions
                }

                if (typeof(ISerializable).IsAssignableFrom(typeof(SqlDecimal)))
                {
                    return null; // type contains custom logic - cannot use fast path assumptions
                }

                foreach (MethodInfo method in typeof(SqlDecimal).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.IsDefined(typeof(OnDeserializingAttribute)) || method.IsDefined(typeof(OnDeserializedAttribute)))
                    {
                        return null; // type contains custom logic - cannot use fast path assumptions
                    }
                }

                // GetSerializableMembers filters out [NonSerialized] fields for us automatically.

                FieldInfo fiData1 = null, fiData2 = null, fiData3 = null, fiData4 = null;
                foreach (MemberInfo candidate in FormatterServices.GetSerializableMembers(typeof(SqlDecimal)))
                {
                    if (candidate is FieldInfo fi && fi.FieldType == typeof(uint))
                    {
                        if (fi.Name == "m_data1")
                        { fiData1 = fi; }
                        else if (fi.Name == "m_data2")
                        { fiData2 = fi; }
                        else if (fi.Name == "m_data3")
                        { fiData3 = fi; }
                        else if (fi.Name == "m_data4")
                        { fiData4 = fi; }
                    }
                }

                if (fiData1 is null || fiData2 is null || fiData3 is null || fiData4 is null)
                {
                    return null; // missing one of the expected member fields - cannot use fast path assumptions
                }

                Type refToUInt32 = typeof(uint).MakeByRefType();
                DynamicMethod dm = new(
                    name: "sqldecimal-decomposer",
                    returnType: typeof(void),
                    parameterTypes: new[] { typeof(SqlDecimal), refToUInt32, refToUInt32, refToUInt32, refToUInt32 },
                    restrictedSkipVisibility: true); // perf: JITs method at delegate creation time

                ILGenerator ilGen = dm.GetILGenerator();
                ilGen.Emit(OpCodes.Ldarg_1); // eval stack := [UInt32&]
                ilGen.Emit(OpCodes.Ldarg_0); // eval stack := [UInt32&] [SqlDecimal]
                ilGen.Emit(OpCodes.Ldfld, fiData1); // eval stack := [UInt32&] [UInt32]
                ilGen.Emit(OpCodes.Stind_I4); // eval stack := <empty>
                ilGen.Emit(OpCodes.Ldarg_2); // eval stack := [UInt32&]
                ilGen.Emit(OpCodes.Ldarg_0); // eval stack := [UInt32&] [SqlDecimal]
                ilGen.Emit(OpCodes.Ldfld, fiData2); // eval stack := [UInt32&] [UInt32]
                ilGen.Emit(OpCodes.Stind_I4); // eval stack := <empty>
                ilGen.Emit(OpCodes.Ldarg_3); // eval stack := [UInt32&]
                ilGen.Emit(OpCodes.Ldarg_0); // eval stack := [UInt32&] [SqlDecimal]
                ilGen.Emit(OpCodes.Ldfld, fiData3); // eval stack := [UInt32&] [UInt32]
                ilGen.Emit(OpCodes.Stind_I4); // eval stack := <empty>
                ilGen.Emit(OpCodes.Ldarg_S, (byte)4); // eval stack := [UInt32&]
                ilGen.Emit(OpCodes.Ldarg_0); // eval stack := [UInt32&] [SqlDecimal]
                ilGen.Emit(OpCodes.Ldfld, fiData4); // eval stack := [UInt32&] [UInt32]
                ilGen.Emit(OpCodes.Stind_I4); // eval stack := <empty>
                ilGen.Emit(OpCodes.Ret);

                return (Decomposer)dm.CreateDelegate(typeof(Decomposer), null /* target */);
            }

            // Used in case we can't use a [Serializable]-like mechanism.
            private static void FallbackDecomposer(SqlDecimal value, out uint data1, out uint data2, out uint data3, out uint data4)
            {
                if (value.IsNull)
                {
                    data1 = default;
                    data2 = default;
                    data3 = default;
                    data4 = default;
                }
                else
                {
                    int[] data = value.Data; // allocation
                    data4 = (uint)data[3]; // write in reverse to avoid multiple bounds checks
                    data3 = (uint)data[2];
                    data2 = (uint)data[1];
                    data1 = (uint)data[0];
                }
            }
        }
        #endregion

        #region Work around inability to access SqlBinary.ctor(byte[], bool)
        private static readonly Func<byte[], SqlBinary> s_sqlBinaryfactory = CtorHelper.CreateFactory<SqlBinary, byte[], bool>(); // binds to SqlBinary..ctor(byte[], bool) if it exists

        internal static SqlBinary SqlBinaryCtor(byte[] value, bool ignored)
        {
            SqlBinary val;
            if (s_sqlBinaryfactory is not null)
            {
                val = s_sqlBinaryfactory(value);
            }
            else
            {
                val = new SqlBinary(value);
            }

            return val;
        }
        #endregion

        #region Work around inability to access SqlGuid.ctor(byte[], bool)
        private static readonly Func<byte[], SqlGuid> s_sqlGuidfactory = CtorHelper.CreateFactory<SqlGuid, byte[], bool>(); // binds to SqlGuid..ctor(byte[], bool) if it exists

        internal static SqlGuid SqlGuidCtor(byte[] value, bool ignored)
        {
            SqlGuid val;
            if (s_sqlGuidfactory is not null)
            {
                val = s_sqlGuidfactory(value);
            }
            else
            {
                val = new SqlGuid(value);
            }

            return val;
        }
        #endregion

        private static class CtorHelper
        {
            // Returns null if .ctor(TValue, TIgnored) cannot be found.
            // Caller should have fallback logic in place in case the API doesn't exist.
            internal unsafe static Func<TValue, TInstance> CreateFactory<TInstance, TValue, TIgnored>() where TInstance : struct
            {
                try
                {
                    ConstructorInfo fullCtor = typeof(TInstance).GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.ExactBinding,
                        null, new[] { typeof(TValue), typeof(TIgnored) }, null);
                    if (fullCtor is not null)
                    {
                        // Need to use fnptr rather than delegate since MulticastDelegate expects to point to a MethodInfo,
                        // not a ConstructorInfo. The convention for invoking struct ctors is that the caller zeros memory,
                        // then passes a ref to the zeroed memory as the implicit arg0 "this". We don't need to worry
                        // about keeping this pointer alive; the fact that we're instantiated over TInstance will do it
                        // for us.
                        //
                        // On Full Framework, creating a delegate to InvocationHelper before invoking it for the first time
                        // will cause the delegate to point to the pre-JIT stub, which has an expensive preamble. Instead,
                        // we invoke InvocationHelper manually with a captured no-op fnptr. We'll then replace it with the
                        // real fnptr before creating a new delegate (pointing to the real codegen, not the stub) and
                        // returning that new delegate to our caller.

                        static void DummyNoOp(ref TInstance @this, TValue value, TIgnored ignored)
                        { }

                        IntPtr fnPtr;
                        TInstance InvocationHelper(TValue value)
                        {
                            TInstance retVal = default; // ensure zero-inited
                            ((delegate* managed<ref TInstance, TValue, TIgnored, void>)fnPtr)(ref retVal, value, default);
                            return retVal;
                        }

                        fnPtr = (IntPtr)(delegate* managed<ref TInstance, TValue, TIgnored, void>)(&DummyNoOp);
                        InvocationHelper(default); // no-op to trigger JIT

                        fnPtr = fullCtor.MethodHandle.GetFunctionPointer(); // replace before returning to caller
                        return InvocationHelper;
                    }
                }
                catch
                {
                }

                return null; // factory not found or an exception occurred
            }
        }
    }
}
