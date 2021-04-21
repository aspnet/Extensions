// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting.IntegrationTesting
{
    internal class LoggingHandler : DelegatingHandler
    {
        private ILogger _logger;

        public LoggingHandler(ILoggerFactory loggerFactory, HttpMessageHandler innerHandler) : base(innerHandler)
        {
            _logger = loggerFactory.CreateLogger<HttpClient>();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Sending {method} {url}", request.Method, request.RequestUri);
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                _logger.LogDebug("Received {statusCode} {reasonPhrase} {url}", response.StatusCode, response.ReasonPhrase, request.RequestUri);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Exception while sending '{method} {url}' : {exception}", request.Method, request.RequestUri, ex);
                throw;
            }
        }
    }
}
