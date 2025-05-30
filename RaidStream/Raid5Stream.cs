namespace ConsoleApp2;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class Raid5Stream : Stream
{
    private readonly List<Stream> _disks;
    private readonly int _stripeUnitSize; // Size of a data block on one disk for a stripe
    private readonly int _numDisks;
    private readonly int _numDataDisks; // numDisks - 1
    private readonly long _dataStripeSize; // Total data in one stripe = _stripeUnitSize * _numDataDisks

    private long _position;
    private long _logicalLength; // User-settable logical length of the RAID stream

    private readonly bool[] _failedDisks;

    // Buffers for internal operations to avoid frequent allocations
    private readonly byte[] _singleUnitBuffer;
    private readonly byte[] _parityCalculationBuffer;


    /// <summary>
    /// Khởi tạo một Raid5Stream mới.
    /// </summary>
    /// <param name="diskStreams">Danh sách các stream đại diện cho các ổ đĩa vật lý. Phải có ít nhất 3 stream.</param>
    /// <param name="stripeUnitSize">Kích thước của một khối dữ liệu trên một ổ đĩa (tính bằng byte). Phải lớn hơn 0.</param>
    /// <exception cref="ArgumentNullException">Ném ra nếu diskStreams là null.</exception>
    /// <exception cref="ArgumentException">Ném ra nếu số lượng stream nhỏ hơn 3, stripeUnitSize nhỏ hơn hoặc bằng 0, hoặc các stream không đáp ứng yêu cầu.</exception>
    public Raid5Stream(IList<Stream> diskStreams, int stripeUnitSize)
    {
        if (diskStreams == null)
            throw new ArgumentNullException(nameof(diskStreams));
        if (diskStreams.Count < 3)
            throw new ArgumentException("RAID 5 requires at least 3 disks.", nameof(diskStreams));
        if (stripeUnitSize <= 0)
            throw new ArgumentException("Stripe unit size must be greater than 0.", nameof(stripeUnitSize));

        _disks = new List<Stream>(diskStreams);
        _numDisks = _disks.Count;
        _numDataDisks = _numDisks - 1;
        _stripeUnitSize = stripeUnitSize;
        _dataStripeSize = (long)_stripeUnitSize * _numDataDisks;

        _failedDisks = new bool[_numDisks];
        _singleUnitBuffer = new byte[_stripeUnitSize];
        _parityCalculationBuffer = new byte[_stripeUnitSize];


        foreach (var disk in _disks)
        {
            if (!disk.CanRead)
                throw new ArgumentException("All disk streams must be readable.", nameof(diskStreams));
            if (!disk.CanWrite)
                throw new ArgumentException("All disk streams must be writable.", nameof(diskStreams));
            if (!disk.CanSeek)
                throw new ArgumentException("All disk streams must be seekable.", nameof(diskStreams));
        }

        // Initialize logical length based on the smallest disk, aligned to data stripe size
        // Or, you might want to set it to 0 and require explicit SetLength
        long minDiskPhysicalLength = _disks.Min(d => d.Length);
        long numPhysicalStripesOnSmallestDisk = minDiskPhysicalLength / _stripeUnitSize;
        _logicalLength = numPhysicalStripesOnSmallestDisk * _dataStripeSize;
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;

    public override long Length => _logicalLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Mô phỏng lỗi của một ổ đĩa.
    /// </summary>
    /// <param name="diskIndex">Chỉ số của ổ đĩa bị lỗi (0 đến N-1).</param>
    public void FailDisk(int diskIndex)
    {
        if (diskIndex < 0 || diskIndex >= _numDisks)
            throw new ArgumentOutOfRangeException(nameof(diskIndex));
        _failedDisks[diskIndex] = true;
        Console.WriteLine($"Warning: Disk {diskIndex} has been marked as failed.");
    }

    /// <summary>
    /// Khôi phục một ổ đĩa (mô phỏng ổ đĩa được thay thế và xây dựng lại).
    /// Lưu ý: Việc xây dựng lại toàn bộ ổ đĩa là một quá trình phức tạp và không được triển khai ở đây.
    /// Phương thức này chỉ đơn giản là đánh dấu ổ đĩa là không bị lỗi nữa.
    /// </summary>
    /// <param name="diskIndex">Chỉ số của ổ đĩa cần khôi phục.</param>
    public void RecoverDisk(int diskIndex)
    {
        if (diskIndex < 0 || diskIndex >= _numDisks)
            throw new ArgumentOutOfRangeException(nameof(diskIndex));
        if (!_failedDisks[diskIndex])
            return; // Already healthy

        // Rebuild the failed disk's data
        long numStripes = _disks.Min(d => d.Length) / _stripeUnitSize;
        var buffer = new byte[_stripeUnitSize];

        for (long stripe = 0; stripe < numStripes; stripe++)
        {
            int parityDisk = (_numDisks - 1) - (int)(stripe % _numDisks);
            long offset = stripe * _stripeUnitSize;

            // Only rebuild if this disk is used in this stripe
            if (parityDisk == diskIndex || (stripe % _numDisks) < _numDataDisks)
            {
                Array.Clear(buffer, 0, buffer.Length);
                for (int i = 0; i < _numDisks; i++)
                {
                    if (i == diskIndex) continue;
                    _disks[i].Seek(offset, SeekOrigin.Begin);
                    var temp = new byte[_stripeUnitSize];
                    _disks[i].ReadExactly(temp, 0, _stripeUnitSize);
                    XORBuffers(buffer, temp, _stripeUnitSize);
                }

                // Write reconstructed data/parity to the recovered disk
                _disks[diskIndex].Seek(offset, SeekOrigin.Begin);
                _disks[diskIndex].Write(buffer, 0, _stripeUnitSize);
            }
        }

        _failedDisks[diskIndex] = false;
        Console.WriteLine($"Warning: Disk {diskIndex} marked as recovered and rebuilt.");
    }

    public bool IsDiskFailed(int diskIndex)
    {
        if (diskIndex < 0 || diskIndex >= _numDisks)
            throw new ArgumentOutOfRangeException(nameof(diskIndex));
        return _failedDisks[diskIndex];
    }


    public override void Flush()
    {
        foreach (var disk in _disks)
        {
            if (!_failedDisks[_disks.IndexOf(disk)])
            {
                disk.Flush();
            }
        }
    }

    /// <summary>
    /// Đọc một chuỗi byte từ stream hiện tại và nâng cao vị trí hiện tại trong stream này theo số byte đã đọc.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArgs(buffer, offset, count);
        if (count == 0) return 0;

        long remainingBytesInStream = Math.Max(0, _logicalLength - _position);
        int bytesToRead = (int)Math.Min(count, remainingBytesInStream);
        if (bytesToRead == 0) return 0; // End of stream or past it

        int totalBytesRead = 0;
        int currentOutputOffset = offset;

        while (totalBytesRead < bytesToRead)
        {
            long currentStripeGlobalIndex = _position / _dataStripeSize;
            int logicalDataBlockInStripeIndex = (int)((_position % _dataStripeSize) / _stripeUnitSize);
            int offsetInStripeUnit = (int)(_position % _stripeUnitSize);

            int parityDiskForStripe = (_numDisks - 1) - (int)(currentStripeGlobalIndex % _numDisks);
            long physicalDiskOffsetForStripe = currentStripeGlobalIndex * _stripeUnitSize;

            int targetPhysicalDataDisk = GetPhysicalDiskIndex(logicalDataBlockInStripeIndex, parityDiskForStripe);

            int bytesToProcessInCurrentUnit = Math.Min(bytesToRead - totalBytesRead, _stripeUnitSize - offsetInStripeUnit);

            if (_failedDisks[targetPhysicalDataDisk])
            {
                // A disk has failed, attempt to reconstruct data for this unit
                if (_failedDisks.Count(f => f) > 1)
                    throw new IOException("RAID 5 failure: Too many disks failed. Cannot reconstruct data.");

                Array.Clear(_parityCalculationBuffer, 0, _stripeUnitSize); // Start with zeros for XOR sum

                for (int i = 0; i < _numDisks; i++)
                {
                    if (i == targetPhysicalDataDisk) continue; // Skip the failed disk itself

                    if (_failedDisks[i]) // Should not happen if already checked for >1 failure
                        throw new IOException($"RAID 5 consistency error: Disk {i} also failed during reconstruction.");

                    ReadFromPhysicalDisk(_disks[i], physicalDiskOffsetForStripe, _singleUnitBuffer, _stripeUnitSize);
                    XORBuffers(_parityCalculationBuffer, _singleUnitBuffer, _stripeUnitSize);
                }

                // _parityCalculationBuffer now holds the reconstructed data for the targetPhysicalDataDisk's unit
                Buffer.BlockCopy(_parityCalculationBuffer, offsetInStripeUnit, buffer, currentOutputOffset, bytesToProcessInCurrentUnit);
            }
            else
            {
                // Read directly from the target data disk
                ReadFromPhysicalDisk(_disks[targetPhysicalDataDisk], physicalDiskOffsetForStripe + offsetInStripeUnit, buffer, currentOutputOffset,
                    bytesToProcessInCurrentUnit);
            }

            _position += bytesToProcessInCurrentUnit;
            totalBytesRead += bytesToProcessInCurrentUnit;
            currentOutputOffset += bytesToProcessInCurrentUnit;
        }

        return totalBytesRead;
    }

    /// <summary>
    /// Ghi một chuỗi byte vào stream hiện tại và nâng cao vị trí hiện tại trong stream này theo số byte đã ghi.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArgs(buffer, offset, count);
        if (count == 0) return;

        long endPosition = _position + count;
        if (endPosition > _logicalLength)
        {
            SetLength(endPosition); // Automatically extend if writing past the current end
        }

        int totalBytesWritten = 0;
        int currentInputOffset = offset;

        byte[] oldDataUnit = new byte[_stripeUnitSize];
        byte[] oldParityUnit = new byte[_stripeUnitSize];
        byte[] newDataUnit = new byte[_stripeUnitSize];

        while (totalBytesWritten < count)
        {
            long currentStripeGlobalIndex = _position / _dataStripeSize;
            int logicalDataBlockInStripeIndex = (int)((_position % _dataStripeSize) / _stripeUnitSize);
            int offsetInStripeUnit = (int)(_position % _stripeUnitSize);

            int parityDiskForStripe = (_numDisks - 1) - (int)(currentStripeGlobalIndex % _numDisks);
            long physicalDiskOffsetForStripe = currentStripeGlobalIndex * _stripeUnitSize;

            int targetPhysicalDataDisk = GetPhysicalDiskIndex(logicalDataBlockInStripeIndex, parityDiskForStripe);

            int bytesToProcessInCurrentUnit = Math.Min(count - totalBytesWritten, _stripeUnitSize - offsetInStripeUnit);

            // --- Read-Modify-Write for data and parity ---
            // 1. Read old data unit from target data disk
            if (_failedDisks[targetPhysicalDataDisk])
                throw new IOException($"Cannot write: Target data disk {targetPhysicalDataDisk} has failed.");
            ReadFromPhysicalDisk(_disks[targetPhysicalDataDisk], physicalDiskOffsetForStripe, oldDataUnit, _stripeUnitSize);

            // 2. Read old parity unit from parity disk
            if (_failedDisks[parityDiskForStripe])
                throw new IOException($"Cannot write: Parity disk {parityDiskForStripe} has failed.");
            ReadFromPhysicalDisk(_disks[parityDiskForStripe], physicalDiskOffsetForStripe, oldParityUnit, _stripeUnitSize);

            // 3. Prepare new data unit
            Buffer.BlockCopy(oldDataUnit, 0, newDataUnit, 0, _stripeUnitSize); // Start with old data
            Buffer.BlockCopy(buffer, currentInputOffset, newDataUnit, offsetInStripeUnit,
                bytesToProcessInCurrentUnit); // Overwrite with new data portion

            // 4. Calculate new parity unit: newParity = oldParity XOR oldDataUnit XOR newDataUnit
            XORBuffers(oldParityUnit, oldDataUnit, _stripeUnitSize); // oldParityUnit now holds oldParity XOR oldDataUnit
            XORBuffers(oldParityUnit, newDataUnit, _stripeUnitSize); // oldParityUnit now holds newParity
            // byte[] newParityUnit = oldParityUnit; // Renaming for clarity

            // 5. Write new data unit to target data disk
            WriteToPhysicalDisk(_disks[targetPhysicalDataDisk], physicalDiskOffsetForStripe, newDataUnit, _stripeUnitSize);

            // 6. Write new parity unit to parity disk
            WriteToPhysicalDisk(_disks[parityDiskForStripe], physicalDiskOffsetForStripe, oldParityUnit,
                _stripeUnitSize); // oldParityUnit contains the new parity

            _position += bytesToProcessInCurrentUnit;
            totalBytesWritten += bytesToProcessInCurrentUnit;
            currentInputOffset += bytesToProcessInCurrentUnit;
        }
    }


    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition;
        switch (origin)
        {
            case SeekOrigin.Begin:
                newPosition = offset;
                break;
            case SeekOrigin.Current:
                newPosition = _position + offset;
                break;
            case SeekOrigin.End:
                newPosition = _logicalLength + offset;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (newPosition < 0)
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");

        _position = newPosition;
        return _position;
    }

    public override void SetLength(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        _logicalLength = value; // User-defined logical length

        // Calculate physical size needed on each disk based on the new logical length
        long requiredTotalDataStripes = (_logicalLength + _dataStripeSize - 1) / _dataStripeSize; // Ceiling division
        if (_logicalLength == 0) requiredTotalDataStripes = 0; // Handle case for 0 length

        long requiredPhysicalSizePerDisk = requiredTotalDataStripes * _stripeUnitSize;

        foreach (var disk in _disks)
        {
            if (!_failedDisks[_disks.IndexOf(disk)])
            {
                // Ensure underlying disk has enough space.
                // If growing, new areas are typically zeroed by OS/FileStream or MemoryStream.
                // This works for RAID 5 as XOR of zeros is zero, so new parity for zeroed data is also zero.
                if (disk.Length < requiredPhysicalSizePerDisk)
                {
                    disk.SetLength(requiredPhysicalSizePerDisk);
                }
                // Optionally, if shrinking physical disks: disk.SetLength(requiredPhysicalSizePerDisk);
                // However, usually we only grow physical disks and _logicalLength dictates usable space.
                // For simplicity here, we'll ensure disks are at least this size.
                // A more robust implementation might handle physical shrinking carefully.
            }
        }
        // Note: If shrinking _logicalLength, existing data beyond the new length on physical disks remains
        // until overwritten or explicitly truncated by a more complex SetLength/Trim operation.
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var disk in _disks)
            {
                disk.Dispose();
            }

            _disks.Clear();
        }

        base.Dispose(disposing);
    }

    // --- Helper Methods ---

    private int GetPhysicalDiskIndex(int logicalDataBlockInStripeIndex, int parityDiskForStripe)
    {
        // Maps a logical data block index within a stripe to a physical disk index,
        // skipping the parity disk for that stripe.
        if (logicalDataBlockInStripeIndex < parityDiskForStripe)
        {
            return logicalDataBlockInStripeIndex;
        }
        else
        {
            return logicalDataBlockInStripeIndex + 1;
        }
    }

    private void XORBuffers(byte[] target, byte[] source, int length)
    {
        for (int i = 0; i < length; i++)
        {
            target[i] ^= source[i];
        }
    }

    private void ReadFromPhysicalDisk(Stream disk, long physicalOffset, byte[] buffer, int bufferOffset, int count)
    {
        disk.Seek(physicalOffset, SeekOrigin.Begin);
        int bytesRead = 0;
        while (bytesRead < count)
        {
            int read = disk.Read(buffer, bufferOffset + bytesRead, count - bytesRead);
            if (read == 0) throw new EndOfStreamException("Failed to read full unit from underlying disk.");
            bytesRead += read;
        }
    }

    private void ReadFromPhysicalDisk(Stream disk, long physicalOffset, byte[] bufferToFill, int countToFill) // Overload for filling a buffer
    {
        ReadFromPhysicalDisk(disk, physicalOffset, bufferToFill, 0, countToFill);
    }


    private void WriteToPhysicalDisk(Stream disk, long physicalOffset, byte[] buffer, int count)
    {
        disk.Seek(physicalOffset, SeekOrigin.Begin);
        disk.Write(buffer, 0, count);
    }

    private void ValidateBufferArgs(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (buffer.Length - offset < count) throw new ArgumentException("Invalid offset or count for buffer.");
    }
}