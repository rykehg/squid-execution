using Squid.Service;

// Composition root of the service process: silo + API in one host (doc 05 section 11).
// The wiring lives in SquidServiceHost so the tests start this exact application.
var app = SquidServiceHost.Build(args);

await app.RunAsync();

// Exposes the implicit Program class to the test assembly.
public partial class Program;
