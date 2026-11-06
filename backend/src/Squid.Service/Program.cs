using Squid.Api;

// Composition root of the service process: silo + API in one host (doc 05 §11).
// The writer of this file is the only place allowed to know every layer at once.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();
app.MapSquidApi();

await app.RunAsync();

// Exposes the implicit Program class to the test assembly.
public partial class Program;