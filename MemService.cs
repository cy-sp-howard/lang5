using Blish_HUD;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Iced.Intel;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Iced.Intel.AssemblerRegisters;
using System.ServiceModel.Channels;

namespace BhModule.Lang5
{
    public class MemService
    {
        private readonly Lang5Module module;
        private IntPtr ZHDataAddress;
        private IntPtr ZHFuncAddress;
        private IntPtr detourTarget;
        private List<byte> backupOpcodes;
        private List<byte> detourOpcodes;
        public MemService(Lang5Module module)
        {
            this.module = module;
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) Init();
            GameService.GameIntegration.Gw2Instance.Gw2Started += delegate { Init(); };
        }
        public void Upadate() { }
        public void Unload()
        {
            if (!GameService.GameIntegration.Gw2Instance.Gw2IsRunning) return;
            Restore();
            Utils.FreeMemory(ZHDataAddress);
            Utils.FreeMemory(ZHFuncAddress);
        }
        public void Init()
        {
            SetLangPtr();
            WriteZHData();
            WriteFuncData();
        }
        public void SetUILang()
        {

        }
        private void SetLangPtr()
        {
            IntPtr layer0 = Utils.FindReadonlyStringRef("ValidateLanguage(language)");
            IntPtr layer2 = Utils.FollowAddress(IntPtr.Add(layer0, 0x24));
            IntPtr targetFuncAddress = Utils.FollowAddress(IntPtr.Add(layer2, 0x9));

            Utils.ReadMemory(IntPtr.Add(targetFuncAddress, 0x10), 1);
            Utils.ReadMemory(IntPtr.Add(targetFuncAddress, 0x13), 4);

            IntPtr baseAddress = Utils.AllocMemory(1000);
        }
        private void WriteZHData()
        {
            System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
            ZHDataAddress = Utils.AllocMemory(40000);
            List<byte> data_byte = new();
            ZH[] data = JsonSerializer.Deserialize<ZH[]>(MapZH.text);
            foreach (var item in data)
            {
                data_byte.AddRange(unicodeEncoding.GetBytes(item.In));
                data_byte.AddRange(unicodeEncoding.GetBytes(item.Out));
            }
            Utils.WriteMemory(ZHDataAddress, data_byte.ToArray());
        }
        private void WriteFuncData()
        {
            var mainModule = GameService.GameIntegration.Gw2Instance.Gw2Process.MainModule;
            ZHFuncAddress = Utils.AllocMemory(100, IntPtr.Add(mainModule.BaseAddress, int.MinValue + mainModule.ModuleMemorySize));
            detourTarget = IntPtr.Add(Utils.FindReadonlyStringRef("ch >= STRING_CHAR_FIRST"), 0x26);
            GenDetourOpcodes(detourTarget);
            byte[] funcBytes = GenFuncOpcodes(IntPtr.Add(detourTarget, backupOpcodes.Count));

            Utils.WriteMemory(ZHFuncAddress, funcBytes);
        }
        private void GenDetourOpcodes(IntPtr target)
        {
            ulong setTextAddr_long = (ulong)target.ToInt64();
            byte[] setTextOpcodeBytes = Utils.ReadMemory(target, 100);
            var setTextInstructions = Utils.ParseOpcodes(setTextOpcodeBytes, target);

            backupOpcodes = new();
            List<byte> jmpFuncBytes = GenJmpRelAdrressBytes(target, ZHFuncAddress).ToList();
            for (int row = 0; backupOpcodes.Count < jmpFuncBytes.Count; row++)
            {
                for (int i = backupOpcodes.Count, i_start = backupOpcodes.Count; i < i_start + setTextInstructions[row].Length; i++)
                {
                    backupOpcodes.Add(setTextOpcodeBytes[i]);
                    if (i > jmpFuncBytes.Count - 1) jmpFuncBytes.Add(0x90);
                }
            }
            detourOpcodes = jmpFuncBytes;

        }
        private byte[] GenFuncOpcodes(IntPtr backAddress)
        {
            IntPtr rip = ZHFuncAddress;
            ListCodeWriter codeWriter = new();
            var c = new Assembler(64);
            var loopStartlabel = c.CreateLabel();
            var endLabel = c.CreateLabel();

            c.push(rdi);
            c.push(rax);
            c.mov(rdi, ZHDataAddress.ToInt64());
            c.Label(ref loopStartlabel);
            c.mov(rax, __qword_ptr[rdi]);
            c.lea(rdi, __qword_ptr[rdi + 4]);
            c.test(ax, ax);
            c.je(endLabel);
            c.cmp(si, ax);
            c.jne(loopStartlabel);
            c.lea(rdi, __qword_ptr[rdi - 2]);
            c.mov(si, __qword_ptr[rdi]);
            c.Label(ref endLabel);
            c.pop(rax);
            c.pop(rdi);

            c.Assemble(codeWriter, (ulong)rip.ToInt64());
            List<byte> funcBytes = codeWriter.data.ToList();
            funcBytes.AddRange(backupOpcodes);

            byte[] jmpBytes = GenJmpRelAdrressBytes(IntPtr.Add(rip, funcBytes.Count), backAddress);
            funcBytes.AddRange(jmpBytes);

            //Utils.PrintOpcodes(funcBytesWithJmp.ToArray(), rip);

            return funcBytes.ToArray();
        }
        private void Detour()
        {
            Utils.WriteMemory(detourTarget, detourOpcodes.ToArray());
        }
        private void Restore()
        {
            Utils.WriteMemory(detourTarget, backupOpcodes.ToArray());
        }
        private byte[] GenJmpRelAdrressBytes(IntPtr rip, IntPtr target)
        {
            List<byte> list = new List<byte>();
            list.Add(0xe9);
            list.AddRange(BitConverter.GetBytes((int)(target.ToInt64() - (rip.ToInt64() + 5))));
            return list.ToArray();
        }
    }
    public class ZH
    {
        [JsonPropertyName("o")]
        public string Out { get; set; }
        [JsonPropertyName("i")]
        public string In { get; set; }
    }
}
