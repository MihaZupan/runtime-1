// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;

namespace System.Net.Http
{
    [EventSource(Name = "System.Net.Http")]
    internal sealed class HttpTelemetry : EventSource
    {
        public static readonly HttpTelemetry Log = new HttpTelemetry();

        private IncrementingPollingCounter? _startedRequestsPerSecondCounter;
        private IncrementingPollingCounter? _failedRequestsPerSecondCounter;
        private PollingCounter? _startedRequestsCounter;
        private PollingCounter? _currentRequestsCounter;
        private PollingCounter? _failedRequestsCounter;
        private PollingCounter? _totalHttp11ConnectionsCounter;
        private PollingCounter? _totalHttp20ConnectionsCounter;
        private EventCounter? _requestsQueueDurationCounter;

        private long _startedRequests;
        private long _stoppedRequests;
        private long _failedRequests;

        private long _openedHttp11Connections;
        private long _openedHttp20Connections;

        // NOTE
        // - The 'Start' and 'Stop' suffixes on the following event names have special meaning in EventSource. They
        //   enable creating 'activities'.
        //   For more information, take a look at the following blog post:
        //   https://blogs.msdn.microsoft.com/vancem/2015/09/14/exploring-eventsource-activity-correlation-and-causation-features/
        // - A stop event's event id must be next one after its start event.

        [Event(1, Level = EventLevel.Informational)]
        private void RequestStart(string scheme, string host, int port, string pathAndQuery, byte versionMajor, byte versionMinor, HttpVersionPolicy versionPolicy)
        {
            Interlocked.Increment(ref _startedRequests);
            WriteEvent(eventId: 1, scheme, host, port, pathAndQuery, versionMajor, versionMinor, versionPolicy);
        }

        [NonEvent]
        public void RequestStart(HttpRequestMessage request)
        {
            Debug.Assert(request.RequestUri != null);

            RequestStart(
                request.RequestUri.Scheme,
                request.RequestUri.IdnHost,
                request.RequestUri.Port,
                request.RequestUri.PathAndQuery,
                (byte)request.Version.Major,
                (byte)request.Version.Minor,
                request.VersionPolicy);
        }

        [Event(2, Level = EventLevel.Informational)]
        public void RequestStop()
        {
            Interlocked.Increment(ref _stoppedRequests);
            WriteEvent(eventId: 2);
        }

        [Event(3, Level = EventLevel.Error)]
        public void RequestFailed()
        {
            Interlocked.Increment(ref _failedRequests);
            WriteEvent(eventId: 3);
        }

        [Event(4, Level = EventLevel.Informational)]
        public void Http11ConnectionEstablished()
        {
            Interlocked.Increment(ref _openedHttp11Connections);
            WriteEvent(eventId: 4);
        }

        [Event(5, Level = EventLevel.Informational)]
        public void Http11ConnectionClosed()
        {
            long count = Interlocked.Decrement(ref _openedHttp11Connections);
            Debug.Assert(count >= 0);
            WriteEvent(eventId: 5);
        }

        [Event(6, Level = EventLevel.Informational)]
        public void Http20ConnectionEstablished()
        {
            Interlocked.Increment(ref _openedHttp20Connections);
            WriteEvent(eventId: 6);
        }

        [Event(7, Level = EventLevel.Informational)]
        public void Http20ConnectionClosed()
        {
            long count = Interlocked.Decrement(ref _openedHttp20Connections);
            Debug.Assert(count >= 0);
            WriteEvent(eventId: 7);
        }

        [Event(8, Level = EventLevel.Informational)]
        public void Http11RequestLeftQueue(double timeOnQueueMilliseconds)
        {
            _requestsQueueDurationCounter!.WriteMetric(timeOnQueueMilliseconds);
            WriteEvent(eventId: 8, timeOnQueueMilliseconds);
        }

        [Event(9, Level = EventLevel.Informational)]
        public void ResponseHeadersBegin()
        {
            WriteEvent(eventId: 9);
        }

