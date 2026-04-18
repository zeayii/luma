using System.CommandLine;
using Zeayii.Luma.CommandLine.Commands;
using Zeayii.Luma.CommandLine.Commands.Root;

var rootCommand = new RootCommand("Luma Command Line");
var rootOptions = RootCommandOptions.Create();
rootCommand.ApplyRootOptions(rootOptions);
rootCommand.AddGeneratedLumaCommands();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync().ConfigureAwait(false);
