using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JavaScriptEngineSwitcher.Core;
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
        private static IJsEngine _jsengine;
        private static readonly HttpClient httpClient = new HttpClient();
        private static string _commonmarkjs;
        private static string _mardownit;

        public ApiController(ILogger<ApiController> logger)
        {
            this.logger = logger;
        }

        public async Task<JObject> GetCommonMarkJs(string text)
        {
            _jsengine = _jsengine ?? JsEngineSwitcher.Instance.CreateDefaultEngine();

            var version = "0.28.1";
            if (_commonmarkjs == null)
            {
                _commonmarkjs = await httpClient.GetStringAsync($"https://raw.githubusercontent.com/commonmark/commonmark.js/{version}/dist/commonmark.min.js");
                _jsengine.Execute(_commonmarkjs);
            }

            var inputval = "input_" + Guid.NewGuid().ToString().Replace("-", "_");
            _jsengine.SetVariableValue(inputval, text);

            var script = $"(new commonmark.HtmlRenderer()).render((new commonmark.Parser()).parse({inputval}));";
            var result = _jsengine.Evaluate(script)?.ToString();
            _jsengine.RemoveVariable(inputval);

            var jsonResult = new JObject
            {
                ["name"] = "commonmark.js",
                ["version"] = version,
                ["html"] = result
            };

            return jsonResult;
        }

        public async Task<JObject> GetMarkdownIt(string text)
        {
            _jsengine = _jsengine ?? JsEngineSwitcher.Instance.CreateDefaultEngine();

            var version = "8.4.0";
            if (_mardownit == null)
            {
                _mardownit = await httpClient.GetStringAsync($"https://raw.githubusercontent.com/markdown-it/markdown-it/{version}/dist/markdown-it.min.js");
                _jsengine.Execute(_mardownit);
                _jsengine.Evaluate("var MarkdownIt = markdownit();");
            }

            var inputval = "input_" + Guid.NewGuid().ToString().Replace("-", "_");
            _jsengine.SetVariableValue(inputval, text);

            var script = $"MarkdownIt.render({inputval});";
            var result = _jsengine.Evaluate(script)?.ToString();
            _jsengine.RemoveVariable(inputval);

            var jsonResult = new JObject
            {
                ["name"] = "markdown-it",
                ["version"] = version,
                ["html"] = result
            };

            return jsonResult;
        }

        [HttpGet]
        [Route("api/get")]
        public async Task Get([FromQuery] string text)
        {
            // Make sure that the length is limited to 1000 characters
            text = text ?? string.Empty;
            if (text.Length > 1000)
            {
                text = text.Substring(0, 1000);
            }

            var getResultBlock = new TransformBlock<MarkdownEntry, JObject>(async implem =>
            {
                var clock = Stopwatch.StartNew();
                JObject jobject;
                try
                {
                    if (implem.Url == "js:commonmark.js")
                    {
                        jobject = await GetCommonMarkJs(text);
                    }
                    else if (implem.Url == "js:markdown-it")
                    {
                        jobject = await GetMarkdownIt(text);
                    }
                    else
                    {
                        var jsonText =
                            await httpClient.GetStringAsync(implem.Url + "text=" + Uri.EscapeDataString(text));
                        jobject = JObject.Parse(jsonText);
                    }
                    var html = jobject["html"]?.ToString() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(html))
                    {
                        html = string.Empty;
                        jobject["html_clean"] = html;
                        jobject["html_safe"] = html;
                    }
                    else
                    {
                        // Generates also a clean html in order to compare implems
                        var settings = HtmlSettings.Pretty();
                        settings.IsFragmentOnly = true;
                        settings.MinifyCss = false;
                        settings.MinifyCssAttributes = false;
                        settings.MinifyJs = false;
                        var result = Uglify.Html(html, settings);
                        jobject["html_clean"] = result.Code;

                        // Remove any javascript
                        settings.RemoveJavaScript = true;
                        result = Uglify.Html(html, settings);
                        jobject["html_safe"] = result.Code;
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
                jobject["cmark"] = implem.CommonMark;
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
