using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Iced.Intel;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BhModule.Lang5
{
    public static class Utils
    {
        public static NotifyClass Notify = new NotifyClass();
        public static IntPtr FindReadonlyStringRef(String str)
        {
            return Find.FindReadonlyStringRef(str, GameService.GameIntegration.Gw2Instance.Gw2Process);
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
                // Don't use instr.ToString(), it allocates more, uses masm syntax and default options
                formatter.Format(instr, output);
                Trace.Write(instr.IP.ToString("X16"));
                Trace.Write(" ");
                int instrLen = instr.Length;
                int byteBaseIndex = (int)(instr.IP - rip_long);
                for (int i = 0; i < instrLen; i++)
                    Trace.Write(bytes[byteBaseIndex + i].ToString("X2") + " ");
                int missingSpaces = 20 - instrLen * 3;
                for (int i = 0; i < missingSpaces; i++)
                    Trace.Write(" ");
                Trace.Write(" ");
                Trace.WriteLine(output.ToStringAndReset());
            }
        }

    }
    public class UtilsExtern
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
    public class Find
    {
        public static IntPtr FindReadonlyStringRef(String str, Process process, ProcessModule module = null)
        {
            Encoding utf8Encoding = Encoding.UTF8;
            String pattern = BitConverter.ToString(utf8Encoding.GetBytes(str)).Replace("-", "");
            IntPtr stringAddress = FindPattern(pattern, process, module);
            return FinRef(stringAddress, process, module);
        }
        public static IntPtr FinRef(IntPtr addr, Process process, ProcessModule module = null)
        {
            long addr_long = addr.ToInt64();
            if (module == null) module = process.MainModule;
            int totalMemoryBytesSize = module.ModuleMemorySize;
            IntPtr startAddr = module.BaseAddress;
            IntPtr endAddr = IntPtr.Add(module.BaseAddress, totalMemoryBytesSize);


            int pageSize = 6400000;
            int remainSize = module.ModuleMemorySize / pageSize;
            int maxPage = module.ModuleMemorySize / pageSize + (remainSize == 0 ? 0 : 1);
            int currentPage = 1;
            byte[] buffer = new byte[totalMemoryBytesSize < pageSize ? totalMemoryBytesSize : pageSize];
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out IntPtr bufferReadSize);
                long pageStartAddr_long = pageStartAddr.ToInt64();
                for (int i = 0; i < buffer.Length - 3; i++)
                {
                    long rip_long = pageStartAddr_long + i + 4;
                    int currentValue = BitConverter.ToInt32(buffer, i);
                    long followAddr_long = rip_long + currentValue;
                    if (addr_long == followAddr_long)
                    {
                        return IntPtr.Add(pageStartAddr, i);
                    };
                }
                currentPage += 1;
            } while (currentPage <= maxPage);
            return IntPtr.Zero;

        }
        public static IntPtr FindPattern(string pattern, Process process, ProcessModule module = null)
        {
            if (module == null) module = process.MainModule;
            string[] patternAry = PatternToAry(pattern);


            int totalMemoryBytesSize = module.ModuleMemorySize;
            IntPtr startAddr = module.BaseAddress;
            IntPtr endAddr = IntPtr.Add(module.BaseAddress, totalMemoryBytesSize - patternAry.Length);


            int pageSize = 6400000;
            int remainSize = module.ModuleMemorySize / pageSize;
            int maxPage = module.ModuleMemorySize / pageSize + (remainSize == 0 ? 0 : 1);
            int currentPage = 1;


            byte[] buffer = new byte[totalMemoryBytesSize < pageSize ? totalMemoryBytesSize : pageSize];
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out IntPtr bufferReadSize);
                int index = -1;
                if (pattern.IndexOf("?") == -1)
                {
                    int foundIndex = BitConverter.ToString(buffer).Replace("-", "").IndexOf(pattern);
                    if (foundIndex > -1)
                    {
                        index = foundIndex / 2;
                    }
                }
                else
                {
                    index = IndexOfPattern(ref patternAry, ref buffer);
                }
                if (index > -1) return IntPtr.Add(pageStartAddr, index);
                currentPage += 1;
            } while (currentPage <= maxPage);
            return IntPtr.Zero;
        }
        static string[] PatternToAry(string pattern)
        {
            pattern = pattern.Replace(" ", "").ToUpper();
            bool isEven = pattern.Length % 2 == 0;
            if (!isEven) pattern = "0" + pattern;
            string[] result = new string[pattern.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = pattern.Substring(i * 2, 2);
            }
            return result;
        }
        static int IndexOfPattern(ref string[] pattern, ref byte[] source)
        {
            if (source.Length < pattern.Length) { return -1; }
            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                if (IsEqual(pattern, SubAry(ref source, i, pattern.Length))) return i;
            }
            return -1;
        }
        static T[] SubAry<T>(ref T[] ary, int startIndex, int size)
        {
            T[] result = new T[size];
            for (int i = 0; i < size; i++)
            {
                result[i] = ary[i + startIndex];
            }
            return result;
        }
        static bool IsEqual(byte[] pattern, byte[] memory)
        {
            for (int i = 0; i < pattern.Length; i++)
            {

                if (pattern[i] != memory[i])
                {
                    return false;
                }
            }
            return true;
        }
        static bool IsEqual(string[] pattern, byte[] memory)
        {
            string patternString = String.Join("", pattern);
            if (patternString.IndexOf("?") == -1)
            {
                return patternString == BitConverter.ToString(memory).Replace("-", "");
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                string patternItem = pattern[i];

                int maskIndex = patternItem.IndexOf("?");
                if (maskIndex > -1)
                {
                    if (patternItem != "??" && memory[i].ToString()[maskIndex] != patternItem[maskIndex])
                    {
                        return false;
                    }

                }
                else if (memory[i] != Convert.ToByte(patternItem, 16))
                {
                    return false;
                }
            }
            return true;
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
        private const float duration = 3000;
        private string message;
        private DateTime msgStartTime = DateTime.Now;
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (message == null) return;
            double existTime = (DateTime.Now - msgStartTime).TotalMilliseconds;
            float opacity = (duration - (float)existTime) / duration;
            if (opacity < 0)
            {
                Clear();
                return;
            }
            Color textColor = Color.Yellow * opacity;
            spriteBatch.DrawStringOnCtrl(this, message, GameService.Content.DefaultFont32, new Rectangle(0, 0, Width, Height), textColor, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Middle);

        }
        public void Clear()
        {
            Parent = null;
            message = null;
        }
        public void Show(string text)
        {
            Parent = GameService.Graphics.SpriteScreen;
            Size = new Point(Parent.Size.X, 50);
            Location = new Point(0, Parent.Size.Y / 10 * 2);
            msgStartTime = DateTime.Now;
            message = text;
        }
        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
        }
    }
    public class IcedUtils
    {
        public static byte[] GetAddressBytes(IntPtr addr, IntPtr rip)
        {
            long val = addr.ToInt64() - rip.ToInt64();
            return BitConverter.GetBytes((int)val);
        }
    }
}
