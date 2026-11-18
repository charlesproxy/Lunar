using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Lunar {
// Support log at %LOCALAPPDATA%\Lunar\logs\lunar.log. Never contains tokens, cookies, passwords, player IDs or names:
// callers log steps and status codes, and every line is scrubbed again (GUIDs, JWTs, Basic/Bearer values, ports'
// passwords) before it is written. Rotates at 1 MB (keeps one previous file).
static class LunarLog {
    static readonly object Gate=new object();
    static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","logs");
    static readonly string FilePath=Path.Combine(Folder,"lunar.log");
    const long MaxBytes=1024*1024;
    static readonly Regex Guid=new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",RegexOptions.Compiled);
    static readonly Regex Jwt=new Regex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*",RegexOptions.Compiled);
    static readonly Regex Auth=new Regex(@"(?i)\b(basic|bearer)\s+[A-Za-z0-9+/=_\-\.]+",RegexOptions.Compiled);
    static readonly Regex Secrets=new Regex(@"(?i)(access_token|id_token|entitlements?_token|token|ssid|password|cookie)=([^&\s]+)",RegexOptions.Compiled);
    static readonly Regex RiotId=new Regex(@"[^\s#:]{2,16}#[A-Za-z0-9]{2,5}\b",RegexOptions.Compiled);
    static string last="";static int repeats;

    public static string Scrub(string text){
        if(String.IsNullOrEmpty(text))return text??"";
        text=Jwt.Replace(text,"{jwt}");
        text=Auth.Replace(text,m=>m.Groups[1].Value+" {hidden}");
        text=Secrets.Replace(text,m=>m.Groups[1].Value+"={hidden}");
        text=Guid.Replace(text,"{id}");
        text=RiotId.Replace(text,"{riot-id}");
        return text.Replace("\r"," ").Replace("\n"," | ");
    }
    public static void Write(string area,string message){
        try{
            string line=Scrub(message);
            lock(Gate){
                // Collapse identical consecutive lines (polling loops) into a counter.
                if(area+line==last){repeats++;return;}
                Directory.CreateDirectory(Folder);
                if(File.Exists(FilePath)&&new FileInfo(FilePath).Length>MaxBytes){
                    string old=FilePath+".1";
                    if(File.Exists(old))File.Delete(old);
                    File.Move(FilePath,old);
                }
                var builder=new StringBuilder();
                if(repeats>0)builder.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  (previous line repeated ").Append(repeats).Append("x)\r\n");
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("  [").Append(area).Append("] ").Append(line).Append("\r\n");
                File.AppendAllText(FilePath,builder.ToString(),new UTF8Encoding(false));
                last=area+line;repeats=0;
            }
        }catch(Exception){}
    }
    public static void Error(string area,string message,Exception error){
        Write(area,message+(error==null?"":" → "+error.GetType().Name+": "+error.Message));
    }
    public static void Start(){
        Write("app","Lunar "+LunarUpdates.DisplayVersion+" started · "+Environment.OSVersion.VersionString+(Environment.Is64BitOperatingSystem?" 64-bit":"")+
            " · .NET "+Environment.Version+" · culture "+System.Globalization.CultureInfo.CurrentCulture.Name+" · language "+LunarLanguage.Code+
            " · installed copy: "+String.Equals(Application.ExecutablePath,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Lunar","Lunar.exe"),StringComparison.OrdinalIgnoreCase));
        Application.ThreadException+=(s,e)=>Error("crash","UI thread exception",e.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(s,e)=>Error("crash","Unhandled exception",e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException+=(s,e)=>{Error("crash","Unobserved task exception",e.Exception==null?null:e.Exception.GetBaseException());e.SetObserved();};
    }
    // Writes a scrubbed copy of the log (+ the previous file) to the Desktop and selects it in Explorer.
    public static string Export(){
        lock(Gate){
            var text=new StringBuilder();
            text.Append("Lunar support log · exported ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(" · version ").Append(LunarUpdates.DisplayVersion).Append("\r\n");
            text.Append("This file contains no passwords, tokens, cookies, player IDs or names.\r\n\r\n");
            foreach(var file in new[]{FilePath+".1",FilePath})
                if(File.Exists(file))foreach(var line in File.ReadAllLines(file))text.Append(Scrub(line)).Append("\r\n");
            string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string target=Path.Combine(desktop,"Lunar-log-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".txt");
            File.WriteAllText(target,text.ToString(),new UTF8Encoding(false));
            try{Process.Start("explorer.exe","/select,\""+target+"\"");}catch(Exception){}
            return target;
        }
    }
}
}
