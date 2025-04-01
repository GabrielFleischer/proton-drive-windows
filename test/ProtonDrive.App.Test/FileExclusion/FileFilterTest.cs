using NUnit.Framework;
using ProtonDrive.App.FileExclusion;

namespace ProtonDrive.App.Test.FileExclusion;

[TestFixture]
public class FileFilterTest
{
    [Test]
    public void SimpleInclusionWithGlob()
    {
        var filter = new FileFilter
        {
            Includes = "**/included.*"
        };
        
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/included.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/included.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/eee.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.exe"), Is.True);
    }
    
    [Test]
    public void EmptyInclusionAcceptEverything()
    {
        var filter = new FileFilter();
        
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/included.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/included.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/eee.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.exe"), Is.False);
    }
    
    [Test]
    public void SimpleInclusionFromRegex()
    {
        var filter = new FileFilter
        {
            Includes = @"\raaa(/...)+\.txt"
        };
        
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.exe"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.exe"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/eee.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("bbb/aaa.txt"), Is.True);
    }
    
    [Test]
    public void SimpleExclusionFromRegex()
    {
        var filter = new FileFilter
        {
            Excludes = @"\raaa(/...)+\.txt"
        };
        
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/eee.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("bbb/aaa.txt"), Is.False);
    }
    
    [Test]
    public void SimpleInclusionExclusionFromGlob()
    {
        var filter = new FileFilter
        {
            Includes = "aaa/**",
            Excludes = "**.txt"
        };
        
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/ddd.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb/ccc/test.exe"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/eee.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/bbb.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("bbb/aaa.txt"), Is.True);
    }
    
    [Test]
    public void InclusionWithExceptions()
    {
        var filter = new FileFilter
        {
            Includes = """
                **/entry/*.txt
                !**/entry/exception.txt
                """
        };
        
        // Should include other *.txt files in "entry" folder
        Assert.That(filter.ShouldExcludeFile("aaa/entry/file1.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/entry/file2.txt"), Is.False);

        // Should not include this file because it's specifically excluded by exception
        Assert.That(filter.ShouldExcludeFile("aaa/entry/exception.txt"), Is.True);
        
        // Should not include files in other directories
        Assert.That(filter.ShouldExcludeFile("aaa/other/file.txt"), Is.True);
    }

    [Test]
    public void ExclusionWithExceptions()
    {
        var filter = new FileFilter
        {
            Excludes = """
                **/entry/*.txt
                !**/entry/special.txt
                """
        };

        // Should exclude this file because it's included in the exclusion pattern
        Assert.That(filter.ShouldExcludeFile("aaa/entry/file.txt"), Is.True);
        
        // Should not exclude this file because it's marked as an exception
        Assert.That(filter.ShouldExcludeFile("aaa/entry/special.txt"), Is.False);

        // Should exclude other files
        Assert.That(filter.ShouldExcludeFile("aaa/entry/anotherfile.txt"), Is.True);
    }

    [Test]
    public void ComplexInclusionWithMultipleExceptions()
    {
        var filter = new FileFilter
        {
            Includes = """
                **/files/*.txt
                !**/files/excluded.txt
                !**/files/exclude_this_too.txt
                """
        };

        // Should exclude the excluded files
        Assert.That(filter.ShouldExcludeFile("aaa/files/excluded.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/files/exclude_this_too.txt"), Is.True);

        // Should include all other files under "files"
        Assert.That(filter.ShouldExcludeFile("aaa/files/valid.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/files/another_valid.txt"), Is.False);

        // Should exclude files from other directories
        Assert.That(filter.ShouldExcludeFile("aaa/other/excluded.txt"), Is.True);
    }

    [Test]
    public void ComplexExclusionWithMultipleExceptions()
    {
        var filter = new FileFilter
        {
            Excludes = """
                **/files/*.txt
                !**/files/allowed.txt
                !**/files/allowed_again.txt
                """
        };

        // Should exclude the general pattern
        Assert.That(filter.ShouldExcludeFile("aaa/files/file1.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/files/file2.txt"), Is.True);

        // Should not exclude the files marked as exceptions
        Assert.That(filter.ShouldExcludeFile("aaa/files/allowed.txt"), Is.False);
        Assert.That(filter.ShouldExcludeFile("aaa/files/allowed_again.txt"), Is.False);

        // Should include files from other directories
        Assert.That(filter.ShouldExcludeFile("aaa/other/file.txt"), Is.False);
    }

    [Test]
    public void InclusionExclusionWithExceptions()
    {
        var filter = new FileFilter
        {
            Includes = """
                **/files/*.txt
                !**/files/excluded.txt
                !**/files/exclude_this_too.txt
                **/files/*.exe
                """,
            Excludes = "**/anotherprogram.exe"
        };

        // Should include simple included file
        Assert.That(filter.ShouldExcludeFile("aaa/files/included.txt"), Is.False);
        
        // Should exclude the files that are excluded by the Excludes pattern, including exceptions
        Assert.That(filter.ShouldExcludeFile("aaa/files/excluded.txt"), Is.True);
        Assert.That(filter.ShouldExcludeFile("aaa/files/exclude_this_too.txt"), Is.True);

        // Should include .exe files because it's included by the Includes pattern
        Assert.That(filter.ShouldExcludeFile("aaa/files/program.exe"), Is.False);

        // Should exclude the .exe files that are excluded by the Excludes pattern
        Assert.That(filter.ShouldExcludeFile("aaa/files/anotherprogram.exe"), Is.True);
    }
}
