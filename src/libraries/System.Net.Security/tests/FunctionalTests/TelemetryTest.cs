﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace System.Net.Security.Tests
{
    public class TelemetryTest
    {
        [Fact]
        public static void EventSource_ExistsWithCorrectId()
        {
            Type esType = typeof(SslStream).Assembly.GetType("System.Net.Security.NetSecurityTelemetry", throwOnError: true, ignoreCase: false);
            Assert.NotNull(esType);

            Assert.Equal("System.Net.Security", EventSource.GetName(esType));
            Assert.Equal(Guid.Parse("7beee6b1-e3fa-5ddb-34be-1404ad0e2520"), EventSource.GetGuid(esType));

            Assert.NotEmpty(EventSource.GenerateManifest(esType, esType.Assembly.Location));
        }

        [Fact]
        public static async Task LongTest()
        {
            await Task.Delay(30_000);
        }

        //[OuterLoop]
        [ConditionalFact(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
        public static void EventSource_SuccessfulHandshake_LogsStartStop()
        {
            RemoteExecutor.Invoke(async () =>
            {
                using var listener = new TestEventListener("System.Net.Security", EventLevel.Verbose, eventCounterInterval: 0.1d);

                var events = new ConcurrentQueue<EventWrittenEventArgs>();
                await listener.RunWithCallbackAsync(events.Enqueue, async () =>
                {
                    // Invoke tests that'll cause some events to be generated
                    var test = new SslStreamStreamToStreamTest_Async();
                    await test.SslStream_StreamToStream_Authentication_Success();
                    await Task.Delay(300);
                });
                Assert.DoesNotContain(events, ev => ev.EventId == 0); // errors from the EventSource itself

                EventWrittenEventArgs[] starts = events.Where(e => e.EventName == "HandshakeStart").ToArray();
                Assert.Equal(2, starts.Length);
                Assert.All(starts, s => Assert.Equal(2, s.Payload.Count));
                Assert.Single(starts, s => s.Payload[0] is bool isServer && isServer);
                Assert.Single(starts, s => s.Payload[1] is string targetHost && targetHost.Length == 0);

                EventWrittenEventArgs[] stops = events.Where(e => e.EventName == "HandshakeStop").ToArray();
                Assert.Equal(2, stops.Length);
                Assert.All(stops, s => Assert.Equal(1, s.Payload.Count));
                Assert.All(stops, s => Assert.NotEmpty(s.Payload[0] as string));

                Assert.DoesNotContain(events, e => e.EventName == "HandshakeFailed");

                VerifyEventCounters(events, shouldHaveFailures: false);
            }).Dispose();
        }

        //[OuterLoop]
        [ConditionalFact(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
        public static void EventSource_UnsuccessfulHandshake_LogsStartFailureStop()
        {
            RemoteExecutor.Invoke(async () =>
            {
                using var listener = new TestEventListener("System.Net.Security", EventLevel.Verbose, eventCounterInterval: 0.1d);

                var events = new ConcurrentQueue<EventWrittenEventArgs>();
                await listener.RunWithCallbackAsync(events.Enqueue, async () =>
                {
                    // Invoke tests that'll cause some events to be generated
                    var test = new SslStreamStreamToStreamTest_Async();
                    await test.SslStream_ServerLocalCertificateSelectionCallbackReturnsNull_Throw();
                    await Task.Delay(300);
                });
                Assert.DoesNotContain(events, ev => ev.EventId == 0); // errors from the EventSource itself

                EventWrittenEventArgs[] starts = events.Where(e => e.EventName == "HandshakeStart").ToArray();
                Assert.Equal(2, starts.Length);
                Assert.All(starts, s => Assert.Equal(2, s.Payload.Count));
                Assert.Single(starts, s => s.Payload[0] is bool isServer && isServer);
                Assert.Single(starts, s => s.Payload[1] is string targetHost && targetHost.Length == 0);

                EventWrittenEventArgs[] failures = events.Where(e => e.EventName == "HandshakeFailed").ToArray();
                Assert.Equal(2, failures.Length);
                Assert.All(failures, f => Assert.Equal(3, f.Payload.Count));
                Assert.Single(failures, f => f.Payload[0] is bool isServer && isServer);
                Assert.All(failures, f => Assert.NotEmpty(f.Payload[2] as string)); // exceptionMessage

                EventWrittenEventArgs[] stops = events.Where(e => e.EventName == "HandshakeStop").ToArray();
                Assert.Equal(2, stops.Length);
                Assert.All(stops, s => Assert.Equal(1, s.Payload.Count));
                Assert.All(stops, s => Assert.Empty(s.Payload[0] as string));

                VerifyEventCounters(events, shouldHaveFailures: true);
            }).Dispose();
        }

        private static void VerifyEventCounters(ConcurrentQueue<EventWrittenEventArgs> events, bool shouldHaveFailures)
        {
            Dictionary<string, double[]> eventCounters = events
                .Where(e => e.EventName == "EventCounters")
                .Select(e => (IDictionary<string, object>)e.Payload.Single())
                .GroupBy(d => (string)d["Name"], d => (double)(d.ContainsKey("Mean") ? d["Mean"] : d["Increment"]))
                .ToDictionary(p => p.Key, p => p.ToArray());

            Assert.True(eventCounters.TryGetValue("total-tls-handshakes", out double[] totalHandshakes));
            Assert.Equal(2, totalHandshakes[^1]);

            Assert.True(eventCounters.TryGetValue("tls-handshake-rate", out double[] handshakeRate));
            Assert.Contains(handshakeRate, r => r > 0);

            Assert.True(eventCounters.TryGetValue("failed-tls-handshakes", out double[] failedHandshakes));
            if (shouldHaveFailures)
            {
                Assert.Equal(2, failedHandshakes[^1]);
            }
            else
            {
                Assert.All(failedHandshakes, f => Assert.Equal(0, f));
            }

            Assert.True(eventCounters.TryGetValue("current-tls-handshakes", out double[] currentHandshakes));
            Assert.Contains(currentHandshakes, h => h > 0);
            Assert.Equal(0, currentHandshakes[^1]);


            double[] openedConnections = eventCounters
                .Where(pair => pair.Key.EndsWith("-connections-open"))
                .Select(pair => pair.Value[^1])
                .ToArray();

            // Events should be emitted for all 4 connections-open counters
            Assert.Equal(4, openedConnections.Length);
            Assert.All(openedConnections, oc => Assert.Equal(0, oc));


            double[][] handshakeDurations = eventCounters
                .Where(pair => pair.Key.EndsWith("-handshake-duration"))
                .Select(pair => pair.Value)
                .ToArray();

            // Events should be emitted for all 4 handshake-duration counters
            Assert.Equal(4, handshakeDurations.Length);

            if (shouldHaveFailures)
            {
                Assert.All(handshakeDurations, durations => Assert.All(durations, d => Assert.Equal(0, d)));
            }
            else
            {
                Assert.Contains(handshakeDurations, durations => durations.Any(d => d > 0));
            }
        }
    }
}
