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
using System.Collections.Generic;

namespace ZXing.Common
{
   /// <summary>
   ///     Encapsulates the result of decoding a matrix of bits. This typically
   ///     applies to 2D barcode formats. For now it contains the raw bytes obtained,
   ///     as well as a String interpretation of those bytes, if applicable.
   ///     <author>Sean Owen</author>
   /// </summary>
   public sealed class DecoderResult
    {
        public DecoderResult(byte[] rawBytes, string text, IList<byte[]> byteSegments, string ecLevel)
            : this(rawBytes, text, byteSegments, ecLevel, -1, -1)
        {
        }

        public DecoderResult(byte[] rawBytes, string text, IList<byte[]> byteSegments, string ecLevel, int saSequence,
            int saParity)
        {
            if (rawBytes == null && text == null) throw new ArgumentException();
            RawBytes = rawBytes;
            Text = text;
            ByteSegments = byteSegments;
            ECLevel = ecLevel;
            StructuredAppendParity = saParity;
            StructuredAppendSequenceNumber = saSequence;
        }

        public byte[] RawBytes { get; }

        public string Text { get; }

        public IList<byte[]> ByteSegments { get; }

        public string ECLevel { get; }

        public bool StructuredAppend => StructuredAppendParity >= 0 && StructuredAppendSequenceNumber >= 0;

        public int ErrorsCorrected { get; set; }

        public int StructuredAppendSequenceNumber { get; }

        public int Erasures { get; set; }

        public int StructuredAppendParity { get; }

        /// <summary>
        ///     Miscellanseous data value for the various decoders
        /// </summary>
        /// <value>The other.</value>
        public object Other { get; set; }
    }
}