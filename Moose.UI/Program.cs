using Moose.Core;
using Moose.Graphics;
using Microsoft.Extensions.Hosting;

var context = new GraphicsContex();
context.Run();

var builder = Host.CreateApplicationBuilder(args);
var configuration = builder.Configuration;
var services = builder.Services;

builder.AddEnvironmentOptions();
builder.AddTomlConfiguration();

var app = builder.Build();

app.Run();
