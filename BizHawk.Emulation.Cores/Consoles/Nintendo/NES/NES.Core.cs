using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using BizHawk.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Components.M6502;

#pragma warning disable 162

namespace BizHawk.Emulation.Cores.Nintendo.NES
{
	public partial class NES : IEmulator
	{
		//hardware/state
		public MOS6502X cpu;
		int cpu_accumulate; //cpu timekeeper
		public PPU ppu;
		public APU apu;
		public byte[] ram;
		NESWatch[] sysbus_watch = new NESWatch[65536];
		public byte[] CIRAM; //AKA nametables
		string game_name = string.Empty; //friendly name exposed to user and used as filename base
		CartInfo cart; //the current cart prototype. should be moved into the board, perhaps
		internal INESBoard Board; //the board hardware that is currently driving things
		EDetectionOrigin origin = EDetectionOrigin.None;
		int sprdma_countdown;
		bool _irq_apu; //various irq signals that get merged to the cpu irq pin
		/// <summary>clock speed of the main cpu in hz</summary>
		public int cpuclockrate { get; private set; }

		//irq state management
		public bool irq_apu { get { return _irq_apu; } set { _irq_apu = value; } }

		//user configuration 
		int[,] palette = new int[64,3];
		int[] palette_compiled = new int[64*8];

		// new input system
		NESControlSettings ControllerSettings; // this is stored internally so that a new change of settings won't replace
		IControllerDeck ControllerDeck;
		byte latched4016;

		private DisplayType _display_type = DisplayType.NTSC;

		//Sound config
		public void SetSquare1(int v) { apu.Square1V = v; }
		public void SetSquare2(int v) { apu.Square2V = v; }
		public void SetTriangle(int v) { apu.TriangleV = v; }
		public void SetNoise(int v) { apu.NoiseV = v; }
		public void SetDMC(int v) { apu.DMCV = v; }

		/// <summary>
		/// for debugging only!
		/// </summary>
		/// <returns></returns>
		public INESBoard GetBoard()
		{
			return Board;
		}

		public void Dispose()
		{
			if (magicSoundProvider != null)
				magicSoundProvider.Dispose();
			magicSoundProvider = null;
		}

		class MagicSoundProvider : ISoundProvider, ISyncSoundProvider, IDisposable
		{
			BlipBuffer blip;
			NES nes;

			const int blipbuffsize = 4096;

			public MagicSoundProvider(NES nes, uint infreq)
			{
				this.nes = nes;

				blip = new BlipBuffer(blipbuffsize);
				blip.SetRates(infreq, 44100);

				//var actualMetaspu = new Sound.MetaspuSoundProvider(Sound.ESynchMethod.ESynchMethod_V);
				//1.789773mhz NTSC
				//resampler = new Sound.Utilities.SpeexResampler(2, infreq, 44100 * APU.DECIMATIONFACTOR, infreq, 44100, actualMetaspu.buffer.enqueue_samples);
				//output = new Sound.Utilities.DCFilter(actualMetaspu);
			}

			public void GetSamples(short[] samples)
			{
				//Console.WriteLine("Sync: {0}", nes.apu.dlist.Count);
				int nsamp = samples.Length / 2;
				if (nsamp > blipbuffsize) // oh well.
					nsamp = blipbuffsize;
				uint targetclock = (uint)blip.ClocksNeeded(nsamp);
				uint actualclock = nes.apu.sampleclock;
				foreach (var d in nes.apu.dlist)
					blip.AddDelta(d.time * targetclock / actualclock, d.value);
				nes.apu.dlist.Clear();
				blip.EndFrame(targetclock);
				nes.apu.sampleclock = 0;

				blip.ReadSamples(samples, nsamp, true);
				// duplicate to stereo
				for (int i = 0; i < nsamp * 2; i += 2)
					samples[i + 1] = samples[i];

				//mix in the cart's extra sound circuit
				nes.Board.ApplyCustomAudio(samples);
			}

