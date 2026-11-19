using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lunar {
static class ApiArt {
    const string Media="https://media.valorant-api.com/";
    static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","cache","art");
    static readonly Dictionary<string,Image> memory=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,DateTime> failed=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
    static readonly SemaphoreSlim gate=new SemaphoreSlim(4);
    static SynchronizationContext ui;
    static int repaintQueued;

    public const string ValorantPoints="85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741";
    public const string Radianite="e59aa87c-4cbf-517a-5983-6e81511be9b7";
    public const string KingdomCredits="85ca954a-41f2-ce94-9b45-8ca3dd39a00d";
    const string RankTable="03621f52-342b-cf4e-4f86-9350a49c6d04";

    static string Id(string value){Guid id;return Guid.TryParse(value,out id)&&id!=Guid.Empty?id.ToString():null;}
    public static Image Agent(string id){string g=Id(id);return g==null?null:Get(Media+"agents/"+g+"/displayicon.png",128);}
    public static Image Rank(int tier){return Get(Media+"competitivetiers/"+RankTable+"/"+(tier<3?0:Math.Min(27,tier))+"/largeicon.png",96);}
    public static Image Map(string id){string g=Id(id);return g==null?null:Get(Media+"maps/"+g+"/listviewicon.png",456);}
    public static Image Currency(string code){
        string id=code=="VP"?ValorantPoints:code=="RAD"?Radianite:code=="KC"?KingdomCredits:null;
        return id==null?null:Get(Media+"currencies/"+id+"/displayicon.png",48);
    }
    public static Image ContentTier(string id){string g=Id(id);return g==null?null:Get(Media+"contenttiers/"+g+"/displayicon.png",32);}
    public static string AgentUrl(string id){string g=Id(id);return g==null?"":Media+"agents/"+g+"/displayicon.png";}

    public static Image Get(string url,int maxSize){
        if(String.IsNullOrEmpty(url))return null;
        CaptureUi();
        string key=url+"@"+maxSize;
        lock(memory){
            Image found;
            if(memory.TryGetValue(key,out found))return found;
            if(pending.Contains(key))return null;
            DateTime retry;
            if(failed.TryGetValue(key,out retry)&&DateTime.UtcNow<retry)return null;
        }
        string file=Path.Combine(Folder,Hash(key)+".png");
        var disk=ReadDisk(file);
        if(disk!=null){lock(memory)memory[key]=disk;return disk;}
        lock(memory){if(!pending.Add(key))return null;}
        Task.Run(async()=>{
            await gate.WaitAsync().ConfigureAwait(false);
            Image image=null;
            try{image=Download(url,maxSize,file);}
            catch(Exception){image=null;}
            finally{gate.Release();}
            lock(memory){
                pending.Remove(key);
                if(image!=null)memory[key]=image;
                else failed[key]=DateTime.UtcNow.AddMinutes(2);
            }
            if(image!=null)Repaint();
        });
        return null;
    }
    static void CaptureUi(){
        if(ui==null&&SynchronizationContext.Current is WindowsFormsSynchronizationContext)ui=SynchronizationContext.Current;
    }
    static void Repaint(){
        var context=ui;
        if(context==null||Interlocked.Exchange(ref repaintQueued,1)==1)return;
        context.Post(delegate{
            Interlocked.Exchange(ref repaintQueued,0);
            foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())
                if(!form.IsDisposed&&form.Visible)form.Invalidate(true);
            var handler=Loaded;if(handler!=null)handler();
        },null);
    }
    public static event Action Loaded;
    static string Hash(string value){
        using(var sha=SHA1.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
    }
    static Image ReadDisk(string file){
        try{
            if(!File.Exists(file))return null;
            using(var stream=new MemoryStream(File.ReadAllBytes(file)))
            using(var image=Image.FromStream(stream))return new Bitmap(image);
        }catch(IOException){return null;}
         catch(UnauthorizedAccessException){return null;}
         catch(ArgumentException){try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}return null;}
         catch(System.Runtime.InteropServices.ExternalException){return null;}
    }
    static Image Download(string url,int maxSize,string file){
        var raw=Http.Bytes(url,8*1024*1024);
        if(raw==null)return null;
        Bitmap result;
        using(var stream=new MemoryStream(raw))
        using(var source=Image.FromStream(stream)){
            float scale=Math.Min(1f,(float)maxSize/Math.Max(source.Width,source.Height));
            int w=Math.Max(1,(int)Math.Round(source.Width*scale)),h=Math.Max(1,(int)Math.Round(source.Height*scale));
            result=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result)){
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(source,new Rectangle(0,0,w,h));
            }
        }
        try{
            Directory.CreateDirectory(Folder);
            string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            result.Save(temp,ImageFormat.Png);
            if(File.Exists(file))File.Delete(file);
            File.Move(temp,file);
        }catch(IOException){}
         catch(UnauthorizedAccessException){}
         catch(System.Runtime.InteropServices.ExternalException){}
        return result;
    }
}

