// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;

namespace System
{
    internal static class IriHelper
    {
        //
        // Checks if provided non surrogate char lies in iri range
        //
        internal static bool CheckIriUnicodeRange(char unicode, bool isQuery)
        {
            return ((unicode >= '\u00A0' && unicode <= '\uD7FF') ||
               (unicode >= '\uF900' && unicode <= '\uFDCF') ||
               (unicode >= '\uFDF0' && unicode <= '\uFFEF') ||
               (isQuery && unicode >= '\uE000' && unicode <= '\uF8FF'));
        }

        //
        // Check if highSurr and lowSurr are a surrogate pair then
        // it checks if the combined char is in the range
        // Takes in isQuery because iri restrictions for query are different
        //
        internal static bool CheckIriUnicodeRange(char highSurr, char lowSurr, out bool isSurrogatePair, bool isQuery)
        {
            Debug.Assert(char.IsHighSurrogate(highSurr));

            isSurrogatePair = char.IsLowSurrogate(lowSurr);
            if (isSurrogatePair)
            {
                uint utf32 = (uint)char.ConvertToUtf32(highSurr, lowSurr);

                return ((utf32 - 0x10000) <= (0x1FFFD - 0x10000) ||
                    (utf32 - 0x20000) <= (0x2FFFD - 0x20000) ||
                    (utf32 - 0x30000) <= (0x3FFFD - 0x30000) ||
                    (utf32 - 0x40000) <= (0x4FFFD - 0x40000) ||
                    (utf32 - 0x50000) <= (0x5FFFD - 0x50000) ||
                    (utf32 - 0x60000) <= (0x6FFFD - 0x60000) ||
                    (utf32 - 0x70000) <= (0x7FFFD - 0x70000) ||
                    (utf32 - 0x80000) <= (0x8FFFD - 0x80000) ||
                    (utf32 - 0x90000) <= (0x9FFFD - 0x90000) ||
                    (utf32 - 0xA0000) <= (0xAFFFD - 0xA0000) ||
                    (utf32 - 0xB0000) <= (0xBFFFD - 0xB0000) ||
                    (utf32 - 0xC0000) <= (0xCFFFD - 0xC0000) ||
                    (utf32 - 0xD0000) <= (0xDFFFD - 0xD0000) ||
                    (utf32 - 0xE1000) <= (0xEFFFD - 0xE1000) ||
                    (isQuery &&
                        ((utf32 - 0xF0000) <= (0xFFFFD - 0xF0000) ||
                        (utf32 - 0x100000) <= (0x10FFFD - 0x100000))));
            }
            return false;
        }

        //
        // Check reserved chars according to RFC 3987 in a specific component
        //
        internal static bool CheckIsReserved(char ch, UriComponents component)
        {
            if ((component != UriComponents.Scheme) &&
                    (component != UriComponents.UserInfo) &&
                    (component != UriComponents.Host) &&
                    (component != UriComponents.Port) &&
                    (component != UriComponents.Path) &&
                    (component != UriComponents.Query) &&
                    (component != UriComponents.Fragment)
                )
            {
                return (component == (UriComponents)0) ? UriHelper.IsGenDelim(ch) : false;
            }

            return UriHelper.RFC3986ReservedMarks.IndexOf(ch) >= 0;
        }

