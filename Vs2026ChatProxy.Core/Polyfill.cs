using System.Threading;
using System.Threading.Tasks;

namespace System
{
    // net472 缺少 Math.Clamp
    internal static class MathEx
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

namespace System.IO
{
    // net472 的 Stream / StreamReader / StreamContent 没有带 CancellationToken 的方法
    internal static class StreamExtensions
    {
        public static Task WriteAsync(this Stream stream, byte[] buffer)
            => stream.WriteAsync(buffer, 0, buffer.Length);

        public static Task WriteAsync(this Stream stream, byte[] buffer, CancellationToken ct)
            => stream.WriteAsync(buffer, 0, buffer.Length, ct);

        public static Task FlushAsync(this Stream stream, CancellationToken ct)
            => stream.FlushAsync();

        public static Task<string> ReadAsStringAsync(this System.Net.Http.HttpContent content, CancellationToken ct)
            => content.ReadAsStringAsync();

        public static Task<Stream> ReadAsStreamAsync(this System.Net.Http.HttpContent content, CancellationToken ct)
            => content.ReadAsStreamAsync();

        public static Task<string> ReadLineAsync(this StreamReader reader, CancellationToken ct)
            => reader.ReadLineAsync();
    }
}
