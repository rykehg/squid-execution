using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Squid.Worker.Backup;

namespace Squid.Tests.Backup;

/// <summary>
/// AC-01 and AC-02 of TASK-0057: the five operations of contract backup-recovery section 1 over
/// <see cref="S3CompatibleTarget"/> against the in-process fake — Put lands ciphertext + header,
/// Get hands the bytes back, ListAsync is the discovery (paged, ordered, honouring fromSeq),
/// Exists and Delete close the contract — every request signed by the hand-written SigV4 client
/// over <c>Backup:S3:*</c> with zero new packages (AC-01). The fake re-derives the signature
/// itself, so an unsigned request, a wrong signature, a replayed path and a body its payload hash
/// does not cover are refused before any object moves (AC-02) — loopback only, no external
/// network in the gate.
/// </summary>
public sealed class S3CompatibleTargetTests
{
    private const string AccessKey = "AKIA0TASK0057S3A";
    private const string SecretKey = "task-0057-s3-secret-key-value";

    [Fact] // AC-01
    public async Task Ac01_The_five_operations_run_over_the_configured_s3_destination()
    {
        await using var s3 = await FakeS3.StartAsync("squid-backup", AccessKey, SecretKey);

        // The destination exists only through Backup:S3:* (AC-05): complete section → target,
        // incomplete section → no off-site destination at all, and the credential comes from
        // that configuration and nowhere else.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Backup:S3:Endpoint"] = s3.Endpoint,
                ["Backup:S3:Region"] = s3.Options.Region,
                ["Backup:S3:Bucket"] = s3.Bucket,
                ["Backup:S3:AccessKey"] = AccessKey,
                ["Backup:S3:SecretKey"] = SecretKey,
            }).Build();
        Assert.NotNull(BackupS3Options.From(configuration));
        Assert.Null(BackupS3Options.From(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Backup:S3:Endpoint"] = s3.Endpoint }).Build()));

        using var target = new S3CompatibleTarget(BackupS3Options.From(configuration)!);
        Assert.Equal("s3-offsite", target.TargetId);

        var ciphertext = Encoding.UTF8.GetBytes("ciphertext-only-0123456789");
        var first = Envelope("bk-2026-11-22T0300Z-aaaa1111", seqFrom: 10, seqTo: 20);
        await using (var source = new MemoryStream(ciphertext))
        {
            await target.PutAsync(first, source, CancellationToken.None);
        }

        // Both halves landed in the bucket — the destination sees ciphertext + envelope only.
        Assert.Equal(new[] { first.Id + ".bin", first.Id + ".json" }, s3.Keys);

        Assert.True(await target.ExistsAsync(first.Id, CancellationToken.None));
        await using (var reRead = await target.GetAsync(first.Id, CancellationToken.None))
        {
            using var buffer = new MemoryStream();
            await reRead.CopyToAsync(buffer);
            Assert.Equal(ciphertext, buffer.ToArray());
        }

        // A second, older envelope: ListAsync is ordered by seqTo and filters by fromSeq. Four
        // objects over a fake paging at 2 — the discovery only sees both if the client follows
        // every NextContinuationToken (a truncated listing would silently hide an envelope).
        var second = Envelope("bk-2026-11-22T0301Z-bbbb2222", seqFrom: 0, seqTo: 5);
        await using (var source = new MemoryStream(ciphertext))
        {
            await target.PutAsync(second, source, CancellationToken.None);
        }

        var listed = await target.ListAsync(0, CancellationToken.None);
        Assert.Equal(new[] { second.Id, first.Id }, listed.Select(envelope => envelope.Id));

        var found = listed[1];
        Assert.Equal(first.KeyEpoch, found.KeyEpoch);
        Assert.Equal(first.SeqFrom, found.SeqFrom);
        Assert.Equal(first.SeqTo, found.SeqTo);
        Assert.Equal(first.ContentHash, found.ContentHash);
        Assert.Equal(first.SizeBytes, found.SizeBytes);
        Assert.Equal(first.TargetId, found.TargetId);
        Assert.Equal(first.CreatedAt, found.CreatedAt);

        Assert.Single(await target.ListAsync(first.SeqTo, CancellationToken.None));
        Assert.Empty(await target.ListAsync(first.SeqTo + 1, CancellationToken.None));

        await target.DeleteAsync(first.Id, CancellationToken.None);
        Assert.False(await target.ExistsAsync(first.Id, CancellationToken.None));
        Assert.Single(await target.ListAsync(0, CancellationToken.None));

        // Delete is idempotent: rotation/purge may repeat it (contract section 7).
        await target.DeleteAsync(first.Id, CancellationToken.None);

        // Every one of those requests carried a signature the fake validated — the positive
        // half of AC-02: a client that signs wrong gets nothing done.
        Assert.True(s3.RequestCount >= 10, "the five operations really went over HTTP to the fake");
        Assert.All(s3.Requests, request =>
            Assert.StartsWith("AWS4-HMAC-SHA256 ", request.Headers["authorization"], StringComparison.Ordinal));
    }

    [Fact] // AC-02
    public async Task Ac02_The_fake_s3_refuses_what_the_signature_does_not_cover()
    {
        await using var s3 = await FakeS3.StartAsync("squid-backup", AccessKey, SecretKey);
        using var target = new S3CompatibleTarget(s3.Options);

        var envelope = Envelope("bk-2026-11-22T0302Z-cccc3333", seqFrom: 0, seqTo: 3);
        var ciphertext = Encoding.UTF8.GetBytes("ciphertext-to-protect");
        await using (var source = new MemoryStream(ciphertext))
        {
            await target.PutAsync(envelope, source, CancellationToken.None);
        }

        var signed = s3.Requests[^1]; // the real client's signed header PUT
        var storedHeader = s3.Read(envelope.Id + ".json")!;
        var storedCiphertext = s3.Read(envelope.Id + ".bin")!;
        using var raw = new HttpClient { BaseAddress = new Uri(s3.Endpoint) };

        // (a) no signature at all → refused before anything else is looked at.
        var unsigned = await raw.GetAsync($"/{s3.Bucket}/{envelope.Id}.bin");
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        Assert.Contains("SignatureDoesNotMatch", await unsigned.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // (b) a wrong signature: one hex digit of the real one flipped → still refused.
        var wrong = await raw.SendAsync(Replay(signed, authorization: Flip(signed.Headers["authorization"])));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Contains("SignatureDoesNotMatch", await wrong.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // (c) the signature binds the path: replayed over the object it never signed → refused.
        var moved = await raw.SendAsync(Replay(signed, path: $"/{s3.Bucket}/{envelope.Id}.bin"));
        Assert.Equal(HttpStatusCode.Forbidden, moved.StatusCode);

        // (d) the payload hash binds the body: same signature, one byte changed → 400 BadDigest.
        var tampered = signed.Body.ToArray();
        tampered[0] ^= 0xFF;
        var retyped = await raw.SendAsync(Replay(signed, body: tampered));
        Assert.Equal(HttpStatusCode.BadRequest, retyped.StatusCode);
        Assert.Contains("BadDigest", await retyped.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Nothing any of the four refused moved an object: the destination is byte-identical.
        Assert.Equal(storedHeader, s3.Read(envelope.Id + ".json"));
        Assert.Equal(storedCiphertext, s3.Read(envelope.Id + ".bin"));
        Assert.Equal(2, s3.Keys.Count);
    }

    /// <summary>One stored envelope of the contract section 2 shape, over the S3 destination.</summary>
    private static BackupEnvelope Envelope(string id, long seqFrom, long seqTo) => new()
    {
        Id = id,
        KeyEpoch = 1,
        SeqFrom = seqFrom,
        SeqTo = seqTo,
        ContentHash = "sha256:001122",
        SizeBytes = 30,
        TargetId = "s3-offsite",
        CreatedAt = new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero),
    };

    /// <summary>
    /// Replays a captured request byte-for-byte — same signed headers, same body — with at most
    /// one field changed, so each tamper below isolates exactly what the signature covers.
    /// </summary>
    private static HttpRequestMessage Replay(
        FakeS3.CapturedRequest source, string? authorization = null, string? path = null, byte[]? body = null)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(source.Method), (path ?? source.Path) + source.Query);
        foreach (var header in source.Headers)
        {
            // Content-* is recomputed by HttpClient and Host comes from the same endpoint; the
            // rest (x-amz-*, Authorization) rides exactly as the client signed it. An overridden
            // Authorization is never COPIED too — two values would let the fake read the valid
            // signature first and accept the tampered replay.
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                || (authorization is not null
                    && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        var payload = body ?? source.Body;
        if (payload.Length > 0)
        {
            request.Content = new ByteArrayContent(payload);
        }

        return request;
    }

    /// <summary>Flips the first hex digit of the signature so the value can never match.</summary>
    private static string Flip(string authorization) =>
        System.Text.RegularExpressions.Regex.Replace(
            authorization, @"Signature=([0-9a-f])",
            match => "Signature=" + (match.Groups[1].Value == "0" ? "1" : "0"));
}
