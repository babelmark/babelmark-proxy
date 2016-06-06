﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUglify;
using NUglify.Html;

namespace BabelMark
{
    public class ApiController : Controller
    {
        [HttpGet]
        [Route("api/get")]
        public async Task Get([FromQuery] string text)
        {
            text = text ?? string.Empty;

            var getResultBlock = new TransformBlock<MarkdownEntry, JObject>(async implem =>
            {
                var clock = Stopwatch.StartNew();
                JObject jobject;
                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        var jsonText = await httpClient.GetStringAsync(implem.Url + "text=" + Uri.EscapeDataString(text));
                        jobject = JObject.Parse(jsonText);
                        var html = jobject["html"]?.ToString() ?? string.Empty;

                        // Generates also a clean html in order to compare implems
                        var settings = HtmlSettings.Pretty();
                        settings.IsFragmentOnly = true;
                        var result = Uglify.Html(html, settings);
                        jobject["html_clean"] = result.Code;
                    }
                    catch (Exception exception)
                    {
                        // In case we have an error, we still return an object
                        jobject = new JObject
                        {
                            ["name"] = implem.Name,
                            ["version"] = "unknown",
                            ["error"] = GetPrettyMessageFromException(exception)
                        };
                    }
                }
                clock.Stop();

                jobject["repo"] = implem.Repo;
                jobject["lang"] = implem.Lang;
                // name, html, version, JObject               
                jobject["time"] = clock.Elapsed.TotalSeconds;

                return jobject;
            }, new ExecutionDataflowBlockOptions() {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded});


            var returnResultBlock = new ActionBlock<JObject>(async jobject =>
            {
                var textWriter = new StringWriter();
                var writer = new JsonTextWriter(textWriter) {Formatting = Formatting.None};
                jobject.WriteTo(writer);
                textWriter.Write("\n\n");
                var buffer = Encoding.UTF8.GetBytes(textWriter.ToString());

                await HttpContext.Response.Body.WriteAsync(buffer, 0, buffer.Length);
                await HttpContext.Response.Body.FlushAsync();
            }, new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1});

            getResultBlock.LinkTo(returnResultBlock, new DataflowLinkOptions() {PropagateCompletion = true});

            var entries = await MarkdownRegistry.Instance.GetEntriesAsync();
            foreach (var entry in entries)
            {
                await getResultBlock.SendAsync(entry);
            }

            getResultBlock.Complete();

            await returnResultBlock.Completion;
        }

        private static string GetPrettyMessageFromException(Exception exception)
        {
            var builder = new StringBuilder();
            while (exception != null)
            {
                builder.Append(exception.Message);
                exception = exception.InnerException;
            }
            return builder.ToString();
        }
    }
}
