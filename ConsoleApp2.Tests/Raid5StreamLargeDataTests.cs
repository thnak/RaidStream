using RaidStream;

namespace ConsoleApp2.Tests;

using System.Security.Cryptography;
using Xunit;

public class Raid5StreamLargeDataTests
{
    private IList<Stream> CreateDisks(int numDisks, int diskSize)
    {
        var disks = new List<Stream>();
        for (int i = 0; i < numDisks; i++)
            disks.Add(new MemoryStream(new byte[diskSize]));
        return disks;
    }

    [Fact]
    public void LargeWriteRead_WithDiskFailure_HashShouldMatch()
    {
        for (int i = 0; i < 100; i++)
            RunWithRandomSize();
    }

    [Fact]
    public async Task LargeWriteRead_WithDiskFailure_HashShouldMatchAsync()
    {
        for (int i = 0; i < 100; i++)
            await RunWithRandomSizeAsync();
    }

    private void RunWithRandomSize()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long memoryBefore = GC.GetTotalMemory(true);

        int numDisks = Random.Shared.Next(3, 10);
        int diskSize = Random.Shared.Next(2 * 1024 * 1024, 10 * 1024 * 1024);

        // Logging the parameters for debugging
        Console.WriteLine($"Running test with {numDisks} disks, each of size {diskSize} bytes.");

        int stripeUnitSize = 4096;
        int dataSize = diskSize;

        var disks = CreateDisks(numDisks, diskSize);
        using var raid = new Raid5Stream(disks, stripeUnitSize);

        byte[] data = new byte[dataSize];
        new Random(12345).NextBytes(data);

        // Write data
        raid.Write(data, 0, data.Length);

        // Compute hash of original data
        byte[] originalHash;
        using (var sha = SHA256.Create())
            originalHash = sha.ComputeHash(data);

        // Simulate disk failure
        raid.Position = 0;
        raid.FailDisk(Random.Shared.Next(0, numDisks));

        // Read back data
        byte[] readBack = new byte[dataSize];
        int bytesRead = raid.Read(readBack, 0, readBack.Length);

        // Compute hash of read data
        byte[] readHash;
        using (var sha = SHA256.Create())
            readHash = sha.ComputeHash(readBack);

        Assert.Equal(data.Length, bytesRead);
        Assert.Equal(originalHash, readHash);

        stopwatch.Stop();
        long memoryAfter = GC.GetTotalMemory(false);
        Console.WriteLine($"RunWithRandomSize: Elapsed: {stopwatch.ElapsedMilliseconds} ms, Memory Allocated: {memoryAfter - memoryBefore} bytes");
    }

    private async Task RunWithRandomSizeAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long memoryBefore = GC.GetTotalMemory(true);

        int numDisks = Random.Shared.Next(3, 10);
        int diskSize = Random.Shared.Next(2 * 1024 * 1024, 10 * 1024 * 1024);

        // Logging the parameters for debugging
        Console.WriteLine($"Running test with {numDisks} disks, each of size {diskSize} bytes.");

        int stripeUnitSize = 4096;
        int dataSize = diskSize;

        var disks = CreateDisks(numDisks, diskSize);
        await using var raid = new Raid5Stream(disks, stripeUnitSize);

        byte[] data = new byte[dataSize];
        new Random(12345).NextBytes(data);

        // Write data
        await raid.WriteAsync(data, 0, data.Length);

        // Compute hash of original data
        byte[] originalHash;
        using (var sha = SHA256.Create())
            originalHash = sha.ComputeHash(data);

        // Simulate disk failure
        raid.Position = 0;
        raid.FailDisk(Random.Shared.Next(0, numDisks));

        // Read back data
        byte[] readBack = new byte[dataSize];
        int bytesRead = await raid.ReadAsync(readBack, 0, readBack.Length);

        // Compute hash of read data
        byte[] readHash;
        using (var sha = SHA256.Create())
            readHash = sha.ComputeHash(readBack);

        Assert.Equal(data.Length, bytesRead);
        Assert.Equal(originalHash, readHash);

        stopwatch.Stop();
        long memoryAfter = GC.GetTotalMemory(false);
        Console.WriteLine($"RunWithRandomSizeAsync: Elapsed: {stopwatch.ElapsedMilliseconds} ms, Memory Allocated: {memoryAfter - memoryBefore} bytes");
    }
}

