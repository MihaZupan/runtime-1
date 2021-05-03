// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal sealed class DynamicServiceProviderEngine : CompiledServiceProviderEngine
    {
        public DynamicServiceProviderEngine(IEnumerable<ServiceDescriptor> serviceDescriptors)
            : base(serviceDescriptors)
        {
        }

        protected override Func<ServiceProviderEngineScope, object> RealizeService(ServiceCallSite callSite)
        {
            int callCount = 0;
            return scope =>
            {
                // Resolve the result before we increment the call count, this ensures that singletons
                // won't cause any side effects during the compilation of the resolve function.
                var result = RuntimeResolver.Resolve(callSite, scope);

                if (Interlocked.Increment(ref callCount) == 2)
                {
                    // Don't capture the ExecutionContext when forking to build the compiled version of the
                    // resolve function
                    _ = ThreadPool.UnsafeQueueUserWorkItem(_ =>
                    {
                        try
                        {
                            base.RealizeService(callSite);
                        }
                        catch (Exception ex)
                        {
                            DependencyInjectionEventSource.Log.ServiceRealizationFailed(ex);
                        }
                    },
                    null);
                }

                return result;
            };
        }
    }
}
