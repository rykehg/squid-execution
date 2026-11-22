using Microsoft.Extensions.DependencyInjection;
using Squid.Service;
using Squid.Worker.Backup;

// Composition root of the service process: silo + API in one host (doc 05 section 11).
// The wiring lives in SquidServiceHost so the tests start this exact application.

// TASK-0056 (SPEC-0012 section D): `--restore` runs contract backup-recovery section 4 over the
// configured destination and volume and exits with its verdict — the operator's entry point the
// runbook restaurar-backup.md documents. The flag is pulled out before the host reads the
// arguments (a bare switch is not a configuration key), and the silo is built but never started:
// the restore needs the wiring, not the serving process.
var restoreMode = Array.Exists(args, argument =>
    string.Equals(argument, "--restore", StringComparison.OrdinalIgnoreCase));
var hostArgs = restoreMode
    ? args.Where(argument => !string.Equals(argument, "--restore", StringComparison.OrdinalIgnoreCase)).ToArray()
    : args;

var app = SquidServiceHost.Build(hostArgs);

if (restoreMode)
{
    var restore = await app.Services.GetRequiredService<RestorePipeline>().RunAsync();
    if (restore.Succeeded)
    {
        Console.WriteLine(
            $"restore: ok — envelopes={restore.Report!.Envelopes} eventos={restore.Report.Events} "
            + $"agregados={restore.Report.Aggregates} seqTo={restore.Report.SeqTo}");
        return 0;
    }

    Console.Error.WriteLine($"restore: falhou — {restore.Error}");
    return 1;
}

await app.RunAsync();
return 0;

// Exposes the implicit Program class to the test assembly.
public partial class Program;
