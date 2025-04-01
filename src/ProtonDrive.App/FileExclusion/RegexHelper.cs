using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtonDrive.App.FileExclusion;

public static class RegexHelper
{
    public static Regex GlobToRegex(string glob)
    {
        // The function both escape regex characters and transforms glob wildcards
        StringBuilder expr = new StringBuilder();
        int groupCount = 0;

        StringBuilder OpenGroup()
        {
            groupCount++;
            return expr.Append('[');
        }

        StringBuilder CloseGroup()
        {
            groupCount--;
            return expr.Append(']');
        }

        for (int i = 0; i < glob.Length; i++)
        {
            var ch = glob[i];

            if (ch == '*' && groupCount == 0)
            {
                // We need to handle stars, first count them.
                var stars = glob[i..].TakeWhile(c => c == '*').Count();
                // If there are more than 1 star, match multiple segments. Otherwise, match exactly one.
                expr.Append(stars > 1 ? @"[^\/]+(\/[^\/]+)*" : @"([^\/]+)");

                i += stars - 1;
                continue;
            }

            _ = ch switch
            {
                // If there is an escaped character, add it as is
                '\\' => expr.Append('\\').Append(glob[++i]),

                // Regex meta characters that are not used by glob
                '/' or '$' or '^' or '*' or '+' or '.' or '(' or ')' or '=' or '!' or '|' =>
                    expr.Append('\\').Append(ch),

                // For ?, it is mapped to a .
                '?' => expr.Append('.'),

                // Ranges have the same syntax
                '[' or ']' => expr.Append(ch),

                // For groups, we need to keep track of opened ones
                '{' => OpenGroup(),
                '}' => CloseGroup(),

                // commas are only important if inside a group
                ',' when groupCount > 0 => expr.Append('|'), // In a group
                ',' => expr.Append("\\,"), // Out of a group

                _ => expr.Append(ch)
            };
        }

        return ToRegex(expr.ToString());
    }

    public static Regex ToRegex(string pattern)
    {
        if (!pattern.StartsWith("^"))
        {
            pattern = "^" + pattern;
        }

        if (!pattern.EndsWith("$"))
        {
            pattern += "$";
        }

        return new Regex(pattern);
    }
}
