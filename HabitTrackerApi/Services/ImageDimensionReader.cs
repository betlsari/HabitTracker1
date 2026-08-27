namespace Services;

/// <summary>
/// Görselleri tam olarak decode etmeden (bellek/CPU açısından pahalı olan
/// kısmı atlayarak), sadece dosya formatının header'ından genişlik/yükseklik
/// bilgisini okur. Amaç: küçük dosya boyutlu ama aşırı yüksek çözünürlüklü
/// ("decompression bomb") görsellerin, sunucu tarafında ileride
/// işlenirken/gösterilirken aşırı bellek tüketimine yol açmasını önlemek.
/// </summary>
public static class ImageDimensionReader
{
    public static async Task<(int Width, int Height)?> TryReadAsync(
        Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        return contentType switch
        {
            "image/png" => await TryReadPngAsync(stream, cancellationToken),
            "image/jpeg" => await TryReadJpegAsync(stream, cancellationToken),
            "image/webp" => await TryReadWebPAsync(stream, cancellationToken),
            _ => null
        };
    }

    private static async Task<(int, int)?> TryReadPngAsync(Stream stream, CancellationToken cancellationToken)
    {
        // PNG imzası (8) + IHDR uzunluğu (4) + "IHDR" (4) + genişlik (4) + yükseklik (4) = 24 byte
        var header = new byte[24];
        if (await ReadExactAsync(stream, header, cancellationToken) < 24)
        {
            return null;
        }

        if (header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
        {
            return null;
        }

        int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return (width, height);
    }

    private static async Task<(int, int)?> TryReadJpegAsync(Stream stream, CancellationToken cancellationToken)
    {
        var soi = new byte[2];
        if (await ReadExactAsync(stream, soi, cancellationToken) < 2 || soi[0] != 0xFF || soi[1] != 0xD8)
        {
            return null;
        }

        var marker = new byte[2];
        var lenBuf = new byte[2];

        // Sonsuz döngüye girmemek için makul bir segment sayısı sınırı.
        for (int i = 0; i < 200; i++)
        {
            if (await ReadExactAsync(stream, marker, cancellationToken) < 2 || marker[0] != 0xFF)
            {
                return null;
            }

            var markerType = marker[1];

            // Uzunluk alanı taşımayan standalone marker'lar.
            if (markerType == 0xD8 || markerType == 0xD9 || (markerType >= 0xD0 && markerType <= 0xD7))
            {
                continue;
            }

            if (await ReadExactAsync(stream, lenBuf, cancellationToken) < 2)
            {
                return null;
            }

            int segmentLength = (lenBuf[0] << 8) | lenBuf[1];
            if (segmentLength < 2)
            {
                return null;
            }

            // SOF0-SOF15 arası (SOF marker'ları hariç DHT/DAC hariç tutuluyor)
            bool isSofMarker = markerType >= 0xC0 && markerType <= 0xCF
                && markerType != 0xC4 && markerType != 0xC8 && markerType != 0xCC;

            if (isSofMarker)
            {
                // SOF segment: hassasiyet(1) + yükseklik(2) + genişlik(2)
                var sofData = new byte[5];
                if (await ReadExactAsync(stream, sofData, cancellationToken) < 5)
                {
                    return null;
                }

                int height = (sofData[1] << 8) | sofData[2];
                int width = (sofData[3] << 8) | sofData[4];
                return (width, height);
            }

            var toSkip = segmentLength - 2;
            if (toSkip > 0 && !await SkipAsync(stream, toSkip, cancellationToken))
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<(int, int)?> TryReadWebPAsync(Stream stream, CancellationToken cancellationToken)
    {
        // RIFF header: "RIFF"(4) + dosyaBoyutu(4) + "WEBP"(4) = 12 byte
        var riffHeader = new byte[12];
        if (await ReadExactAsync(stream, riffHeader, cancellationToken) < 12)
        {
            return null;
        }

        var chunkHeader = new byte[8];
        if (await ReadExactAsync(stream, chunkHeader, cancellationToken) < 8)
        {
            return null;
        }

        var fourCc = System.Text.Encoding.ASCII.GetString(chunkHeader, 0, 4);

        if (fourCc == "VP8X")
        {
            // VP8X: bayraklar(1) + ayrılmış(3) + canvasGenişlik-1(3, LE) + canvasYükseklik-1(3, LE)
            var vp8x = new byte[10];
            if (await ReadExactAsync(stream, vp8x, cancellationToken) < 10)
            {
                return null;
            }

            int width = 1 + (vp8x[4] | (vp8x[5] << 8) | (vp8x[6] << 16));
            int height = 1 + (vp8x[7] | (vp8x[8] << 8) | (vp8x[9] << 16));
            return (width, height);
        }

        if (fourCc == "VP8 ")
        {
            var vp8 = new byte[10];
            if (await ReadExactAsync(stream, vp8, cancellationToken) < 10)
            {
                return null;
            }

            // Üst 2 bit ölçekleme biti olduğu için maskeleniyor.
            int width = ((vp8[7] << 8) | vp8[6]) & 0x3FFF;
            int height = ((vp8[9] << 8) | vp8[8]) & 0x3FFF;
            return (width, height);
        }

        if (fourCc == "VP8L")
        {
            var vp8l = new byte[5];
            if (await ReadExactAsync(stream, vp8l, cancellationToken) < 5 || vp8l[0] != 0x2F)
            {
                return null;
            }

            int bits = vp8l[1] | (vp8l[2] << 8) | (vp8l[3] << 16) | (vp8l[4] << 24);
            int width = (bits & 0x3FFF) + 1;
            int height = ((bits >> 14) & 0x3FFF) + 1;
            return (width, height);
        }

        return null;
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }

    private static async Task<bool> SkipAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return true;
        }

        var buffer = new byte[Math.Min(count, 4096)];
        var remaining = count;
        while (remaining > 0)
        {
            var toRead = Math.Min(remaining, buffer.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                return false;
            }
            remaining -= read;
        }
        return true;
    }
}