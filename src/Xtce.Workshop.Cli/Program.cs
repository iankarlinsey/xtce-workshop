using Xtce.Workshop.Cli;

if (args.Length >= 2 && args[0] == "validate")
{
    var json = args.Contains("--json");
    var files = args.Skip(1).Where(a => a != "--json").ToList();
    if (files.Count == 1)
    {
        return ValidateCommand.Run(files[0], json, Console.Out, Console.Error);
    }
}

Console.Error.WriteLine("usage: xtce-workshop validate [--json] <file.xml>");
return ValidateCommand.ExitError;
