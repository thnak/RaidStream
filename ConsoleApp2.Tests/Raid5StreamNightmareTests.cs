using System.Security.Cryptography;
using RaidStream;

namespace ConsoleApp2.Tests;

public class Raid5StreamNightmareTests
{
    [Fact]
    public void NightmareScenario_MultipleFailuresAndEdgeCases()
    {
        var rand = new Random(98765);
        int numDisks = rand.Next(4, 8);
        int stripeUnitSize = rand.Next(512, 8192);
        var diskSizes = new int[numDisks];
        for (int i = 0; i < numDisks; i++)
        {
            // Some disks are just big enough, some are much larger, some are tiny
            diskSizes[i] = (i % 2 == 0)
                ? rand.Next(stripeUnitSize * 2, stripeUnitSize * 10)
                : rand.Next(stripeUnitSize * 10, stripeUnitSize * 100);
        }

        // Logical length is based on the smallest disk
        int minDisk = diskSizes.Min();
        int numDataDisks = numDisks - 1;
        long logicalLength = (minDisk / stripeUnitSize) * stripeUnitSize * numDataDisks;

        // Create disks
        var disks = new List<Stream>();
        for (int i = 0; i < numDisks; i++)
            disks.Add(new MemoryStream(new byte[diskSizes[i]]));

        using var raid = new Raid5Stream(disks, stripeUnitSize);

        // Write data that exactly fills the logical RAID length
        byte[] data = new byte[logicalLength];
        rand.NextBytes(data);
        raid.Write(data, 0, data.Length);

        // Hash for later comparison
        byte[] originalHash;
        using (var sha = SHA256.Create())
            originalHash = sha.ComputeHash(data);

        // Try to write past the end (should auto-extend or throw)
        Assert.ThrowsAny<Exception>(() => raid.Write(new byte[1], 0, 1));

        // Fail a random disk, read back, check hash
        raid.Position = 0;
        int fail1 = rand.Next(0, numDisks);
        raid.FailDisk(fail1);
        byte[] readBack1 = new byte[data.Length];
        int bytesRead1 = raid.Read(readBack1, 0, readBack1.Length);
        using (var sha = SHA256.Create())
            Assert.Equal(originalHash, sha.ComputeHash(readBack1));

        // Recover disk, fail another, check again
        raid.RecoverDisk(fail1);
        int fail2;
        do { fail2 = rand.Next(0, numDisks); } while (fail2 == fail1);
        raid.FailDisk(fail2);
        raid.Position = 0;
        byte[] readBack2 = new byte[data.Length];
        int bytesRead2 = raid.Read(readBack2, 0, readBack2.Length);
        using (var sha = SHA256.Create())
            Assert.Equal(originalHash, sha.ComputeHash(readBack2));

        // Fail a second disk (should throw)
        int fail3;
        do { fail3 = rand.Next(0, numDisks); } while (fail3 == fail1 || fail3 == fail2);
        raid.FailDisk(fail3);
        raid.Position = 0;
        Assert.Throws<IOException>(() => raid.Read(new byte[data.Length], 0, data.Length));
    }
}