        [Event(10, Level = EventLevel.Informational)]
        public void ResponseContentBegin()
        {
            WriteEvent(eventId: 10);
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (command.Command == EventCommand.Enable)
            {
                // This is the convention for initializing counters in the RuntimeEventSource (lazily on the first enable command).
                // They aren't disabled afterwards...

                // The cumulative number of HTTP requests started since the process started.
                _startedRequestsCounter ??= new PollingCounter("requests-started", this, () => Interlocked.Read(ref _startedRequests))
                {
                    DisplayName = "Requests Started",
                };

                // The number of HTTP requests started per second since the process started.
                _startedRequestsPerSecondCounter ??= new IncrementingPollingCounter("requests-started-rate", this, () => Interlocked.Read(ref _startedRequests))
                {
                    DisplayName = "Requests Started Rate",
                    DisplayRateTimeScale = TimeSpan.FromSeconds(1)
                };

                // The cumulative number of HTTP requests failed since the process started.
                // Failed means that an exception occurred during the handler's Send(Async) call as a result of a connection related error, timeout, or explicitly cancelled.
                // In case of using HttpClient's SendAsync(and friends) with buffering, this includes exceptions that occured while buffering the response content
                // In case of using HttpClient's helper methods (GetString/ByteArray/Stream), this includes responses with non-success status codes
                _failedRequestsCounter ??= new PollingCounter("requests-failed", this, () => Interlocked.Read(ref _failedRequests))
                {
                    DisplayName = "Requests Failed"
                };

                // The number of HTTP requests failed per second since the process started.
                _failedRequestsPerSecondCounter ??= new IncrementingPollingCounter("requests-failed-rate", this, () => Interlocked.Read(ref _failedRequests))
                {
                    DisplayName = "Requests Failed Rate",
                    DisplayRateTimeScale = TimeSpan.FromSeconds(1)
                };

                // The current number of active HTTP requests that have started but not yet completed or failed.
                // Use (-_stoppedRequests + _startedRequests) to avoid returning a negative value if _stoppedRequests is
                // incremented after reading _startedRequests due to race conditions with completing the HTTP request.
                _currentRequestsCounter ??= new PollingCounter("current-requests", this, () => -Interlocked.Read(ref _stoppedRequests) + Interlocked.Read(ref _startedRequests))
                {
                    DisplayName = "Current Requests"
                };

                _totalHttp11ConnectionsCounter ??= new PollingCounter("http11-connections-current-total", this, () => Interlocked.Read(ref _openedHttp11Connections))
                {
                    DisplayName = "Current Http 1.1 Connections"
                };

                _totalHttp20ConnectionsCounter ??= new PollingCounter("http20-connections-current-total", this, () => Interlocked.Read(ref _openedHttp20Connections))
                {
                    DisplayName = "Current Http 2.0 Connections"
                };

                _requestsQueueDurationCounter ??= new EventCounter("http11-requests-queue-duration", this)
                {
                    DisplayName = "HTTP 1.1 Requests Queue Duration",
                    DisplayUnits = "ms"
                };
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, string? arg1, string? arg2, int arg3, string? arg4, byte arg5, byte arg6, HttpVersionPolicy arg7)
        {
            if (IsEnabled())
            {
                if (arg1 == null) arg1 = "";
                if (arg2 == null) arg2 = "";
                if (arg4 == null) arg4 = "";

                fixed (char* arg1Ptr = arg1)
                fixed (char* arg2Ptr = arg2)
                fixed (char* arg4Ptr = arg4)
                {
                    const int NumEventDatas = 7;
                    var descrs = stackalloc EventData[NumEventDatas];

                    descrs[0] = new EventData
                    {
                        DataPointer = (IntPtr)(arg1Ptr),
                        Size = (arg1.Length + 1) * sizeof(char)
                    };
                    descrs[1] = new EventData
                    {
                        DataPointer = (IntPtr)(arg2Ptr),
                        Size = (arg2.Length + 1) * sizeof(char)
                    };
                    descrs[2] = new EventData
                    {
                        DataPointer = (IntPtr)(&arg3),
                        Size = sizeof(int)
                    };
                    descrs[3] = new EventData
                    {
                        DataPointer = (IntPtr)(arg4Ptr),
                        Size = (arg4.Length + 1) * sizeof(char)
                    };
                    descrs[4] = new EventData
                    {
                        DataPointer = (IntPtr)(&arg5),
                        Size = sizeof(byte)
                    };
                    descrs[5] = new EventData
                    {
                        DataPointer = (IntPtr)(&arg6),
                        Size = sizeof(byte)
                    };
                    descrs[6] = new EventData
                    {
                        DataPointer = (IntPtr)(&arg7),
                        Size = sizeof(HttpVersionPolicy)
                    };

                    WriteEventCore(eventId, NumEventDatas, descrs);
                }
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, double arg1)
        {
            if (IsEnabled())
            {
                EventData descr = new EventData
                {
                    DataPointer = (IntPtr)(&arg1),
                    Size = sizeof(double)
                };

                WriteEventCore(eventId, eventDataCount: 1, &descr);
            }
        }
    }
}
