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
builder.Window(window => window
    .Title(Configuration.String(Keys.Window.Title, "Moose"))
    .Size(1024, 768));

var app = builder.Build();

app.UseEffect<DebugBounds>();

app.Run<RootView>();
