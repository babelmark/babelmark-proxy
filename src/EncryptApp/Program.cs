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

    /// <summary>
    /// Programs to encode/decode the babelmark-registry (https://github.com/babelmark/babelmark-registry)
    /// Or to encode locally an URL
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.WriteLine("Usage: passphrase decode|encode [url?]");
                return 1;
            }

            if (!(args[1] == "decode" || args[1] == "encode"))
            {
                Console.WriteLine("Usage: passphrase decode|encode");
                Console.WriteLine($"Invalid argument ${args[1]}");
                return 1;
            }

            var passphrase = args[0];
            var encode = args[1] == "encode";


            if (args.Length == 3)
            {
                var url = args[2];
                Console.WriteLine(encode ? StringCipher.Encrypt(url, passphrase) : StringCipher.Decrypt(url, passphrase));
            }
            else
            {
                Environment.SetEnvironmentVariable(MarkdownRegistry.PassphraseEnv, passphrase);
                var entries = MarkdownRegistry.Instance.GetEntriesAsync().Result;

                foreach (var entry in entries)
                {
                    if (encode)
                    {
                        var originalUrl = entry.Url;
                        entry.Url = StringCipher.Encrypt(entry.Url, passphrase);
                        var testDecrypt = StringCipher.Decrypt(entry.Url, passphrase);
                        if (originalUrl != testDecrypt)
                        {
                            Console.WriteLine("Unexpected error while encrypt/decrypt. Not matching");
                            return 1;
                        }
                    }
                }

                Console.WriteLine(JsonConvert.SerializeObject(entries, Formatting.Indented));
            }


            return 0;
        }
    }
}
