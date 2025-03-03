﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Checksum of data can be used later to see whether two data are same or not
    /// without actually comparing data itself
    /// </summary>
    [DataContract]
    internal sealed partial record class Checksum(
        [property: DataMember(Order = 0)] Checksum.HashData Hash) : IObjectWritable
    {
        /// <summary>
        /// The intended size of the <see cref="HashData"/> structure. 
        /// </summary>
        public const int HashSize = 20;

        public static readonly Checksum Null = new(Hash: default);

        /// <summary>
        /// Create Checksum from given byte array. if byte array is bigger than <see cref="HashSize"/>, it will be
        /// truncated to the size.
        /// </summary>
        public static Checksum From(byte[] checksum)
            => From(checksum.AsSpan());

        /// <summary>
        /// Create Checksum from given byte array. if byte array is bigger than <see cref="HashSize"/>, it will be
        /// truncated to the size.
        /// </summary>
        public static Checksum From(ImmutableArray<byte> checksum)
            => From(checksum.AsSpan());

        public static Checksum From(ReadOnlySpan<byte> checksum)
        {
            if (checksum.Length == 0)
                return Null;

            if (checksum.Length < HashSize)
                throw new ArgumentException($"checksum must be equal or bigger than the hash size: {HashSize}", nameof(checksum));

            Contract.ThrowIfFalse(MemoryMarshal.TryRead(checksum, out HashData hash));
            return new Checksum(hash);
        }

        public string ToBase64String()
        {
#if NETCOREAPP
            Span<byte> bytes = stackalloc byte[HashSize];
            this.WriteTo(bytes);
            return Convert.ToBase64String(bytes);
#else
            unsafe
            {
                var data = new byte[HashSize];
                fixed (byte* dataPtr = data)
                {
                    *(HashData*)dataPtr = Hash;
                }

                return Convert.ToBase64String(data, 0, HashSize);
            }
#endif
        }

        public static Checksum FromBase64String(string value)
            => value == null ? null : From(Convert.FromBase64String(value));

        public override string ToString()
            => ToBase64String();

        bool IObjectWritable.ShouldReuseInSerialization => true;

        public void WriteTo(ObjectWriter writer)
            => Hash.WriteTo(writer);

        public void WriteTo(Span<byte> span)
        {
            Hash.WriteTo(span);
        }

        public static Checksum ReadFrom(ObjectReader reader)
            => new(HashData.ReadFrom(reader));

        public static Func<Checksum, string> GetChecksumLogInfo { get; }
            = checksum => checksum.ToString();

        public static Func<IEnumerable<Checksum>, string> GetChecksumsLogInfo { get; }
            = checksums => string.Join("|", checksums.Select(c => c.ToString()));

        /// <summary>
        /// This structure stores the 20-byte hash as an inline value rather than requiring the use of
        /// <c>byte[]</c>.
        /// </summary>
        [DataContract, StructLayout(LayoutKind.Explicit, Size = HashSize)]
        public readonly record struct HashData(
            [field: FieldOffset(0)][property: DataMember(Order = 0)] long Data1,
            [field: FieldOffset(8)][property: DataMember(Order = 1)] long Data2,
            [field: FieldOffset(16)][property: DataMember(Order = 2)] int Data3)
        {
            public void WriteTo(ObjectWriter writer)
            {
                writer.WriteInt64(Data1);
                writer.WriteInt64(Data2);
                writer.WriteInt32(Data3);
            }

            public void WriteTo(Span<byte> span)
            {
                Contract.ThrowIfFalse(span.Length >= HashSize);
                Contract.ThrowIfFalse(MemoryMarshal.TryWrite(span, ref Unsafe.AsRef(in this)));
            }

            public static unsafe HashData FromPointer(HashData* hash)
                => new(hash->Data1, hash->Data2, hash->Data3);

            public static HashData ReadFrom(ObjectReader reader)
                => new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32());

            public override int GetHashCode()
            {
                // The checksum is already a hash. Just read a 4-byte value to get a well-distributed hash code.
                return (int)Data1;
            }
        }
    }
}
