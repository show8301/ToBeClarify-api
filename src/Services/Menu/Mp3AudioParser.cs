namespace ToBeClarify.Api.Services.Menu;

public sealed record Mp3AudioInfo(double DurationSeconds, int FrameCount);

/// <summary>
/// Validates the MPEG Layer III frame structure without requiring an external executable.
/// It deliberately accepts MP3 audio frames only; ID3v2, ID3v1 and APE trailing metadata are
/// treated as container metadata rather than audio frames.
/// </summary>
public static class Mp3AudioParser
{
    private static readonly int[] Mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static readonly int[] Mpeg2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
    private static readonly int[] BaseSampleRates = [44100, 48000, 32000];

    public static Mp3AudioInfo Parse(ReadOnlySpan<byte> data)
    {
        var offset = SkipId3v2(data);
        var frameCount = 0;
        var durationSeconds = 0d;

        while (offset + 4 <= data.Length)
        {
            if (!TryReadFrame(data[offset..], out var frameLength, out var frameDurationSeconds))
            {
                if (frameCount == 0 || !IsTrailingMetadata(data[offset..]))
                    throw new InvalidDataException("The file does not contain a valid MP3 frame sequence.");

                break;
            }

            if (frameLength > data.Length - offset)
                throw new InvalidDataException("The MP3 frame is truncated.");

            frameCount++;
            durationSeconds += frameDurationSeconds;
            offset += frameLength;
        }

        if (offset < data.Length && !IsTrailingMetadata(data[offset..]))
            throw new InvalidDataException("The MP3 stream contains an invalid trailing segment.");

        if (frameCount == 0 || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidDataException("The file does not contain playable MP3 audio.");

        return new Mp3AudioInfo(durationSeconds, frameCount);
    }

    private static int SkipId3v2(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || !data[..3].SequenceEqual("ID3"u8))
            return 0;

        if (data.Length < 10 || data[3] == 0xff || data[4] == 0xff || (data[6] & 0x80) != 0 ||
            (data[7] & 0x80) != 0 || (data[8] & 0x80) != 0 || (data[9] & 0x80) != 0)
            throw new InvalidDataException("The ID3v2 header is invalid.");

        var tagLength = 10 + ((data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9]);
        if ((data[5] & 0x10) != 0)
            tagLength += 10;

        if (tagLength > data.Length)
            throw new InvalidDataException("The ID3v2 tag is truncated.");

        return tagLength;
    }

    private static bool TryReadFrame(ReadOnlySpan<byte> header, out int frameLength, out double durationSeconds)
    {
        frameLength = 0;
        durationSeconds = 0;

        if (header.Length < 4 || header[0] != 0xff || (header[1] & 0xe0) != 0xe0)
            return false;

        var version = (header[1] >> 3) & 0x03;
        var layer = (header[1] >> 1) & 0x03;
        var bitrateIndex = (header[2] >> 4) & 0x0f;
        var sampleRateIndex = (header[2] >> 2) & 0x03;
        var padding = (header[2] >> 1) & 0x01;
        var emphasis = header[3] & 0x03;

        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || sampleRateIndex == 3 || emphasis == 2)
            return false;

        var bitrateTable = version == 3 ? Mpeg1Bitrates : Mpeg2Bitrates;
        var bitrate = bitrateTable[bitrateIndex];
        var sampleRate = BaseSampleRates[sampleRateIndex];
        if (version == 2)
            sampleRate /= 2;
        else if (version == 0)
            sampleRate /= 4;

        var coefficient = version == 3 ? 144 : 72;
        frameLength = coefficient * bitrate * 1000 / sampleRate + padding;
        var samplesPerFrame = version == 3 ? 1152 : 576;
        durationSeconds = (double)samplesPerFrame / sampleRate;

        return frameLength >= 4 && durationSeconds > 0;
    }

    private static bool IsTrailingMetadata(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return true;

        var allZero = true;
        foreach (var value in data)
        {
            if (value != 0)
            {
                allZero = false;
                break;
            }
        }

        if (allZero)
            return true;
        if (data.Length >= 3 && (data[..3].SequenceEqual("TAG"u8) || data[..3].SequenceEqual("ID3"u8)))
            return true;
        return data.Length >= 8 && data[..8].SequenceEqual("APETAGEX"u8);
    }
}
