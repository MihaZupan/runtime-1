﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Internal;

namespace System.Net
{
    internal class NameResolutionTelemetry
    {
        public static NameResolutionTelemetry Log => new NameResolutionTelemetry();

        public static readonly bool IsEnabled = false;

        public ValueStopwatch ResolutionStart(string hostNameOrAddress) => default;

        public ValueStopwatch ResolutionStart(IPAddress address) => default;

        public void AfterResolution(string hostNameOrAddress, ValueStopwatch stopwatch, bool successful) { }

        public void AfterResolution(IPAddress address, ValueStopwatch stopwatch, bool successful) { }
    }
}
