using Moose;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var configuration = builder.Configuration;
var services = builder.Services;

builder.AddEnvironmentOptions();
builder.AddTomlConfiguration();

var app = builder.Build();

app.Run();
