using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MoreLinq;

namespace ProtonDrive.App.FileExclusion;

public class FileFilter
{

    private List<Entry> _includeEntries = [];
    private string _includes = "";
    ///<summary>
    /// Defines the inclusions entries.
    /// </summary>
    ///
    /// <remarks>
    /// It takes the form of a multi-line string where each line is an entry.
    /// 
    /// Lines beginning with a # are ignored and interpreted as comments.
    /// Entries can have exceptions. The exceptions to an entry are all the lines that are directly after it and
    /// that begins with a '!'.
    /// </remarks>
    /// 
    /// <example>
    /// <code>
    /// **/entry/*.txt
    /// !**/exception.txt
    ///  w  
    /// **/entry2.*
    /// !**.temp
    /// !**.sh
    /// </code>
    ///
    /// The above example includes every file in the entry directory except exception.txt files.
    /// And includes all entry2 file as long as the extension is not <c>temp</c> nor <c>sh</c>.
    /// </example>
    public string Includes
    {
        get => _includes;
        set
        {
            _includeEntries = BuildEntries(value);
            _includes = value;
        }
    }
    
    private List<Entry> _excludeEntries = [];
    private string _excludes = "";
    ///<summary>
    /// Defines the exclusion entries.
    /// </summary>
    ///
    /// <remarks>
    /// It takes the form of a multi-line string where each line is an entry.
    /// 
    /// Lines beginning with a # are ignored and interpreted as comments.
    /// Entries can have exceptions. The exceptions to an entry are all the lines that are directly after it
    /// and that begins with a '!'.
    /// </remarks>
    /// 
    /// <example>
    /// <code>
    /// **/entry/*.txt
    /// !**/exception.txt
    ///  
    /// **/entry2.*
    /// !**.temp
    /// !**.sh
    /// </code>
    ///
    /// The above example excludes every file in the entry directory except exception.txt files.
    /// And excludes all entry2 file as long as the extension is not <c>temp</c> nor <c>sh</c>.
    /// </example>
    public string Excludes
    {
        get => _excludes;
        set
        {
            _excludeEntries = BuildEntries(value);
            _excludes = value;
        }
    }

    private static List<Entry> BuildEntries(string value)
    {
        var lines = value.Split(["\r\n", "\r", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith('#'))
            .ToList();
        
        List<Entry> entries = [];
        
        Regex? current = null;
        List<Regex> exceptions = [];
        
        foreach (var line in lines)
        {
            if (line.StartsWith("!"))
            {
                if (current == null)
                {
                    throw new ArgumentException("Exceptions needs to be placed after the original instruction.");
                }
                
                exceptions.Add(ConvertToRegex(line[1..]));
            }
            else
            {
                if (current != null)
                {
                    // Reset
                    entries.Add(new Entry(current, exceptions.ToList()));
                    exceptions.Clear();
                }
                
                current = ConvertToRegex(line);
            }
        }

        if (current != null)
        {
            entries.Add(new Entry(current, exceptions));
        }
        
        return entries;
    }


    private static Regex ConvertToRegex(string pattern)
    {
        if (pattern.Length == 0)
        {
            return new Regex(".*");
        }

        return pattern[..2] switch
        {
            "\\r" => RegexHelper.ToRegex(pattern[2..]),
            _ => RegexHelper.GlobToRegex(pattern)
        };
    }

    public bool ShouldExcludeFile(string filePath)
    {
        var excluded = true;
        var segments = filePath.Split([Path.DirectorySeparatorChar, '/']);
        for (var i = segments.Length; i-->0;)
        {
            var completePath = segments[..(i+1)].ToDelimitedString("/");
            if (Excluded(completePath))
            {
                return true;
            }

            if (excluded && Included(completePath))
            {
                excluded = false;
            }
        }

        return excluded;

        bool Included(string path) => 
            _includeEntries.Count == 0 || _includeEntries.Any(expr => expr.Match(path));
        
        bool Excluded(string path) => 
            _excludeEntries.Any(expr => expr.Match(path));
    }
}
