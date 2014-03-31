﻿using System;
using System.Collections.Generic;
using System.IO;

using BizHawk.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Common.Components;
using BizHawk.Emulation.Cores.Components.Z80;

namespace BizHawk.Emulation.Cores.ColecoVision
{
	public sealed partial class ColecoVision : IEmulator
	{
		// ROM
		public byte[] RomData;
		public int RomLength;

		public byte[] BiosRom;

		// Machine
		public Z80A Cpu;
		public TMS9918A VDP;
		public SN76489 PSG;
		public byte[] Ram = new byte[1024];

		public ColecoVision(CoreComm comm, GameInfo game, byte[] rom, object SyncSettings)
		{
			CoreComm = comm;
			this.SyncSettings = (ColecoSyncSettings)SyncSettings ?? new ColecoSyncSettings();
			bool skipbios = this.SyncSettings.SkipBiosIntro;

			Cpu = new Z80A();
			Cpu.ReadMemory = ReadMemory;
			Cpu.WriteMemory = WriteMemory;
			Cpu.ReadHardware = ReadPort;
			Cpu.WriteHardware = WritePort;

			VDP = new TMS9918A(Cpu);
			PSG = new SN76489();

			// TODO: hack to allow bios-less operation would be nice, no idea if its feasible
			string biosPath = CoreComm.CoreFileProvider.GetFirmwarePath("Coleco", "Bios", true, "Coleco BIOS file is required.");
			BiosRom = File.ReadAllBytes(biosPath);

			// gamedb can overwrite the syncsettings; this is ok
			if (game["NoSkip"])
				skipbios = false;
			LoadRom(rom, skipbios);
			this.game = game;
			SetupMemoryDomains();
		}

		public MemoryDomainList MemoryDomains { get { return memoryDomains; } }
		MemoryDomainList memoryDomains;
		const ushort RamSizeMask = 0x03FF;
		void SetupMemoryDomains()
		{
			var domains = new List<MemoryDomain>(3);
			var MainMemoryDomain = new MemoryDomain("Main RAM", Ram.Length, MemoryDomain.Endian.Little,
				addr => Ram[addr],
				(addr, value) => Ram[addr] = value);
			var VRamDomain = new MemoryDomain("Video RAM", VDP.VRAM.Length, MemoryDomain.Endian.Little,
				addr => VDP.VRAM[addr],
				(addr, value) => VDP.VRAM[addr] = value);
			var SystemBusDomain = new MemoryDomain("System Bus", 0x10000, MemoryDomain.Endian.Little,
				(addr) =>
				{
					if (addr < 0 || addr >= 65536)
						throw new ArgumentOutOfRangeException();
					return Cpu.ReadMemory((ushort)addr);
				},
				(addr, value) =>
				{
					if (addr < 0 || addr >= 65536)
						throw new ArgumentOutOfRangeException();
					Cpu.WriteMemory((ushort)addr, value);
				});

			domains.Add(MainMemoryDomain);
			domains.Add(VRamDomain);
			domains.Add(SystemBusDomain);
			memoryDomains = new MemoryDomainList(domains);
		}

		public void FrameAdvance(bool render, bool renderSound)
		{
			Frame++;
			isLag = true;
			PSG.BeginFrame(Cpu.TotalExecutedCycles);
			VDP.ExecuteFrame();
			PSG.EndFrame(Cpu.TotalExecutedCycles);

			if (isLag)
				LagCount++;
		}

		void LoadRom(byte[] rom, bool skipbios)
		{
			RomData = new byte[0x8000];
			for (int i = 0; i < 0x8000; i++)
				RomData[i] = rom[i % rom.Length];

			// hack to skip colecovision title screen
			if (skipbios)
			{
				RomData[0] = 0x55;
				RomData[1] = 0xAA;
			}
		}

		byte ReadPort(ushort port)
		{
			port &= 0xFF;

			if (port >= 0xA0 && port < 0xC0)
			{
				if ((port & 1) == 0)
					return VDP.ReadData();
				return VDP.ReadVdpStatus();
			}

			if (port >= 0xE0)
			{
				if ((port & 1) == 0)
					return ReadController1();
				return ReadController2();
			}

			return 0xFF;
		}

		void WritePort(ushort port, byte value)
		{
			port &= 0xFF;

			if (port >= 0xA0 && port <= 0xBF)
			{
				if ((port & 1) == 0)
					VDP.WriteVdpData(value);
				else
					VDP.WriteVdpControl(value);
				return;
			}

			if (port >= 0x80 && port <= 0x9F)
			{
				InputPortSelection = InputPortMode.Right;
				return;
			}

			if (port >= 0xC0 && port <= 0xDF)
			{
				InputPortSelection = InputPortMode.Left;
				return;
			}

			if (port >= 0xE0)
			{
				PSG.WritePsgData(value, Cpu.TotalExecutedCycles);
				return;
			}
		}

		public byte[] ReadSaveRam() { return null; }
		public void StoreSaveRam(byte[] data) { }
		public void ClearSaveRam() { }
		public bool SaveRamModified { get; set; }

		public bool DeterministicEmulation { get { return true; } }

		public bool BinarySaveStatesPreferred { get { return false; } }
		public void SaveStateBinary(BinaryWriter bw) { SyncState(Serializer.CreateBinaryWriter(bw)); }
		public void LoadStateBinary(BinaryReader br) { SyncState(Serializer.CreateBinaryReader(br)); PostLoadState(); }
		public void SaveStateText(TextWriter tw) { SyncState(Serializer.CreateTextWriter(tw)); }
		public void LoadStateText(TextReader tr) { SyncState(Serializer.CreateTextReader(tr)); PostLoadState(); }

