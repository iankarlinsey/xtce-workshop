using Xtce.Workshop.Cli;

if (args.Length >= 2 && args[0] is "validate" or "report" or "stats")
{
    var json = args.Contains("--json");
    var files = args.Skip(1).Where(a => a != "--json").ToList();
    if (files.Count == 1)
    {
        return args[0] switch
        {
            "validate" => ValidateCommand.Run(files[0], json, Console.Out, Console.Error),
            "report" => ReportCommand.Run(files[0], json, Console.Out, Console.Error),
            _ => StatsCommand.Run(files[0], json, Console.Out, Console.Error),
        };
    }
}

if (args.Length >= 3 && args[0] == "find")
{
    var json = args.Contains("--json");
    var positional = args.Skip(1).Where(a => a != "--json").ToList();
    if (positional.Count == 2)
    {
        return FindCommand.Run(positional[0], positional[1], json, Console.Out, Console.Error);
    }
}

if (args.Length >= 3 && args[0] == "export")
{
    var what = args.FirstOrDefault(a => a is "--parameters" or "--containers") ?? "";
    var positional = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
    if (positional.Count is 1 or 2)
    {
        return ExportCommand.Run(positional[0], what, positional.ElementAtOrDefault(1), Console.Out, Console.Error);
    }
}

Console.Error.WriteLine("usage: xtce-workshop validate|report|stats [--json] <file.xml>");
Console.Error.WriteLine("       xtce-workshop find [--json] <file.xml> <name-or-glob>");
Console.Error.WriteLine("       xtce-workshop export --parameters|--containers <file.xml> [out.csv]");
return ValidateCommand.ExitError;
