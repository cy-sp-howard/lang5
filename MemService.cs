using Blish_HUD;
using System;
using System.Collections.Generic;
using System.Linq;
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
            this.module = module;
        }
        public void Load()
        {
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) Init();
            GameService.GameIntegration.Gw2Instance.Gw2Started += delegate { Init(); };
        }
        public void Upadate() { }
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
        public void SetConvert(bool enable)
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
            GenTextConverter();

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
            byte[] data = TextJson.GetTextBytes();
            int allocSize = (data.Length / 1000 + 1) * 1000;
            TextDataAddress = Utils.AllocMemory(allocSize);
            Utils.WriteMemory(TextDataAddress, data);
        }
        private void GenTextConverter()
        {
            TextConverterAddress = AllocNearMemory(1000);

            IntPtr target = IntPtr.Add(refs["ch >= STRING_CHAR_FIRST"], 0x26);
            byte[] setTextOpcodeBytes = Utils.ReadMemory(target, 100);
            TextConverterDetour = new(target, setTextOpcodeBytes, GenJmpRelAdrressBytes(target, TextConverterAddress));
            IntPtr jmpBackAddress = IntPtr.Add(TextConverterDetour.Address, TextConverterDetour.BackupBytes.Count);

            ListCodeWriter codeWriter = new();
            Assembler c = new Assembler(64);

            foreach (var item in TextConverterDetour.BackupInstructions)
            {
                c.AddInstruction(item);
            }

            Label replaceTextFromCategory = c.CreateLabel();
            Label replaceMatch = c.CreateLabel();
            Label afterReplace = c.CreateLabel();
            Label back = c.CreateLabel();
            Label originText = c.CreateLabel();
            Label replacStart = c.CreateLabel();

            // rax text first addr; rcx current index; rsi current char;[r14+14] current len
            c.push(rax);
            c.lea(rax, __qword_ptr[originText]);
            c.mov(__qword_ptr[rax + rcx * 0x2], si);
            c.pop(rax);
            c.cmp(si, TextJson.cjkStart);
            c.jb(back);
            c.cmp(si, TextJson.cjkEnd);
            c.ja(back);
            c.push(rax);
            c.push(rbx);
            c.push(rcx);
            c.push(rdx);
            c.push(r8);
            c.mov(rdx, rcx);
            c.inc(rdx); // current index
            c.mov(rbx, TextDataAddress.ToInt64());
            c.sub(rsi, TextJson.cjkStart);
            c.lea(r8, __qword_ptr[rbx + rsi * 0x4]);
            c.lea(rcx, __qword_ptr[rax + rcx * 0x2]); // arg0 lastText
            c.xor(rax, rax);
            c.mov(eax, __qword_ptr[r8]);
            c.test(eax, eax);
            c.je(afterReplace);
            c.lea(r8, __qword_ptr[rbx + rax]); // arg2 category address
            c.call(replaceTextFromCategory); // (lastText,currentLen,category)
            c.Label(ref afterReplace);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rbx);
            c.pop(rax);
            c.Label(ref back);
            //c.AddInstruction(Instruction.CreateDeclareByte(TextConverterDetour.BackupBytes.ToArray()));
            c.AddInstruction(Instruction.CreateBranch(Code.Jmp_rel32_64, (ulong)jmpBackAddress.ToInt64()));
            c.int3();
            c.int3();
            c.int3();

            // replaceTextFromCategory(lastText,currentLen,category)
            Label replaceTextFromCategoryLoopStart = c.CreateLabel();
            Label replaceTextFromCategoryEnd = c.CreateLabel();
            c.Label(ref replaceTextFromCategory);
            c.push(rax);
            c.push(rbx);
            c.push(rsi);
            c.push(rdi);
            c.push(r8);
            c.push(r9);
            c.xor(rbx, rbx);
            c.xor(rsi, rsi);
            c.mov(esi, __qword_ptr[r8]); // category list len
            c.lea(rdi, __qword_ptr[r8 + 0x4]); // Item[0]
            c.Label(ref replaceTextFromCategoryLoopStart);
            c.test(esi, esi);
            c.je(replaceTextFromCategoryEnd);
            c.dec(esi); // list remain;
            c.mov(ebx, __qword_ptr[rdi]); // item[n].in len
            c.cmp(edx, ebx);
            c.lea(rdi, __qword_ptr[rdi + 0x4]);  // item[n].in text
            c.mov(ebx, __qword_ptr[rdi + rbx * 0x2]); // item[n].out len
            c.lea(rdi, __qword_ptr[rdi + 0x4]);  // item[n].out text
            c.lea(rdi, __qword_ptr[rdi + rbx * 0x2]);  // item[n+1]
            c.jb(replaceTextFromCategoryLoopStart); // current target not enough len
 

            c.call(replaceMatch); // (targetLastTextAddr,matchLastTextAddr,matchLength,currentTargetLength)
            c.test(rax, rax);
            c.je(replaceTextFromCategoryLoopStart);
            c.Label(ref replaceTextFromCategoryEnd);
            c.pop(r8);
            c.pop(rdi);
            c.pop(rsi);
            c.pop(rdx);
            c.pop(rbx);
            c.pop(rax);
            c.ret();
            c.int3();
            c.int3();
            c.int3();

            // match(refTargetLastTextAddr, inTextLenAddress)
            Label replaceMatchLoopStart = c.CreateLabel();
            Label replaceMatchFalse = c.CreateLabel();
            Label replaceMatchTrue = c.CreateLabel();
            Label replaceCopied = c.CreateLabel();
            Label replaceMatchEnd = c.CreateLabel();
            c.Label(ref replaceMatch);
            c.push(rcx);
            c.push(rbx);
            c.push(rdx);
            c.push(r8);
            c.push(r9);
            c.push(r10);
            c.push(r11);
            c.xor(rax, rax);
            c.lea(rax, rax + r8 * 0x2);
            c.sub(rcx, rax);
            c.mov(r11, rcx);
            c.lea(r10, rdx + 0x2); // stringOut first text address
            c.lea(rax, __qword_ptr[originText]);
            c.lea(rcx, rax + r9 * 0x2);
            c.lea(rcx, rcx - 0x2); // origin last text address
            c.mov(r9, r8); // backup length
            c.xor(rax, rax);
            c.xor(rbx, rbx);
            c.Label(ref replaceMatchLoopStart);
            c.mov(ax, __qword_ptr[rcx]); // get text
            c.mov(bx, __qword_ptr[rdx]); // get text
            c.cmp(ax, bx);
            c.jne(replaceMatchFalse); // not match
            c.lea(rcx, __qword_ptr[rcx - 0x2]); // previous text address
            c.lea(rdx, __qword_ptr[rdx - 0x2]); // previous text address 
            c.dec(r8);
            c.test(r8, r8); // check matchText remain
            c.je(replaceMatchTrue); // is all match
            c.jmp(replaceMatchLoopStart);
            c.Label(ref replaceMatchTrue);
            c.xor(rax, rax);
            c.inc(rax); // true
            c.jmp(replaceMatchEnd);
            c.Label(ref replaceMatchFalse);
            c.xor(rax, rax); // false
            c.Label(ref replaceMatchEnd);
            c.pop(r11);
            c.pop(r10);
            c.pop(r9);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rbx);
            c.pop(rcx);
            c.ret();

            // match(targetLastTextAddress, currentTargetIndexPtr, inLenAddress)


            // replace(targetStartOverwriteAddress, currentTargetIndexPtr, inLenAddress)
            Label replaceLoopStart = c.CreateLabel();
            Label replaceLoopEnd = c.CreateLabel();
            c.Label(ref replacStart);
            c.push(rax);
            c.xor(rax, rax);
            c.mov(eax, __qword_ptr[r8]);
            c.sub(__qword_ptr[rdx], eax);
            c.lea(r8, __qword_ptr[r8 + 0x4]); // in text address
            c.lea(r8, __qword_ptr[r8 + rax * 2]); // out len address
            c.mov(eax, __qword_ptr[r8]); // out len
            c.add(__qword_ptr[rdx], eax); // fix target last Index by  out.len - in.len
            c.xor(rdx, rdx);
            c.mov(eax, __qword_ptr[r8]); // out len
            c.lea(r8, __qword_ptr[r8 + 0x4]); // out text start
            c.Label(ref replaceLoopStart);
            c.mov(dx, __qword_ptr[r8]);
            c.mov(__qword_ptr[rcx], dx); // overwrite
            c.dec(eax); // remain copy
            c.test(eax, eax);
            c.je(replaceLoopEnd);
            c.lea(rcx, __qword_ptr[rcx + 0x2]);
            c.lea(r8, __qword_ptr[r8 + 0x2]);
            c.jmp(replaceLoopStart);
            c.Label(ref replaceLoopEnd);
            c.pop(rax);
            c.ret();



            c.nop();
            c.nop();
            c.nop();
            c.Label(ref originText);
            c.nop();

            c.Assemble(codeWriter, (ulong)TextConverterAddress.ToInt64());
            //Utils.PrintOpcodes(codeWriter.data.ToArray(), ZHFuncAddress);
            Utils.WriteMemory(TextConverterAddress, codeWriter.data.ToArray());
        }
        public int ReloadConverter()
        {
            if (!loaded) return 1;
            IntPtr prepFreeAddr1 = TextDataAddress;
            IntPtr prepFreeAddr2 = TextConverterAddress;

            TextConverterDetour.Undo();
            OverwriteOpcodes.All.Remove(TextConverterDetour);
            GenTextData();
            GenTextConverter();
            if (module.Settings.Cht.Value) TextConverterDetour.Write();

            Utils.FreeMemory(prepFreeAddr1);
            Utils.FreeMemory(prepFreeAddr2);
            return 0;
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
        public const int cjkStart = 0x4E00;
        public const int cjkEnd = 0x9FFF;
        public static byte[] GetTextBytes()
        {

            // https://github.com/kfcd/fanjian
            TextJsonItem[] data = Utils.GetJson<TextJsonItem[]>("jianfan.json");

            TextJsonItem[] add = Utils.GetJson<TextJsonItem[]>("add.json");
            TextJsonItem[] userAdd = [];
            if (Lang5Module.Instance.Settings.ChtJson.Value != "")
            {
                try
                {
                    userAdd = Utils.GetJson<TextJsonItem[]>(Lang5Module.Instance.Settings.ChtJson.Value);
                }
                catch (Exception e)
                {
                    Utils.Notify.Show(e.Message);
                }
            }

            TextDataCollection collection = new(data.Concat(add).Concat(userAdd).ToArray());
            return collection.Bytes;
        }
        public class TextJsonItem
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
            public readonly int InLength;
            public readonly int OutLength;
            public readonly ushort CategoryKey;
            public readonly byte[] Bytes;
            public TextDataItem(string In, string Out)
            {
                this.In = In;
                this.Out = Out;
                this.InLength = In.Length;
                this.OutLength = Out.Length;
                this.CategoryKey = BitConverter.ToUInt16(BitConverter.GetBytes(In[InLength - 1]), 0);

                /*
                    struct {
                        int textLength;
                        char[textLength] in;
                        int outLength;
                        char[outLength] out;
                    };
                 */
                System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
                List<byte> bytes = new();
                bytes.AddRange(BitConverter.GetBytes(this.InLength));
                byte[] inBytes = unicodeEncoding.GetBytes(this.In);
                bytes.AddRange(BitConverter.GetBytes(this.OutLength));
                byte[] outBytes = unicodeEncoding.GetBytes(this.Out);

                bytes.AddRange(inBytes);
                bytes.AddRange(outBytes);
                this.Bytes = bytes.ToArray();
            }
        }
        private class TextDataCategory(ushort key)
        {
            public static IReadOnlyList<TextDataCategory> All => _all;
            private static List<TextDataCategory> _all = [];
            public readonly ushort Key = key;
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
                        TextDataItem same = c.List.Find(i => i.In == item.In);
                        c.List.Remove(same);
                        c.List.Add(item);
                        return;
                    }
                }
                TextDataCategory category = new(item.CategoryKey);
                category.List.Add(item);
                _all.Add(category);
            }
            public static void Clear()
            {
                _all.Clear();
            }
        }
        private class TextDataCollection
        {
            private int DataAddressOffset => (cjkEnd - cjkStart + 1) * sizeof(int);
            public readonly byte[] Bytes;
            public TextDataCollection(TextJsonItem[] source)
            {
                TextDataCategory.Clear();
                System.Text.Encoding unicodeEncoding = System.Text.Encoding.Unicode;
                List<byte> bytes = new();
                foreach (var item in source.OrderByDescending(i => i.In.Length))
                {
                    TextDataItem dataItem = new(item.In, item.Out);
                    TextDataCategory.AutoSort(dataItem);
                }
                byte[] mapBytes = new byte[DataAddressOffset];
                List<byte> dataBytes = [];
                foreach (var category in TextDataCategory.All)
                {
                    int mapBytesIndex = (category.Key - cjkStart) * sizeof(int);
                    if (mapBytesIndex < 0 || category.Key > cjkEnd) continue;
                    byte[] categoryOffsetBytes = BitConverter.GetBytes(DataAddressOffset + dataBytes.Count);
                    Array.Copy(categoryOffsetBytes, 0, mapBytes, mapBytesIndex, categoryOffsetBytes.Length);
                    dataBytes.AddRange(category.Bytes);
                }
                Bytes = mapBytes.Concat(dataBytes).ToArray();
            }
        }
    }
}
