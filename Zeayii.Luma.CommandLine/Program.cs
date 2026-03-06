using System.CommandLine;
using Zeayii.Luma.CommandLine.Commands;

var rootCommand = new RootCommand("Luma Command Line");
rootCommand.AddGeneratedLumaCommands();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync().ConfigureAwait(false);

