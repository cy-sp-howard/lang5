using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using SharpDX.Direct3D9;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BhModule.Lang5
{
    public static class Utils
    {
        public static NotifyClass Notify = new NotifyClass();
        public static IntPtr FindReadonlyStringRef(String str)
        {
            return Find.FindReadonlyStringRef(str, GameService.GameIntegration.Gw2Instance.Gw2Process);
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
            IntPtr bufferReadSize;
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out bufferReadSize);
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
            IntPtr bufferReadSize;
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out bufferReadSize);
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
                if (isEqual(pattern, SubAry(ref source, i, pattern.Length))) return i;
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
        static bool isEqual(byte[] pattern, byte[] memory)
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
        static bool isEqual(string[] pattern, byte[] memory)
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
}
