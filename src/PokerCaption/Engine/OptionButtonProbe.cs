using System.Globalization;
using PokerCaption.Native;

namespace PokerCaption.Engine;

internal static class OptionButtonProbe
{
    internal static int Run(string handle, string appDir)
    {
        using var log = new StreamWriter(Path.Combine(appDir, "option_button_trace.txt")) { AutoFlush = true };
        int Fail(string error)
        {
            log.WriteLine(error);
            return 1;
        }

        var hex = handle.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? handle.AsSpan(2) : handle.AsSpan();
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) || value == 0)
            return Fail("Expected a hexadecimal table handle.");
        var table = new IntPtr(unchecked((int)value));
        uint thread = NativeMethods.GetWindowThreadProcessId(table, out uint process);
        if (thread == 0 || !ClientLocator.FindClients().Any(client => client.ProcessId == process) ||
            !TableScanner.ScanTables().Any(candidate => candidate.Hwnd == table))
            return Fail("The window is not a table from a recognized client.");

        using var instance = new Mutex(true, "Local\\PokerCaption_SingleInstance", out bool created);
        if (!created) return Fail("PokerCaption is already running.");

        using var shared = new SharedMemory();
        string arch = Environment.Is64BitProcess ? "x64" : "x86";
        using var hook = new HookInstaller(Path.Combine(appDir, "native", arch, "captionHook.dll"));
        if (!hook.Install((int)thread, messagesOnly: true)) return Fail("Could not start the table scan.");
        const int slot = 0;
        shared.SetTableHwnd(slot, table);
        shared.EnableOptionButtonTrace(slot, true);
        log.WriteLine($"Table 0x{value:X}");
        (int Scan, SharedMemory.OptionButtonTrace Buttons, System.Drawing.Point? BackPosition, byte SitOutIntent)? previous = null;
        string previousNodes = null;
        long end = Environment.TickCount64 + 120_000;
        while (Environment.TickCount64 < end && NativeMethods.GetWindowThreadProcessId(table, out uint current) == thread && current == process)
        {
            long next = Environment.TickCount64 + 1000;
            int request = shared.RequestTableScan(thread, slot);
            if (request == 0) return Fail("Could not post a scan request.");
            while (shared.ReadTableScanId(slot) != request && Environment.TickCount64 < next) Thread.Sleep(50);
            if (shared.ReadTableScanId(slot) != request) return Fail("No scan response within one second. Probe stopped.");

            var result = shared.ReadOptionButtonTrace(slot);
            var trace = (result.Scan, result.Buttons, BackPosition: shared.ReadBackPosition(slot), SitOutIntent: shared.ReadSitOutIntent(slot));
            if (trace != previous)
            {
                log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {(SharedMemory.CocosScanStatus)trace.Scan} {trace.Buttons} back={trace.BackPosition} sit-out={trace.SitOutIntent:X2}");
                previous = trace;
            }
            string nodes = string.Join(" | ", shared.ReadTableTexts(slot)
                .Select(item => $"{item.rect.X + 1}/{item.rect.Y} {item.node} {item.text}"));
            if (nodes != previousNodes)
            {
                log.WriteLine("checkbox nodes: " + nodes);
                previousNodes = nodes;
            }
            int remaining = (int)(next - Environment.TickCount64);
            if (remaining > 0) Thread.Sleep(remaining);
        }
        log.WriteLine("Probe stopped.");
        return 0;
    }
}
