using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

public static class Module3
    {

    public static int Session;
    public static int ScannedImages;

    public static void myCallback(int DIBHandle)
    {
        // the callback has to be defined in a module, not in a form
        // else AddressOf doesn't work
        // Increases the image number
        MessageBox.Show("0","INFO");
        ScannedImages = ScannedImages + 1;
        MessageBox.Show("1","INFO");
        // Save the image as scan1.tif scan2.tif scanxxxx.tif in current directory
        // IO_SaveTIFImage IOSession, DIBHandle, ".\scan" & ScannedImages & ".tif", 0
        // Save the image as scan1.tif scan2.tif scanxxxx.tif in current directory
        ImagingDemo.RecoIO.IO_SavePDFImage(Session, DIBHandle, ".\\scan" + ScannedImages + ".pdf", 80);
        MessageBox.Show("2","INFO");
    }
        

    
}
 /*
using System;
using System.Runtime.InteropServices;

public delegate bool CallBack(int hwnd, int lParam);

public class EnumReportApp {

    [DllImport("user32")]
    public static extern int EnumWindows(CallBack x, int y); 

    public static void Main() 
    {
        CallBack myCallBack = new CallBack(EnumReportApp.Report);
        EnumWindows(myCallBack, 0);
    }

   public static bool Report(int hwnd, int lParam) { 
        Console.Write("Window handle is ");
        Console.WriteLine(hwnd);
        return true;
    }
}


*/
