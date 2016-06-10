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

        public bool CommonMark { get; set; }
    }

    public class MarkdownRegistry
    {
        public const string PassphraseEnv = "BABELMARK_PASSPHRASE";

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

            var passPhrase = Environment.GetEnvironmentVariable(PassphraseEnv)?.Trim();
            if (string.IsNullOrWhiteSpace(passPhrase))
            {
                throw new InvalidOperationException("The BABELMARK_PASSPHRASE env is empty");
            }

            try
            {
                var client = new HttpClient();
                var textRegistry =
                    await
                        client.GetStringAsync(
                            "https://raw.githubusercontent.com/babelmark/babelmark-registry/master/registry.json");

                var jsonRegistry = JsonConvert.DeserializeObject<List<MarkdownEntry>>(textRegistry);

                newEntries.Clear();
                foreach (var entry in jsonRegistry)
                {
                    // Decrypt an url if it doesn't starts by http
                    if (!entry.Url.StartsWith("http"))
                    {
                        entry.Url = StringCipher.Decrypt(entry.Url, passPhrase);
                    }

                    newEntries.Add(entry);
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
