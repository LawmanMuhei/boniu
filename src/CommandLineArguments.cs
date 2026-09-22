using System;
using System.Text;

namespace MiniView
{
    internal static class CommandLineArguments
    {
        internal static string Join(string[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException("arguments");
            StringBuilder builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(QuoteWindowsArgument(argument));
            }
            return builder.ToString();
        }

        internal static string QuoteWindowsArgument(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return value;

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(character);
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
