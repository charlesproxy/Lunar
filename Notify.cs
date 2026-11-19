using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lunar {
static class TaskbarAlert {
    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo {public uint Size;public IntPtr Window;public uint Flags,Count,Timeout;}
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FlashInfo info);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    const uint FlashStop=0,FlashAll=3,FlashUntilForeground=12;
    const int ShowMinimizedNoActivate=7;

    [ComImport,Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3 {
        void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)]bool fullscreen);
        void SetProgressValue(IntPtr hwnd,ulong completed,ulong total);void SetProgressState(IntPtr hwnd,int state);
        void RegisterTab(IntPtr tab,IntPtr main);void UnregisterTab(IntPtr tab);void SetTabOrder(IntPtr tab,IntPtr before);void SetTabActive(IntPtr tab,IntPtr main,uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd,uint count,IntPtr buttons);void ThumbBarUpdateButtons(IntPtr hwnd,uint count,IntPtr buttons);void ThumbBarSetImageList(IntPtr hwnd,IntPtr images);
        void SetOverlayIcon(IntPtr hwnd,IntPtr icon,[MarshalAs(UnmanagedType.LPWStr)]string description);
    }
    [ComImport,Guid("56FDF344-FD6D-11d0-958A-006097C9A090"),ClassInterface(ClassInterfaceType.None)]
    class TaskbarList {}
    const int ProgressNone=0,ProgressPaused=8;

    static ITaskbarList3 taskbar;
    static Icon badge;
    static bool active;
    static ITaskbarList3 Taskbar(){
        if(taskbar==null){try{var list=(ITaskbarList3)new TaskbarList();list.HrInit();taskbar=list;}catch(Exception){}}
        return taskbar;
    }

    public static bool Active{get{return active;}}
    public static void Raise(Form form,string description,bool sound){
        if(form==null||form.IsDisposed)return;
        try{
            if(!form.Visible){
                form.WindowState=FormWindowState.Minimized;
                ShowWindow(form.Handle,ShowMinimizedNoActivate);
                MarkVisible(form);
            }
            var info=new FlashInfo{Window=form.Handle,Flags=FlashAll|FlashUntilForeground,Count=0,Timeout=0};
            info.Size=(uint)Marshal.SizeOf(info);
            FlashWindowEx(ref info);
            var list=Taskbar();
            if(list!=null){
                list.SetProgressValue(form.Handle,100,100);
                list.SetProgressState(form.Handle,ProgressPaused);
                list.SetOverlayIcon(form.Handle,Badge().Handle,LunarLanguage.Translate(description));
            }
            active=true;
        }catch(Exception){}
        if(sound)Chime.Play();
    }
    static void MarkVisible(Form form){
        try{
            var setState=typeof(Control).GetMethod("SetState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(int),typeof(bool)},null);
            if(setState!=null)setState.Invoke(form,new object[]{0x2,true});
        }catch(Exception){}
    }
    internal static void ShowRestored(Form form){ShowWindow(form.Handle,9);form.Activate();}
    public static void Clear(Form form){
        if(!active||form==null||form.IsDisposed||!form.IsHandleCreated)return;
        active=false;
        try{
            var info=new FlashInfo{Window=form.Handle,Flags=FlashStop};info.Size=(uint)Marshal.SizeOf(info);FlashWindowEx(ref info);
            var list=Taskbar();
            if(list!=null){list.SetProgressState(form.Handle,ProgressNone);list.SetOverlayIcon(form.Handle,IntPtr.Zero,null);}
        }catch(Exception){}
    }
    static Icon Badge(){
        if(badge!=null)return badge;
        using(var bitmap=new Bitmap(16,16)){
            using(var g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var fill=new LinearGradientBrush(new Rectangle(0,0,16,16),Color.FromArgb(255,206,110),Color.FromArgb(222,146,48),LinearGradientMode.ForwardDiagonal))g.FillEllipse(fill,0.5f,0.5f,15,15);
                using(var ink=new SolidBrush(Color.FromArgb(48,30,8))){g.FillRectangle(ink,7,3.5f,2.2f,6.5f);g.FillEllipse(ink,6.9f,11.2f,2.4f,2.4f);}
            }
            IntPtr handle=bitmap.GetHicon();
            badge=(Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
        }
        return badge;
    }
}

static class Chime {
    static byte[] wave;
    public static void Play(){
        try{
            if(wave==null)wave=Build();
            var player=new SoundPlayer(new MemoryStream(wave));
            player.Play();
        }catch(Exception){}
    }
    static byte[] Build(){
        const int rate=44100;
        double[][] notes={new[]{987.77,0.0,0.16},new[]{1318.51,0.11,0.34}};
        int samples=(int)(rate*0.48);
        var pcm=new short[samples];
        foreach(var note in notes){
            int start=(int)(note[1]*rate),length=(int)(note[2]*rate);
            for(int i=0;i<length&&start+i<samples;i++){
                double t=(double)i/rate;
                double envelope=Math.Min(1,t/0.006)*Math.Exp(-t*9);
                double value=(Math.Sin(2*Math.PI*note[0]*t)+0.25*Math.Sin(4*Math.PI*note[0]*t))*envelope*0.22;
                int mixed=pcm[start+i]+(int)(value*short.MaxValue);
                pcm[start+i]=(short)Math.Max(short.MinValue,Math.Min(short.MaxValue,mixed));
            }
        }
        using(var stream=new MemoryStream())
        using(var writer=new BinaryWriter(stream)){
            int data=samples*2;
            writer.Write(new[]{'R','I','F','F'});writer.Write(36+data);writer.Write(new[]{'W','A','V','E'});
            writer.Write(new[]{'f','m','t',' '});writer.Write(16);writer.Write((short)1);writer.Write((short)1);
            writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
            writer.Write(new[]{'d','a','t','a'});writer.Write(data);
            foreach(var sample in pcm)writer.Write(sample);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
}
