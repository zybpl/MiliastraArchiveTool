using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MiliastraArchiveTool.Automation;

public sealed record FilePickerProcess(int Id, int ParentId, string Name);

public static class FilePickerProcessTree
{
    public static bool BelongsToGame(int candidate, int game, IReadOnlyDictionary<int, FilePickerProcess> processes)
    {
        if (candidate <= 0 || game <= 0 || !processes.ContainsKey(game)) return false;
        var visited = new HashSet<int>();
        for (var depth = 0; depth < 32 && candidate > 0 && visited.Add(candidate); depth++)
        {
            if (!processes.TryGetValue(candidate, out var process)) return false;
            if (candidate == game) return true;
            candidate = process.ParentId;
        }
        return false;
    }

    public static IReadOnlyDictionary<int, FilePickerProcess> Read()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == (nint)(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect file picker process ancestry.");
        try
        {
            var result = new Dictionary<int, FilePickerProcess>();
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Exe = "" };
            if (!Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do { result[(int)entry.Id] = new((int)entry.Id, (int)entry.Parent, entry.Exe); }
            while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Id;
        public nuint Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(nint snapshot, ref Entry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(nint snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
