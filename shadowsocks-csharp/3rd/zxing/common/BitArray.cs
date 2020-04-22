/*
* Copyright 2007 ZXing authors
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*      http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;

namespace ZXing.Common
{
   /// <summary>
   ///     A simple, fast array of bits, represented compactly by an array of ints internally.
   /// </summary>
   /// <author>Sean Owen</author>
   public sealed class BitArray
    {
        private static readonly int[] _lookup =
        {
            32, 0, 1, 26, 2, 23, 27, 0, 3, 16, 24, 30, 28, 11, 0, 13, 4, 7, 17,
            0, 25, 22, 31, 15, 29, 10, 12, 6, 0, 21, 14, 9, 5, 20, 8, 19, 18
        };

        public BitArray()
        {
            Size = 0;
            Array = new int[1];
        }

        public BitArray(int size)
        {
            if (size < 1) throw new ArgumentException("size must be at least 1");
            Size = size;
            Array = makeArray(size);
        }

        // For testing only
        private BitArray(int[] bits, int size)
        {
            Array = bits;
            Size = size;
        }

        public int Size { get; private set; }

        public int SizeInBytes => (Size + 7) >> 3;

        public bool this[int i]
        {
            get => (Array[i >> 5] & (1 << (i & 0x1F))) != 0;
            set
            {
                if (value)
                    Array[i >> 5] |= 1 << (i & 0x1F);
            }
        }

        /// <returns>
        ///     underlying array of ints. The first element holds the first 32 bits, and the least
        ///     significant bit is bit 0.
        /// </returns>
        public int[] Array { get; private set; }

        private void ensureCapacity(int size)
        {
            if (size > Array.Length << 5)
            {
                var newBits = makeArray(size);
                System.Array.Copy(Array, 0, newBits, 0, Array.Length);
                Array = newBits;
            }
        }


        private static int numberOfTrailingZeros(int num)
        {
            var index = (-num & num) % 37;
            if (index < 0)
                index *= -1;
            return _lookup[index];
        }


        /// <summary>
        ///     Sets a block of 32 bits, starting at bit i.
        /// </summary>
        /// <param name="i">
        ///     first bit to set
        /// </param>
        /// <param name="newBits">
        ///     the new value of the next 32 bits. Note again that the least-significant bit
        ///     corresponds to bit i, the next-least-significant to i+1, and so on.
        /// </param>
        public void setBulk(int i, int newBits)
        {
            Array[i >> 5] = newBits;
        }


        /// <summary> Clears all bits (sets to false).</summary>
        public void clear()
        {
            var max = Array.Length;
            for (var i = 0; i < max; i++) Array[i] = 0;
        }

        /// <summary>
        ///     Appends the bit.
        /// </summary>
        /// <param name="bit">The bit.</param>
        public void appendBit(bool bit)
        {
            ensureCapacity(Size + 1);
            if (bit) Array[Size >> 5] |= 1 << (Size & 0x1F);
            Size++;
        }

        /// <summary>
        ///     Appends the least-significant bits, from value, in order from most-significant to
        ///     least-significant. For example, appending 6 bits from 0x000001E will append the bits
        ///     0, 1, 1, 1, 1, 0 in that order.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="numBits">The num bits.</param>
        public void appendBits(int value, int numBits)
        {
            if (numBits < 0 || numBits > 32) throw new ArgumentException("Num bits must be between 0 and 32");
            ensureCapacity(Size + numBits);
            for (var numBitsLeft = numBits; numBitsLeft > 0; numBitsLeft--)
                appendBit(((value >> (numBitsLeft - 1)) & 0x01) == 1);
        }

        public void appendBitArray(BitArray other)
        {
            var otherSize = other.Size;
            ensureCapacity(Size + otherSize);
            for (var i = 0; i < otherSize; i++) appendBit(other[i]);
        }

        public void xor(BitArray other)
        {
            if (Array.Length != other.Array.Length) throw new ArgumentException("Sizes don't match");
            for (var i = 0; i < Array.Length; i++)
                // The last byte could be incomplete (i.e. not have 8 bits in
                // it) but there is no problem since 0 XOR 0 == 0.
                Array[i] ^= other.Array[i];
        }

        /// <summary>
        ///     Toes the bytes.
        /// </summary>
        /// <param name="bitOffset">first bit to start writing</param>
        /// <param name="array">
        ///     array to write into. Bytes are written most-significant byte first. This is the opposite
        ///     of the internal representation, which is exposed by BitArray
        /// </param>
        /// <param name="offset">position in array to start writing</param>
        /// <param name="numBytes">how many bytes to write</param>
        public void toBytes(int bitOffset, byte[] array, int offset, int numBytes)
        {
            for (var i = 0; i < numBytes; i++)
            {
                var theByte = 0;
                for (var j = 0; j < 8; j++)
                {
                    if (this[bitOffset]) theByte |= 1 << (7 - j);
                    bitOffset++;
                }

                array[offset + i] = (byte) theByte;
            }
        }

        private static int[] makeArray(int size)
        {
            return new int[(size + 31) >> 5];
        }

        /// <summary>
        ///     Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="o">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///     <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object o)
        {
            var other = o as BitArray;
            if (other == null)
                return false;
            if (Size != other.Size)
                return false;
            for (var index = 0; index < Size; index++)
                if (Array[index] != other.Array[index])
                    return false;
            return true;
        }

        /// <summary>
        ///     Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        ///     A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            var hash = Size;
            foreach (var bit in Array) hash = 31 * hash + bit.GetHashCode();
            return hash;
        }


        /// <summary>
        ///     Erstellt ein neues Objekt, das eine Kopie der aktuellen Instanz darstellt.
        /// </summary>
        /// <returns>
        ///     Ein neues Objekt, das eine Kopie dieser Instanz darstellt.
        /// </returns>
        public object Clone()
        {
            return new BitArray((int[]) Array.Clone(), Size);
        }
    }
}