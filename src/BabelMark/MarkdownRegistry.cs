using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BabelMark
{
    public class MarkdownEntry
    {
        public string Name { get; set; }

        public string Url { get; set; }

        public string Lang { get; set; }

        public string Repo { get; set; }
    }

    public class MarkdownRegistry
    {
        private List<MarkdownEntry> entries;
        private DateTime lastTime;

        private MarkdownRegistry()
        {
            entries = new List<MarkdownEntry>();
        }

        public async Task<List<MarkdownEntry>> GetEntriesAsync()
        {
            var newEntries = entries.ToList();
            if (newEntries.Count > 0 && (DateTime.Now - lastTime).TotalHours <= 1)
            {
                return newEntries;
            }

            try
            {
                var client = new HttpClient();
                var textRegistry =
                    await
                        client.GetStringAsync(
                            "https://raw.githubusercontent.com/babelmark/babelmark-registry/master/registry.json");

                var jsonRegistry = JsonConvert.DeserializeObject<Dictionary<string, MarkdownEntry>>(textRegistry);

                newEntries.Clear();
                foreach (var entry in jsonRegistry)
                {
                    entry.Value.Name = entry.Key;
                    newEntries.Add(entry.Value);
                }

                entries = newEntries;
                lastTime = DateTime.Now;
            }
            catch (HttpRequestException) when(entries.Count > 0) // Don't throw an exception if we have already entries
            {
            }

            return entries.ToList();
        }

        public static MarkdownRegistry Instance { get; } = new MarkdownRegistry();
    }
}
