using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;
using System.Text;
using Iced.Intel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace BhModule.Lang5
{
    public static class Utils
    {
        public static NotifyClass Notify = new NotifyClass();
        public static Dictionary<string, IntPtr> FindReadonlyStringRefs(string[] strings)
        {
            IntPtr[] addresses = Find.FindReadonlyStringRefs(strings);
            Dictionary<string, IntPtr> result = new();
            for (int i = 0; i < addresses.Length; i++)
            {
                result[strings[i]] = addresses[i];
            }
            return result;
        }
        public static IntPtr AllocMemory(int size)
        {
            return AllocMemory(size, IntPtr.Zero);
        }
        public static IntPtr AllocMemory(int size, IntPtr target)
        {
            return UtilsExtern.VirtualAllocEx(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, target,
                                 size,
                                 0x00001000 | 0x00002000,
                                 0x40);
        }
        public static bool FreeMemory(IntPtr address)
        {
            return UtilsExtern.VirtualFreeEx(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, address, 0, 0x8000);
        }
        public static bool WriteMemory(IntPtr lpBaseAddress, byte[] lpBuffer)
        {
            return UtilsExtern.WriteProcessMemory(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, lpBaseAddress, lpBuffer, lpBuffer.Length, out int bytesWritten);
        }
        public static byte[] ReadMemory(IntPtr address, int size)
        {
            byte[] buffer = new byte[size];
            UtilsExtern.ReadProcessMemory(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, address, buffer, buffer.Length, out IntPtr bufferReadSize);
            return buffer;
        }
        public static IntPtr FollowAddress(IntPtr address)
        {
            int val = BitConverter.ToInt32(ReadMemory(address, 4), 0);
            return IntPtr.Add(address, val + 4);
        }
        public static List<Instruction> ParseOpcodes(byte[] opcodes, IntPtr rip)
        {
            ByteArrayCodeReader codeReader = new(opcodes);
            var decoder = Iced.Intel.Decoder.Create(64, codeReader);
            ulong firstRIP = (ulong)rip.ToInt64();
            decoder.IP = firstRIP;
            List<Instruction> instructions = new();
            while (decoder.IP < firstRIP + (ulong)opcodes.Length)
                instructions.Add(decoder.Decode());
            return instructions;
        }
        static public void PrintOpcodes(byte[] bytes, IntPtr RIP)
        {
            ulong rip_long = (ulong)RIP.ToInt64();
            var codeReader = new ByteArrayCodeReader(bytes);
            var decoder = Iced.Intel.Decoder.Create(64, codeReader);
            decoder.IP = rip_long;
            var instructions = new List<Instruction>();
            while (decoder.IP < rip_long + (ulong)bytes.Length)
                instructions.Add(decoder.Decode());


            var formatter = new IntelFormatter();
            var output = new StringOutput();
            foreach (var instr in instructions)
            {
                formatter.Format(instr, output);
                Trace.Write(instr.IP.ToString("X16"));
                Trace.Write(" ");
                int instrLen = instr.Length;
                int byteBaseIndex = (int)(instr.IP - rip_long);
                for (int i = 0; i < instrLen; i++)
                    Trace.Write(bytes[byteBaseIndex + i].ToString("X2") + " ");
                int missingSpaces = 30 - instrLen * 3;
                for (int i = 0; i < missingSpaces; i++)
                    Trace.Write(" ");
                Trace.Write(" ");
                Trace.WriteLine(output.ToStringAndReset());
            }
        }
        static public T GetJson<T>(string filePath)
        {
            byte[] buffer;
            using (MemoryStream fileStream = Lang5Module.Instance.ContentsManager.GetFileStream(filePath) as MemoryStream)
            {
                buffer = fileStream?.ToArray();
            }
            if (buffer != null) return JsonSerializer.Deserialize<T>(buffer);

            string jsonText;
            using (StreamReader sr = new StreamReader(filePath))
            {
                jsonText = sr.ReadToEnd();
            }
            return JsonSerializer.Deserialize<T>(jsonText);
        }
    }
    public static class UtilsExtern
    {
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int flAllocationType, int flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int dwFreeType);
    }
    public static class Find
    {
        public static IntPtr[] FindReadonlyStringRefs(string[] strings)
        {
            Encoding utf8Encoding = Encoding.UTF8;
            List<byte[]> stringPatterns = new();
            foreach (string str in strings)
            {
                stringPatterns.Add(utf8Encoding.GetBytes(str));
            }

            List<byte[]> addressPatterns = new();
            foreach (var address in FindBytes(stringPatterns))
            {
                addressPatterns.Add(BitConverter.GetBytes(address.ToInt64()));
            }
            return FindBytes(addressPatterns, RelFindIndex);
        }
        public static IntPtr[] FindBytes(List<byte[]> patterns, Func<IntPtr, byte[], byte[], int> findIndexFunc = null)
        {
            Process process = GameService.GameIntegration.Gw2Instance.Gw2Process;
            ProcessModule module = process.MainModule;
            int totalMemoryBytesSize = module.ModuleMemorySize;
            IntPtr startAddr = module.BaseAddress;

            int pageSize = 6400000;
            int remainSize = module.ModuleMemorySize / pageSize;
            int maxPage = module.ModuleMemorySize / pageSize + (remainSize == 0 ? 0 : 1);
            int currentPage = 1;

            IntPtr[] result = new IntPtr[patterns.Count];
            int foundCount = 0;
            byte[] buffer = new byte[totalMemoryBytesSize < pageSize ? totalMemoryBytesSize : pageSize];
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out IntPtr bufferReadSize);


                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == IntPtr.Zero)
                    {
                        Func<IntPtr, byte[], byte[], int> findIndex = findIndexFunc ?? DefaultFindIndex;
                        int foundIndex = findIndex(pageStartAddr, buffer, patterns[i]);
                        if (foundIndex > -1)
                        {
                            foundCount++;
                            result[i] = IntPtr.Add(pageStartAddr, foundIndex);
                        }
                    };

                }
                if (foundCount == result.Length) break;
                currentPage += 1;
            } while (currentPage <= maxPage);
            return result;
        }
        static int RelFindIndex(IntPtr sourceAddress, byte[] source, byte[] target)
        {
            long address = sourceAddress.ToInt64();
            long targetAddress = BitConverter.ToInt64(target, 0);

            for (int i = 0; i < source.Length - 3; i++)
            {
                long rip = address + i + 4;
                int currentValue = BitConverter.ToInt32(source, i);
                long followAddress = rip + currentValue;
                if (targetAddress == followAddress)
                {
                    if(source[i - 1] == 0x0d && source[i - 2] == 0x8d && source[i - 3] == 0x48) return i;

                };
            }
            return -1;
        }
        static int DefaultFindIndex(IntPtr sourceAddress, byte[] source, byte[] target)
        {

            for (int i = 0; i <= source.Length - target.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < target.Length; j++)
                {
                    if (source[i + j] != target[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;

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
    public class ListCodeWriter : CodeWriter
    {
        public IReadOnlyList<byte> data => allBytes;
        readonly List<byte> allBytes = new();
        public override void WriteByte(byte value) => allBytes.Add(value);
        public void Clear() { allBytes.Clear(); }

    }
    public class NotifyClass : Control
    {
        private float duration = 3000;
        private string message;
        private bool waitingForPaint = true;
        private DateTime msgStartTime = DateTime.Now;
        public override void DoUpdate(GameTime gameTime)
        {
            Size = new Point(Parent.Size.X, 200);
            Location = new Point(0, Parent.Size.Y / 10 * 2);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (message != null)
            {
                if (waitingForPaint) msgStartTime = DateTime.Now;
                float existTime = (float)(DateTime.Now - msgStartTime).TotalMilliseconds;
                float remainTime = duration - (float)existTime;
                float opacity = remainTime > 1000 ? 1 : remainTime / 1000;
                if (opacity < 0)
                {
                    Clear();
                    return;
                }
                Color textColor = Color.Yellow * opacity;
                spriteBatch.DrawStringOnCtrl(this, message, GameService.Content.DefaultFont32, new Rectangle(0, 0, Width, Height), textColor, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            waitingForPaint = false;
        }
        public void Clear()
        {
            Parent = null;
            message = null;
        }
        public void Show(string text, float duration = 3000)
        {
            Parent = GameService.Graphics.SpriteScreen;
            msgStartTime = DateTime.Now;
            message = text;
            this.duration = duration;
        }
        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
        }
    }
}
