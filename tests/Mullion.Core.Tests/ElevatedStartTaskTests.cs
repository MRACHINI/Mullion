using System.Xml.Linq;
using Mullion.Core;

namespace Mullion.Core.Tests;

public class ElevatedStartTaskTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Theory]
    [InlineData(@"C:\Apps\Mullion\Mullion.exe")]
    [InlineData(@"C:\Users\Zoë & Co\<apps>\Mullion.exe")]
    public void The_task_starts_the_given_program(string path)
    {
        string xml = ElevatedStartTask.BuildXml(path, Sid);

        Assert.Equal(path, ElevatedStartTask.CommandOf(xml));
    }

    [Fact]
    public void The_task_runs_as_administrator_when_that_user_signs_in()
    {
        var task = XDocument.Parse(ElevatedStartTask.BuildXml(@"D:\Mullion.exe", Sid)).Root!;

        Assert.Equal("HighestAvailable", task.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", task.Descendants(Ns + "LogonType").Single().Value);
        Assert.Equal(Sid, task.Element(Ns + "Triggers")!.Element(Ns + "LogonTrigger")!.Element(Ns + "UserId")!.Value);
        Assert.Equal(Sid, task.Descendants(Ns + "Principal").Single().Element(Ns + "UserId")!.Value);
    }

    [Fact]
    public void The_task_is_not_cut_short_by_time_or_battery()
    {
        var settings = XDocument.Parse(ElevatedStartTask.BuildXml(@"D:\Mullion.exe", Sid)).Root!.Element(Ns + "Settings")!;

        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<Task />")]
    public void A_definition_that_cannot_be_read_names_no_program(string xml)
    {
        Assert.Null(ElevatedStartTask.CommandOf(xml));
    }

    [Fact]
    public void Each_user_has_a_task_of_their_own()
    {
        Assert.NotEqual(ElevatedStartTask.NameFor("ann"), ElevatedStartTask.NameFor("bob"));
    }
}
