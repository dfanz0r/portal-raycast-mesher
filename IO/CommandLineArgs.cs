using System;
using System.Collections.Generic;

namespace TerrainTool.IO
{
    public class CommandLineArgs
    {
        public string Command { get; private set; } = "run";
        private Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> PositionalArgs { get; private set; } = new List<string>();

        public static CommandLineArgs Parse(string[] args)
        {
            var result = new CommandLineArgs();
            if (args == null || args.Length == 0) return result;

            int index = 0;

            // Check for command (first argument not starting with -)
            if (!args[0].StartsWith("-"))
            {
                result.Command = args[0].ToLowerInvariant();
                index++;
            }

            for (; index < args.Length; index++)
            {
                string arg = args[index];

                if (arg.StartsWith("-"))
                {
                    string key;
                    string value = "true";

                    int equalsIndex = arg.IndexOf('=');
                    if (equalsIndex > -1)
                    {
                        key = arg.Substring(0, equalsIndex).TrimStart('-');
                        value = arg.Substring(equalsIndex + 1);
                    }
                    else
                    {
                        key = arg.TrimStart('-');
                        if (index + 1 < args.Length && !args[index + 1].StartsWith("-"))
                        {
                            value = args[index + 1];
                            index++;
                        }
                    }

                    result._options[key] = value;
                }
                else
                {
                    result.PositionalArgs.Add(arg);
                }
            }

            return result;
        }

        public string GetOption(string key, string defaultValue)
        {
            return _options.TryGetValue(key, out var val) ? val ?? defaultValue : defaultValue;
        }

        public bool HasFlag(string key)
        {
            return _options.ContainsKey(key);
        }
    }
}