			public void GetSamples(out short[] samples, out int nsamp)
			{
				//Console.WriteLine("ASync: {0}", nes.apu.dlist.Count);
				foreach (var d in nes.apu.dlist)
					blip.AddDelta(d.time, d.value);
				nes.apu.dlist.Clear();
				blip.EndFrame(nes.apu.sampleclock);
				nes.apu.sampleclock = 0;

				nsamp = blip.SamplesAvailable();
				samples = new short[nsamp * 2];

				blip.ReadSamples(samples, nsamp, true);
				// duplicate to stereo
				for (int i = 0; i < nsamp * 2; i += 2)
					samples[i + 1] = samples[i];

				nes.Board.ApplyCustomAudio(samples);
			}

			public void DiscardSamples()
			{
				nes.apu.dlist.Clear();
				nes.apu.sampleclock = 0;
			}

			public int MaxVolume { get; set; }

			public void Dispose()
			{
				if (blip != null)
				{
					blip.Dispose();
					blip = null;
				}
			}
		}
		MagicSoundProvider magicSoundProvider;

		public void HardReset()
		{
			cpu = new MOS6502X();
			cpu.SetCallbacks(ReadMemory, ReadMemory, PeekMemory, WriteMemory);

			cpu.BCD_Enabled = false;
			cpu.OnExecFetch = ExecFetch;
			ppu = new PPU(this);
			ram = new byte[0x800];
			CIRAM = new byte[0x800];
			
			// wire controllers
			// todo: allow changing this
			ControllerDeck = ControllerSettings.Instantiate(ppu.LightGunCallback);
			// set controller definition first time only
			if (ControllerDefinition == null)
			{
				ControllerDefinition = new ControllerDefinition(ControllerDeck.GetDefinition());
				ControllerDefinition.Name = "NES Controller";
				// controls other than the deck
				ControllerDefinition.BoolButtons.Add("Power");
				ControllerDefinition.BoolButtons.Add("Reset");
				if (Board is FDS)
				{
					var b = Board as FDS;
					ControllerDefinition.BoolButtons.Add("FDS Eject");
					for (int i = 0; i < b.NumSides; i++)
						ControllerDefinition.BoolButtons.Add("FDS Insert " + i);
				}
			}

			// don't replace the magicSoundProvider on reset, as it's not needed
			// if (magicSoundProvider != null) magicSoundProvider.Dispose();

			// set up region
			switch (_display_type)
			{
				case Common.DisplayType.PAL:
					apu = new APU(this, apu, true);
					ppu.region = PPU.Region.PAL;
					CoreComm.VsyncNum = 50;
					CoreComm.VsyncDen = 1;
					cpuclockrate = 1662607;
					cpu_sequence = cpu_sequence_PAL;
					_display_type = DisplayType.PAL;
					break;
				case Common.DisplayType.NTSC:
					apu = new APU(this, apu, false);
					ppu.region = PPU.Region.NTSC;
					CoreComm.VsyncNum = 39375000;
					CoreComm.VsyncDen = 655171;
					cpuclockrate = 1789773;
					cpu_sequence = cpu_sequence_NTSC;
					break;
				// this is in bootgod, but not used at all
				case Common.DisplayType.DENDY:
					apu = new APU(this, apu, false);
					ppu.region = PPU.Region.Dendy;
					CoreComm.VsyncNum = 50;
					CoreComm.VsyncDen = 1;
					cpuclockrate = 1773448;
					cpu_sequence = cpu_sequence_NTSC;
					_display_type = DisplayType.DENDY;
					break;
				default:
					throw new Exception("Unknown displaytype!");
			}
			if (magicSoundProvider == null)
				magicSoundProvider = new MagicSoundProvider(this, (uint)cpuclockrate);

			BoardSystemHardReset();

			//check fceux's PowerNES and FCEU_MemoryRand function for more information:
			//relevant games: Cybernoid; Minna no Taabou no Nakayoshi Daisakusen; Huang Di; and maybe mechanized attack
			for(int i=0;i<0x800;i++) if((i&4)!=0) ram[i] = 0xFF; else ram[i] = 0x00;

			SetupMemoryDomains();

			//in this emulator, reset takes place instantaneously
			cpu.PC = (ushort)(ReadMemory(0xFFFC) | (ReadMemory(0xFFFD) << 8));
			cpu.P = 0x34;
			cpu.S = 0xFD;
		}

