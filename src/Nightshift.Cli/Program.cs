using Nightshift.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return await new CliApplication(Console.Out, Console.Error, Environment.CurrentDirectory).RunAsync(args, cancellation.Token);
