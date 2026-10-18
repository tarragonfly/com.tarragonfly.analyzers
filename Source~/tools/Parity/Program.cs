using System;
using System.Collections.Generic;
using System.IO;

namespace TarragonFly.Analyzers.Parity
{
    // Parity --out <report file> --corpus <dir> --oracle <dll>... [--findings <findings.json>]
    //   Writes a report comparing each TF rule with the rule it replaces.
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                Dictionary<string, List<string>> options = ReadOptions(args);
                return ParityRun.Run(
                    Required(options, "--out"),
                    Required(options, "--corpus"),
                    options.TryGetValue("--oracle", out List<string> oracles) ? oracles : new List<string>(),
                    options.TryGetValue("--findings", out List<string> findings) ? findings[0] : null);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("Usage: Parity --out <file> --corpus <dir> --oracle <dll>... [--findings <findings.json>]");
                return 2;
            }
        }

        private static Dictionary<string, List<string>> ReadOptions(string[] args)
        {
            Dictionary<string, List<string>> options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{args[i]}'.");
                if (!options.TryGetValue(args[i], out List<string> values)) options[args[i]] = values = new List<string>();
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) values.Add(Path.GetFullPath(args[++i]));
            }
            return options;
        }

        private static string Required(Dictionary<string, List<string>> options, string name)
        {
            if (!options.TryGetValue(name, out List<string> values) || values.Count == 0) throw new ArgumentException($"{name} is required.");
            return values[0];
        }
    }
}