		bool resetSignal;
		bool hardResetSignal;
		public void FrameAdvance(bool render, bool rendersound)
		{
			if (Tracer.Enabled)
				cpu.TraceCallback = (s) => Tracer.Put(s);
			else
				cpu.TraceCallback = null;

			lagged = true;
			if (resetSignal)
			{
				Board.NESSoftReset();
				cpu.NESSoftReset();
				apu.NESSoftReset();
				ppu.NESSoftReset();
			}
			else if (hardResetSignal)
			{
				HardReset();
			}

			Frame++;

			//if (resetSignal)
				//Controller.UnpressButton("Reset");   TODO fix this
			resetSignal = Controller["Reset"];
			hardResetSignal = Controller["Power"];

			if (Board is FDS)
			{
				var b = Board as FDS;
				if (Controller["FDS Eject"])
					b.Eject();
				for (int i = 0; i < b.NumSides; i++)
					if (Controller["FDS Insert " + i])
						b.InsertSide(i);
			}

			ppu.FrameAdvance();
			if (lagged)
			{
				_lagcount++;
				islag = true;
			}
			else
				islag = false;

			videoProvider.FillFrameBuffer();
		}

		//PAL:
		//0 15 30 45 60 -> 12 27 42 57 -> 9 24 39 54 -> 6 21 36 51 -> 3 18 33 48 -> 0
		//sequence of ppu clocks per cpu clock: 4,3,3,3,3
		//NTSC:
		//sequence of ppu clocks per cpu clock: 3
		ByteBuffer cpu_sequence;
		static ByteBuffer cpu_sequence_NTSC = new ByteBuffer(new byte[]{3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3});
		static ByteBuffer cpu_sequence_PAL = new ByteBuffer(new byte[]{4,3,3,3,3,4,3,3,3,3,4,3,3,3,3,4,3,3,3,3,4,3,3,3,3,4,3,3,3,3,4,3,3,3,3,4,3,3,3,3});
		public int cpu_step, cpu_stepcounter, cpu_deadcounter;

#if VS2012
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
		internal void RunCpuOne()
		{
			cpu_stepcounter++;
			if (cpu_stepcounter == cpu_sequence[cpu_step])
			{
				cpu_step++;
				cpu_step &= 31;
				cpu_stepcounter = 0;

				if (sprdma_countdown > 0)
				{
					sprdma_countdown--;
					if (sprdma_countdown == 0)
					{
						//its weird that this is 514.. normally itd be 512 (and people would say its wrong) or 513 (and people would say its right)
						//but 514 passes test 4-irq_and_dma
						cpu_deadcounter += 514;
					}
				}

				if (cpu_deadcounter > 0)
					cpu_deadcounter--;
				else
				{
					cpu.IRQ = _irq_apu || Board.IRQSignal;
					cpu.ExecuteOne();
				}

				apu.RunOne();
				Board.ClockCPU();
				ppu.PostCpuInstructionOne();
			}
		}

#if VS2012
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
		public byte ReadReg(int addr)
		{
			switch (addr)
			{
				case 0x4000: case 0x4001: case 0x4002: case 0x4003:
				case 0x4004: case 0x4005: case 0x4006: case 0x4007:
				case 0x4008: case 0x4009: case 0x400A: case 0x400B:
				case 0x400C: case 0x400D: case 0x400E: case 0x400F:
				case 0x4010: case 0x4011: case 0x4012: case 0x4013:
					return apu.ReadReg(addr);
				case 0x4014: /*OAM DMA*/ break;
				case 0x4015: return apu.ReadReg(addr); 
				case 0x4016:
				case 0x4017:
					return read_joyport(addr);
				default:
					//Console.WriteLine("read register: {0:x4}", addr);
					break;

			}
			return 0xFF;
		}