static class Http {
    static readonly string[] Hosts={"valorant-api.com","media.valorant-api.com","valorant.dyn.riotcdn.net"};
    static HttpWebRequest Request(Uri uri,int timeout){
        var request=(HttpWebRequest)WebRequest.Create(uri);
        request.Timeout=request.ReadWriteTimeout=timeout;
        request.AllowAutoRedirect=false;
        request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        request.UserAgent="Lunar";
        return request;
    }
    static Uri Allowed(string url){
        Uri uri;
        return Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&Hosts.Contains(uri.Host,StringComparer.OrdinalIgnoreCase)?uri:null;
    }
    public static byte[] Bytes(string url,int maxBytes,int timeout=8000){
        var uri=Allowed(url);
        if(uri==null)return null;
        using(var response=Request(uri,timeout).GetResponse())
        using(var stream=response.GetResponseStream())
        using(var output=new MemoryStream()){
            var chunk=new byte[16384];
            int read;
            while((read=stream.Read(chunk,0,chunk.Length))>0){
                if(output.Length+read>maxBytes)return null;
                output.Write(chunk,0,read);
            }
            return output.ToArray();
        }
    }
    public static string Text(string url,int maxBytes,int timeout=15000){
        var raw=Bytes(url,maxBytes,timeout);
        return raw==null?null:Encoding.UTF8.GetString(raw);
    }
}

