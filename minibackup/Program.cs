using minibackup;

//var (sourcePath, destinationPath) = GetCLIArgs(args);
var (sourcePath, destinationPath) = ("C:\\Users\\Ryszard\\temp\\src", "C:\\Users\\Ryszard\\temp\\dest");
var orchestrator = new Orchestrator(sourcePath, destinationPath);
await orchestrator.RunAsync();

static (string SourcePath, string DestinationPath) GetCLIArgs(string[] args)
{
    if (args.Length != 2)
    {
        throw new InvalidOperationException("Program requires exactly two arguments: minibackup SourcePath DestinationPath");
    }

    if (string.IsNullOrWhiteSpace(args[0]))
    {
        throw new InvalidOperationException("Source path cannot be null nor whitespace");
    }

    if (string.IsNullOrWhiteSpace(args[1]))
    {
        throw new InvalidOperationException("Destination path cannot be null nor whitespace");
    }

    return (args[0], args[1]);
}