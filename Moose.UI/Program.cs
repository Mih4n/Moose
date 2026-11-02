using Microsoft.Extensions.DependencyInjection;
using Moose.Core;
using Moose.Core.Settings;
using Moose.Core.Terminal.Sessions;
using Moose.UI;
using Moose.Graphics.Hosting;

var arguments = StartupSession.Parse(args);
if (arguments.Help)
{
    Console.WriteLine(StartupSession.Usage);
    return 0;
}

var builder = MooseApp.CreateBuilder(arguments.Rest);
builder
    .AddEnvironmentOptions()
    .AddTomlConfiguration()
    .AddTerminal();
var windowOptions = WindowOptions.Load();
builder.Window(window => window
    .Title(windowOptions.Title)
    .Size(1024, 768)
    .Decorations(windowOptions.Decorations)
    .Libdecor(windowOptions.Libdecor));

var sessionOptions = SessionOptions.Load();
StartupSession startup;
try
{
    startup = StartupSession.Resolve(arguments, new SessionStore(EnvironmentOptions.Load()), sessionOptions);
}
catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"moose: {exception.Message}");
    return 1;
}

builder.Services.AddSingleton(startup);
builder.Services.AddSingleton<RootView>();
builder.Services.AddSingleton<DebugBounds>();

var app = builder.Build();

app.UseEffect<DebugBounds>();

app.Run<RootView>();

// closing the last tab closes the window too, and then there's nothing left to remember
if (sessionOptions.SaveOnExit) app.Services.GetRequiredService<RootView>().SaveLastSession();
return 0;
