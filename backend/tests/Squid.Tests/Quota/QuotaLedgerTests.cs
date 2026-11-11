using System.Net;
using Microsoft.Extensions.Configuration;
using Squid.Api.Media;
using Squid.Persistence;
using Squid.Service;
using Squid.Tests.Feed;
using Squid.Tests.Media;
using Squid.Tests.Projections;

namespace Squid.Tests.Quota;

/// <summary>
/// AC-03 of TASK-0028 (data-model §2.13, R-19): <c>UsedBytes</c>/<c>LimitBytes</c> are INTEGER
/// columns, the limit defaults to 10 GiB when nothing is configured — both in the configuration
/// layer and in the versioned migration — and the ledger row is BORN on the first use: delete it
/// and the next accepted upload creates it, carrying the configured limit and the stored size.
/// </summary>
[Collection("ServiceHost")]
public sealed class QuotaLedgerTests : IClassFixture<QuotaHostFixture>
{
    private readonly QuotaHostFixture _host;

    public QuotaLedgerTests(QuotaHostFixture host) => _host = host;

    [Fact] // AC-03 — R-19 integer columns + default 10 GiB without configuration
    public async Task Ac03_ledger_columns_are_integer_and_the_default_limit_is_10_gib()
    {
        // Configuration layer: no Quota:LimitBytes anywhere → the 10 GiB of data-model §2.13.
        Assert.Equal(10_737_418_240L, MediaOptions.From(new ConfigurationBuilder().Build()).QuotaLimitBytes);

        // Storage layer: a brand-new database carries INTEGER columns (R-19: bytes are integers,
        // never REAL/TEXT) and the same default the migration seeds.
        var file = ProjectionDb.NewFile();
        try
        {
            await SchemaMigrator.ApplyAsync(ProjectionDb.ConnectionOf(file), SchemaMigrations.Load());

            var types = await ProjectionDb.QueryAsync<string>(file,
                "SELECT type FROM pragma_table_info('quota_ledger') WHERE name IN ('used_bytes', 'limit_bytes')");
            Assert.Equal(2, types.Count);
            Assert.All(types, type => Assert.Equal("INTEGER", type));
            Assert.Equal(10_737_418_240L, await ProjectionDb.ScalarAsync<long>(file,
                "SELECT limit_bytes FROM quota_ledger WHERE id = 1"));
        }
        finally
        {
            ProjectionDb.TryDelete(file);
        }
    }

    [Fact] // AC-03 — a linha nasce no primeiro uso
    public async Task Ac03_the_ledger_row_is_born_on_the_first_use()
    {
        await QuotaHarness.ExecuteAsync(_host.StoragePath, "DELETE FROM quota_ledger");
        Assert.Equal(0, await ProjectionDb.ScalarAsync<long>(
            _host.StoragePath, "SELECT COUNT(*) FROM quota_ledger"));

        var (client, _) = await QuotaHarness.SignedInAsync(_host, "q03");
        var gif = MediaHarness.MinimalGif();
        var response = await QuotaHarness.UploadAsync(client, gif, "primeiro uso da cota", "image/gif", "primeiro.gif");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var size = (await FeedHarness.JsonOfAsync(response)).GetProperty("sizeBytes").GetInt64();

        Assert.Equal(1, await ProjectionDb.ScalarAsync<long>(
            _host.StoragePath, "SELECT COUNT(*) FROM quota_ledger"));
        Assert.Equal(size, await QuotaHarness.UsedAsync(_host.StoragePath));
        // Born with the CONFIGURED limit of this host, not with a hard-coded one.
        Assert.Equal(QuotaHarness.LimitBytes, await QuotaHarness.LimitAsync(_host.StoragePath));
        Assert.False(string.IsNullOrEmpty(await QuotaHarness.UpdatedAtAsync(_host.StoragePath)));
    }
}
