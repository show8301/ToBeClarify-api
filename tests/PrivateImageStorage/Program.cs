using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Models.Media;
using ToBeClarify.Api.Services.Media;

var root = Path.Combine(Path.GetTempPath(), "art-files-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool value) { if (!value) throw new Exception("Assertion failed."); checks++; }
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { checks++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
try
{
    var environment = new TestEnvironment { ContentRootPath = Path.Combine(root, "api") };
    var options = new MediaOptions { PrivateRootPath = Path.Combine(root, "media") };
    var disk = new MediaDiskStorage(Options.Create(options), environment);
    var delivery = Guid.NewGuid().ToString();
    var asset = Guid.NewGuid().ToString();
    var bytes = RandomNumberGenerator.GetBytes(9_957_796);
    var stored = await disk.WritePrivate("delivery", asset, bytes, default, delivery);
    Check(stored.StoragePath == $"art-deliveries/{delivery}/{asset}.png");
    Check((await disk.ReadPrivate(stored, default)).SequenceEqual(bytes));
    Check((await disk.WritePrivate("delivery", asset, bytes, default, delivery)) == stored);
    Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any());
    var legacy = new PrivateImageRow { LegacyBytes = bytes };
    Check((await legacy.Read(disk, default)).SequenceEqual(bytes));
    var calls = 0;
    var restored = await PrivateImageMigration.ReadChunks(bytes.LongLength, (offset, count) =>
    {
        Check(count <= 1024 * 1024 && offset == calls * 1024 * 1024);
        calls++;
        return Task.FromResult(bytes.AsSpan(offset, count).ToArray());
    }, default);
    Check(calls == 10 && SHA256.HashData(restored).SequenceEqual(SHA256.HashData(bytes)));
    await Reject<InvalidDataException>(() => PrivateImageMigration.ReadChunks(10, (_, _) => Task.FromResult(new byte[9]), default));
    await Reject<InvalidDataException>(() => PrivateImageMigration.ReadChunks(13 * 1024 * 1024, (_, _) => throw new Exception(), default));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Reject<OperationCanceledException>(() => PrivateImageMigration.ReadChunks(10, (_, _) => throw new Exception(), cancelled.Token));
    await Reject<InvalidDataException>(() => disk.ReadPrivate(stored with { StoragePath = "../outside.png" }, default));
    await Reject<InvalidDataException>(() => disk.ReadPrivate(stored with { Sha256 = new string('0', 64) }, default));
    await Reject<InvalidDataException>(() => disk.WritePrivate("delivery", asset, new byte[] { 1 }, default, delivery));
    Check((await disk.ReadPrivate(stored, default)).SequenceEqual(bytes));
    var file = Path.Combine(disk.PrivateRoot, stored.StoragePath);
    await File.WriteAllBytesAsync(file, new byte[] { 1 });
    await Reject<InvalidDataException>(() => disk.ReadPrivate(stored, default));
    options.PrivateRootPath = environment.ContentRootPath;
    await Reject<InvalidOperationException>(() => disk.WritePrivate("delivery", asset, bytes, default, delivery));
    options.PrivateRootPath = "relative";
    await Reject<InvalidOperationException>(() => disk.WritePrivate("delivery", asset, bytes, default, delivery));
    Console.WriteLine($"PASS: {checks} checks (disk IO, immutable retry, corruption, confinement, legacy fallback, 9,957,796-byte chunk reconstruction).");
}
finally
{
    // Only delete the unique test-owned directory created above.
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test root.");
    Directory.Delete(root, recursive: true);
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "StorageTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
