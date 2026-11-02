using Microsoft.Extensions.DependencyInjection;
using Moose.Core;
using Moose.Core.Settings;
using Moose.Core.Terminal.Sessions;
using Moose.UI;
using Moose.Graphics.Hosting;

var builder = MooseApp.CreateBuilder(args);
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

builder.Services.AddSingleton<RootView>();
builder.Services.AddSingleton<DebugBounds>();

var app = builder.Build();

app.UseEffect<DebugBounds>();

app.Run<RootView>();
