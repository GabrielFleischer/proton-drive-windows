using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ProtonDrive.App.FileExclusion;

namespace ProtonDrive.App.Test.FileExclusion;

[TestFixture]
public class EntryTest
{
    [Test]
    public void EntryWithAllIncludes()
    {
        var include = RegexHelper.GlobToRegex("**");
        var entry = new Entry(include, []);
        
        Assert.That(entry.Match("aaa/bbb/ccc.exe"), Is.True);
        Assert.That(entry.Match("aaa/bbb/ccc.txt"), Is.True);
        Assert.That(entry.Match("aaa/bbb.exe.txt"), Is.True);
        Assert.That(entry.Match("aaa/bbb/ccc/ddd.exe"), Is.True);
        Assert.That(entry.Match("aaa/ccc/bbb/ddd.exe"), Is.True);
    }
    
    [Test]
    public void EntryWithoutExceptionWorks()
    {
        var include = RegexHelper.GlobToRegex("**/*.exe");
        var entry = new Entry(include, []);
        
        Assert.That(entry.Match("aaa/bbb/ccc.exe"), Is.True);
        Assert.That(entry.Match("aaa/bbb/ccc.txt"), Is.False);
        Assert.That(entry.Match("aaa/bbb.exe.txt"), Is.False);
        Assert.That(entry.Match("aaa/bbb/ccc/ddd.exe"), Is.True);
        Assert.That(entry.Match("aaa/ccc/bbb/ddd.exe"), Is.True);
    }
    
    [Test]
    public void EntryWithExceptionWorks()
    {
        var include = RegexHelper.GlobToRegex("**/*.exe");
        List<Regex> exceptions = [
            RegexHelper.GlobToRegex("**/bbb/*/*.*"),
            RegexHelper.GlobToRegex("**/ccc/*/*.*"),
        ];
        var entry = new Entry(include, exceptions);
        
        Assert.That(entry.Match("aaa/bbb.exe"), Is.True);
        Assert.That(entry.Match("aaa/bbb/ccc.exe"), Is.True);
        Assert.That(entry.Match("aaa/bbb/ccc/ddd.exe"), Is.False);
        Assert.That(entry.Match("aaa/bbb.exe"), Is.True);
        Assert.That(entry.Match("aaa/ccc/bbb/ddd.exe"), Is.False);
    }
}
