using Xtce.Workshop.Cli;

if (args.Length >= 2 && args[0] is "validate" or "report")
{
    var json = args.Contains("--json");
    var files = args.Skip(1).Where(a => a != "--json").ToList();
    if (files.Count == 1)
    {
        return args[0] == "validate"
            ? ValidateCommand.Run(files[0], json, Console.Out, Console.Error)
            : ReportCommand.Run(files[0], json, Console.Out, Console.Error);
    }
}

Console.Error.WriteLine("usage: xtce-workshop validate|report [--json] <file.xml>");
return ValidateCommand.ExitError;
