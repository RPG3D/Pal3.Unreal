// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Command
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using Core.Command;

    public static class CommandExtensions
    {
        public static string ToString(ICommand command)
        {
            StringBuilder builder = new();
            Type type = command.GetType();

            builder.Append(type.Name[..^"Command".Length]);
            builder.Append(' ');

            foreach (PropertyInfo propertyInfo in type.GetProperties())
            {
                object value = propertyInfo.GetValue(command);
                string text = value switch
                {
                    string s when s.Length == 0 || s.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) >= 0
                        => JsonSerializer.Serialize(s),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value?.ToString() ?? ""
                };
                builder.Append(text);
                builder.Append(' ');
            }

            return builder.ToString().TrimEnd();
        }
    }
}
