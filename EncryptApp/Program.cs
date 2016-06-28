using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BabelMark;
using Newtonsoft.Json;

namespace EncryptApp
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: passphrase decode|encode");
                return 1;
            }

            if (!(args[1] == "decode" || args[1] == "encode"))
            {
                Console.WriteLine("Usage: passphrase decode|encode");
                Console.WriteLine($"Invalid argument ${args[1]}");
                return 1;
            }

            var encode = args[1] == "encode";

            Environment.SetEnvironmentVariable(MarkdownRegistry.PassphraseEnv, args[0]);

            var entries = MarkdownRegistry.Instance.GetEntriesAsync().Result;

            foreach (var entry in entries)
            {
                if (encode)
                {
                    var originalUrl = entry.Url;
                    entry.Url = StringCipher.Encrypt(entry.Url, args[0]);
                    var testDecrypt = StringCipher.Decrypt(entry.Url, args[0]);
                    if (originalUrl != testDecrypt)
                    {
                        Console.WriteLine("Unexpected error while encrypt/decrypt. Not matching");
                        return 1;
                    }
                }
            }

            Console.WriteLine(JsonConvert.SerializeObject(entries, Formatting.Indented));

            return 0;
        }
    }
}
