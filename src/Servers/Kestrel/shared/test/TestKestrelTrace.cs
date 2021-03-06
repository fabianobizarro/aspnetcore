// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Testing
{
    internal class TestKestrelTrace : KestrelTrace
    {
        public TestKestrelTrace() : this(new TestApplicationErrorLogger())
        {
        }

        public TestKestrelTrace(TestApplicationErrorLogger testLogger) : this(new LoggerFactory(new[] { new KestrelTestLoggerProvider(testLogger) }))
        {
            Logger = testLogger;
        }

        private TestKestrelTrace(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
            LoggerFactory = loggerFactory;
        }

        public TestApplicationErrorLogger Logger { get; }
        public ILoggerFactory LoggerFactory { get; }
    }
}
