using System;
using System.Numerics;

namespace NistTests
{
    /// <summary>
    /// Minimal FFT utilities for the NIST SP 800-22 §2.6 Discrete Fourier Transform test.
    /// Exposes a Cooley-Tukey radix-2 FFT for power-of-two sizes and Bluestein's chirp-z
    /// algorithm for arbitrary sizes (reduces to a larger radix-2 FFT).
    /// </summary>
    public static class Fft
    {
        public static Complex[] Forward(double[] real)
        {
            int n = real.Length;
            if (IsPowerOfTwo(n))
            {
                var a = new Complex[n];
                for (int i = 0; i < n; i++) a[i] = new Complex(real[i], 0.0);
                Radix2(a, false);
                return a;
            }
            return Bluestein(real);
        }

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        private static void Radix2(Complex[] a, bool inverse)
        {
            int n = a.Length;
            // Bit-reversal permutation
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) (a[i], a[j]) = (a[j], a[i]);
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = (inverse ? 2.0 : -2.0) * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    for (int k = 0; k < len / 2; k++)
                    {
                        Complex u = a[i + k];
                        Complex v = a[i + k + len / 2] * w;
                        a[i + k] = u + v;
                        a[i + k + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
            if (inverse)
            {
                for (int i = 0; i < n; i++) a[i] /= n;
            }
        }

        /// <summary>
        /// Bluestein's algorithm: DFT of arbitrary length n via convolution on a power-of-two size.
        /// X_k = w_k * conv(a, b)_k where a_j = x_j * w_j, w_j = exp(-i π j² / n), b_j = conj(w_j).
        /// </summary>
        private static Complex[] Bluestein(double[] real)
        {
            int n = real.Length;
            int m = 1;
            while (m < 2 * n - 1) m <<= 1;

            Complex[] a = new Complex[m];
            Complex[] b = new Complex[m];
            for (int j = 0; j < n; j++)
            {
                double phase = -Math.PI * ((long)j * j % (2L * n)) / n;
                Complex w = new Complex(Math.Cos(phase), Math.Sin(phase));
                a[j] = real[j] * w;
                b[j] = Complex.Conjugate(w);
                if (j > 0) b[m - j] = Complex.Conjugate(w);
            }

            Radix2(a, false);
            Radix2(b, false);
            for (int i = 0; i < m; i++) a[i] *= b[i];
            Radix2(a, true);

            Complex[] result = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                double phase = -Math.PI * ((long)k * k % (2L * n)) / n;
                Complex w = new Complex(Math.Cos(phase), Math.Sin(phase));
                result[k] = a[k] * w;
            }
            return result;
        }
    }
}