		void SyncState(Serializer ser)
		{
			ser.BeginSection("Coleco");
			Cpu.SyncState(ser);
			VDP.SyncState(ser);
			PSG.SyncState(ser);
			ser.Sync("RAM", ref Ram, false);
			ser.Sync("Frame", ref frame);
			ser.Sync("LagCount", ref lagCount);
			ser.Sync("IsLag", ref isLag);
			ser.EndSection();
		}

		void PostLoadState()
		{
			VDP.PostLoadState();
			PSG.PostLoadState();
		}

		byte[] stateBuffer;
		public byte[] SaveStateBinary()
		{
			if (stateBuffer == null)
			{
				var stream = new MemoryStream();
				var writer = new BinaryWriter(stream);
				SaveStateBinary(writer);
				stateBuffer = stream.ToArray();
				writer.Close();
				return stateBuffer;
			}
			else
			{
				var stream = new MemoryStream(stateBuffer);
				var writer = new BinaryWriter(stream);
				SaveStateBinary(writer);
				writer.Close();
				return stateBuffer;
			}
		}

		public void Dispose() { }
		public void ResetCounters()
		{
			Frame = 0;
			lagCount = 0;
			isLag = false;
		}

		public string SystemId { get { return "Coleco"; } }
		public GameInfo game;
		public CoreComm CoreComm { get; private set; }
		public IVideoProvider VideoProvider { get { return VDP; } }
		public ISoundProvider SoundProvider { get { return PSG; } }

		public string BoardName { get { return null; } }

		public ISyncSoundProvider SyncSoundProvider { get { return null; } }
		public bool StartAsyncSound() { return true; }
		public void EndAsyncSound() { }

		public object GetSettings() { return null; }
		public object GetSyncSettings() { return SyncSettings.Clone(); }
		public bool PutSettings(object o) { return false; }
		public bool PutSyncSettings(object o)
		{
			var n = (ColecoSyncSettings)o;
			bool ret = n.SkipBiosIntro != SyncSettings.SkipBiosIntro;
			SyncSettings = n;
			return ret;
		}

		ColecoSyncSettings SyncSettings;

		public class ColecoSyncSettings
		{
			public bool SkipBiosIntro = false;
			public ColecoSyncSettings Clone()
			{
				return (ColecoSyncSettings)MemberwiseClone();
			}
		}

		public List<KeyValuePair<string, int>> GetCpuFlagsAndRegisters()
		{
			return new List<KeyValuePair<string, int>>
			{
				new KeyValuePair<string, int>("A", Cpu.RegisterA),
				new KeyValuePair<string, int>("AF", Cpu.RegisterAF),
				new KeyValuePair<string, int>("B", Cpu.RegisterB),
				new KeyValuePair<string, int>("BC", Cpu.RegisterBC),
				new KeyValuePair<string, int>("C", Cpu.RegisterC),
				new KeyValuePair<string, int>("D", Cpu.RegisterD),
				new KeyValuePair<string, int>("DE", Cpu.RegisterDE),
				new KeyValuePair<string, int>("E", Cpu.RegisterE),
				new KeyValuePair<string, int>("F", Cpu.RegisterF),
				new KeyValuePair<string, int>("H", Cpu.RegisterH),
				new KeyValuePair<string, int>("HL", Cpu.RegisterHL),
				new KeyValuePair<string, int>("I", Cpu.RegisterI),
				new KeyValuePair<string, int>("IX", Cpu.RegisterIX),
				new KeyValuePair<string, int>("IY", Cpu.RegisterIY),
				new KeyValuePair<string, int>("L", Cpu.RegisterL),
				new KeyValuePair<string, int>("PC", Cpu.RegisterPC),
				new KeyValuePair<string, int>("R", Cpu.RegisterR),
				new KeyValuePair<string, int>("Shadow AF", Cpu.RegisterShadowAF),
				new KeyValuePair<string, int>("Shadow BC", Cpu.RegisterShadowBC),
				new KeyValuePair<string, int>("Shadow DE", Cpu.RegisterShadowDE),
				new KeyValuePair<string, int>("Shadow HL", Cpu.RegisterShadowHL),
				new KeyValuePair<string, int>("SP", Cpu.RegisterSP),
				new KeyValuePair<string, int>("Flag C", Cpu.RegisterF.Bit(0) ? 1 : 0),
				new KeyValuePair<string, int>("Flag N", Cpu.RegisterF.Bit(1) ? 1 : 0),
				new KeyValuePair<string, int>("Flag P/V", Cpu.RegisterF.Bit(2) ? 1 : 0),
				new KeyValuePair<string, int>("Flag 3rd", Cpu.RegisterF.Bit(3) ? 1 : 0),
				new KeyValuePair<string, int>("Flag H", Cpu.RegisterF.Bit(4) ? 1 : 0),
				new KeyValuePair<string, int>("Flag 5th", Cpu.RegisterF.Bit(5) ? 1 : 0),
				new KeyValuePair<string, int>("Flag Z", Cpu.RegisterF.Bit(6) ? 1 : 0),
				new KeyValuePair<string, int>("Flag S", Cpu.RegisterF.Bit(7) ? 1 : 0),
			};
		}
	}
}