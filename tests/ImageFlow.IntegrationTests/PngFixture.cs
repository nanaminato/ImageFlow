using System.Buffers.Binary;
using System.IO.Compression;
namespace ImageFlow.IntegrationTests;

internal static class PngFixture
{
    // A real 1x1 RGBA PNG. Assert its compressed scanline is decodable in tests.
    public static byte[] Bytes => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
    public static string Response => System.Text.Json.JsonSerializer.Serialize(new
    {
        data = new[] { new { b64_json = Convert.ToBase64String(Bytes) } },
        usage = new { total_tokens = 42, input_tokens = 10, output_tokens = 32 }
    });
    public static void AssertDecodable(byte[] bytes)
    {
        Assert.Equal(Bytes, bytes);
        var offset = 8;
        while (offset < bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                using var compressed = new MemoryStream(bytes, offset + 8, length);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var decoded = new MemoryStream(); zlib.CopyTo(decoded);
                Assert.Equal(5, decoded.Length); // filter byte + one RGBA pixel
                return;
            }
            offset += length + 12;
        }
        Assert.Fail("PNG has no image data.");
    }
}
