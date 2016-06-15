// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BabelMark
{
    public class RequestThrottlingMiddleware
    {
        private readonly MemoryCache clientCache;

        private readonly RequestDelegate next;

        private readonly ILogger<RequestThrottlingMiddleware> logger;

        /// <summary>
        /// The maximum number of concurrent clients (default is 100)
        /// </summary>
        private const int MaxNumberOfClients = 100;

        private const int ExpirationMinutes = 1;

        private const double NumberRequestPerSeconds = 2.0;

        public RequestThrottlingMiddleware(RequestDelegate next, ILogger<RequestThrottlingMiddleware> logger)
        {
            this.next = next;
            this.logger = logger;
            clientCache = new MemoryCache(new MemoryCacheOptions()
            {
                ExpirationScanFrequency = TimeSpan.FromSeconds(ExpirationMinutes * 60 / 2.0)
            });
        }

        public async Task Invoke(HttpContext context)
        {
            // Verify the client
            var ip = GetClientId(context);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace($"Client [{ip}] issued a request -> {context.Request.GetDisplayUrl()}");
            }

            string errorText = null;
            lock (clientCache)
            {
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    // Check that a particular client is not making too many requests
                    DateTime lastTime;
                    if (clientCache.TryGetValue(ip, out lastTime))
                    {
                        var delta = (DateTime.Now - lastTime);
                        if (delta.TotalSeconds < (1.0 / NumberRequestPerSeconds))
                        {
                            errorText = "Error: Requests exceeded for your IP/location. Please wait a few seconds before issuing another request.";
                        }
                    }
                    clientCache.Set(ip, DateTime.Now, TimeSpan.FromMinutes(ExpirationMinutes));
                }
                else
                {
                    errorText = "Fatal: Unable to identify the connection. Client not allowed";
                }

                // Do we have more concurrent clients than we should?
                if (clientCache.Count > MaxNumberOfClients)
                {
                    errorText = "Error: Maximum number of clients reached. Please wait before issuing another request.";
                }
            }

            // Return an object with the description
            if (errorText != null)
            {
                logger.LogError($"An error occured for client [{ip}]. {errorText}");

                var textWriter = new StringWriter();
                var writer = new JsonTextWriter(textWriter) {Formatting = Formatting.None};
                var jobject = new JObject
                {
                    ["name"] = "unknown",
                    ["repo"] = "#",
                    ["cmark"] = false,
                    ["lang"] = "",
                    ["time"] = 0.0,
                    ["html"] = errorText,
                    ["html_clean"] = errorText,
                    ["html_safe"] = errorText
                };

                jobject.WriteTo(writer);
                textWriter.Write("\n\n");
                var buffer = Encoding.UTF8.GetBytes(textWriter.ToString());

                await context.Response.Body.WriteAsync(buffer, 0, buffer.Length);
                await context.Response.Body.FlushAsync();

                return;
            }

            await next(context);
        }

        private string GetClientId(HttpContext context)
        {
            var request = context.Request;
            if (request == null)
            {
                return null;
            }

            string id = null;
            StringValues values;
            if (request.Headers != null && request.Headers.TryGetValue("X-Forwarded-For", out values))
            {
                id = values.ToString(); // We don't split the values, we just get them as-is
            }

            if (string.IsNullOrWhiteSpace(id) && request.Headers != null && request.Headers.TryGetValue("REMOTE_ADDR", out values))
            {
                id = values.ToString(); // We don't split the values, we just get them as-is
            }

            if (string.IsNullOrWhiteSpace(id) && context.Connection?.RemoteIpAddress != null)
            {
                id = context.Connection.RemoteIpAddress.ToString();
            }

            return id;
        }
    }
}