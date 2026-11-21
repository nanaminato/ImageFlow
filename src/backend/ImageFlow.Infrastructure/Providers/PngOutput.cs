using System.Buffers.Binary;
namespace ImageFlow;

// Checks the PNG envelope before publishing it. Not a general-purpose image decoder.
internal static class PngOutput
{
    public static void Validate(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.StartsWith(signature)) throw new FormatException();
        var offset = 8; var header = false; var data = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12) throw new FormatException();
            var type = bytes.Slice(offset + 4, 4);
            var payload = bytes.Slice(offset + 8, (int)length);
            var crc = uint.MaxValue;
            foreach (var b in bytes.Slice(offset + 4, (int)length + 4))
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + (int)length, 4))) throw new FormatException();
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13 ||
                    BinaryPrimitives.ReadUInt32BigEndian(payload[..4]) == 0 ||
                    BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) == 0) throw new FormatException();
                header = true;
            }
            else if (type.SequenceEqual("IHDR"u8)) throw new FormatException();
            if (type.SequenceEqual("IDAT"u8) && length > 0) data = true;
            offset += (int)length + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                if (!data || length != 0 || offset != bytes.Length) throw new FormatException();
                return;
            }
        }
        throw new FormatException();
    }
}
