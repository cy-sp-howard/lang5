using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Pkgs;
using Iced.Intel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BhModule.Lang5
{
    public static class Utils
    {
        public static readonly NotifyClass Notify = new();
        public static Dictionary<string, IntPtr> FindReadonlyStringRefs(string[] strings)
        {
            IntPtr[] addresses = Find.FindReadonlyStringRefs(strings);
            Dictionary<string, IntPtr> result = [];
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
            return UtilsExtern.WriteProcessMemory(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, lpBaseAddress, lpBuffer, lpBuffer.Length, out int _);
        }
        public static byte[] ReadMemory(IntPtr address, int size)
        {
            byte[] buffer = new byte[size];
            UtilsExtern.ReadProcessMemory(GameService.GameIntegration.Gw2Instance.Gw2Process.Handle, address, buffer, buffer.Length, out IntPtr _);
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
            List<Instruction> instructions = [];
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
            using (StreamReader sr = new(filePath))
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
            List<byte[]> stringPatterns = [];
            foreach (string str in strings)
            {
                stringPatterns.Add(utf8Encoding.GetBytes(str));
            }

            List<byte[]> addressPatterns = [];
            foreach (var address in FindBytes(stringPatterns))
            {
                addressPatterns.Add(BitConverter.GetBytes(address.ToInt64()));
            }
            return FindBytes(addressPatterns, RelFindIndex);
        }
        public static IntPtr[] FindBytes(List<byte[]> patterns, Func<IntPtr, byte[], byte[], byte[], int?> findIndexFunc = null)
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
            int keepBufferTailLen = patterns.Select(i => i.Length).Max();
            do
            {
                IntPtr pageStartAddr = IntPtr.Add(startAddr, (currentPage - 1) * pageSize);
                byte[] previousPageBufferTail = [.. buffer.Skip(buffer.Length - keepBufferTailLen)];
                if (currentPage == maxPage)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] = 0;
                    }
                }

                UtilsExtern.ReadProcessMemory(process.Handle, pageStartAddr, buffer, buffer.Length, out IntPtr bufferReadSize);


                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == IntPtr.Zero)
                    {
                        Func<IntPtr, byte[], byte[], byte[], int?> findIndex = findIndexFunc ?? DefaultFindIndex;

                        int? foundIndex = findIndex(pageStartAddr, buffer, patterns[i], previousPageBufferTail);
                        if (foundIndex != null)
                        {
                            foundCount++;
                            result[i] = IntPtr.Add(pageStartAddr, foundIndex ?? 0);
                        }
                    }
                }
                if (foundCount == result.Length) break;
                currentPage += 1;
            } while (currentPage <= maxPage);
            return result;
        }
        static int? RelFindIndex(IntPtr sourceAddress, byte[] source, byte[] target, byte[] previousSource)
        {
            long address = sourceAddress.ToInt64();
            long targetAddress = BitConverter.ToInt64(target, 0);

            for (int i = -3; i < source.Length - 3; i++)
            {
                long rip = address + i + 4;
                int currentValue = i >= 0 ? BitConverter.ToInt32(source, i) : BitConverter.ToInt32([.. previousSource.Skip(8 + i), .. source.Take(4 + i)], 0);
                long followAddress = rip + currentValue;
                if (targetAddress == followAddress)
                {
                    int _i = i;
                    if ((i - 3) < 0)
                    {
                        _i = i + 3 + 3; // i - 3   min -6
                    }
                    if (source[_i - 1] == 0x0d && source[_i - 2] == 0x8d && source[_i - 3] == 0x48) return i;
                }
            }
            return null;
        }
        static int? DefaultFindIndex(IntPtr sourceAddress, byte[] source, byte[] target, byte[] previousSource)
        {
            previousSource = [.. previousSource.Skip(previousSource.Length - target.Length + 1)];
            for (int i = previousSource.Length * -1; i <= source.Length - target.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < target.Length; j++)
                {
                    int sourceIndex = i + j;
                    byte sourceByte = sourceIndex >= 0 ? source[sourceIndex] : previousSource[previousSource.Length + i];
                    if (sourceByte != target[j])
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
            return null;

        }
    }
    public static class UpdateModule
    {
        public class UpdateManifest : PkgManifestV1
        {
            public UpdateManifest(Manifest manifest)
            {
                SetValue("Name", manifest.Name);
                SetValue("Namespace", manifest.Namespace);
                SetValue("Version", manifest.Version);
                SetValue("Contributors", manifest.Contributors);
                SetValue("Dependencies", manifest.Dependencies);
                Url = manifest.Url;
                Description = manifest.Description;
            }
            public void SetValue(string key, object value)
            {
                typeof(PkgManifest).GetProperty(key,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).SetValue(this, value);
            }
        }
        public class Release
        {
            [JsonPropertyName("tag_name")]
            public string Verison { get; set; }
            [JsonPropertyName("assets")]
            public List<File> Files { get; set; }
        }
        public class File
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("browser_download_url")]
            public string Url { get; set; }
            [JsonPropertyName("digest")]
            public string Hash { get; set; }
        }
    }
    public class OverwriteOpcodes
    {
        public readonly IReadOnlyList<byte> BackupBytes;
        public readonly IReadOnlyList<byte> OverwriteBytes;
        public readonly IReadOnlyList<Instruction> BackupInstructions;
        public readonly IntPtr Address;
        public readonly bool Valid;
        public OverwriteOpcodes(IntPtr address, byte[] originBytes, byte[] overwriteBytes) : this(address, originBytes, overwriteBytes, []) { }
        public OverwriteOpcodes(IntPtr address, byte[] originBytes, byte[] overwriteBytes, byte[] expectedOriginBytes)
        {
            List<byte> backupOpcodes = [];
            List<byte> overwriteOpcodes = [.. overwriteBytes];

            int atLeastbackupSize = overwriteBytes.Length;
            List<Instruction> originBytesInstructions = Utils.ParseOpcodes([.. originBytes], address);
            List<Instruction> backupInstructions = [];

            for (int row = 0; backupOpcodes.Count < atLeastbackupSize; row++)
            {
                backupInstructions.Add(originBytesInstructions[row]);
                for (int i = backupOpcodes.Count, i_start = backupOpcodes.Count; i < i_start + originBytesInstructions[row].Length; i++)
                {
                    backupOpcodes.Add(originBytes[i]);
                    if (i > atLeastbackupSize - 1) overwriteOpcodes.Add(0x90);
                }
            }

            Address = address;
            BackupBytes = backupOpcodes;
            OverwriteBytes = overwriteOpcodes;
            BackupInstructions = backupInstructions;
            Valid = Validate(expectedOriginBytes);
        }
        public void Write()
        {
            if (!Valid) return;
            Utils.WriteMemory(Address, [.. OverwriteBytes]);
        }
        public void Undo()
        {
            Utils.WriteMemory(Address, [.. BackupBytes]);
        }
        bool Validate(byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (BackupBytes[i] != expected[i])
                {
                    var expectedBytesStr = string.Join(", ", expected.Select(i => $"0x{i:X}"));
                    Utils.Notify.Show($"Unexpected opcodes {expectedBytesStr}", 6000);
                    return false;
                }
            }
            return true;
        }
    }
    public class ListCodeWriter : CodeWriter
    {
        public IReadOnlyList<byte> Data => _allBytes;
        readonly List<byte> _allBytes = [];
        public override void WriteByte(byte value) => _allBytes.Add(value);
        public void Clear() { _allBytes.Clear(); }
    }
    public class NotifyClass : Control
    {
        float _duration = 3000;
        string _message;
        bool _waitingForPaint = true;
        DateTime _msgStartTime = DateTime.Now;
        public override void DoUpdate(GameTime gameTime)
        {
            Size = new Point(Parent.Size.X, 200);
            Location = new Point(0, Parent.Size.Y / 10 * 2);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_message != null)
            {
                if (_waitingForPaint) _msgStartTime = DateTime.Now;
                float existTime = (float)(DateTime.Now - _msgStartTime).TotalMilliseconds;
                float remainTime = _duration - (float)existTime;
                float opacity = remainTime > 1000 ? 1 : remainTime / 1000;
                if (opacity < 0)
                {
                    Clear();
                    return;
                }
                Color textColor = Color.Yellow * opacity;
                spriteBatch.DrawStringOnCtrl(this, _message, GameService.Content.DefaultFont32, new Rectangle(0, 0, Width, Height), textColor, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            _waitingForPaint = false;
        }
        public void Clear()
        {
            Parent = null;
            _message = null;
        }
        public void Show(string text, float duration = 3000)
        {
            Parent = GameService.Graphics.SpriteScreen;
            _msgStartTime = DateTime.Now;
            _message = text;
            _duration = duration;
        }
        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
        }
    }
}
