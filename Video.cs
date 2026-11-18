using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lunar {
// Video surface for the skin preview player.
// Preferred: WebView2 (Chromium, hardware decode, plays the 1080p60 Riot clips at 60 fps).
// Fallback: the built-in WebBrowser (IE11 engine, ~30 fps) when the WebView2 runtime is missing.
interface IVideoSurface {
    Control View{get;}
    void Load(string html);
    void Toggle();
    event Action CloseRequested;
}

// The WebView2 managed DLLs and native loader are embedded in the EXE (so the single-file updater keeps working)
// and extracted once to %LOCALAPPDATA%\Lunar\webview2\<version>.
static class WebView2Host {
    const string Version="1.0.2903.40";
    static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","webview2",Version);
    static bool? ready;

    public static bool Prepare(){
        if(ready.HasValue)return ready.Value;
        try{
            Directory.CreateDirectory(Folder);
            Extract("webview2/Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.Core.dll");
            Extract("webview2/Microsoft.Web.WebView2.WinForms.dll","Microsoft.Web.WebView2.WinForms.dll");
            Extract(Environment.Is64BitProcess?"webview2/WebView2Loader.x64.dll":"webview2/WebView2Loader.x86.dll","WebView2Loader.dll");
            AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            ready=Probe();
        }catch(Exception){ready=false;}
        return ready.Value;
    }
    static void Extract(string resource,string file){
        string path=Path.Combine(Folder,file);
        using(var stream=typeof(WebView2Host).Assembly.GetManifestResourceStream(resource)){
            if(stream==null)throw new FileNotFoundException(resource);
            if(File.Exists(path)&&new FileInfo(path).Length==stream.Length)return;
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var output=File.Create(temp))stream.CopyTo(output);
            if(File.Exists(path))File.Delete(path);
            File.Move(temp,path);
        }
    }
    static Assembly Resolve(object sender,ResolveEventArgs args){
        string name=new AssemblyName(args.Name).Name;
        if(name!="Microsoft.Web.WebView2.Core"&&name!="Microsoft.Web.WebView2.WinForms")return null;
        string path=Path.Combine(Folder,name+".dll");
        return File.Exists(path)?Assembly.LoadFrom(path):null;
    }
    // Kept in its own non-inlined method so WebView2 types are only touched after the resolver is registered.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Probe(){
        Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(Folder);
        return !String.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
    }
    public static string DataFolder{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","webview2-data");}}
}

static class VideoSurfaces {
    public static IVideoSurface Create(){
        if(WebView2Host.Prepare()){
            try{return CreateModern();}catch(Exception){}
        }
        return new LegacyVideoSurface();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static IVideoSurface CreateModern(){return new ModernVideoSurface();}
}

sealed class ModernVideoSurface:IVideoSurface {
    static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> environment;
    readonly Microsoft.Web.WebView2.WinForms.WebView2 view=new Microsoft.Web.WebView2.WinForms.WebView2{Dock=DockStyle.Fill};
    string pending;
    bool ready;
    public event Action CloseRequested;
    public Control View{get{return view;}}

    public ModernVideoSurface(){
        view.DefaultBackgroundColor=Palette.Panel;
        Initialize();
    }
    async void Initialize(){
        try{
            if(environment==null){
                var options=new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                environment=Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,WebView2Host.DataFolder,options);
            }
            var env=await environment;
            if(view.IsDisposed)return;
            await view.EnsureCoreWebView2Async(env);
            if(view.IsDisposed)return;
            var settings=view.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled=false;settings.AreDevToolsEnabled=false;settings.IsStatusBarEnabled=false;
            settings.IsZoomControlEnabled=false;settings.AreBrowserAcceleratorKeysEnabled=false;
            view.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
            view.CoreWebView2.WebMessageReceived+=(s,e)=>{
                string message;try{message=e.TryGetWebMessageAsString();}catch(Exception){return;}
                if(message=="close"&&CloseRequested!=null)CloseRequested();
            };
            ready=true;
            if(pending!=null){view.NavigateToString(pending);pending=null;}
        }catch(Exception){}
    }
    public void Load(string html){if(ready)view.NavigateToString(html);else pending=html;}
    public void Toggle(){if(ready)view.ExecuteScriptAsync("lunarToggle()");}
}

sealed class LegacyVideoSurface:IVideoSurface {
    readonly WebBrowser browser=new WebBrowser{Dock=DockStyle.Fill,ScrollBarsEnabled=false,ScriptErrorsSuppressed=true,IsWebBrowserContextMenuEnabled=false,AllowWebBrowserDrop=false,WebBrowserShortcutsEnabled=false};
    public event Action CloseRequested{add{}remove{}}
    public Control View{get{return browser;}}
    // Legacy engine: opt this EXE (HKCU, no admin) into IE11 mode + GPU rendering.
    static LegacyVideoSurface(){
        try{
            string exe=Path.GetFileName(Application.ExecutablePath);
            foreach(var feature in new[]{new System.Collections.Generic.KeyValuePair<string,int>("FEATURE_BROWSER_EMULATION",11001),new System.Collections.Generic.KeyValuePair<string,int>("FEATURE_GPU_RENDERING",1)})
                using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\"+feature.Key))
                    if(key!=null)key.SetValue(exe,feature.Value,Microsoft.Win32.RegistryValueKind.DWord);
        }catch(Exception){}
    }
    public void Load(string html){browser.DocumentText=html;}
    public void Toggle(){try{browser.Document.InvokeScript("lunarToggle");}catch(Exception){}}
}
}
