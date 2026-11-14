// Console client that stands in for a remote instance: the federation path is exercised against
// its failure scenarios without a real peer (SPEC-0011 section G — stub since TASK-0002, scenarios
// since TASK-0046). One scenario per run:
//   dotnet run --project backend/src/Squid.PeerClient -- --scenario=500 [--delay=3000]
// --delay only feeds the slow scenario (ms); --help prints the usage; Ctrl+C stops the server.
using System.Globalization;
using Squid.PeerClient;

var scenarioName = "ok";
var delayMs = PeerScenarioServer.DefaultSlowDelayMs;
foreach (var arg in args)
{
    var parts = arg.Split('=', 2);
    switch (parts[0])
    {
        case "--scenario" when parts.Length == 2:
            scenarioName = parts[1];
            break;
        case "--delay" when parts.Length == 2:
            delayMs = int.Parse(parts[1], CultureInfo.InvariantCulture);
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Squid.PeerClient: unknown argument '{arg}'");
            PrintUsage();
            return 1;
    }
}

if (!TryScenario(scenarioName, out var scenario))
{
    Console.Error.WriteLine($"Squid.PeerClient: unknown scenario '{scenarioName}'");
    PrintUsage();
    return 1;
}

await using var server = PeerScenarioServer.Start(scenario, TimeSpan.FromMilliseconds(delayMs));
Console.WriteLine(
    $"Squid.PeerClient: scenario '{scenarioName}' listening on {server.Origin} "
    + "(deliveries to {origin}/users/<name>/inbox; Ctrl+C to stop)");

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stopped.TrySetResult();
await stopped.Task;
return 0;

// The CLI spelling of each scenario — the enum name is for code, these tokens are for humans.
static bool TryScenario(string name, out PeerScenario scenario)
{
    switch (name.ToLowerInvariant())
    {
        case "ok":
        case "202":
            scenario = PeerScenario.Ok;
            return true;
        case "500":
            scenario = PeerScenario.ServerError;
            return true;
        case "401":
            scenario = PeerScenario.Unauthorized;
            return true;
        case "404":
            scenario = PeerScenario.NotFound;
            return true;
        case "timeout":
            scenario = PeerScenario.Timeout;
            return true;
        case "slow":
            scenario = PeerScenario.Slow;
            return true;
        default:
            scenario = default;
            return false;
    }
}

static void PrintUsage() => Console.WriteLine(
    "usage: Squid.PeerClient --scenario=ok|500|401|404|timeout|slow [--delay=3000] [--help]");
