using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProtonDrive.App.FileExclusion;

public class Entry(Regex entry, List<Regex> exceptions)
{
    public bool Match(string input) => 
        entry.IsMatch(input) && !exceptions.Any(x => x.IsMatch(input));
}
