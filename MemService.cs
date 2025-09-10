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
        public bool ForceRestoreMem = false;
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
            if (!GameService.GameIntegration.Gw2Instance.Gw2IsRunning || (!module.Settings.RestoreMem.Value && !ForceRestoreMem)) return;
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
                Utils.Notify.Show("Please restart game, can not handle codes that injected.", 6000);
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
            TextConverterAddress = AllocNearMemory(100000);

            IntPtr target = IntPtr.Add(refs["ch >= STRING_CHAR_FIRST"], 0x26);
            byte[] setTextOpcodeBytes = Utils.ReadMemory(target, 100);
            TextConverterDetour = new(target, setTextOpcodeBytes, GenJmpRelAdrressBytes(target, TextConverterAddress));
            IntPtr jmpBackAddress = IntPtr.Add(TextConverterDetour.Address, TextConverterDetour.BackupBytes.Count);

            byte[] expectedBytes = [0x66, 0x89, 0x34, 0x48, 0x41, 0xff, 0x46];
            for (int i = 0; i < expectedBytes.Length; i++)
            {
                if (TextConverterDetour.BackupBytes[i] != expectedBytes[i])
                {
                    Utils.Notify.Show("Unexpected opcodes; can not generate Chinese coverter.", 6000);
                    return;
                }
            }

            ListCodeWriter codeWriter = new();
            Assembler c = new Assembler(64);

            //c.AddInstruction(Instruction.CreateDeclareByte(TextConverterDetour.BackupBytes.ToArray()));
            foreach (var item in TextConverterDetour.BackupInstructions)
            {
                c.AddInstruction(item);
            }

            Label originLength = c.CreateLabel();
            Label originText = c.CreateLabel();
            // func
            Label replaceTextFromCategory = c.CreateLabel();
            Label match = c.CreateLabel();
            Label replace = c.CreateLabel();
            Label isEqual = c.CreateLabel();
            Label getBackupLastTextAddress = c.CreateLabel();
            Label nextItem = c.CreateLabel();


            // rax text first addr; rcx current index; rsi current char;[r14+14] current len
            Label end = c.CreateLabel();
            Label back = c.CreateLabel();

            c.push(rax);
            c.push(rcx);
            c.test(rcx, rcx);
            c.jne(c.@F);
            c.mov(__dword_ptr[originLength], 0x0);
            c.AnonymousLabel();
            c.lea(rax, __qword_ptr[originText]);
            c.mov(ecx, __dword_ptr[originLength]);
            c.mov(__qword_ptr[rax + rcx * 0x2], si); // backup text
            c.inc(__dword_ptr[originLength]); // backup length
            c.pop(rcx);
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
            c.mov(rbx, TextDataAddress.ToInt64());
            c.sub(rsi, TextJson.cjkStart);
            c.lea(r8, __qword_ptr[rbx + rsi * 0x4]); // map address
            c.lea(rcx, __qword_ptr[rax + rcx * 0x2]); // arg0 lastText
            c.mov(eax, __qword_ptr[r8]);
            c.test(eax, eax); // no category
            c.je(end);
            c.lea(r8, __qword_ptr[rbx + rax]); // arg2 category address
            c.lea(rdx, __qword_ptr[r14 + TextConverterDetour.BackupInstructions[1].MemoryDisplacement32]); // arg1 targetLenPtr
            c.call(replaceTextFromCategory); // replaceTextFromCategory(targetLastAddress,targetLenPtr,category)
            c.Label(ref end);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rbx);
            c.pop(rax);
            c.Label(ref back);
            c.AddInstruction(Instruction.CreateBranch(Code.Jmp_rel32_64, (ulong)jmpBackAddress.ToInt64()));
            c.int3();
            c.int3();
            c.int3();


            // replaceTextFromCategory(targetLastAddress,targetLenPtr,category)
            Label replaceTextFromCategoryLoopStart = c.CreateLabel();
            Label replaceTextFromCategoryEnd = c.CreateLabel();

            c.Label(ref replaceTextFromCategory);
            c.push(rax);
            c.push(rbx);
            c.push(rcx);
            c.push(rdx);
            c.push(rsi);
            c.push(rdi);
            c.push(r8);
            c.push(r9);
            c.mov(r9, rcx); // targetLastAddress
            c.mov(esi, __qword_ptr[r8]); // category list len
            c.lea(rdi, __qword_ptr[r8 + 0x4]); // Item[0]
            c.Label(ref replaceTextFromCategoryLoopStart);
            c.test(esi, esi);
            c.je(replaceTextFromCategoryEnd);
            c.dec(esi); // list remain;
            c.mov(ebx, __qword_ptr[rdi]); // item[n].in len
            c.cmp(__qword_ptr[originLength], ebx);
            c.mov(rcx, rdi);
            c.call(nextItem); // nextItem(item[n])
            c.mov(rdi, rax);
            c.jb(replaceTextFromCategoryLoopStart); // current item target len < in.length
            c.call(match); // match(item[n])
            c.test(rax, rax);
            c.je(replaceTextFromCategoryLoopStart);
            c.mov(r8, rcx); // inLenAddress (item[n])
            c.mov(rbx, rdx); // backup arg1
            c.mov(edx, __qword_ptr[r8]);
            c.dec(edx);
            c.mov(rax, 0x2);
            c.mul(edx);
            c.mov(edx, eax);
            c.sub(r9, rax);
            c.mov(rcx, r9);
            c.mov(rdx, rbx);
            c.call(replace); // replace(targetOverwriteStartAddress, targetLenPtr, inLenAddress)
            c.Label(ref replaceTextFromCategoryEnd);
            c.pop(r9);
            c.pop(r8);
            c.pop(rdi);
            c.pop(rsi);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rbx);
            c.pop(rax);
            c.ret();
            c.int3();
            c.int3();
            c.int3();

            // nextItem(item[n])
            c.Label(ref nextItem);
            c.push(rcx);
            c.mov(eax, __qword_ptr[rcx]); // in len
            c.lea(rcx, __qword_ptr[rcx + 0x4]); // in text address
            c.lea(rcx, __qword_ptr[rcx + rax * 0x2]); // out len address
            c.mov(eax, __qword_ptr[rcx]); // out len
            c.lea(rcx, __qword_ptr[rcx + 0x4]); // out text address
            c.lea(rcx, __qword_ptr[rcx + rax * 0x2]); // next item
            c.mov(rax, rcx);
            c.pop(rcx);
            c.ret();
            c.int3();
            c.int3();
            c.int3();


            // match(inTextLenAddress)
            c.Label(ref match);
            c.push(rcx);
            c.push(rdx);
            c.push(r8);
            c.mov(edx, __qword_ptr[rcx]); // in text length
            c.mov(r8d, edx);
            c.dec(edx); // in text last index;
            c.mov(rax, 0x2);
            c.mul(edx);
            c.mov(edx, eax);
            c.lea(rcx, __qword_ptr[rcx + 0x4]); // in text address
            c.call(getBackupLastTextAddress); // get backup origin text last address
            c.sub(rax, rdx);
            c.mov(rdx, rax);
            c.call(isEqual);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.ret();
            c.int3();
            c.int3();
            c.int3();


            // isEqual(address1,addres2,size)
            Label isEqualLoopStart = c.CreateLabel();
            Label isEqualTrue = c.CreateLabel();
            Label isEqualEnd = c.CreateLabel();

            c.Label(ref isEqual);
            c.push(rbx);
            c.push(rcx);
            c.push(rdx);
            c.push(r8);
            c.xor(rax, rax); // move word only reset 2 bytes,reset it for comfortable
            c.xor(rbx, rbx);
            c.Label(ref isEqualLoopStart);
            c.test(r8d, r8d);
            c.je(isEqualTrue);
            c.dec(r8d);
            c.mov(ax, __qword_ptr[rcx]);
            c.mov(bx, __qword_ptr[rdx]);
            c.lea(rcx, __qword_ptr[rcx + 0x2]);
            c.lea(rdx, __qword_ptr[rdx + 0x2]);
            c.cmp(ax, bx);
            c.je(isEqualLoopStart);
            c.xor(rax, rax);
            c.jmp(isEqualEnd);
            c.Label(ref isEqualTrue);
            c.xor(rax, rax);
            c.inc(rax);
            c.Label(ref isEqualEnd);
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rbx);
            c.ret();
            c.int3();
            c.int3();
            c.int3();

            // replace(targetOverwriteStartAddress, targetLenPtr, inLenAddress)
            Label replaceLoopStart = c.CreateLabel();
            Label replaceLoopEnd = c.CreateLabel();
            c.Label(ref replace);
            c.push(rax);
            c.push(rcx);
            c.push(rdx);
            c.push(r8);
            c.mov(eax, __qword_ptr[r8]);
            c.sub(__qword_ptr[rdx], eax);
            c.lea(r8, __qword_ptr[r8 + 0x4]); // in text address
            c.lea(r8, __qword_ptr[r8 + rax * 0x2]); // out len address
            c.mov(eax, __qword_ptr[r8]); // out len
            c.add(__qword_ptr[rdx], eax); // fix target last Index by "out.len - in.len"
            c.lea(r8, __qword_ptr[r8 + 0x4]); // out text start
            c.xor(rdx, rdx);
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
            c.pop(r8);
            c.pop(rdx);
            c.pop(rcx);
            c.pop(rax);
            c.ret();
            c.int3();
            c.int3();
            c.int3();

            // getBackupLastTextAddress()
            c.Label(ref getBackupLastTextAddress);
            c.push(rbx);
            c.lea(rax, __qword_ptr[originText]);
            c.mov(ebx, __qword_ptr[originLength]);
            c.dec(ebx); // len to last index
            c.lea(rax, rax + rbx * 0x2);
            c.pop(rbx);
            c.ret();
            c.int3();
            c.int3();
            c.int3();

            c.Label(ref originLength);
            c.nop();
            c.nop();
            c.nop();
            c.nop();
            c.Label(ref originText);
            c.nop();

            c.Assemble(codeWriter, (ulong)TextConverterAddress.ToInt64());
            Utils.WriteMemory(TextConverterAddress, codeWriter.data.ToArray());
            // Utils.PrintOpcodes(codeWriter.data.ToArray(), IntPtr.Zero);
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

            // jianfan.json edited from https://github.com/kfcd/fanjian, appreciate kfcd.
            // LICENSE http://creativecommons.org/licenses/by/3.0/deed.zh_TW
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
                this.CategoryKey = InLength == 0 ? (ushort)0 : BitConverter.ToUInt16(BitConverter.GetBytes(In[InLength - 1]), 0);

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
                bytes.AddRange(inBytes);
                bytes.AddRange(BitConverter.GetBytes(this.OutLength));
                byte[] outBytes = unicodeEncoding.GetBytes(this.Out);
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
                if (item.In.Length == 0 || item.Out.Length == 0) return;
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
