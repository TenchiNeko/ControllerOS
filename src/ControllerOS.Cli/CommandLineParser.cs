namespace ControllerOS.Cli;

public sealed record ParsedCommand(string Name, IReadOnlyList<string> Arguments, bool Json);

public sealed class CommandLineParseException(string message) : Exception(message);

public static class CommandLineParser
{
    public static ParsedCommand Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0 || arguments.Contains("--help", StringComparer.Ordinal) || arguments.Contains("-h", StringComparer.Ordinal))
            return new("help", Array.Empty<string>(), false);

        bool json = arguments.Contains("--json", StringComparer.Ordinal);
        string[] values = arguments.Where(value => value != "--json").ToArray();
        if (values.Length == 0)
            return new("help", Array.Empty<string>(), json);

        string name;
        string[] commandArguments;
        if (values[0] == "runtime")
        {
            if (values.Length < 2 || values[1] is not ("start" or "stop"))
                throw new CommandLineParseException("Use 'runtime start' or 'runtime stop'.");
            name = $"runtime {values[1]}";
            commandArguments = values.Skip(2).ToArray();
        }
        else
        {
            name = values[0];
            commandArguments = values.Skip(1).ToArray();
        }

        bool valid = name switch
        {
            "devices" or "self-test" or "version" or "runtime stop" => commandArguments.Length == 0,
            "inspect" => commandArguments.Length == 1,
            "teach" => commandArguments.Length == 1 || commandArguments.Length == 3 && commandArguments[1] == "--model",
            "validate" or "simulate" => commandArguments.Length == 1,
            "export-report" => commandArguments.Length <= 2,
            "runtime start" => commandArguments.Length == 7 && commandArguments[1] == "--device" &&
                commandArguments[3] == "--definition" && commandArguments[5] == "--calibration",
            "help" => commandArguments.Length == 0,
            _ => false
        };
        if (!valid)
            throw new CommandLineParseException(UsageFor(name));
        if (json && name is "teach" or "runtime start")
            throw new CommandLineParseException($"'{name}' is interactive and does not support --json.");
        return new(name, Array.AsReadOnly(commandArguments), json);
    }

    public static string UsageFor(string command) => command switch
    {
        "devices" => "Usage: controlleros devices [--json]",
        "version" => "Usage: controlleros version [--json]",
        "inspect" => "Usage: controlleros inspect <controller-id> [--json]",
        "teach" => "Usage: controlleros teach <controller-id> [--model <name>]",
        "validate" => "Usage: controlleros validate <profile-or-report.json> [--json]",
        "simulate" => "Usage: controlleros simulate <profile.json> [--json]",
        "export-report" => "Usage: controlleros export-report [input-report.json] [output-report.json] [--json]",
        "runtime start" => "Usage: controlleros runtime start <profile.json> --device <controller-id> --definition <definition-id> --calibration <calibration-id>",
        "runtime stop" => "Usage: controlleros runtime stop [--json]",
        "self-test" => "Usage: controlleros self-test [--json]",
        _ => "Use controlleros --help to list commands."
    };
}
