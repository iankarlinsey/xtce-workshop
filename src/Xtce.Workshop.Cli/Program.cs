using Xtce.Workshop.Cli;

if (args.Length == 1 && args[0] is "--version" or "version")
{
    Console.WriteLine($"xtce-workshop {BuildInfo.Version}");
    return 0;
}

if (args.Length >= 2 && args[0] is "validate" or "report" or "stats")
{
    var json = args.Contains("--json");
    string? outPath = null;
    var rest = new List<string>();
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--json")
        {
            continue;
        }
        if (args[i] == "--out" && i + 1 < args.Length)
        {
            outPath = args[++i];
            continue;
        }
        rest.Add(args[i]);
    }
    if (rest.Count == 1 && (outPath is null || args[0] == "report"))
    {
        return args[0] switch
        {
            "validate" => ValidateCommand.Run(rest[0], json, Console.Out, Console.Error),
            "report" => ReportCommand.Run(rest[0], json, Console.Out, Console.Error, outPath),
            _ => StatsCommand.Run(rest[0], json, Console.Out, Console.Error),
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

Console.Error.WriteLine("usage: xtce-workshop validate|stats [--json] <file.xml>");
Console.Error.WriteLine("       xtce-workshop report [--json] [--out <report-file>] <file.xml>");
Console.Error.WriteLine("       xtce-workshop find [--json] <file.xml> <name-or-glob>");
Console.Error.WriteLine("       xtce-workshop export --parameters|--containers <file.xml> [out.csv]");
return ValidateCommand.ExitError;
