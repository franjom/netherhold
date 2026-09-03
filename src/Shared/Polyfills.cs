// Compiler shims required to use modern C# (records, init accessors, required
// members) while targeting netstandard2.1 for Unity compatibility.
// These types are compiler-recognised by name; they carry no runtime behaviour.

#if NETSTANDARD2_1

namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

namespace System.IO
{
    /// <summary>
    /// <c>Stream.ReadExactly</c> is .NET 7+ and netstandard2.1 does not have it.
    ///
    /// <para>Declared in <c>System.IO</c> on purpose, so that call sites resolve
    /// it through the implicit <c>using System.IO</c> and no code has to know
    /// whether it is running on the polyfill or the framework. The same
    /// technique the attribute shims above use.</para>
    ///
    /// <para><b>The exact semantics matter here.</b> A short read is not an
    /// end-of-file condition to be tolerated: every caller is reading a fixed
    /// record from a game file, so a stream that runs out mid-record means the
    /// file is truncated or an offset is wrong, and a parser that silently
    /// carried on with a half-filled buffer would produce plausible garbage.
    /// That is precisely the failure the record-size checks exist to catch, so
    /// this throws exactly as the framework's own version does.</para>
    /// </summary>
    internal static class StreamReadExactlyPolyfill
    {
        public static void ReadExactly(this Stream stream, byte[] buffer) =>
            stream.ReadExactly(buffer, 0, buffer.Length);

        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));

            int read = 0;
            while (read < count)
            {
                int got = stream.Read(buffer, offset + read, count - read);
                if (got <= 0)
                    throw new EndOfStreamException(
                        $"expected {count} bytes, got {read} before the end of the stream.");

                read += got;
            }
        }

        public static void ReadExactly(this Stream stream, Span<byte> buffer)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            int read = 0;
            while (read < buffer.Length)
            {
                int got = stream.Read(buffer.Slice(read));
                if (got <= 0)
                    throw new EndOfStreamException(
                        $"expected {buffer.Length} bytes, got {read} before the end of the stream.");

                read += got;
            }
        }
    }
}

#endif
