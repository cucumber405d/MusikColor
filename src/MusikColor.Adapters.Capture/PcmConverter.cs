using System;
using NAudio.Wave;

namespace MusikColor.Adapters.Capture;

/// <summary>
/// Переводит "сырые" байты из WASAPI-коллбэка в float-сэмплы [-1..1].
/// NAudio нормализует WaveFormat перед тем, как отдать его наружу
/// (WasapiCapture.WaveFormat уже приведён к стандартному виду, а не
/// "сырой" WAVEFORMATEXTENSIBLE), поэтому достаточно проверять
/// Encoding/BitsPerSample напрямую.
/// </summary>
internal static class PcmConverter
{
    public static float[] ToFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            int count = bytesRecorded / 4;
            var result = new float[count];
            Buffer.BlockCopy(buffer, 0, result, 0, count * 4);
            return result;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            int count = bytesRecorded / 2;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                result[i] = sample / 32768f;
            }
            return result;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
        {
            const int bytesPerSample = 3;
            int count = bytesRecorded / bytesPerSample;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * bytesPerSample;
                int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((sample & 0x00800000) != 0)
                {
                    sample = unchecked((int)(sample | 0xFF000000));
                }
                result[i] = sample / 8388608f;
            }
            return result;
        }

        throw new NotSupportedException(
            $"Формат {format.Encoding}, {format.BitsPerSample} бит не поддерживается PcmConverter. " +
            "Добавьте нужную ветку конвертации.");
    }
}
