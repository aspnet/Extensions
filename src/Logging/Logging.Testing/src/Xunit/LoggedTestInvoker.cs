// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Extensions.Logging.Testing
{
    public class LoggedTestInvoker : XunitTestInvoker
    {
        private readonly ITestOutputHelper _output;
        private readonly RetryContext _retryContext;
        private readonly bool _collectDumpOnFailure;

        public LoggedTestInvoker(
            ITest test,
            IMessageBus messageBus,
            Type testClass,
            object[] constructorArguments,
            MethodInfo testMethod,
            object[] testMethodArguments,
            IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            ITestOutputHelper output,
            RetryContext retryContext,
            bool collectDumpOnFailure)
            : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes, aggregator, cancellationTokenSource)
        {
            _output = output;
            _retryContext = retryContext;
            _collectDumpOnFailure = collectDumpOnFailure;
        }

        protected override object CreateTestClass()
        {
            var testClass = base.CreateTestClass();

            (testClass as ILoggedTest).Initialize(
                TestMethod,
                TestMethodArguments,
                _output ?? ConstructorArguments.SingleOrDefault(a => typeof(ITestOutputHelper).IsAssignableFrom(a.GetType())) as ITestOutputHelper);

            if (testClass is LoggedTestBase loggedTestBase)
            {
                // Used for testing
                loggedTestBase.RetryContext = _retryContext;

                if (_retryContext != null)
                {
                    // Log retry attempt as warning
                    if (_retryContext.CurrentIteration > 0)
                    {
                        loggedTestBase.Logger.LogWarning($"{TestMethod.Name} failed and retry conditions are met, re-executing. The reason for failure is {_retryContext.Reason}.");
                    }

                    // Save the test class instance for non-static predicates
                    _retryContext.TestClassInstance = testClass;
                }
            }

            return testClass;
        }

        protected override object CallTestMethod(object testClassInstance)
        {
            try
            {
                return base.CallTestMethod(testClassInstance);
            }
            catch
            {
                if (testClassInstance is LoggedTestBase loggedTestBase)
                {
                    CollectDumpAndThreadPoolStacks(loggedTestBase);
                }

                throw;
            }
        }

        private void CollectDumpAndThreadPoolStacks(LoggedTestBase loggedTestBase)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var path = Path.Combine(loggedTestBase.ResolvedLogOutputDirectory, loggedTestBase.ResolvedTestMethodName + ".dmp");

            var process = Process.GetCurrentProcess();

            DumpCollector.Collect(process, path);

            var pid = process.Id;

            var sb = new StringBuilder();

            using (var dataTarget = DataTarget.AttachToProcess(pid, 5000, AttachFlag.Passive))
            {
                var runtime = dataTarget.ClrVersions[0].CreateRuntime();

                var threadPoolThreads = runtime.Threads.Where(t => t.IsThreadpoolWorker).ToList();

                sb.Append($"\nThreadPool Threads: {threadPoolThreads.Count}\n");

                foreach (var t in threadPoolThreads)
                {
                    if (!t.IsThreadpoolWorker)
                    {
                        continue;
                    }

                    // id
                    // stacktrace
                    var stackTrace = string.Join("\n", t.StackTrace.Select(f => f.ToString()));
                    sb.Append("\n====================================\n");
                    sb.Append($"Thread ID: {t.ManagedThreadId}\n");

                    if (t.StackTrace.Count == 0)
                    {
                        sb.Append("No stack\n");
                    }
                    else
                    {
                        sb.Append(stackTrace + "\n");
                    }
                    sb.Append("====================================\n");
                }
            }

            loggedTestBase.Logger.LogDebug(sb.ToString());
        }
    }
}
