namespace PokeScanner;

public static class StreamExtensions
{
    public static async Task<byte[]> ToByteArrayAsync(this Stream stream)
    {
        if (stream is MemoryStream memoryStream)
        {
            return memoryStream.ToArray();
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
