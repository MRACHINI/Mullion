using System.Xml.Linq;

namespace Mullion.Core;

/// <summary>
/// The scheduled task that starts Mullion as administrator when the user signs
/// in. The "Run" registry key cannot do that without Windows asking for
/// permission at every sign-in; a task registered once, with permission, can.
/// </summary>
public static class ElevatedStartTask
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>The task's name: one per Windows user, since task names are shared by the whole PC.</summary>
    public static string NameFor(string userName) => $"Mullion ({userName})";

    /// <summary>The task definition, as the XML the Task Scheduler takes.</summary>
    /// <param name="command">The full path of Mullion.exe.</param>
    /// <param name="userSid">The security identifier of the user who signs in.</param>
    public static string BuildXml(string command, string userSid)
    {
        var task = new XElement(
            Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(
                Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Starts Mullion as administrator when you sign in to Windows.")),
            new XElement(
                Ns + "Triggers",
                new XElement(
                    Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "UserId", userSid))),
            new XElement(
                Ns + "Principals",
                new XElement(
                    Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(
                Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),

                // A task is by default not started on battery power, stopped when the
                // PC goes onto it, and ended after three days. Mullion runs until the
                // user exits it.
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "true"),
                new XElement(Ns + "StartWhenAvailable", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "Hidden", "false"),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),

                // Tasks run below normal priority unless told otherwise, which would
                // make the grid slow to follow the mouse.
                new XElement(Ns + "Priority", "4")),
            new XElement(
                Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    Ns + "Exec",
                    new XElement(Ns + "Command", command))));

        return new XDocument(task).ToString();
    }

    /// <summary>The program a task definition starts, or null when it cannot be read.</summary>
    public static string? CommandOf(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants(Ns + "Command").FirstOrDefault()?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
