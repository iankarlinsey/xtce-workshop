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

Console.Error.WriteLine("usage: xtce-workshop validate|report|stats [--json] <file.xml>");
return ValidateCommand.ExitError;