		public byte PeekReg(int addr)
		{
			switch (addr)
			{
				case 0x4000: case 0x4001: case 0x4002: case 0x4003:
				case 0x4004: case 0x4005: case 0x4006: case 0x4007:
				case 0x4008: case 0x4009: case 0x400A: case 0x400B:
				case 0x400C: case 0x400D: case 0x400E: case 0x400F:
				case 0x4010: case 0x4011: case 0x4012: case 0x4013:
					return apu.PeekReg(addr);
				case 0x4014: /*OAM DMA*/ break;
				case 0x4015: return apu.PeekReg(addr); 
				case 0x4016:
				case 0x4017:
					return peek_joyport(addr);
				default:
					//Console.WriteLine("read register: {0:x4}", addr);
					break;

			}
			return 0xFF;
		}

		void WriteReg(int addr, byte val)
		{
			switch (addr)
			{
				case 0x4000: case 0x4001: case 0x4002: case 0x4003:
				case 0x4004: case 0x4005: case 0x4006: case 0x4007:
				case 0x4008: case 0x4009: case 0x400A: case 0x400B:
				case 0x400C: case 0x400D: case 0x400E: case 0x400F:
				case 0x4010: case 0x4011: case 0x4012: case 0x4013:
					apu.WriteReg(addr, val);
					break;
				case 0x4014: Exec_OAMDma(val); break;
				case 0x4015: apu.WriteReg(addr, val); break;
				case 0x4016:
					write_joyport(val);
					break;
				case 0x4017: apu.WriteReg(addr, val); break;
				default:
					//Console.WriteLine("wrote register: {0:x4} = {1:x2}", addr, val);
					break;
			}
		}

		void write_joyport(byte value)
		{
			var si = new StrobeInfo(latched4016, value);
			ControllerDeck.Strobe(si, Controller);
			latched4016 = value;
		}

		byte read_joyport(int addr)
		{
			InputCallbacks.Call();
			lagged = false;
				byte ret = addr == 0x4016 ? ControllerDeck.ReadA(Controller) : ControllerDeck.ReadB(Controller);
				ret &= 0x1f;
				ret |= (byte)(0xe0 & DB);
				return ret;
		}

		byte peek_joyport(int addr)
		{
			// at the moment, the new system doesn't support peeks
			return 0;
		}

		void Exec_OAMDma(byte val)
		{
			ushort addr = (ushort)(val << 8);
			for (int i = 0; i < 256; i++)
			{
				byte db = ReadMemory((ushort)addr);
				WriteMemory(0x2004, db);
				addr++;
			}
			//schedule a sprite dma event for beginning 1 cycle in the future.
			//this receives 2 because thats just the way it works out.
			sprdma_countdown = 2;
		}

		/// <summary>
		/// sets the provided palette as current
		/// </summary>
		private void SetPalette(int[,] pal)
		{
			Array.Copy(pal,palette,64*3);
			for(int i=0;i<64*8;i++)
			{
				int d = i >> 6;
				int c = i & 63;
				int r = palette[c, 0];
				int g = palette[c, 1];
				int b = palette[c, 2];
				Palettes.ApplyDeemphasis(ref r, ref g, ref b, d);
				palette_compiled[i] = (int)unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
			}
		}

		/// <summary>
		/// looks up an internal NES pixel value to an rgb int (applying the core's current palette and assuming no deemph)
		/// </summary>
		public int LookupColor(int pixel)
		{
			return palette_compiled[pixel];
		}

		public byte DummyReadMemory(ushort addr) { return 0; }

