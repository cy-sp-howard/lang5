using Blish_HUD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;

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
        public static event EventHandler OnLoaded;

        public MemService(Lang5Module module)
        {
            var a = TextJson.GetTextBytes(TextDataAddress);
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
            if (!GameService.GameIntegration.Gw2Instance.Gw2IsRunning || !module.Settings.RestoreMem.Value) return;
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
            Assembler c = new Assembler(64);
            Label endLabel = c.CreateLabel();
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
            Assembler c = new Assembler(64);
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
            TextDataAddress = Utils.AllocMemory(40000);
            byte[] data = TextJson.GetTextBytes(TextDataAddress);
            Utils.WriteMemory(TextDataAddress, data);
        }
        private void GenTextCoverter()
        {
            TextConverterAddress = AllocNearMemory(1000);

            IntPtr target = IntPtr.Add(refs["ch >= STRING_CHAR_FIRST"], 0x26);
            byte[] setTextOpcodeBytes = Utils.ReadMemory(target, 100);
            TextConverterDetour = new(target, setTextOpcodeBytes, GenJmpRelAdrressBytes(target, TextConverterAddress));
            IntPtr jmpBackAddress = IntPtr.Add(TextConverterDetour.Address, TextConverterDetour.BackupBytes.Count);

            ListCodeWriter codeWriter = new();
            Assembler c = new Assembler(64);
            c.int3();
            c.int3();
            c.int3();

            // replaceMatch(targetLastText,matchLastText,replacefirstText,size)
            Label replaceMatch = c.CreateLabel();
            Label replaceMatchLoopStart = c.CreateLabel();
            Label replaceMatchFalse = c.CreateLabel();
            Label replaceMatchTrue = c.CreateLabel();
            Label replaceCopied = c.CreateLabel();
            Label replaceMatchEnd = c.CreateLabel();
            c.Label(ref replaceMatch);
            c.push(rbx);
            c.push(r9);
            c.mov(r9, r8); // backup size
            c.Label(ref replaceMatchLoopStart);
            c.mov(ax, __qword_ptr[rcx]); // get text
            c.mov(bx, __qword_ptr[rdx]); // get text
            c.cmp(ax, bx);
            c.jne(replaceMatchFalse); // not match
            c.lea(rcx, __qword_ptr[rcx - 0x2]); // previous text
            c.lea(rdx, __qword_ptr[rdx - 0x2]); // previous text
            c.test(r8, r8); // check index 0
            c.je(replaceMatchTrue); // match
            c.dec(r8); // previous index
            c.jmp(replaceMatchLoopStart);
            c.Label(ref replaceMatchTrue);
            c.mov(bx, __qword_ptr[rdx]);
            c.mov(__qword_ptr[rcx], bx); // copy
            c.lea(rcx, __qword_ptr[rcx + 0x2]); // next text
            c.lea(rdx, __qword_ptr[rdx + 0x2]); // next text
            c.cmp(r8, r9); //check last index
            c.je(replaceCopied);
            c.inc(r8); // next index
            c.jmp(replaceMatchTrue);
            c.Label(ref replaceCopied);
            c.mov(rax, 0x1); // true
            c.jmp(replaceMatchEnd);
            c.Label(ref replaceMatchFalse);
            c.mov(rax, 0x0); // false
            c.Label(ref replaceMatchEnd);
            c.pop(r9);
            c.pop(rbx);
            c.ret();

            //replaceTextFromCategory(lastText,currentLen,category)
            Label replaceTextFromCategory = c.CreateLabel();
            Label replaceTextFromCategoryLoopStart = c.CreateLabel();
            c.push(r10);
            c.push(r11);
            c.push(r12);
            c.push(rdi);
            c.push(rsi);
            c.Label(ref replaceTextFromCategory);
            c.mov(r10, rcx); //backup last text
            c.mov(r11, rdx); //backup last text
            c.mov(r12, r8); //backup category text
            c.xor(rdi, rdi);
            c.mov(edi, __qword_ptr[r8]); // category list len
            c.Label(ref replaceTextFromCategoryLoopStart);
            c.mov(r8, __qword_ptr[r8 + 0x4]); // item[n] len
            c.lea(rdx, __qword_ptr[r8 + 0x8]); // item[n] stringIn
            c.dec(rdx);
            c.mov(rdx, rdx + r8); // item[n] stringIn last address
            c.mov(rsi, rdx + 0x1); //item[n] stringOut address
            c.call(replaceMatch);
            c.test(rax, rax);
            c.je(replaceTextFromCategoryLoopStart);
            c.pop(rsi);
            c.pop(rdi);
            c.pop(r12);
            c.pop(r11);
            c.pop(r10);
            c.ret();






            Label loopStartlabel = c.CreateLabel();
            Label endLabel = c.CreateLabel();
            Label originOpcodesLabel = c.CreateLabel();

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
    public static class TextJson
    {
        public static byte[] GetTextBytes(IntPtr address)
        {

            // https://github.com/kfcd/fanjian
            TextJsonItem[] data = Utils.GetJson<TextJsonItem[]>("jianfan.json");
            TextDataCollection collection = new(data, address);

            return collection.Bytes;
        }
        private class TextJsonItem
        {
            [JsonPropertyName("o")]
            public string Out { get; set; }
            [JsonPropertyName("i")]
            public string In { get; set; }
        }
        private class TextDataItem
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
                byte[] outOriginBytes = unicodeEncoding.GetBytes(this.Out);
                for (int i = 0; i < outOriginBytes.Length; i++)
                {
                    int outBytesIndex = outBytes.Length - 1 - i;
                    int outOriginBytesIndex = outOriginBytes.Length - 1 - i;
                    if (outBytesIndex < 0) break;
                    outBytes[outBytesIndex] = outOriginBytes[outOriginBytesIndex];
                }
                bytes.AddRange(inBytes);
                bytes.AddRange(outBytes);
                this.Bytes = bytes.ToArray();
            }
        }
        private class TextDataCategory(short key)
        {
            public static IReadOnlyList<TextDataCategory> All => _all;
            private static List<TextDataCategory> _all = [];
            public readonly short Key = key;
            public List<TextDataItem> List = [];
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
        private class TextDataCollection
        {
            const int startIndex = 0x4E00;
            const int endIndex = 0x9FFF;
            public readonly IntPtr MapAddress;
            public IntPtr DataAddres => IntPtr.Add(MapAddress, DataAddressOffset);
            private int DataAddressOffset => (endIndex - startIndex + 1) * sizeof(int);
            public readonly byte[] Bytes;
            public TextDataCollection(TextJsonItem[] source, IntPtr address)
            {
                MapAddress = address;
                System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
                List<byte> bytes = new();
                foreach (var item in source.OrderByDescending(i => i.In.Length))
                {
                    TextDataItem dataItem = new(item.In, item.Out);
                    TextDataCategory.AutoSort(dataItem);
                }
                List<byte> mapBytes = new byte[DataAddressOffset].ToList();
                List<byte> dataBytes = [];
                foreach (var category in TextDataCategory.All)
                {
                    int mapBytesIndex = (category.Key - startIndex) * sizeof(int);
                    if (mapBytesIndex < 0) continue;
                    int categoryOffset = DataAddressOffset + dataBytes.Count;
                    mapBytes.InsertRange(mapBytesIndex, BitConverter.GetBytes(categoryOffset));
                    dataBytes.AddRange(category.Bytes);
                }
                Bytes = mapBytes.Concat(dataBytes).ToArray();
            }
        }
    }
}
