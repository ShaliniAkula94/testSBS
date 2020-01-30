﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.Data.SqlClient.SqlClientEventSource;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests.EventSourceTest
{
    public class EventSourceListenerGeneral : EventListener
    {
        public EventLevel Level { get; set; }
        public EventKeywords Keyword { get; set; }

        public List<object> events = new List<object>();

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            events = eventData.Payload.ToList();
        }
    }

    public class SqlClientEventSourceTest
    {
        [Fact]
        public void TestSqlClientEventSourceName()
        {
            Assert.Equal("Microsoft.Data.SqlClient.EventSource", SqlClientEventSource.Log.Name);
        }

        //The values are equivilant to Keywords value inside SqlClientEventSource
        [Theory]
        [InlineData(new int[] { 1, 2, 4, 8, 16 })]
        public async void TestEventKeywords(int[] values)
        {
            using (EventSourceListenerGeneral listener = new EventSourceListenerGeneral())
            {
                listener.Keyword = (EventKeywords)values[0];
                listener.Level = EventLevel.Informational;
                bool status = false;

                //Events should be disabled by default
                Assert.False(Log.IsEnabled());

                //We try to Enable events. 
                //Since we have to have 2 arguments in EnableEvents Informational is selected arbitrary.
                //If we do not wait for tasks to be completed Assert.True will run before other calls and will return false.
                var task1 = Task.Run(() =>
                {
                    listener.EnableEvents(Log, EventLevel.Informational);
                });
                await task1.ContinueWith((t) =>
                 {
                     status = Log.IsEnabled();
                 });
                Assert.True(status);

                //check if we are able to disable all the events
                var task2 = Task.Run(() =>
                {
                    listener.DisableEvents(Log);
                });
                await task2.ContinueWith((t) =>
                {
                    status = Log.IsEnabled();
                });
                Assert.False(status);

                //Check if we are able to enable specific Event keyword  Trace
                var task3 = Task.Run(() =>
                {
                    listener.EnableEvents(Log, listener.Level, listener.Keyword);
                });
                await task3.ContinueWith((t) =>
                {
                    //(EventKeywords)1 is Trace which is defined in SqlClientEventSource Keywords class
                    status = Log.IsEnabled(EventLevel.Informational, SqlClientEventSource.Keywords.Trace);
                });
                Assert.True(status);

                //Check if we are able to enable specific Event keyword. Scope
                listener.Keyword = (EventKeywords)values[1];
                var task4 = Task.Run(() =>
                {
                    listener.EnableEvents(Log, listener.Level, listener.Keyword);
                });
                await task4.ContinueWith((t) =>
                {
                    //(EventKeywords)1 is Trace which is defined in SqlClientEventSource Keywords class
                    status = Log.IsEnabled(EventLevel.Informational, SqlClientEventSource.Keywords.Scope);
                });
                Assert.True(status);
            }
        }
    }
}