        //
        // IRI normalization for strings containing characters that are not allowed or
        // escaped characters that should be unescaped in the context of the specified Uri component.
        //
        internal static unsafe string EscapeUnescapeIri(char* pInput, int start, int end, UriComponents component)
        {
            int size = end - start;
            ValueStringBuilder dest = new ValueStringBuilder(size);
            byte[]? bytes = null;

            const int percentEncodingLen = 3; // Escaped UTF-8 will take 3 chars: %AB.
            int bufferRemaining = 0;

            int next = start;
            char ch;
            bool escape = false;
            bool surrogatePair = false;

            for (; next < end; ++next)
            {
                escape = false;
                surrogatePair = false;

                if ((ch = pInput[next]) == '%')
                {
                    if (next + 2 < end)
                    {
                        ch = UriHelper.EscapedAscii(pInput[next + 1], pInput[next + 2]);

                        // Do not unescape a reserved char
                        if (ch == Uri.c_DummyChar || ch == '%' || CheckIsReserved(ch, component) || UriHelper.IsNotSafeForUnescape(ch))
                        {
                            // keep as is
                            dest.Append(pInput[next++]);
                            dest.Append(pInput[next++]);
                            dest.Append(pInput[next]);
                            continue;
                        }
                        else if (ch <= '\x7F')
                        {
                            Debug.Assert(ch < 0xFF, "Expecting ASCII character.");
                            //ASCII
                            dest.Append(ch);
                            next += 2;
                            continue;
                        }
                        else
                        {
                            // possibly utf8 encoded sequence of unicode

                            // check if safe to unescape according to Iri rules

                            Debug.Assert(ch < 0xFF, "Expecting ASCII character.");

                            int startSeq = next;
                            int byteCount = 1;
                            // lazy initialization of max size, will reuse the array for next sequences
                            if (bytes is null)
                                bytes = new byte[end - next];

                            bytes[0] = (byte)ch;
                            next += 3;
                            while (next < end)
                            {
                                // Check on exit criterion
                                if ((ch = pInput[next]) != '%' || next + 2 >= end)
                                    break;

                                // already made sure we have 3 characters in str
                                ch = UriHelper.EscapedAscii(pInput[next + 1], pInput[next + 2]);

                                //invalid hex sequence ?
                                if (ch == Uri.c_DummyChar)
                                    break;
                                // character is not part of a UTF-8 sequence ?
                                else if (ch < '\x80')
                                    break;
                                else
                                {
                                    //a UTF-8 sequence
                                    bytes[byteCount++] = (byte)ch;
                                    next += 3;
                                }

                                Debug.Assert(ch < 0xFF, "Expecting ASCII character.");
                            }
                            next--; // for loop will increment


                            // Using encoder with no replacement fall-back will skip all invalid UTF-8 sequences.
                            Encoding noFallbackCharUTF8 = Encoding.GetEncoding(
                                                                                Encoding.UTF8.CodePage,
                                                                                new EncoderReplacementFallback(""),
                                                                                new DecoderReplacementFallback(""));

                            char[] unescapedChars = new char[bytes.Length];
                            int charCount = noFallbackCharUTF8.GetChars(bytes, 0, byteCount, unescapedChars, 0);


                            if (charCount != 0)
                            {
                                // If invalid sequences were present in the original escaped string, we need to
                                // copy the escaped versions of those sequences.
                                // Decoded Unicode values will be kept only when they are allowed by the URI/IRI RFC
                                // rules.
                                UriHelper.MatchUTF8Sequence(ref dest, unescapedChars, charCount, bytes,
                                    byteCount, component == UriComponents.Query, true);
                            }
                            else
                            {
                                // copy escaped sequence as is
                                for (int i = startSeq; i <= next; ++i)
                                {
                                    dest.Append(pInput[i]);
                                }
                            }
                        }
                    }
                    else
                    {
                        dest.Append(pInput[next]);
                    }
                }
                else if (ch > '\x7f')
                {
                    // unicode

                    char ch2;

                    if ((char.IsHighSurrogate(ch)) && (next + 1 < end))
                    {
                        ch2 = pInput[next + 1];
                        escape = !CheckIriUnicodeRange(ch, ch2, out surrogatePair, component == UriComponents.Query);
                        if (!escape)
                        {
                            // copy the two chars
                            dest.Append(pInput[next++]);
                            dest.Append(pInput[next]);
                        }
                    }
                    else
                    {
                        if (CheckIriUnicodeRange(ch, component == UriComponents.Query))
                        {
                            // copy it
                            dest.Append(pInput[next]);
                        }
                        else
                        {
                            // escape it
                            escape = true;
                        }
                    }
                }
                else
                {
                    // just copy the character
                    dest.Append(pInput[next]);
                }

                if (escape)
                {
                    const int MaxNumberOfBytesEncoded = 4;

                    byte[] encodedBytes = new byte[MaxNumberOfBytesEncoded];
                    fixed (byte* pEncodedBytes = &encodedBytes[0])
                    {
                        int encodedBytesCount = Encoding.UTF8.GetBytes(pInput + next, surrogatePair ? 2 : 1, pEncodedBytes, MaxNumberOfBytesEncoded);
                        Debug.Assert(encodedBytesCount <= MaxNumberOfBytesEncoded, "UTF8 encoder should not exceed specified byteCount");

                        bufferRemaining -= encodedBytesCount * percentEncodingLen;

                        for (int count = 0; count < encodedBytesCount; ++count)
                        {
                            UriHelper.EscapeAsciiChar(encodedBytes[count], ref dest);
                        }
                    }
                }
            }

            string result = dest.ToString();
            return result;
        }
    }
}
