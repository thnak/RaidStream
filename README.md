# RaidStream
[![NuGet](https://img.shields.io/nuget/v/RaidStream.svg)](https://www.nuget.org/packages/RaidStream/)

A .NET implementation of a RAID 5 stream. This library allows you to combine multiple streams (representing physical disks) into a single fault-tolerant stream with RAID 5 parity. It supports reading, writing, seeking, disk failure simulation, and recovery.

## Features
- RAID 5 parity with N disks (minimum 3)
- Read, write, and seek support
- Simulate disk failure and recovery
- Automatic data/parity reconstruction on read
- .NET 9.0 compatible

## Getting Started

### Installation
Add the project to your solution or reference the NuGet package (if published).

### Usage Example
```csharp
using RaidStream;
using System.IO;

// Create 3 or more streams (e.g., MemoryStream, FileStream)
var disk1 = new MemoryStream(new byte[1024 * 1024]);
var disk2 = new MemoryStream(new byte[1024 * 1024]);
var disk3 = new MemoryStream(new byte[1024 * 1024]);
var disks = new List<Stream> { disk1, disk2, disk3 };

int stripeUnitSize = 4096; // 4KB per disk per stripe
using var raid5 = new Raid5Stream(disks, stripeUnitSize);

// Write data
byte[] data = new byte[10000];
new Random().NextBytes(data);
raid5.Write(data, 0, data.Length);
raid5.Seek(0, SeekOrigin.Begin);

// Read data
byte[] readBuffer = new byte[10000];
raid5.Read(readBuffer, 0, readBuffer.Length);

// Simulate disk failure
raid5.FailDisk(1);
raid5.Seek(0, SeekOrigin.Begin);
raid5.Read(readBuffer, 0, readBuffer.Length); // Data is reconstructed

// Recover disk
raid5.RecoverDisk(1);
```

## API
- `Raid5Stream(IList<Stream> diskStreams, int stripeUnitSize)`
- `void FailDisk(int diskIndex)`
- `void RecoverDisk(int diskIndex)`
- Standard `Stream` methods: `Read`, `Write`, `Seek`, `SetLength`, etc.

## License
MIT

## Author
thnak

## Repository
https://github.com/thnak/RaidStream

