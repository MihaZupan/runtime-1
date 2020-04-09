// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Globalization
{
    internal static partial class Normalization
    {
        internal static bool IsNormalized(string strInput, NormalizationForm normalizationForm)
        {
            if (GlobalizationMode.Invariant)
            {
                // In Invariant mode we assume all characters are normalized.
                // This is because we don't support any linguistic operation on the strings
                return true;
            }

            ValidateArguments(strInput, normalizationForm);

            int ret;
            unsafe
            {
                fixed (char* pInput = strInput)
                {
                    ret = Interop.Globalization.IsNormalized(normalizationForm, pInput, strInput.Length);
                }
            }

            if (ret == -1)
            {
                throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));
            }

            return ret == 1;
        }

        internal static string Normalize(string strInput, NormalizationForm normalizationForm)
        {
            if (GlobalizationMode.Invariant)
            {
                // In Invariant mode we assume all characters are normalized.
                // This is because we don't support any linguistic operation on the strings
                return strInput;
            }

            ValidateArguments(strInput, normalizationForm);

            char[]? toReturn = null;
            try
            {
                Span<char> buffer = strInput.Length <= 512
                    ? stackalloc char[512]
                    : (toReturn = ArrayPool<char>.Shared.Rent(strInput.Length));

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int realLen;
                    unsafe
                    {
                        fixed (char* pInput = strInput)
                        fixed (char* pDest = &MemoryMarshal.GetReference(buffer))
                        {
                            realLen = Interop.Globalization.NormalizeString(normalizationForm, pInput, strInput.Length, pDest, buffer.Length);
                        }
                    }

                    if (realLen == -1)
                    {
                        throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));
                    }

                    if (realLen <= buffer.Length)
                    {
                        return new string(buffer.Slice(0, realLen));
                    }

                    Debug.Assert(realLen > 512);

                    if (attempt == 0)
                    {
                        if (toReturn != null)
                        {
                            // Clear toReturn first to ensure we don't return the same buffer twice
                            char[] temp = toReturn;
                            toReturn = null;
                            ArrayPool<char>.Shared.Return(temp);
                        }

                        buffer = toReturn = ArrayPool<char>.Shared.Rent(realLen);
                    }
                }

                throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));
            }
            finally
            {
                if (toReturn != null)
                {
                    ArrayPool<char>.Shared.Return(toReturn);
                }
            }
        }

        // -----------------------------
        // ---- PAL layer ends here ----
        // -----------------------------

        private static void ValidateArguments(string strInput, NormalizationForm normalizationForm)
        {
            Debug.Assert(strInput != null);

            if (normalizationForm != NormalizationForm.FormC && normalizationForm != NormalizationForm.FormD &&
                normalizationForm != NormalizationForm.FormKC && normalizationForm != NormalizationForm.FormKD)
            {
                throw new ArgumentException(SR.Argument_InvalidNormalizationForm, nameof(normalizationForm));
            }

            if (HasInvalidUnicodeSequence(strInput))
            {
                throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(strInput));
            }
        }

        /// <summary>
        /// ICU does not signal an error during normalization if the input string has invalid unicode,
        /// unlike Windows (which uses the ERROR_NO_UNICODE_TRANSLATION error value to signal an error).
        ///
        /// We walk the string ourselves looking for these bad sequences so we can continue to throw
        /// ArgumentException in these cases.
        /// </summary>
        private static bool HasInvalidUnicodeSequence(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c < '\ud800')
                {
                    continue;
                }

                if (c == '\uFFFE')
                {
                    return true;
                }

                // If we see low surrogate before a high one, the string is invalid.
                if (char.IsLowSurrogate(c))
                {
                    return true;
                }

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                    {
                        // A high surrogate at the end of the string or a high surrogate
                        // not followed by a low surrogate
                        return true;
                    }
                    else
                    {
                        i++; // consume the low surrogate.
                        continue;
                    }
                }
            }

            return false;
        }
    }
}
