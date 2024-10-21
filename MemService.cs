using Blish_HUD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;
using System.Net.Mail;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace BhModule.Lang5
{
    public class MemService
    {
        private readonly Lang5Module module;
        private IntPtr CallerAddress;
        private IntPtr TextDataAddress;
        private IntPtr TextConverterAddress;
        private IntPtr LangSetterAddress;
        private IntPtr CallFuncPtr => IntPtr.Add(CallerAddress, 100);
        private IntPtr OriginLangPtr;
        private Dictionary<string, IntPtr> refs = new() {
            { "ViewAdvanceText" ,IntPtr.Zero},
            { "ValidateLanguage(language)",IntPtr.Zero},
            { "ch >= STRING_CHAR_FIRST",IntPtr.Zero}
        };
        private OverwriteOpcodes TextConverterDetour;
        private bool loaded = false;
        public bool restoreWhenUnload = true;
        public static event EventHandler OnLoaded;

        public MemService(Lang5Module module)
        {
            this.module = module;
        }
        public void Load()
        {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) Init();
            GameService.GameIntegration.Gw2Instance.Gw2Started += delegate { Init(); };
        }
        public void Upadate()
        {

        }
        public void Unload()
        {
            if (!GameService.GameIntegration.Gw2Instance.Gw2IsRunning || !restoreWhenUnload) return;
            SetZhUI(false);
            foreach (var item in OverwriteOpcodes.All)
            {
                item.Undo();
            }
            Utils.FreeMemory(TextDataAddress);
            Utils.FreeMemory(CallerAddress);
            Utils.FreeMemory(LangSetterAddress);
            Utils.FreeMemory(TextConverterAddress);
        }
        public void SetZhUI(bool enable)
        {
            if (!loaded) return;
            byte[] lang = enable ? [0x5] : Utils.ReadMemory(OriginLangPtr, 1);
            FuncBuffer funcBuffer = new FuncBuffer { address = LangSetterAddress, arg0 = lang[0] };
            Utils.WriteMemory(CallFuncPtr, funcBuffer.bytes);
            Thread.Sleep(100);
        }
        public void SetCovert(bool enable)
        {
            if (!loaded) return;
            if (enable) TextConverterDetour.Write();
            else TextConverterDetour.Undo();
        }
        private void Init()
        {
            FindRefs();
            if (ValidateAddress() > 0) return;
            GenCaller();
            GenLangSetter();
            GenTextData();
            GenTextCoverter();

            loaded = true;
            OnLoaded?.Invoke(this, EventArgs.Empty);
        }
        private void FindRefs()
        {
            refs = Utils.FindReadonlyStringRefs(refs.Keys.ToArray());
        }
        private int ValidateAddress()
        {
            foreach (var item in refs)
            {
                if (item.Value == IntPtr.Zero)
                {
                    Utils.Notify.Show($"Unexpected \"{item.Key}\" address.", 6000);
                    return 1;
                }
            }

            IntPtr callAddress = IntPtr.Add(refs["ViewAdvanceText"], -0x8);
            byte[] originCallBytes = Utils.ReadMemory(callAddress, 100);
            List<Instruction> opcodes = Utils.ParseOpcodes(originCallBytes, callAddress);
            if (opcodes.Count != 0 && opcodes[0].IsJmpNear)
            {
                Utils.Notify.Show("Please restart game, can not handle codes which injected.", 6000);
                return 2;
            };
            return 0;
        }
        private void GenCaller()
        {
            IntPtr callAddress = IntPtr.Add(refs["ViewAdvanceText"], -0x8);
            byte[] originCallBytes = Utils.ReadMemory(callAddress, 100);

            CallerAddress = AllocNearMemory(200); // after 100+ func ptr and args
            OverwriteOpcodes callDetour = new(callAddress, originCallBytes, GenJmpRelAdrressBytes(callAddress, CallerAddress));

            IntPtr jmpBackAddress = IntPtr.Add(callDetour.Address, callDetour.BackupBytes.Count);
            ListCodeWriter codeWriter = new();
            var c = new Assembler(64);
            var endLabel = c.CreateLabel();
            c.push(rax);
            c.push(rbx);
            c.push(rcx);
            c.push(rdx);
            c.push(r8);
            c.push(r9);
            c.mov(rbx, CallFuncPtr.ToInt64());
            c.mov(rax, __qword_ptr[rbx]);
            c.test(rax, rax);
            c.je(endLabel);
            c.mov(rcx, __qword_ptr[rbx + 0x8]);
            c.mov(rdx, __qword_ptr[rbx + 0x10]);
            c.mov(r8, __qword_ptr[rbx + 0x18]);
            c.mov(r9, __qword_ptr[rbx + 0x20]);
            c.call(rax);
            c.mov(__qword_ptr[rbx], 0x0);
            c.Label(ref endLabel);
            c.pop(r9);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rbx);
            c.pop(rax);
            foreach (var item in callDetour.BackupInstructions)
            {
                c.AddInstruction(item);
            }
            c.AddInstruction(Instruction.CreateBranch(Code.Jmp_rel32_64, (ulong)jmpBackAddress.ToInt64()));

            c.Assemble(codeWriter, (ulong)CallerAddress.ToInt64());
            //Utils.PrintOpcodes(codeWriter.data.ToArray(), InjectionCallerAddress);
            Utils.WriteMemory(CallerAddress, codeWriter.data.ToArray());
            callDetour.Write();

        }
        private void GenLangSetter()
        {
            IntPtr parentBlock = refs["ValidateLanguage(language)"];
            OriginLangPtr = Utils.FollowAddress(IntPtr.Add(parentBlock, 0xb));
            IntPtr targetFuncAddress = Utils.FollowAddress(IntPtr.Add(parentBlock, 0x24));
            LangSetterAddress = AllocNearMemory(100);

            ListCodeWriter codeWriter = new();
            var c = new Assembler(64);
            c.push(rbx);
            c.push(rsp);
            c.push(rcx);
            c.push(rdx);
            c.AddInstruction(Instruction.CreateBranch(Code.Call_rel32_64, (ulong)targetFuncAddress.ToInt64()));
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rsp);
            c.pop(rbx);
            c.ret();
            c.Assemble(codeWriter, (ulong)LangSetterAddress.ToInt64());

            Utils.WriteMemory(LangSetterAddress, codeWriter.data.ToArray());
        }
        private void GenTextData()
        {
            System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
            TextDataAddress = Utils.AllocMemory(40000);
            List<byte> data_byte = new();
            TextJsonItem[] data = JsonSerializer.Deserialize<TextJsonItem[]>(MapText.text);
            foreach (var item in data)
            {
                //TextDataCategory.AutoSort(new TextDataItem(item.In, item.Out));

                byte[] inBytes = unicodeEncoding.GetBytes(item.In);
                byte[] outBytes = unicodeEncoding.GetBytes(item.Out);
                if (inBytes.Length != 2 || outBytes.Length != 2) continue;
                data_byte.AddRange(inBytes);
                data_byte.AddRange(outBytes);
            }
            Utils.WriteMemory(TextDataAddress, data_byte.ToArray());
        }
        private void GenTextCoverter()
        {
            TextConverterAddress = AllocNearMemory(200);

            IntPtr target = IntPtr.Add(refs["ch >= STRING_CHAR_FIRST"], 0x26);
            byte[] setTextOpcodeBytes = Utils.ReadMemory(target, 100);
            TextConverterDetour = new(target, setTextOpcodeBytes, GenJmpRelAdrressBytes(target, TextConverterAddress));
            IntPtr jmpBackAddress = IntPtr.Add(TextConverterDetour.Address, TextConverterDetour.BackupBytes.Count);

            ListCodeWriter codeWriter = new();
            var c = new Assembler(64);
            var loopStartlabel = c.CreateLabel();
            var endLabel = c.CreateLabel();
            var originOpcodesLabel = c.CreateLabel();

            c.cmp(si, 0x4e00);
            c.jb(originOpcodesLabel);
            c.push(rdi);
            c.push(rax);
            c.mov(rdi, TextDataAddress.ToInt64());
            c.xor(rax, rax);
            c.Label(ref loopStartlabel);
            c.mov(ax, __qword_ptr[rdi]);
            c.lea(rdi, __qword_ptr[rdi + 0x4]);
            c.test(ax, ax);
            c.je(endLabel);
            c.cmp(si, ax);
            c.jne(loopStartlabel);
            c.lea(rdi, __qword_ptr[rdi - 0x2]);
            c.mov(si, __qword_ptr[rdi]);
            c.Label(ref endLabel);
            c.pop(rax);
            c.pop(rdi); ;
            c.Label(ref originOpcodesLabel);
            foreach (var item in TextConverterDetour.BackupInstructions)
            {
                c.AddInstruction(item);
            }
            //c.AddInstruction(Instruction.CreateDeclareByte(TextConverterDetour.BackupBytes.ToArray()));
            c.AddInstruction(Instruction.CreateBranch(Code.Jmp_rel32_64, (ulong)jmpBackAddress.ToInt64()));

            c.Assemble(codeWriter, (ulong)TextConverterAddress.ToInt64());
            //Utils.PrintOpcodes(codeWriter.data.ToArray(), ZHFuncAddress);
            Utils.WriteMemory(TextConverterAddress, codeWriter.data.ToArray());
        }
        private byte[] GenJmpRelAdrressBytes(IntPtr rip, IntPtr target)
        {
            List<byte> list = new List<byte>();
            list.Add(0xe9);
            list.AddRange(BitConverter.GetBytes((int)(target.ToInt64() - (rip.ToInt64() + 5))));
            return list.ToArray();
        }
        private IntPtr AllocNearMemory(int size)
        {
            var mainModule = GameService.GameIntegration.Gw2Instance.Gw2Process.MainModule;
            IntPtr tryAddr = IntPtr.Add(mainModule.BaseAddress, int.MinValue + mainModule.ModuleMemorySize);
            while (tryAddr.ToInt64() < mainModule.BaseAddress.ToInt64())
            {
                IntPtr result = Utils.AllocMemory(size, tryAddr);
                if (result != IntPtr.Zero) return result;
                tryAddr = IntPtr.Add(tryAddr, size);
            }
            return IntPtr.Zero;
        }

    }
    public class FuncBuffer
    {
        public IntPtr address;
        public long arg0;
        public long arg1;
        public long arg2;
        public long arg3;
        public byte[] bytes
        {
            get
            {
                List<byte> _bytes = new List<byte>();
                _bytes.AddRange(BitConverter.GetBytes(address.ToInt64()));
                _bytes.AddRange(BitConverter.GetBytes(arg0));
                _bytes.AddRange(BitConverter.GetBytes(arg1));
                _bytes.AddRange(BitConverter.GetBytes(arg2));
                _bytes.AddRange(BitConverter.GetBytes(arg3));
                return _bytes.ToArray();
            }
        }
    }
    public class OverwriteOpcodes
    {
        public static List<OverwriteOpcodes> All = new();
        public readonly IReadOnlyList<byte> BackupBytes;
        public readonly IReadOnlyList<byte> OverwriteBytes;
        public readonly IReadOnlyList<Instruction> BackupInstructions;
        public readonly IntPtr Address;
        public OverwriteOpcodes(IntPtr address, byte[] originBytes, byte[] overwriteBytes)
        {
            List<byte> backupOpcodes = new();
            List<byte> overwriteOpcodes = overwriteBytes.ToList();

            int atLeastbackupSize = overwriteBytes.Length;
            List<Instruction> originBytesInstructions = Utils.ParseOpcodes(originBytes.ToArray(), address);
            List<Instruction> backupInstructions = new();

            for (int row = 0; backupOpcodes.Count < atLeastbackupSize; row++)
            {
                backupInstructions.Add(originBytesInstructions[row]);
                for (int i = backupOpcodes.Count, i_start = backupOpcodes.Count; i < i_start + originBytesInstructions[row].Length; i++)
                {
                    backupOpcodes.Add(originBytes[i]);
                    if (i > atLeastbackupSize - 1) overwriteOpcodes.Add(0x90);
                }
            }

            this.Address = address;
            this.BackupBytes = backupOpcodes;
            this.OverwriteBytes = overwriteOpcodes;
            this.BackupInstructions = backupInstructions;
            All.Add(this);
        }
        public void Write()
        {
            Utils.WriteMemory(Address, OverwriteBytes.ToArray());
        }
        public void Undo()
        {
            Utils.WriteMemory(Address, BackupBytes.ToArray());
        }

    }
    public class BackupOpcodes
    {
        static List<BackupOpcodes> all = new();
        List<byte> list = new();
        IntPtr address;
        public int Count => list.Count;
        public BackupOpcodes(IntPtr address)
        {
            this.address = address;
            all.Add(this);
        }
        public void Add(byte i)
        {
            list.Add(i);
        }
        public byte[] ToArray()
        {
            return list.ToArray();
        }
    }
    public class TextJsonItem
    {
        [JsonPropertyName("o")]
        public string Out { get; set; }
        [JsonPropertyName("i")]
        public string In { get; set; }
    }
    public class TextDataItem
    {
        public readonly string In;
        public readonly string Out;
        public readonly int Length;
        public readonly short CategoryKey;
        public readonly byte[] Bytes;
        public TextDataItem(string In, string Out)
        {
            this.In = In;
            this.Out = Out;
            this.Length = In.Length;
            this.CategoryKey = BitConverter.ToInt16(BitConverter.GetBytes(In[Length - 1]), 0);

            /*
                struct {
                    int textLength;
                    char[textLength] in;
                    char[textLength] out;
                };
             */
            System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
            List<byte> bytes = new();
            bytes.AddRange(BitConverter.GetBytes(this.Length));
            byte[] inBytes = unicodeEncoding.GetBytes(this.In);
            byte[] outBytes = new byte[inBytes.Length];
            Array.Copy(unicodeEncoding.GetBytes(this.Out), outBytes, outBytes.Length);
            bytes.AddRange(inBytes);
            bytes.AddRange(outBytes);
            this.Bytes = bytes.ToArray();
        }
    }
    public class TextDataCategory(short key)
    {
        public static IReadOnlyList<TextDataCategory> All => _all;
        private static List<TextDataCategory> _all = new();
        public readonly short Key = key;
        public List<TextDataItem> List = new();
        public int Size => List.Count;
        public byte[] Bytes
        {
            get
            {
                List<byte> bytes = new();
                bytes.AddRange(BitConverter.GetBytes(this.Size));
                foreach (var item in List)
                {
                    bytes.AddRange(item.Bytes);
                }
                return bytes.ToArray();
            }
        }
        public static void AutoSort(TextDataItem item)
        {
            foreach (var c in All)
            {
                if (c.Key == item.CategoryKey)
                {
                    c.List.Add(item);
                    return;
                }
            }
            TextDataCategory category = new(item.CategoryKey);
            category.List.Add(item);
            _all.Add(category);
        }
    }
}
