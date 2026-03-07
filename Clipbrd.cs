using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ImagingDemo
{
    public static class Clipbrd
    {
        internal const string clipbrd = "user32.dll";

        [DllImport(clipbrd, EntryPoint = "EmptyClipboard")]
        internal static extern int EmptyClipboard();

        [DllImport(clipbrd, EntryPoint = "GetClipboardData")]
        internal static extern int GetClipboardData(int wFormat);

        [DllImport(clipbrd, EntryPoint = "SetClipboardData")]
        internal static extern int SetClipboardData(int wFormat, int hMem);

        [DllImport(clipbrd, EntryPoint = "OpenClipboard")]
        internal static extern int OpenClipboard(int hwnd);

        [DllImport(clipbrd, EntryPoint = "CloseClipboard")]
        internal static extern int CloseClipboard();

    }
}
