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

namespace BhModule.Lang5
{
    public class MemService
    {
        private readonly Lang5Module module;
        private IntPtr ZHDataAddress;
        public MemService(Lang5Module module)
        {
            this.module = module;
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) Init();
            GameService.GameIntegration.Gw2Instance.Gw2Started += delegate { Init(); };
        }
        public void Upadate() { }
        public void Unload() {
            Utils.FreeMemory(ZHDataAddress);
        }
        public void Init()
        {
            AllocZHData();
        }
        public void SetUILang()
        {

        }
        private void SetLangPtr()
        {
            var result = Utils.FindReadonlyStringRef("ValidateLanguage(language)");
            //var result2 = Utils.FindReadonlyStringRef("ch >= STRING_CHAR_FIRST");
            //IntPtr.Add(result2, 0x26);
            //"66 89 34 48 41 FF 46 14" =>  "E9 A1B0ADFF 0F1F 00" jmp
            // "66 89 34 48 41 FF 46 14"  "E9 494F5200" jmp back

            IntPtr baseAddress = Utils.AllocMemory(1000);

            HowTo_Disassemble.Example();
        }
        private void AllocZHData()
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
            byte[] data_byte_ary = data_byte.ToArray();
            Utils.WriteMemory(ZHDataAddress, ref data_byte_ary);
        }
        private void GenFuncBytes()
        {
            IntPtr.Add(Utils.FindReadonlyStringRef("ch >= STRING_CHAR_FIRST"), 0x26);
            var c = new Assembler(64);
            var replaceCodeAddress = c.CreateLabel();

        }
    }
    public class ZH
    {
        [JsonPropertyName("o")]
        public string Out { get; set; }
        [JsonPropertyName("i")]
        public string In { get; set; }
    }
    static class HowTo_Disassemble
    {
        /*
         * This method produces the following output:
    00007FFAC46ACDA4 48895C2410           mov       [rsp+10h],rbx
    00007FFAC46ACDA9 4889742418           mov       [rsp+18h],rsi
    00007FFAC46ACDAE 55                   push      rbp
    00007FFAC46ACDAF 57                   push      rdi
    00007FFAC46ACDB0 4156                 push      r14
    00007FFAC46ACDB2 488DAC2400FFFFFF     lea       rbp,[rsp-100h]
    00007FFAC46ACDBA 4881EC00020000       sub       rsp,200h
    00007FFAC46ACDC1 488B0518570A00       mov       rax,[rel 7FFA`C475`24E0h]
    00007FFAC46ACDC8 4833C4               xor       rax,rsp
    00007FFAC46ACDCB 488985F0000000       mov       [rbp+0F0h],rax
    00007FFAC46ACDD2 4C8B052F240A00       mov       r8,[rel 7FFA`C474`F208h]
    00007FFAC46ACDD9 488D05787C0400       lea       rax,[rel 7FFA`C46F`4A58h]
    00007FFAC46ACDE0 33FF                 xor       edi,edi
        */
        public static void Example()
        {

            // You can also pass in a hex string, eg. "90 91 929394", or you can use your own CodeReader
            // reading data from a file or memory etc
            var codeBytes = exampleCode;
            var codeReader = new ByteArrayCodeReader(codeBytes);
            var decoder = Decoder.Create(exampleCodeBitness, codeReader);
            decoder.IP = exampleCodeRIP;//code start address
            ulong endRip = decoder.IP + (uint)codeBytes.Length;

            var instructions = new List<Instruction>();
            while (decoder.IP < endRip)
                instructions.Add(decoder.Decode());

            // Formatters: Masm*, Nasm*, Gas* (AT&T) and Intel* (XED).
            // There's also `FastFormatter` which is ~2x faster. Use it if formatting speed is more
            // important than being able to re-assemble formatted instructions.
            var formatter = new IntelFormatter();
            formatter.Options.DigitSeparator = "`";
            formatter.Options.FirstOperandCharIndex = 10;
            var output = new StringOutput();
            foreach (var instr in instructions)
            {
                // Don't use instr.ToString(), it allocates more, uses masm syntax and default options
                formatter.Format(instr, output);
                Trace.Write(instr.IP.ToString("X16"));
                Trace.Write(" ");
                int instrLen = instr.Length;
                int byteBaseIndex = (int)(instr.IP - exampleCodeRIP);
                for (int i = 0; i < instrLen; i++)
                    Trace.Write(codeBytes[byteBaseIndex + i].ToString("X2"));
                int missingBytes = HEXBYTES_COLUMN_BYTE_LENGTH - instrLen;
                for (int i = 0; i < missingBytes; i++)
                    Trace.Write("  ");
                Trace.Write(" ");
                Trace.WriteLine(output.ToStringAndReset());
            }
        }

        const int HEXBYTES_COLUMN_BYTE_LENGTH = 10;
        const int exampleCodeBitness = 64;
        const ulong exampleCodeRIP = 0x00007FFAC46ACDA4;
        static readonly byte[] exampleCode = new byte[] {
        0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x55, 0x57, 0x41, 0x56, 0x48, 0x8D,
        0xAC, 0x24, 0x00, 0xFF, 0xFF, 0xFF, 0x48, 0x81, 0xEC, 0x00, 0x02, 0x00, 0x00, 0x48, 0x8B, 0x05,
        0x18, 0x57, 0x0A, 0x00, 0x48, 0x33, 0xC4, 0x48, 0x89, 0x85, 0xF0, 0x00, 0x00, 0x00, 0x4C, 0x8B,
        0x05, 0x2F, 0x24, 0x0A, 0x00, 0x48, 0x8D, 0x05, 0x78, 0x7C, 0x04, 0x00, 0x33, 0xFF
    };
    }
}
