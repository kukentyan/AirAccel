using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AirAccel
{
    class Program
    {
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int VK_RSHIFT = 0xA1;

        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);

        private delegate bool ConsoleEventDelegate(int eventType);
        private static ConsoleEventDelegate? _consoleEventCallback;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
        private const uint STILL_ACTIVE = 259;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, nuint dwSize, out nuint lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nuint VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nuint dwLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private static IntPtr _processHandle = IntPtr.Zero;
        private static IntPtr _airAccelAddress = IntPtr.Zero;
        private static byte[]? _airAccelOriginalBytes = null;
        private static IntPtr _airAccelAllocated = IntPtr.Zero;
        private const float AirAccelBaseValue = 0.02f;
        private static Mutex? _appMutex;
        private static readonly string MutexName = "AirAccel-{B9C8E7F6-D1A2-4B3C-9F8E-7D6C5B4A3921}";
        private static float _currentMultiplier = 1.0f;
        private static readonly object _syncLock = new object();

        static void Main(string[] args)
        {
            AllocConsole();
            
            IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
            if (GetConsoleMode(consoleHandle, out uint consoleMode))
            {
                consoleMode &= ~ENABLE_QUICK_EDIT_MODE;
                SetConsoleMode(consoleHandle, consoleMode);
            }

            Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetIn(new System.IO.StreamReader(Console.OpenStandardInput()));

            _appMutex = new Mutex(false, MutexName, out bool createdNew);
            if (!createdNew)
            {
                return;
            }

            try
            {
                Console.Title = "AirAccel - Made by Ducky | .gg/xq8sWQhuXG";
                Console.ForegroundColor = ConsoleColor.Magenta;

                if (OperatingSystem.IsWindows())
                {
                    Console.SetWindowSize(70, 20);
                    Console.SetBufferSize(70, 20);
                }
            }
            catch { }

            AppDomain.CurrentDomain.ProcessExit += OnExit;
            Console.CancelKeyPress += (s, e) => { OnExit(s, e); };

            _consoleEventCallback = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(_consoleEventCallback, true);

            Thread monitorThread = new Thread(() =>
            {
                while (true)
                {
                    bool isConnected = false;
                    lock (_syncLock)
                    {
                        if (_processHandle != IntPtr.Zero)
                        {
                            if (GetExitCodeProcess(_processHandle, out uint exitCode) && exitCode == STILL_ACTIVE)
                            {
                                isConnected = true;
                            }
                            else
                            {
                                CloseHandle(_processHandle);
                                _processHandle = IntPtr.Zero;
                                _airAccelAddress = IntPtr.Zero;
                                _airAccelAllocated = IntPtr.Zero;
                                _airAccelOriginalBytes = null;
                            }
                        }

                        if (!isConnected)
                        {
                            var procs = Process.GetProcessesByName("Minecraft.Windows");
                            if (procs.Length > 0)
                            {
                                _processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, procs[0].Id);
                                if (_processHandle != IntPtr.Zero)
                                {
                                    IntPtr found = ScanSignature("81 F3 41 0F 11 40 0C");
                                    if (found != IntPtr.Zero)
                                    {
                                        _airAccelAddress = found + 1;
                                        ApplyAirAcceleration(_currentMultiplier);
                                        isConnected = true;
                                    }
                                    else
                                    {
                                        CloseHandle(_processHandle);
                                        _processHandle = IntPtr.Zero;
                                    }
                                }
                            }
                        }
                    }
                    Thread.Sleep(2000);
                }
            });
            monitorThread.IsBackground = true;
            monitorThread.Start();

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        Thread toggleThread = new Thread(() =>
        {
            bool isHidden = false;
            IntPtr hWnd = GetConsoleWindow();
            bool wasPressed = false;
            while (true)
            {
                bool isPressed = (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;
                if (isPressed && !wasPressed)
                {
                    isHidden = !isHidden;
                    if (isHidden)
                    {
                        int style = GetWindowLong(hWnd, GWL_EXSTYLE);
                        SetWindowLong(hWnd, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
                        ShowWindow(hWnd, SW_HIDE);
                    }
                    else
                    {
                        int style = GetWindowLong(hWnd, GWL_EXSTYLE);
                        SetWindowLong(hWnd, GWL_EXSTYLE, (style | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW);
                        ShowWindow(hWnd, SW_SHOW);
                    }
                }
                wasPressed = isPressed;
                Thread.Sleep(10);
            }
        });
            toggleThread.IsBackground = true;
            toggleThread.Start();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("\nHide: RSHIFT");
                Console.WriteLine($"Air (Ex: 1.02): {_currentMultiplier}\n");
                Console.Write("Enter Air Accel: ");
                
                string? input = Console.ReadLine();
                if (input == null)
                {
                    Thread.Sleep(100);
                    continue;
                }
                
                if (float.TryParse(input, out float multiplier))
                {
                    lock (_syncLock)
                    {
                        _currentMultiplier = multiplier;
                        if (_processHandle != IntPtr.Zero && _airAccelAddress != IntPtr.Zero)
                        {
                            ApplyAirAcceleration(multiplier);
                        }
                    }
                }
            }
        }

        private static void OnExit(object? sender, EventArgs? e)
        {
            lock (_syncLock)
            {
                ResetAirAcceleration();
                if (_processHandle != IntPtr.Zero)
                {
                    CloseHandle(_processHandle);
                    _processHandle = IntPtr.Zero;
                }
                if (_appMutex != null)
                {
                    try { _appMutex.Dispose(); } catch { }
                    _appMutex = null;
                }
            }
        }

        private static bool ConsoleEventCallback(int eventType)
        {
            OnExit(null, null);
            return false;
        }

        private static void ResetAirAcceleration()
        {
            if (_airAccelAddress != IntPtr.Zero && _airAccelOriginalBytes != null)
            {
                WriteProtectedBytes(_airAccelAddress, _airAccelOriginalBytes);
            }

            if (_airAccelAllocated != IntPtr.Zero)
            {
                VirtualFreeEx(_processHandle, _airAccelAllocated, 0, MEM_RELEASE);
                _airAccelAllocated = IntPtr.Zero;
            }
        }

        private static bool ApplyAirAcceleration(float multiplier)
        {
            float finalValue = AirAccelBaseValue * Math.Clamp(multiplier, 0.1f, 10.0f);

            if (_airAccelAllocated != IntPtr.Zero)
            {
                WriteBytes((IntPtr)(_airAccelAllocated.ToInt64() + 4), BitConverter.GetBytes(finalValue));
                return true;
            }

            int instrLen = 6;
            if (_airAccelOriginalBytes == null)
            {
                _airAccelOriginalBytes = ReadBytes(_airAccelAddress, instrLen);
                if (_airAccelOriginalBytes == null) return false;
            }

            _airAccelAllocated = AllocateNear(_airAccelAddress, 0x100);
            if (_airAccelAllocated == IntPtr.Zero) return false;

            var sc = new List<byte>();
            sc.AddRange(new byte[] { 0x41, 0xC7, 0x40, 0x0C });
            sc.AddRange(BitConverter.GetBytes(finalValue));

            long retAddr = _airAccelAddress.ToInt64() + instrLen;
            long jmpBack = retAddr - (_airAccelAllocated.ToInt64() + sc.Count + 5);
            if (jmpBack < int.MinValue || jmpBack > int.MaxValue) return false;
            
            sc.Add(0xE9);
            sc.AddRange(BitConverter.GetBytes((int)jmpBack));

            if (!WriteBytes(_airAccelAllocated, sc.ToArray())) return false;

            long rel = _airAccelAllocated.ToInt64() - (_airAccelAddress.ToInt64() + 5);
            if (rel < int.MinValue || rel > int.MaxValue) return false;

            var patch = new byte[instrLen];
            patch[0] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)rel), 0, patch, 1, 4);
            for (int i = 5; i < instrLen; i++) patch[i] = 0x90;

            return WriteProtectedBytes(_airAccelAddress, patch);
        }

        private static IntPtr AllocateNear(IntPtr baseAddress, nuint size)
        {
            long start = baseAddress.ToInt64() & ~0xFFFL;
            for (long offset = 0; offset <= 0x7FFFFF00L; offset += 0x1000L)
            {
                foreach (long candidateOffset in new[] { offset, -offset })
                {
                    long candidate = start + candidateOffset;
                    if (candidate < 0x10000) continue;
                    IntPtr alloc = VirtualAllocEx(_processHandle, new IntPtr(candidate), size,
                                                  MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                    if (alloc != IntPtr.Zero) return alloc;
                }
            }
            return IntPtr.Zero;
        }

        private static bool WriteProtectedBytes(IntPtr address, byte[] data)
        {
            if (address == IntPtr.Zero || data == null || data.Length == 0) return false;
            if (!VirtualProtectEx(_processHandle, address, (nuint)data.Length, PAGE_EXECUTE_READWRITE, out var old))
                return false;
            bool ok = WriteBytes(address, data);
            VirtualProtectEx(_processHandle, address, (nuint)data.Length, old, out _);
            return ok;
        }

        private static bool WriteBytes(IntPtr address, byte[] data)
        {
            return WriteProcessMemory(_processHandle, address, data, (nuint)data.Length, out _);
        }

        private static byte[]? ReadBytes(IntPtr address, int length)
        {
            var buf = new byte[length];
            if (ReadProcessMemory(_processHandle, address, buf, (nuint)length, out var read) && read == (nuint)length)
                return buf;
            return null;
        }

        private static IntPtr ScanSignature(string patternStr)
        {
            string[] parts = patternStr.Split(' ');
            var pattern = new byte[parts.Length];
            var mask = new bool[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "??")
                {
                    mask[i] = true;
                }
                else
                {
                    pattern[i] = Convert.ToByte(parts[i], 16);
                    mask[i] = false;
                }
            }

            IntPtr address = IntPtr.Zero;
            while (VirtualQueryEx(_processHandle, address, out var memInfo, (nuint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) != 0)
            {
                long start = memInfo.BaseAddress.ToInt64();
                long size = memInfo.RegionSize.ToInt64();
                
                if (memInfo.State == MEM_COMMIT && (memInfo.Protect == 0x20 || memInfo.Protect == 0x40 || memInfo.Protect == 0x10 || memInfo.Protect == 0x80))
                {
                    if (size > int.MaxValue) continue;
                    byte[] buffer = new byte[(int)size];
                    if (ReadProcessMemory(_processHandle, memInfo.BaseAddress, buffer, (nuint)size, out nuint read))
                    {
                        for (int i = 0; i < (int)read - pattern.Length; i++)
                        {
                            bool found = true;
                            for (int j = 0; j < pattern.Length; j++)
                            {
                                if (!mask[j] && buffer[i + j] != pattern[j])
                                {
                                    found = false;
                                    break;
                                }
                            }
                            if (found)
                            {
                                return memInfo.BaseAddress + i;
                            }
                        }
                    }
                }

                long nextAddress = memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64();
                if (nextAddress <= 0) break;
                address = (IntPtr)nextAddress;
            }

            return IntPtr.Zero;
        }
    }
}