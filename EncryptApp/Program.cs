using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BabelMark;

namespace EncryptApp
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: string_to_encode passphrase");
                return 1;
            }

            var result = StringCipher.Encrypt(args[0], args[1]);

            var check = StringCipher.Decrypt(result, args[1]);
            if (check != args[0])
            {
                Console.WriteLine("Unexpected error while encrypt/decrypt. Not matching");
                return 1;
            }

            Console.WriteLine(result);

            return 0;
        }
    }
}
