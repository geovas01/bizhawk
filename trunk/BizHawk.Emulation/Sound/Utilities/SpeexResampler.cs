﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace BizHawk.Emulation.Sound.Utilities
{
	/// <summary>
	/// junk wrapper around LibSpeexDSP.  quite inefficient.  will be replaced
	/// </summary>
	public class SpeexResampler
	{
		static class LibSpeexDSP
		{
			public const int QUALITY_MAX = 10;
			public const int QUALITY_MIN = 0;
			public const int QUALITY_DEFAULT = 4;
			public const int QUALITY_VOIP = 3;
			public const int QUALITY_DESKTOP = 5;

			public enum RESAMPLER_ERR
			{
				SUCCESS = 0,
				ALLOC_FAILED = 1,
				BAD_STATE = 2,
				INVALID_ARG = 3,
				PTR_OVERLAP = 4,
				MAX_ERROR
			};

			/// <summary>
			/// Create a new resampler with integer input and output rates.
			/// </summary>
			/// <param name="nb_channels">Number of channels to be processed</param>
			/// <param name="in_rate">Input sampling rate (integer number of Hz).</param>
			/// <param name="out_rate">Output sampling rate (integer number of Hz).</param>
			/// <param name="quality">Resampling quality between 0 and 10, where 0 has poor quality and 10 has very high quality.</param>
			/// <param name="err"></param>
			/// <returns>Newly created resampler state</returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern IntPtr speex_resampler_init(uint nb_channels, uint in_rate, uint out_rate, int quality, ref RESAMPLER_ERR err);

			/// <summary>
			/// Create a new resampler with fractional input/output rates. The sampling
			/// rate ratio is an arbitrary rational number with both the numerator and
			/// denominator being 32-bit integers.
			/// </summary>
			/// <param name="nb_channels">Number of channels to be processed</param>
			/// <param name="ratio_num">Numerator of the sampling rate ratio</param>
			/// <param name="ratio_den">Denominator of the sampling rate ratio</param>
			/// <param name="in_rate">Input sampling rate rounded to the nearest integer (in Hz).</param>
			/// <param name="out_rate">Output sampling rate rounded to the nearest integer (in Hz).</param>
			/// <param name="quality">Resampling quality between 0 and 10, where 0 has poor quality and 10 has very high quality.</param>
			/// <param name="err"></param>
			/// <returns>Newly created resampler state</returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern IntPtr speex_resampler_init_frac(uint nb_channels, uint ratio_num, uint ratio_den, uint in_rate, uint out_rate, int quality, ref RESAMPLER_ERR err);

			/// <summary>
			/// Destroy a resampler state.
			/// </summary>
			/// <param name="st">Resampler state</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_destroy(IntPtr st);

			/// <summary>
			/// Resample a float array. The input and output buffers must *not* overlap.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="channel_index">Index of the channel to process for the multi-channel base (0 otherwise)</param>
			/// <param name="inp">Input buffer</param>
			/// <param name="in_len">Number of input samples in the input buffer. Returns the number of samples processed</param>
			/// <param name="outp">Output buffer</param>
			/// <param name="out_len">Size of the output buffer. Returns the number of samples written</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_process_float(IntPtr st, uint channel_index, float[] inp, ref uint in_len, float[] outp, ref uint out_len);

			/// <summary>
			/// Resample an int array. The input and output buffers must *not* overlap.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="channel_index">Index of the channel to process for the multi-channel base (0 otherwise)</param>
			/// <param name="inp">Input buffer</param>
			/// <param name="in_len">Number of input samples in the input buffer. Returns the number of samples processed</param>
			/// <param name="outp">Output buffer</param>
			/// <param name="out_len">Size of the output buffer. Returns the number of samples written</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_process_int(IntPtr st, uint channel_index, short[] inp, ref uint in_len, short[] outp, ref uint out_len);

			/// <summary>
			/// Resample an interleaved float array. The input and output buffers must *not* overlap.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="inp">Input buffer</param>
			/// <param name="in_len">Number of input samples in the input buffer. Returns the number of samples processed. This is all per-channel.</param>
			/// <param name="outp">Output buffer</param>
			/// <param name="out_len">Size of the output buffer. Returns the number of samples written. This is all per-channel.</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_process_interleaved_float(IntPtr st, float[] inp, ref uint in_len, float[] outp, ref uint out_len);

			/// <summary>
			/// Resample an interleaved int array. The input and output buffers must *not* overlap.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="inp">Input buffer</param>
			/// <param name="in_len">Number of input samples in the input buffer. Returns the number of samples processed. This is all per-channel.</param>
			/// <param name="outp">Output buffer</param>
			/// <param name="out_len">Size of the output buffer. Returns the number of samples written. This is all per-channel.</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_process_interleaved_int(IntPtr st, short[] inp, ref uint in_len, short[] outp, ref uint out_len);

			/// <summary>
			/// Set (change) the input/output sampling rates (integer value).
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="in_rate">Input sampling rate (integer number of Hz).</param>
			/// <param name="out_rate">Output sampling rate (integer number of Hz).</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_set_rate(IntPtr st, uint in_rate, uint out_rate);

			/// <summary>
			/// Get the current input/output sampling rates (integer value).
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="in_rate">Input sampling rate (integer number of Hz) copied.</param>
			/// <param name="out_rate">Output sampling rate (integer number of Hz) copied.</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_get_rate(IntPtr st, ref uint in_rate, ref uint out_rate);

			/// <summary>
			/// Set (change) the input/output sampling rates and resampling ratio (fractional values in Hz supported).
			/// </summary>
			/// <param name="st">esampler state</param>
			/// <param name="ratio_num">Numerator of the sampling rate ratio</param>
			/// <param name="ratio_den">Denominator of the sampling rate ratio</param>
			/// <param name="in_rate">Input sampling rate rounded to the nearest integer (in Hz).</param>
			/// <param name="out_rate">Output sampling rate rounded to the nearest integer (in Hz).</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_set_rate_frac(IntPtr st, uint ratio_num, uint ratio_den, uint in_rate, uint out_rate);

			/// <summary>
			/// Get the current resampling ratio. This will be reduced to the least common denominator.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="ratio_num">Numerator of the sampling rate ratio copied</param>
			/// <param name="ratio_den">Denominator of the sampling rate ratio copied</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_get_ratio(IntPtr st, ref uint ratio_num, ref uint ratio_den);

			/// <summary>
			/// Set (change) the conversion quality.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="quality">Resampling quality between 0 and 10, where 0 has poor quality and 10 has very high quality.</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_set_quality(IntPtr st, int quality);

			/// <summary>
			/// Get the conversion quality.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="quality">Resampling quality between 0 and 10, where 0 has poor quality and 10 has very high quality.</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_get_quality(IntPtr st, ref int quality);

			/// <summary>
			/// Set (change) the input stride.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="stride">Input stride</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_set_input_stride(IntPtr st, uint stride);

			/// <summary>
			/// Get the input stride.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="stride">Input stride copied</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_get_input_stride(IntPtr st, ref uint stride);

			/// <summary>
			/// Set (change) the output stride.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="stride">Output stride</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_set_output_stride(IntPtr st, uint stride);

			/// <summary>
			/// Get the output stride.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <param name="stride">Output stride copied</param>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern void speex_resampler_get_output_stride(IntPtr st, ref uint stride);

			/// <summary>
			/// Get the latency in input samples introduced by the resampler.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern int speex_resampler_get_input_latency(IntPtr st);

			/// <summary>
			/// Get the latency in output samples introduced by the resampler.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern int speex_resampler_get_output_latency(IntPtr st);

			/// <summary>
			/// Make sure that the first samples to go out of the resamplers don't have
			/// leading zeros. This is only useful before starting to use a newly created
			/// resampler. It is recommended to use that when resampling an audio file, as
			/// it will generate a file with the same length. For real-time processing,
			/// it is probably easier not to use this call (so that the output duration
			/// is the same for the first frame).
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_skip_zeroes(IntPtr st);

			/// <summary>
			/// Reset a resampler so a new (unrelated) stream can be processed.
			/// </summary>
			/// <param name="st">Resampler state</param>
			/// <returns></returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern RESAMPLER_ERR speex_resampler_reset_mem(IntPtr st);

			/// <summary>
			/// Returns the English meaning for an error code
			/// </summary>
			/// <param name="err">Error code</param>
			/// <returns>English string</returns>
			[DllImport("libspeexdsp.dll", CallingConvention = CallingConvention.Cdecl)]
			public static extern string speex_resampler_strerror(RESAMPLER_ERR err);
		}


		/// <summary>
		/// opaque pointer to state
		/// </summary>
		IntPtr st;

		public SpeexResampler(int quality)
		{
			LibSpeexDSP.RESAMPLER_ERR err = 0;

			st = LibSpeexDSP.speex_resampler_init_frac(2, 64081, 88200, 32041, 44100, quality, ref err);
		}

		public void StartSession(double ratio)
		{
			
		}


		public void ResampleChunk(Queue<short> input, Queue<short> output, bool finish)
		{
			while (input.Count > 0)
			{
				short[] ina = input.ToArray();

				short[] outa = new short[8192];

				uint inal = (uint)ina.Length / 2;
				uint outal = (uint)outa.Length / 2;

				// very important: feeding too big a buffer at once causes garbage to come back
				// don't know what "too big a buffer" is in general
				if (inal > 512) inal = 512;


				LibSpeexDSP.speex_resampler_process_interleaved_int(st, ina, ref inal, outa, ref outal);


				while (inal > 0)
				{
					input.Dequeue();
					input.Dequeue();
					inal--;
				}

				int i = 0;
				if (outal == 0)
				{
					// resampler refuses to make more data; bail
					return;
				}
				while (outal > 0)
				{
					output.Enqueue(outa[i++]);
					output.Enqueue(outa[i++]);
					outal--;
				}
			}
		}
	}
}