sealed class SkinVideo {public string Name,Url;}
sealed class SkinInfo {
    public string Name,Weapon,TierId,Tier;
    public int TierRank=-1;
    public List<SkinVideo> Videos=new List<SkinVideo>();
}
static class SkinCatalog {
    static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","cache","skins-index-v2.json");
    static Dictionary<string,SkinInfo> byLevel;
    static DateTime nextRefresh=DateTime.MinValue;
    static readonly object sync=new object();
    static readonly Dictionary<string,int> Ranks=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){
        {"Select",0},{"Deluxe",1},{"Premium",2},{"Exclusive",3},{"Ultra",4}};

    public static SkinInfo Find(string levelId){
        if(String.IsNullOrEmpty(levelId))return null;
        lock(sync){
            if(byLevel==null)byLevel=ReadDisk()??new Dictionary<string,SkinInfo>(StringComparer.OrdinalIgnoreCase);
            SkinInfo info;
            if(byLevel.TryGetValue(levelId,out info))return info;
        }
        return null;
    }
    public static void Ensure(IEnumerable<string> levelIds){
        var ids=levelIds.Where(id=>!String.IsNullOrEmpty(id)).ToArray();
        lock(sync){
            if(byLevel==null)byLevel=ReadDisk();
            bool fresh=byLevel!=null&&File.Exists(FilePath)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(FilePath)<TimeSpan.FromDays(3);
            if(fresh&&ids.All(byLevel.ContainsKey))return;
            if(DateTime.UtcNow<nextRefresh)return;
            nextRefresh=DateTime.UtcNow.AddMinutes(10);
        }
        try{
            string tiersJson=Http.Text("https://valorant-api.com/v1/contenttiers",512*1024);
            var tiers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var tier in Json.A(Json.Get(Json.Codec.DeserializeObject(tiersJson),"data")))
                tiers[Json.S(tier,"uuid")]=Json.S(tier,"devName");
            string json=Http.Text("https://valorant-api.com/v1/weapons?language=en-US",64*1024*1024,30000);
            if(json==null)return;
            var big=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
            var index=new Dictionary<string,SkinInfo>(StringComparer.OrdinalIgnoreCase);
            var rows=new List<object>();
            foreach(var weapon in Json.A(Json.Get(big.DeserializeObject(json),"data")))
            foreach(var skin in Json.A(Json.Get(weapon,"skins"))){
                string tierId=Json.S(skin,"contentTierUuid"),tierName;
                tiers.TryGetValue(tierId,out tierName);
                string category=Json.S(weapon,"category").Replace("EEquippableCategory::","");
                if(category=="Heavy")category="Heavy weapon";
                var info=new SkinInfo{Name=Json.S(skin,"displayName"),TierId=tierId,Tier=tierName??"",Weapon=category};
                int rank;if(Ranks.TryGetValue(info.Tier,out rank))info.TierRank=rank;
                foreach(var part in Json.A(Json.Get(skin,"levels")).Concat(Json.A(Json.Get(skin,"chromas")))){
                    string url=Json.S(part,"streamedVideo");
                    if(url=="")continue;
                    if(info.Videos.Any(v=>v.Url==url))continue;
                    info.Videos.Add(new SkinVideo{Name=VideoLabel(Json.S(part,"displayName"),info.Name),Url=url});
                }
                var levels=Json.A(Json.Get(skin,"levels")).Select(level=>Json.S(level,"uuid")).Where(id=>id!="").ToArray();
                foreach(string id in levels)index[id]=info;
                rows.Add(new Dictionary<string,object>{
                    {"n",info.Name},{"t",info.TierId},{"tn",info.Tier},{"w",info.Weapon},{"l",levels},
                    {"v",info.Videos.Select(v=>new Dictionary<string,object>{{"n",v.Name},{"u",v.Url}}).ToArray()}});
            }
            if(index.Count==0)return;
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var codec=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
                File.WriteAllText(FilePath,codec.Serialize(rows),Encoding.UTF8);
            }catch(IOException){}catch(UnauthorizedAccessException){}
            lock(sync)byLevel=index;
        }catch(WebException){}
         catch(IOException){}
         catch(ArgumentException){}
         catch(InvalidOperationException){}
    }
    internal static string VideoLabel(string displayName,string skin){
        var lines=(displayName??"").Replace("\r","").Split('\n');
        string first=lines[0].Trim();
        if(skin!=null&&first.StartsWith(skin,StringComparison.OrdinalIgnoreCase))first=first.Substring(skin.Length).Trim();
        if(lines.Length>1){
            var variant=System.Text.RegularExpressions.Regex.Match(lines[1],@"^\s*\(\s*Variant\s*\d+\s*(.*?)\s*\)\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(variant.Success&&variant.Groups[1].Value!="")return variant.Groups[1].Value;
        }
        return first==""?"Base":first;
    }
    static Dictionary<string,SkinInfo> ReadDisk(){
        try{
            if(!File.Exists(FilePath))return null;
            var codec=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=Int32.MaxValue};
            var index=new Dictionary<string,SkinInfo>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in Json.A(codec.DeserializeObject(File.ReadAllText(FilePath,Encoding.UTF8)))){
                var info=new SkinInfo{Name=Json.S(row,"n"),TierId=Json.S(row,"t"),Tier=Json.S(row,"tn"),Weapon=Json.S(row,"w")};
                int rank;if(Ranks.TryGetValue(info.Tier,out rank))info.TierRank=rank;
                foreach(var video in Json.A(Json.Get(row,"v")))info.Videos.Add(new SkinVideo{Name=Json.S(video,"n"),Url=Json.S(video,"u")});
                foreach(var id in Json.A(Json.Get(row,"l")))index[Convert.ToString(id)]=info;
            }
            return index.Count==0?null:index;
        }catch(IOException){return null;}
         catch(UnauthorizedAccessException){return null;}
         catch(ArgumentException){return null;}
         catch(InvalidOperationException){return null;}
    }
}
}
