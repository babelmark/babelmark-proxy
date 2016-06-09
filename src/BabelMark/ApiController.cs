using System;
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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUglify;
using NUglify.Html;

namespace BabelMark
{
    public class ApiController : Controller
    {
        private readonly ILogger<ApiController> logger;

        public ApiController(ILogger<ApiController> logger)
        {
            this.logger = logger;
        }

        [HttpGet]
        [Route("api/get")]
        public async Task Get([FromQuery] string text)
        {
            text = text ?? string.Empty;

            var getResultBlock = new TransformBlock<MarkdownEntry, JObject>(async implem =>
            {
                var clock = Stopwatch.StartNew();
                JObject jobject;
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        var jsonText =
                            await httpClient.GetStringAsync(implem.Url + "text=" + Uri.EscapeDataString(text));
                        jobject = JObject.Parse(jsonText);
                        var html = jobject["html"]?.ToString() ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(html))
                        {
                            html = string.Empty;
                            jobject["html_clean"] = html;
                        }
                        else
                        {
                            // Generates also a clean html in order to compare implems
                            var settings = HtmlSettings.Pretty();
                            settings.IsFragmentOnly = true;
                            var result = Uglify.Html(html, settings);
                            jobject["html_clean"] = result.Code;
                        }
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError("Unexpected exception: " + exception);

                    // In case we have an error, we still return an object
                    jobject = new JObject
                    {
                        ["version"] = "unknown",
                        ["error"] = GetPrettyMessageFromException(exception)
                    };
                }
                clock.Stop();

                // Set common fields
                jobject["name"] = implem.Name; // use the name from the registry, not the one returned
                jobject["repo"] = implem.Repo;
                jobject["lang"] = implem.Lang;
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
            }, new ExecutionDataflowBlockOptions() {MaxDegreeOfParallelism = 1});

            getResultBlock.LinkTo(returnResultBlock, new DataflowLinkOptions() {PropagateCompletion = true});

            try
            {
                var entries = await MarkdownRegistry.Instance.GetEntriesAsync();

                // We shuffle the entries to random the order of the latency of the results
                Shuffle(entries);

                foreach (var entry in entries)
                {
                    await getResultBlock.SendAsync(entry);
                }

                getResultBlock.Complete();

                await returnResultBlock.Completion;
            }
            catch (Exception ex)
            {
                logger.LogError("Unexpected exception while fetching/returning: " + ex);
            }
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

        public static void Shuffle<T>(List<T> list)
        {
            // from: http://stackoverflow.com/a/1262619/1356325
            var random = new Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                var value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