		private void ApplySystemBusPoke(int addr, byte value)
		{
			if (addr < 0x2000)
			{
				ram[(addr & 0x7FF)] = value;
			}
			else if (addr < 0x4000)
			{
				ppu.WriteReg((addr & 0x07), value);
			}
			else if (addr < 0x4020)
			{
				WriteReg(addr, value);
			}
			else
			{
				ApplyGameGenie(addr, value, null); //Apply a cheat to the remaining regions since they have no direct access, this may not be the best way to handle this situation
			}
		}

		public byte PeekMemory(ushort addr)
		{
			byte ret;

			if (addr >= 0x4020)
			{
				ret = Board.PeekCart(addr); //easy optimization, since rom reads are so common, move this up (reordering the rest of these elseifs is not easy)
			}
			else if (addr < 0x0800)
			{
				ret = ram[addr];
			}
			else if (addr < 0x2000)
			{
				ret = ram[addr & 0x7FF];
			}
			else if (addr < 0x4000)
			{
				ret = Board.PeekReg2xxx(addr);
			}
			else if (addr < 0x4020)
			{
				ret = PeekReg(addr); //we're not rebasing the register just to keep register names canonical
			}
			else
			{
				throw new Exception("Woopsie-doodle!");
				ret = 0xFF;
			}

			return ret;
		}

		//old data bus values from previous reads
		public byte DB;

		public void ExecFetch(ushort addr)
		{
			MemoryCallbacks.CallExecutes(addr);
		}

		public byte ReadMemory(ushort addr)
		{
			byte ret;
			
			if (addr >= 0x8000)
			{
				ret = Board.ReadPRG(addr - 0x8000); //easy optimization, since rom reads are so common, move this up (reordering the rest of these elseifs is not easy)
			}
			else if (addr < 0x0800)
			{
				ret = ram[addr];
			}
			else if (addr < 0x2000)
			{
				ret = ram[addr & 0x7FF];
			}
			else if (addr < 0x4000)
			{
				ret = Board.ReadReg2xxx(addr);
			}
			else if (addr < 0x4020)
			{
				ret = ReadReg(addr); //we're not rebasing the register just to keep register names canonical
			}
			else if (addr < 0x6000)
			{
				ret = Board.ReadEXP(addr - 0x4000);
			}
			else
			{
				ret = Board.ReadWRAM(addr - 0x6000);
			}
			
			//handle breakpoints and stuff.
			//the idea is that each core can implement its own watch class on an address which will track all the different kinds of monitors and breakpoints and etc.
			//but since freeze is a common case, it was implemented through its own mechanisms
			if (sysbus_watch[addr] != null)
			{
				sysbus_watch[addr].Sync();
				ret = sysbus_watch[addr].ApplyGameGenie(ret);
			}

			MemoryCallbacks.CallReads(addr);

			DB = ret;

			return ret;
		}

		public void ApplyGameGenie(int addr, byte value, byte? compare)
		{
			if (addr < sysbus_watch.Length)
			{
				GetWatch(NESWatch.EDomain.Sysbus, addr).SetGameGenie(compare, value);
			}
		}

		public void RemoveGameGenie(int addr)
		{
			if (addr < sysbus_watch.Length)
			{
				GetWatch(NESWatch.EDomain.Sysbus, addr).RemoveGameGenie();
			}
		}

		public void WriteMemory(ushort addr, byte value)
		{
			if (addr < 0x0800)
			{
				ram[addr] = value;
			}
			else if (addr < 0x2000)
			{
				ram[addr & 0x7FF] = value;
			}
			else if (addr < 0x4000)
			{
				Board.WriteReg2xxx(addr,value);
			}
			else if (addr < 0x4020)
			{
				WriteReg(addr, value);  //we're not rebasing the register just to keep register names canonical
			}
			else if (addr < 0x6000)
			{
				Board.WriteEXP(addr - 0x4000, value);
			}
			else if (addr < 0x8000)
			{
				Board.WriteWRAM(addr - 0x6000, value);
			}
			else
			{
				Board.WritePRG(addr - 0x8000, value);
			}

			MemoryCallbacks.CallWrites(addr);
		}

	}
}