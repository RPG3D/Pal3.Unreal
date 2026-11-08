using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pal3.Core.Command;
using Pal3.Core.Command.SceCommands;

namespace Pal3.Game.Command
{
    internal static class CommandText
    {
        private static readonly Regex ArgumentPattern = new("""\G\s*("(?:\\.|[^"\\])*"|[^\s"]+)""");
        private static readonly Dictionary<string, Type> CommandTypes = new[] { typeof(SceneLoadCommand).Assembly, typeof(CommandText).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Command", StringComparison.Ordinal) &&
                typeof(ICommand).IsAssignableFrom(type))
            .ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);

        public static ICommand[] Parse(string text) =>
            text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseLine)
                .ToArray();

        private static ICommand ParseLine(string line)
        {
            var tokens = new List<string>();
            int offset = 0;
            foreach (Match match in ArgumentPattern.Matches(line))
            {
                string value = match.Groups[1].Value;
                tokens.Add(value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value)! : value);
                offset += match.Length;
            }
            if (offset != line.Length || tokens.Count == 0 ||
                !CommandTypes.TryGetValue(tokens[0] + "Command", out Type? type))
            {
                throw new FormatException($"Invalid PAL3 command: {line}");
            }

            var constructor = type.GetConstructors()
                .SingleOrDefault(candidate => candidate.GetParameters().Length == tokens.Count - 1)
                ?? throw new FormatException($"Invalid PAL3 command arguments: {line}");

            object[] arguments = constructor.GetParameters()
                .Select((parameter, index) => ConvertArgument(tokens[index + 1], parameter.ParameterType))
                .ToArray();
            return (ICommand)constructor.Invoke(arguments);
        }

        private static object ConvertArgument(string value, Type type)
        {
            if (type == typeof(float))
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                    ? number : float.Parse(value, NumberStyles.Float, CultureInfo.CurrentCulture);
            }
            if (type.IsEnum) return Enum.Parse(type, value, ignoreCase: true);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
