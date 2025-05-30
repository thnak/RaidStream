using RaidStream;

namespace ConsoleApp2.Tests;

public class UnitTest1
{
    private IList<Stream> CreateDisks(int numDisks, int diskSize)
    {
        var disks = new List<Stream>();
        for (int i = 0; i < numDisks; i++)
        {
            disks.Add(new MemoryStream(new byte[diskSize]));
        }

        return disks;
    }

    [Fact]
    public void WriteAndRead_ShouldReturnSameData()
    {
        var disks = CreateDisks(3, 1024);
        using var raid = new Raid5Stream(disks, 128);

        byte[] data = new byte[256];
        new Random(42).NextBytes(data);

        raid.Write(data, 0, data.Length);
        raid.Position = 0;

        byte[] readBack = new byte[256];
        int bytesRead = raid.Read(readBack, 0, readBack.Length);

        Assert.Equal(data.Length, bytesRead);
        Assert.Equal(data, readBack);
    }

    [Fact]
    public void ReadAfterDiskFailure_ShouldReconstructData()
    {
        var disks = CreateDisks(4, 2048);
        using var raid = new Raid5Stream(disks, 256);

        byte[] data = new byte[512];
        new Random(99).NextBytes(data);

        raid.Write(data, 0, data.Length);
        raid.Position = 0;

        // Simulate disk failure
        raid.FailDisk(1);

        byte[] readBack = new byte[512];
        int bytesRead = raid.Read(readBack, 0, readBack.Length);

        Assert.Equal(data.Length, bytesRead);
        Assert.Equal(data, readBack);
    }

    [Fact]
    public void WriteToFailedDisk_ShouldThrow()
    {
        var disks = CreateDisks(3, 1024);
        using var raid = new Raid5Stream(disks, 128);

        byte[] data = new byte[128];
        raid.FailDisk(0);

        Assert.Throws<IOException>(() => raid.Write(data, 0, data.Length));
    }

    [Fact]
    public void RecoverDisk_ShouldAllowWritesAgain()
    {
        var disks = CreateDisks(3, 1024);
        using var raid = new Raid5Stream(disks, 128);

        byte[] data = new byte[128];
        raid.FailDisk(0);

        Assert.Throws<IOException>(() => raid.Write(data, 0, data.Length));

        raid.RecoverDisk(0);
        raid.Write(data, 0, data.Length); // Should not throw
    }
}