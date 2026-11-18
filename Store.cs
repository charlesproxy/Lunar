using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace Lunar {
sealed class StoreOffer {
    public string Id,Name;
    public long Price;
    public byte[] Art;
}
sealed class StoreSnapshot {
    public StoreOffer[] Offers,Bundles;
    public StoreBalance[] Balances;
    public DateTime? ResetsAt;
    public bool NightMarket;
    public Guid Account;
    public DateTime SavedAt;
    public bool IsSaved;
    public bool SavedRotationExpired(DateTime now){
        return IsSaved&&ResetsAt.HasValue&&ResetsAt.Value<=now;
    }
}
sealed class StoreBalance {
    public readonly string Code;
    public readonly long Amount;
    public StoreBalance(string code,long amount){Code=code;Amount=amount;}
}
static class StoreRarity {
    // Edition comes from the Valorant-API skin catalog (cached on disk) instead of an embedded table.
    public static long Rank(string offerId){var info=SkinCatalog.Find(offerId);return info==null?-1:info.TierRank;}
    public static string Name(string offerId){var info=SkinCatalog.Find(offerId);return info==null||info.Tier==""?"":info.Tier+" Edition";}
    public static StoreOffer[] WithRarestFirst(IEnumerable<StoreOffer> offers){
        var list=offers.ToList();
        if(list.Count<2)return list.ToArray();
        var featured=list.OrderByDescending(offer=>Rank(offer.Id)).First();
        list.Remove(featured);
        list.Insert(0,featured);
        return list.ToArray();
    }
}
static class RiotStore {
    const string ValorantPoints="85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741";
    const string Radianite="e59aa87c-4cbf-517a-5983-6e81511be9b7";
    const string KingdomCredits="85ca954a-41f2-ce94-9b45-8ca3dd39a00d";
    static readonly Dictionary<string,StoreOffer> artCache=new Dictionary<string,StoreOffer>();

    static HttpWebRequest PublicRequest(Uri uri){
        var request=(HttpWebRequest)WebRequest.Create(uri);
        request.Timeout=request.ReadWriteTimeout=5000;
        request.AllowAutoRedirect=false;
        request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        return request;
    }
    static object Catalog(string path){
        var request=PublicRequest(new Uri("https://valorant-api.com/v1/"+path));
        using(var response=request.GetResponse())
        using(var reader=new StreamReader(response.GetResponseStream()))
            return Json.Get(Json.Codec.DeserializeObject(reader.ReadToEnd()),"data");
    }
    public static string CurrentClientVersion(){
        try{return Json.S(Catalog("version"),"riotClientVersion");}
        catch(WebException){return "";}
        catch(IOException){return "";}
        catch(ArgumentException){return "";}
    }
    static byte[] LoadArt(string url,int maxBytes=3*1024*1024){
        Uri uri;
        if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="media.valorant-api.com"||!uri.IsDefaultPort)
            return null;
        using(var response=PublicRequest(uri).GetResponse())
        using(var stream=response.GetResponseStream())
        using(var output=new MemoryStream()){
            if(!response.ContentType.StartsWith("image/",StringComparison.OrdinalIgnoreCase))return null;
            byte[] chunk=new byte[8192];
            int read;
            while((read=stream.Read(chunk,0,chunk.Length))>0){
                if(output.Length+read>maxBytes)return null;
                output.Write(chunk,0,read);
            }
            return output.ToArray();
        }
    }
    static byte[] TryArt(string url,bool banner){
        try{
            var raw=LoadArt(url,banner?12*1024*1024:3*1024*1024);
            if(raw==null||!banner)return raw;
            using(var stream=new MemoryStream(raw))
            using(var art=Image.FromStream(stream)){
                if(art.Width>8192||art.Height>8192)return null;
                return ComposeBanner(art,false);
            }
        }catch(WebException){return null;}
         catch(IOException){return null;}
         catch(ArgumentException){return null;}
         catch(System.Runtime.InteropServices.ExternalException){return null;}
    }
    static byte[] ComposeBanner(Image art,bool item){
        double scale=Math.Min(1,Math.Min(1060.0/art.Width,680.0/art.Height));
        int width=item?800:Math.Max(1,(int)(art.Width*scale));
        int height=item?450:Math.Max(1,(int)(art.Height*scale));
        using(var bitmap=new Bitmap(width,height))
        using(var output=new MemoryStream()){
            using(var g=Graphics.FromImage(bitmap)){
                g.Clear(Palette.Bg);
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                float ratio=item?Math.Min(610f/art.Width,302f/art.Height):(float)scale;
                var bounds=new RectangleF((width-art.Width*ratio)/2,(height-art.Height*ratio)/2,art.Width*ratio,art.Height*ratio);
                g.DrawImage(art,bounds);
            }
            bitmap.Save(output,System.Drawing.Imaging.ImageFormat.Jpeg);
            return output.ToArray();
        }
    }
    static byte[] BundleItemArt(object bundleData){
        var ids=Json.A(Json.Get(bundleData,"Items")).Select(row=>Json.S(Json.Get(row,"Item")??row,"ItemID"))
            .Where(id=>{Guid parsed;return Guid.TryParse(id,out parsed);})
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(StoreRarity.Rank).Take(3);
        foreach(string id in ids){
            var item=new StoreOffer{Id=id};
            Decorate(item,"weapons/skinlevels/","",false);
            if(item.Art==null)continue;
            try{
                using(var stream=new MemoryStream(item.Art))
                using(var art=Image.FromStream(stream))return ComposeBanner(art,true);
            }catch(ArgumentException){}
             catch(System.Runtime.InteropServices.ExternalException){}
        }
        return null;
    }
    static void Decorate(StoreOffer offer,string path,string fallback,bool bundle,object bundleData=null){
        string key=path+offer.Id;
        StoreOffer cached;
        lock(artCache)artCache.TryGetValue(key,out cached);
        if(cached!=null){offer.Name=cached.Name;offer.Art=cached.Art;return;}
        offer.Name=fallback;
        try{
            var item=Catalog(key+"?language=en-US");
            string name=Json.S(item,"displayName");
            if(name!="")offer.Name=name;
            string[] fields=bundle?new[]{"displayIcon2","displayIcon3","displayIcon","verticalPromoImage","logoIcon"}:new[]{"displayIcon"};
            foreach(string art in fields.Select(field=>Json.S(item,field)).Where(url=>url!="").Distinct()){
                offer.Art=TryArt(art,bundle);
                if(offer.Art!=null)break;
            }
        }catch(WebException){}
         catch(IOException){}
         catch(ArgumentException){}
        if(offer.Art!=null){
            lock(artCache){
                if(artCache.Count>=64)artCache.Clear();
                artCache[key]=offer;
            }
        }else if(bundle)offer.Art=BundleItemArt(bundleData);
    }
    internal static List<StoreOffer> ReadOffers(object panel){
        var offers=new List<StoreOffer>();
        var ids=new HashSet<Guid>();
        foreach(var item in Json.A(Json.Get(panel,"SingleItemStoreOffers"))){
            Guid id;
            if(!Guid.TryParse(Json.S(item,"OfferID"),out id)||!ids.Add(id))continue;
            offers.Add(new StoreOffer{Id=id.ToString(),Price=HistoryLogic.N(Json.Get(item,"Cost"),ValorantPoints,-1)});
        }
        foreach(var item in Json.A(Json.Get(panel,"SingleItemOffers"))){
            Guid id;
            if(Guid.TryParse(Convert.ToString(item),out id)&&ids.Add(id))offers.Add(new StoreOffer{Id=id.ToString(),Price=-1});
        }
        return offers;
    }
    public static StoreSnapshot Load(){return Load(new RiotReader());}
    internal static StoreSnapshot Load(RiotReader reader){
        var snapshot=reader.ReadStore();
        StoreCache.Save(snapshot);
        return snapshot;
    }
    internal static StoreSnapshot RepairSavedArtwork(StoreSnapshot saved){
        if(saved==null||saved.Bundles.All(bundle=>bundle.Art!=null))return saved;
        var bundles=(StoreOffer[])saved.Bundles.Clone();
        bool changed=false;
        for(int i=0;i<bundles.Length;i++){
            var original=bundles[i];
            Guid bundleId;
            if(original.Art!=null||!Guid.TryParse(original.Id,out bundleId))continue;
            var replacement=new StoreOffer{Id=bundleId.ToString(),Price=original.Price};
            Decorate(replacement,"bundles/",original.Name,true);
            if(replacement.Art!=null){bundles[i]=replacement;changed=true;}
        }
        if(!changed)return saved;
        var updated=new StoreSnapshot{
            Account=saved.Account,SavedAt=saved.SavedAt,IsSaved=true,ResetsAt=saved.ResetsAt,
            Offers=saved.Offers,Bundles=bundles,Balances=saved.Balances
        };
        StoreCache.Save(updated);
        return updated;
    }
    internal static StoreSnapshot Build(object shop,object wallet){
        var panel=Json.Get(shop,"SkinsPanelLayout");
        var offers=ReadOffers(panel);
        if(offers.Count==0)throw new InvalidOperationException("Riot did not return daily offers for this session.");
        long seconds=HistoryLogic.N(panel,"SingleItemOffersRemainingDurationInSeconds",-1);
        DateTime? reset=seconds>=0&&seconds<=604800?(DateTime?)DateTime.UtcNow.AddSeconds(seconds):null;
        var feature=Json.Get(shop,"FeaturedBundle");
        var rows=Json.A(Json.Get(feature,"Bundles")).Concat(new[]{Json.Get(feature,"Bundle")});
        var bundles=new List<KeyValuePair<StoreOffer,object>>();
        var ids=new HashSet<Guid>();
        foreach(var row in rows){
            Guid id;
            if(!Guid.TryParse(Json.S(row,"DataAssetID"),out id)||!ids.Add(id))continue;
            bundles.Add(new KeyValuePair<StoreOffer,object>(new StoreOffer{Id=id.ToString(),Price=-1},row));
        }
        var balances=Json.Get(wallet,"Balances");
        var amounts=new List<StoreBalance>();
        foreach(var currency in new[]{new[]{ValorantPoints,"VP"},new[]{Radianite,"RAD"},new[]{KingdomCredits,"KC"}}){
            long value=HistoryLogic.N(balances,currency[0],-1);
            if(value>=0)amounts.Add(new StoreBalance(currency[1],value));
        }
        SkinCatalog.Ensure(offers.Select(offer=>offer.Id));
        var loadOffers=offers.Select((offer,index)=>new Action(()=>Decorate(offer,"weapons/skinlevels/","Daily offer "+(index+1),false)));
        var loadBundles=bundles.Select(offer=>new Action(()=>Decorate(offer.Key,"bundles/","Featured bundle",true,offer.Value)));
        loadOffers.Concat(loadBundles).AsParallel().WithDegreeOfParallelism(4).ForAll(load=>load());
        return new StoreSnapshot{
            Offers=StoreRarity.WithRarestFirst(offers),Bundles=bundles.Select(bundle=>bundle.Key).ToArray(),Balances=amounts.ToArray(),ResetsAt=reset,
            NightMarket=Json.Get(shop,"BonusStore")!=null
        };
    }
}
partial class RiotReader {
    internal Guid StoreAccount{
        get{Guid account;return Guid.TryParse(self,out account)?account:Guid.Empty;}
    }
    internal Guid IdentifyStoreAccount(out bool clientOpen){
        clientOpen=HasProcess("RiotClientServices")||HasProcess("Riot Client");
        if(!clientOpen)return Guid.Empty;
        try{
            if(!ConnectLocalClient())return Guid.Empty;
            try{
                if(ReadSessionAuthorization())return StoreAccount;
            }catch(ApiFailure){}
             catch(WebException){}
            var chat=Request(origin+"/chat/v1/session","GET",null,true,false);
            Guid account;
            return Json.B(chat,"loaded")&&Guid.TryParse(Json.S(chat,"puuid"),out account)?account:Guid.Empty;
        }catch(ApiFailure){return Guid.Empty;}
         catch(WebException){return Guid.Empty;}
         catch(IOException){return Guid.Empty;}
         catch(UnauthorizedAccessException){return Guid.Empty;}
        finally{basic=access=entitlement=null;}
    }
    internal StoreSnapshot ReadStore(){
        bool sessionReady=false;
        try {
            var pending=InitializeSession(false,true);
            if(pending!=null)throw new InvalidOperationException("No store session available. Sign in to Riot Client. If the store still does not load, open VALORANT and visit Daily Store once.");
            sessionReady=true;
            string host="https://pd."+shard+".a.pvp.net";
            var shop=Request(host+"/store/v3/storefront/"+self,"POST",new Dictionary<string,object>(),false,false);
            object wallet=null;
            try { wallet=Request(host+"/store/v1/wallet/"+self,"GET",null,false,false); }
            catch(ApiFailure){}
            catch(WebException){}
            catch(IOException){}
            catch(ArgumentException){}
            var data=RiotStore.Build(shop,wallet);
            data.Account=StoreAccount;
            data.SavedAt=DateTime.UtcNow;
            return data;
        } catch(FileNotFoundException){
            throw new InvalidOperationException("Open Riot Client and sign in to load your daily offers.");
        } catch(DirectoryNotFoundException){
            throw new InvalidOperationException("Open Riot Client and sign in to load your daily offers.");
        } catch(ApiFailure error){
            if(!sessionReady&&(error.Code==404||error.Code==401||error.Code==403))
                throw new InvalidOperationException("Riot Client has not made your store session available. Open VALORANT and visit Daily Store once to save the rotation.");
            LunarLog.Write("store","storefront failed: HTTP "+error.Code);
            if(error.Code==429)throw new InvalidOperationException("Riot rate limit reached. Try again in a minute.");
            if(error.Code==400||error.Code==401||error.Code==403||error.Code==404)throw new InvalidOperationException("Sign in to VALORANT to load your daily offers.");
            throw new InvalidOperationException("Riot returned HTTP "+error.Code+" while loading the store.");
        } catch(WebException){
            throw new InvalidOperationException(sessionReady?"Could not connect to the Riot store.":"Could not connect to Riot Client. Open it and sign in.");
        } finally {
            basic=access=entitlement=null;
        }
    }
}
static class StoreCache {
    const int FormatVersion=1,MaxBytes=32*1024*1024,MaxImageBytes=3*1024*1024;
    static readonly string FilePath=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","daily-store.dat");

    public static StoreSnapshot Read(){return Read(FilePath);}
    internal static StoreSnapshot Read(string path){
        try{
            var info=new FileInfo(path);
            if(!info.Exists||info.Length>MaxBytes)return null;
            var bytes=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
            using(var stream=new MemoryStream(bytes,false))
            using(var reader=new BinaryReader(stream,Encoding.UTF8)){
                if(reader.ReadInt32()!=FormatVersion)return null;
                var account=new Guid(reader.ReadBytes(16));
                var saved=new DateTime(reader.ReadInt64(),DateTimeKind.Utc);
                long reset=reader.ReadInt64();
                var offers=ReadOffers(reader);
                var bundles=ReadOffers(reader);
                if(account==Guid.Empty||offers.Length==0||stream.Position!=stream.Length)return null;
                return new StoreSnapshot{
                    IsSaved=true,Account=account,SavedAt=saved,Offers=StoreRarity.WithRarestFirst(offers),Bundles=bundles,
                    ResetsAt=reset==0?(DateTime?)null:new DateTime(reset,DateTimeKind.Utc),
                    Balances=new StoreBalance[0]
                };
            }
        }catch(IOException){return null;}
         catch(UnauthorizedAccessException){return null;}
         catch(CryptographicException){return null;}
         catch(ArgumentException){return null;}
    }
    public static void Save(StoreSnapshot store){Save(FilePath,store);}
    internal static void Save(string path,StoreSnapshot store){
        if(store.Account==Guid.Empty||store.Offers.Length==0)return;
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            byte[] bytes;
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream,Encoding.UTF8)){
                writer.Write(FormatVersion);
                writer.Write(store.Account.ToByteArray());
                writer.Write(store.SavedAt.Ticks);
                writer.Write(store.ResetsAt.HasValue?store.ResetsAt.Value.Ticks:0);
                WriteOffers(writer,store.Offers);
                WriteOffers(writer,store.Bundles);
                if(stream.Length>MaxBytes-1024)return;
                bytes=ProtectedData.Protect(stream.ToArray(),null,DataProtectionScope.CurrentUser);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(temp,bytes);
            if(File.Exists(path))File.Replace(temp,path,null);
            else File.Move(temp,path);
        }catch(IOException){}
         catch(UnauthorizedAccessException){}
         catch(CryptographicException){}
        finally{
            try{File.Delete(temp);}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }
    static StoreOffer[] ReadOffers(BinaryReader reader){
        int count=reader.ReadInt32();
        if(count<0||count>32)throw new IOException("Invalid offer count.");
        var offers=new StoreOffer[count];
        for(int i=0;i<count;i++){
            string id=reader.ReadString(),name=reader.ReadString();
            long price=reader.ReadInt64();
            int len=reader.ReadInt32();
            if(len<0||len>MaxImageBytes)throw new IOException("Invalid artwork size.");
            byte[] art=len==0?null:reader.ReadBytes(len);
            if(art!=null&&art.Length!=len)throw new EndOfStreamException();
            offers[i]=new StoreOffer{Id=id,Name=name,Price=price,Art=art};
        }
        return offers;
    }
    static void WriteOffers(BinaryWriter writer,StoreOffer[] offers){
        int count=Math.Min(32,offers.Length);
        writer.Write(count);
        for(int i=0;i<count;i++){
            var offer=offers[i];
            writer.Write(offer.Id??"");
            writer.Write(offer.Name??"");
            writer.Write(offer.Price);
            var image=offer.Art;
            int len=image==null||image.Length>MaxImageBytes?0:image.Length;
            writer.Write(len);
            if(len>0)writer.Write(image);
        }
    }
}
sealed class StoreView:UserControl {
    readonly Panel viewport=new Panel{Dock=DockStyle.Fill};
    readonly StoreScroll scroll=new StoreScroll{Dock=DockStyle.Right,Width=14};
    readonly StoreCanvas canvas=new StoreCanvas();
    readonly BackgroundWorker worker=new BackgroundWorker();
    readonly Timer clock=new Timer{Interval=1000};
    DateTime nextRequest=DateTime.MinValue;
    StoreSnapshot saved,lastLive;
    Guid lastAccount;
    DateTime nextLiveRequest=DateTime.MinValue;
    string liveError="";
    bool readSaved,triedSavedArtwork;

    sealed class StoreResult {
        public StoreSnapshot Snapshot;
        public string Error;
    }

    public StoreView(bool preview=false){
        BackColor=StoreCanvas.Background;
        viewport.BackColor=BackColor;
        viewport.Controls.Add(canvas);
        Controls.Add(viewport);
        Controls.Add(scroll);
        scroll.ValueChanged+=delegate{canvas.Top=-scroll.Value;};
        foreach(Control target in new Control[]{viewport,canvas}){
            target.MouseWheel+=delegate(object sender,MouseEventArgs e){scroll.Value-=e.Delta*65/120;};
            target.MouseEnter+=delegate{scroll.Focus();};
        }
        viewport.Resize+=delegate{LayoutCanvas();};
        canvas.RetryRequested+=delegate{nextRequest=DateTime.MinValue;nextLiveRequest=DateTime.MinValue;LoadIfNeeded();};
        VisibleChanged+=delegate{clock.Enabled=!preview&&Visible;};
        clock.Tick+=delegate{canvas.RefreshCountdown();LoadIfNeeded();};
        worker.WorkerReportsProgress=true;
        worker.DoWork+=delegate(object sender,DoWorkEventArgs e){
            if(!readSaved){
                saved=StoreCache.Read();
                readSaved=true;
            }
            var reader=new RiotReader();
            bool clientOpen;
            Guid account=reader.IdentifyStoreAccount(out clientOpen);
            var fallback=ForAccount(saved,account,clientOpen);
            if(!clientOpen){
                lastLive=null;lastAccount=Guid.Empty;nextLiveRequest=DateTime.MinValue;
                e.Result=OfflineResult(fallback,"Open Riot Client and sign in to load your daily offers.");
                return;
            }
            if(account!=lastAccount){
                lastAccount=account;lastLive=null;nextLiveRequest=DateTime.MinValue;
            }
            if(DateTime.UtcNow<nextLiveRequest){
                e.Result=lastLive!=null&&account!=Guid.Empty
                    ?new StoreResult{Snapshot=lastLive}:OfflineResult(fallback,liveError);
                return;
            }
            worker.ReportProgress(0,ForAccount(lastLive,account,true)??fallback);
            try{
                var live=reader.ReadStore();
                bool stillOpen;
                var verified=new RiotReader().IdentifyStoreAccount(out stillOpen);
                if(!stillOpen||verified==Guid.Empty||verified!=live.Account)
                    throw new InvalidOperationException("Riot account changed or could not be verified. Trying again shortly.");
                StoreCache.Save(live);
                lastAccount=verified;
                lastLive=live;
                saved=new StoreSnapshot{
                    Account=live.Account,SavedAt=live.SavedAt,ResetsAt=live.ResetsAt,IsSaved=true,
                    Offers=live.Offers,Bundles=live.Bundles,Balances=new StoreBalance[0]
                };
                triedSavedArtwork=false;
                DateTime now=DateTime.UtcNow;
                nextLiveRequest=now.AddMinutes(5);
                if(live.ResetsAt.HasValue&&live.ResetsAt.Value<nextLiveRequest){
                    DateTime reset=live.ResetsAt.Value.AddSeconds(10);
                    nextLiveRequest=reset>now.AddSeconds(20)?reset:now.AddSeconds(20);
                }
                e.Result=new StoreResult{Snapshot=live};
            }catch(InvalidOperationException error){
                lastLive=null;
                liveError=error.Message;
                nextLiveRequest=DateTime.UtcNow.AddSeconds(liveError.Contains("rate limit")?75:12);
                if(fallback!=null){
                    bool stillOpen;
                    Guid verified=new RiotReader().IdentifyStoreAccount(out stillOpen);
                    fallback=ForAccount(fallback,verified,stillOpen);
                }
                e.Result=OfflineResult(fallback,liveError);
            }
        };
        worker.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e){
            if(IsDisposed||Disposing||Object.ReferenceEquals(canvas.Data,e.UserState))return;
            canvas.UpdateStore((StoreSnapshot)e.UserState,"Checking Riot account…");
            LayoutCanvas();
        };
        worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){
            if(IsDisposed||Disposing)return;
            var result=e.Error==null?(StoreResult)e.Result:new StoreResult{Error="Could not load the Daily Store. Retrying shortly."};
            if(!Object.ReferenceEquals(canvas.Data,result.Snapshot))
                canvas.UpdateStore(result.Snapshot,result.Error??"");
            else if(canvas.Data==null)canvas.ShowStatus(result.Error??"Loading your daily offers…");
            nextRequest=DateTime.UtcNow.AddSeconds(6);
            LayoutCanvas();
        };
    }
    // Sample rotation for `--render-store` screenshots (real skin ids, art from Valorant-API).
    internal bool DemoReady{get{return canvas.Data!=null&&canvas.Data.Offers.All(o=>o.Art!=null)&&ApiArt.Currency("VP")!=null&&ApiArt.Currency("KC")!=null&&ApiArt.Currency("RAD")!=null&&canvas.Data.Offers.All(o=>{var i=SkinCatalog.Find(o.Id);return i==null||ApiArt.ContentTier(i.TierId)!=null;});}}
    internal void ShowDemo(){
        clock.Enabled=false;
        var offers=new[]{
            new StoreOffer{Id="9f09d408-4e55-b9bb-580a-9fa37e3730b5",Name="RGX 11z Pro Karambit",Price=4350},
            new StoreOffer{Id="d0c07243-4afa-decc-7c5b-4b994f1321ee",Name="Prism II Vandal",Price=875},
            new StoreOffer{Id="eb35e4e1-4138-9ece-08e2-69bd414cf598",Name="Smite Classic",Price=875},
            new StoreOffer{Id="d7aa8579-45bc-8c0b-362b-02a704080d1b",Name="Holo Meridian Vandal",Price=2175}};
        SkinCatalog.Ensure(offers.Select(o=>o.Id));
        foreach(var offer in offers){
            try{
                var info=Json.Get(Json.Codec.DeserializeObject(Http.Text("https://valorant-api.com/v1/weapons/skinlevels/"+offer.Id,1024*1024)),"data");
                if(Json.S(info,"displayName")!="")offer.Name=Json.S(info,"displayName");
                offer.Art=Http.Bytes(Json.S(info,"displayIcon"),4*1024*1024);
            }catch(Exception){}
        }
        var bundle=new StoreOffer{Id="demo-bundle",Name="Snowfall",Price=7100};
        try{
            var data=Json.A(Json.Get(Json.Codec.DeserializeObject(Http.Text("https://valorant-api.com/v1/bundles",8*1024*1024)),"data"))
                .FirstOrDefault(b=>Json.S(b,"displayName")=="Snowfall");
            bundle.Art=Http.Bytes(Json.S(data,"displayIcon"),8*1024*1024);
        }catch(Exception){}
        var demo=new StoreSnapshot{Offers=StoreRarity.WithRarestFirst(offers),Bundles=new[]{bundle},ResetsAt=DateTime.UtcNow.AddHours(23).AddMinutes(3).AddSeconds(52),
            Balances=new[]{new StoreBalance("VP",0),new StoreBalance("RAD",25),new StoreBalance("KC",794)},Account=Guid.NewGuid(),SavedAt=DateTime.UtcNow};
        canvas.UpdateStore(demo,"");
        LayoutCanvas();
    }
    public void LoadIfNeeded(){
        if(!Visible||worker.IsBusy||DateTime.UtcNow<nextRequest)return;
        if(canvas.Data==null&&!canvas.ShowingProblem)canvas.ShowStatus("Loading your daily offers…");
        worker.RunWorkerAsync();
    }
    public void CheckOnOpen(){
        if(!Visible)return;
        canvas.UpdateStore(null,"Checking Riot account…");
        LayoutCanvas();
        nextRequest=DateTime.MinValue;
        LoadIfNeeded();
    }
    internal static StoreSnapshot ForAccount(StoreSnapshot store,Guid account,bool clientOpen){
        if(store==null)return null;
        if(!clientOpen)return store.IsSaved?store:null;
        return account!=Guid.Empty&&store.Account==account?store:null;
    }
    StoreResult OfflineResult(StoreSnapshot fallback,string message){
        if(fallback!=null&&!triedSavedArtwork){
            triedSavedArtwork=true;
            fallback=RiotStore.RepairSavedArtwork(fallback);
            saved=fallback;
        }
        return new StoreResult{Snapshot=fallback,Error=message};
    }
    void LayoutCanvas(){
        canvas.Width=viewport.ClientSize.Width;
        canvas.Height=canvas.HeightForLayout();
        scroll.SetRange(canvas.Height,viewport.ClientSize.Height);
        canvas.Top=-scroll.Value;
    }
    protected override void Dispose(bool disposing){
        if(disposing){clock.Dispose();worker.Dispose();}
        base.Dispose(disposing);
    }
}
sealed class StoreScroll:Control {
    int value,contentHeight,viewportHeight,dragOffset=-1;
    public event EventHandler ValueChanged;
    int Maximum{get{return Math.Max(0,contentHeight-viewportHeight);}}
    int Track{get{return Math.Max(0,Height-12);}}
    int Thumb{get{return contentHeight==0?0:Math.Min(Track,Math.Max(22,Track*viewportHeight/contentHeight));}}
    int ThumbTop{get{return Maximum==0?6:6+(Track-Thumb)*value/Maximum;}}
    public int Value{
        get{return value;}
        set{
            int bounded=Math.Max(0,Math.Min(Maximum,value));
            if(this.value==bounded)return;
            this.value=bounded;
            Invalidate();
            if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);
        }
    }
    public StoreScroll(){DoubleBuffered=true;ResizeRedraw=true;TabStop=true;BackColor=StoreCanvas.Background;AccessibleName="Store scroll";}
    public void SetRange(int content,int viewport){contentHeight=content;viewportHeight=viewport;Value=value;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        if(Maximum<=0||Thumb<8)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        bool active=dragOffset>=0||ClientRectangle.Contains(PointToClient(Cursor.Position));
        using(var path=Skin.Round(new Rectangle(5,ThumbTop,5,Thumb),2))
        using(var brush=new SolidBrush(active?Palette.Accent:Color.FromArgb(70,Palette.Accent)))e.Graphics.FillPath(brush,path);
    }
    protected override bool IsInputKey(Keys key){return key==Keys.Up||key==Keys.Down||key==Keys.PageUp||key==Keys.PageDown||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){
        switch(e.KeyCode){
            case Keys.Up:Value-=45;break;
            case Keys.Down:Value+=45;break;
            case Keys.PageUp:Value-=viewportHeight;break;
            case Keys.PageDown:Value+=viewportHeight;break;
            case Keys.Home:Value=0;break;
            case Keys.End:Value=Maximum;break;
            default:base.OnKeyDown(e);return;
        }
        e.Handled=true;
    }
    protected override void OnMouseWheel(MouseEventArgs e){Value-=e.Delta*65/120;base.OnMouseWheel(e);}
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left||Maximum==0)return;
        Focus();
        if(e.Y>=ThumbTop&&e.Y<ThumbTop+Thumb){dragOffset=e.Y-ThumbTop;Capture=true;}
        else Value+=e.Y<ThumbTop?-viewportHeight:viewportHeight;
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        if(dragOffset>=0&&Track>Thumb)Value=(e.Y-dragOffset-6)*Maximum/(Track-Thumb);
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)dragOffset=-1;base.OnMouseCaptureChanged(e);}
}
sealed class StoreCanvas:Control {
    public static readonly Color Background=Palette.Bg;
    static readonly Color Muted=Palette.Muted;
    static readonly Color Panel=Palette.Panel;
    static readonly Color Outline=Palette.Border;
    static readonly Color Violet=Palette.Accent;
    static readonly Color Need=Color.FromArgb(236,153,172);
    readonly Dictionary<StoreOffer,Image> images=new Dictionary<StoreOffer,Image>();
    readonly List<KeyValuePair<Rectangle,StoreOffer>> videoHits=new List<KeyValuePair<Rectangle,StoreOffer>>();
    StoreOffer hovered;
    public StoreSnapshot Data{get;private set;}
    string status="Loading your daily offers…";
    const int MaxWidth=1260,Gap=14,TileHeight=210,BundleHeight=150,RotationHeight=50;
    StoreOffer[] Offers{get{return Data==null||Data.Offers==null?new StoreOffer[0]:Data.Offers;}}
    StoreOffer[] Bundles{get{return Data==null||Data.Bundles==null?new StoreOffer[0]:Data.Bundles;}}
    bool HasWallet{get{return Data!=null&&!Data.IsSaved&&Data.Balances!=null&&Data.Balances.Length>0;}}
    bool Stacked{get{return Width<640;}}
    int RotationTop{get{return Stacked&&HasWallet?140:100;}}
    int OffersTop{get{return RotationTop+RotationHeight+24;}}
    int ContentWidth{get{return Math.Min(MaxWidth,Width-52);}}
    long WalletVP{
        get{
            if(!HasWallet)return -1;
            var vp=Data.Balances.FirstOrDefault(b=>b.Code=="VP");
            return vp==null?-1:vp.Amount;
        }
    }
    Rectangle RotationBounds{get{int w=ContentWidth;return new Rectangle((Width-w)/2,RotationTop,w,RotationHeight);}}
    public void RefreshCountdown(){
        if(Data!=null&&Data.ResetsAt.HasValue&&Width>=245)Invalidate(new Rectangle(RotationBounds.Location,new Size(RotationBounds.Width+1,RotationBounds.Height+1)));
    }
    public event Action RetryRequested;
    readonly SoftButton openClient=new SoftButton{Text="Open Riot Client",Size=new Size(170,38),FlatStyle=FlatStyle.Flat,BackColor=Palette.Accent,ForeColor=Color.White,Font=new Font("Segoe UI",9,FontStyle.Bold),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter,Visible=false};
    readonly SoftButton retry=new SoftButton{Text="Try again",Size=new Size(130,38),FlatStyle=FlatStyle.Flat,BackColor=Palette.Raised,ForeColor=Palette.Text,Font=new Font("Segoe UI",9,FontStyle.Bold),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter,Visible=false};
    public StoreCanvas(){
        DoubleBuffered=true;ResizeRedraw=true;BackColor=Background;
        foreach(var b in new[]{openClient,retry}){b.FlatAppearance.BorderSize=0;b.SurfaceColor=Panel;Controls.Add(b);}
        openClient.Click+=delegate{try{RiotReader.OpenClient();openClient.Text="Opening…";}catch(Exception e){LunarLog.Error("store","open Riot Client failed",e);ShowStatus(e.Message);}};
        retry.Click+=delegate{retry.Text="Checking…";if(RetryRequested!=null)RetryRequested();};
    }
    // What the empty store screen should say for a given status message.
    enum StoreProblem{Loading,ClientClosed,SignIn,Busy,Network,Other}
    static StoreProblem Classify(string message){
        string m=message??"";
        if(m.StartsWith("Loading",StringComparison.Ordinal)||m.StartsWith("Checking",StringComparison.Ordinal)||m=="")return StoreProblem.Loading;
        if(m.Contains("Open Riot Client and sign in"))return StoreProblem.ClientClosed;
        if(m.Contains("Sign in")||m.Contains("store session")||m.Contains("VALORANT and visit")||m.Contains("signed in"))return StoreProblem.SignIn;
        if(m.Contains("rate limit")||m.Contains("Retrying shortly"))return StoreProblem.Busy;
        if(m.Contains("connect"))return StoreProblem.Network;
        return StoreProblem.Other;
    }
    public void ShowStatus(string message){status=message;retry.Text="Try again";Invalidate();}
    // While a problem is on screen, background retries keep it there instead of flashing "Loading…".
    public bool ShowingProblem{get{return Classify(status)!=StoreProblem.Loading;}}
    void PlaceButton(Control button,bool show,Point at){
        if(button.Visible!=show)button.Visible=show;
        if(show&&button.Location!=at)button.Location=at;
    }
    public void UpdateStore(StoreSnapshot data,string message){
        ReleaseArt();
        Data=data;
        foreach(var offer in Offers.Concat(Bundles)){
            if(offer.Art==null||images.ContainsKey(offer))continue;
            using(var stream=new MemoryStream(offer.Art)){
                var image=TrackArt.Decode(stream);
                if(image!=null)images.Add(offer,image);
            }
        }
        ShowStatus(message);
    }
    public int HeightForLayout(){
        if(Data==null)return 388;
        int tiles=(Offers.Length+1)/2;
        int offerHeight=tiles==0?95:tiles*(TileHeight+Gap)-Gap;
        int bundleHeight=Bundles.Length==0?0:58+Bundles.Length*(BundleHeight+12)-12;
        return OffersTop+44+offerHeight+bundleHeight+(Data.NightMarket?63:30);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(Width<245)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        int w=ContentWidth,left=(Width-w)/2;
        var rotation=RotationBounds;
        if(Data!=null&&Rectangle.Inflate(rotation,1,1).Contains(e.ClipRectangle)){
            DrawRotation(g,rotation);
            return;
        }
        videoHits.Clear();
        ProfileCanvas.T(g,"Daily Store",25,Palette.Text,new Rectangle(left,23,w,48),true);
        string subtitle=Data!=null&&Data.IsSaved?"Saved "+Data.SavedAt.ToLocalTime().ToString("MMM d, HH:mm",LunarLanguage.Culture)
            :"Your VALORANT skin rotation";
        ProfileCanvas.T(g,subtitle,10,Muted,new Rectangle(left,66,w,27));
        if(Data==null){
            var kind=Classify(status);
            bool steps=kind==StoreProblem.ClientClosed||kind==StoreProblem.SignIn;
            var empty=new Rectangle(left,120,w,steps?372:276);
            Card(g,empty,16,Panel,Outline);
            var icon=new Rectangle(empty.X+empty.Width/2-27,empty.Y+30,54,54);
            Color tone=kind==StoreProblem.Busy||kind==StoreProblem.Network?Color.FromArgb(245,190,95):Violet;
            Card(g,icon,15,Color.FromArgb(40,tone),Color.FromArgb(90,tone));
            LunarIcons.Draw(g,LunarGlyph.Shop,new Point(icon.X+27,icon.Y+27),tone,26);
            string title=kind==StoreProblem.Loading?"Daily offers":kind==StoreProblem.ClientClosed?"Riot Client is closed":kind==StoreProblem.SignIn?"Sign in to VALORANT":kind==StoreProblem.Busy?"Riot is busy right now":kind==StoreProblem.Network?"Can't reach Riot":"Daily offers";
            string body=kind==StoreProblem.ClientClosed?"Your daily offers load here as soon as Riot Client is open and signed in.":
                kind==StoreProblem.SignIn?"Your store only appears for an account signed in to VALORANT. Open the game once and the offers load here automatically.":
                kind==StoreProblem.Busy?"Too many requests to Riot. Lunar tries again by itself in a moment.":
                kind==StoreProblem.Network?"Check your internet connection. Lunar tries again by itself.":status;
            Center(g,title,15,Palette.Text,new Rectangle(empty.X+16,empty.Y+96,empty.Width-32,30),true);
            Center(g,body,10,Muted,new Rectangle(empty.X+48,empty.Y+128,empty.Width-96,44),false);
            int y=empty.Y+186;
            if(steps){
                string[] items={"Open Riot Client","Sign in to your account","Open VALORANT (the main menu is enough)"};
                int listW=Math.Min(360,empty.Width-80),lx=empty.X+(empty.Width-listW)/2;
                for(int i=0;i<items.Length;i++){
                    bool done=kind==StoreProblem.SignIn&&i<2;
                    var dot=new Rectangle(lx,y+i*34+4,22,22);
                    using(var shape=Skin.Round(dot,11))using(var fill=new SolidBrush(done?Color.FromArgb(46,Palette.Win):Color.FromArgb(46,Violet)))g.FillPath(fill,shape);
                    Center(g,done?"✓":(i+1).ToString(),8,done?Palette.Win:Violet,dot,true);
                    ProfileCanvas.T(g,items[i],10,done?Muted:Palette.Text,new Rectangle(lx+34,y+i*34,listW-34,30),!done);
                }
                y+=items.Length*34+14;
            }
            bool showOpen=kind==StoreProblem.ClientClosed||kind==StoreProblem.SignIn;
            bool showRetry=kind!=StoreProblem.Loading;
            int buttonsWidth=(showOpen?openClient.Width+10:0)+(showRetry?retry.Width:0);
            int bx=empty.X+(empty.Width-buttonsWidth)/2;
            PlaceButton(openClient,showOpen,new Point(bx,y));
            PlaceButton(retry,showRetry,new Point(bx+(showOpen?openClient.Width+10:0),y));
            if(kind!=StoreProblem.Loading)Center(g,"Lunar checks again automatically.",8,Color.FromArgb(120,Muted),new Rectangle(empty.X+16,empty.Bottom-30,empty.Width-32,20),false);
            return;
        }
        PlaceButton(openClient,false,Point.Empty);PlaceButton(retry,false,Point.Empty);
        if(HasWallet)DrawWallet(g,Stacked?new Rectangle(left,98,w,36):new Rectangle(left,34,w,36),Stacked);
        DrawRotation(g,rotation);
        ProfileCanvas.T(g,"Daily offers",14,Palette.Text,new Rectangle(left,OffersTop,w-220,31),true);
        long total=Offers.Where(o=>o.Price>0).Sum(o=>o.Price);
        string count=Offers.Length+" offers"+(total>0?"  ·  "+total.ToString("N0",CultureInfo.InvariantCulture)+" VP total":"");
        using(var font=new Font("Segoe UI",9))
            LunarText.DrawText(g,count,font,new Rectangle(left+w-260,OffersTop,260,31),Muted,
                TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        int top=OffersTop+44;
        if(Offers.Length==0){
            Card(g,new Rectangle(left,top,w,81),12,Panel,Outline);
            ProfileCanvas.T(g,"No daily offers were returned.",10,Muted,new Rectangle(left+16,top,w-32,81));
            top+=95;
        }else{
            int tileWidth=(w-Gap)/2;
            for(int i=0;i<Offers.Length;i++){
                int x=left+(i%2)*(tileWidth+Gap),y=top+(i/2)*(TileHeight+Gap);
                int size=i%2==0?tileWidth:w-tileWidth-Gap;
                DrawOffer(g,Offers[i],new Rectangle(x,y,size,TileHeight),i==0&&StoreRarity.Rank(Offers[i].Id)>=0);
            }
            top+=((Offers.Length+1)/2)*(TileHeight+Gap)-Gap;
        }
        if(Bundles.Length>0){
            ProfileCanvas.T(g,"Featured bundles",14,Palette.Text,new Rectangle(left,top+20,w-120,31),true);
            using(var font=new Font("Segoe UI",9))
                LunarText.DrawText(g,Bundles.Length==1?"1 bundle":Bundles.Length+" bundles",font,new Rectangle(left+w-120,top+20,120,31),Muted,
                    TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            top+=58;
            foreach(var bundle in Bundles){
                DrawBundle(g,bundle,new Rectangle(left,top,w,BundleHeight));
                top+=BundleHeight+12;
            }
            top-=12;
        }
        if(Data.NightMarket)ProfileCanvas.T(g,"Night Market is open",10,Violet,new Rectangle(left,top+15,w,28),true);
    }
    void DrawWallet(Graphics g,Rectangle area,bool alignLeft){
        const int chipGap=8;
        var chips=new List<KeyValuePair<StoreBalance,int>>();
        int totalWidth=0;
        using(var bold=new Font("Segoe UI",10,FontStyle.Bold))
        using(var small=new Font("Segoe UI",8,FontStyle.Bold))
            foreach(var balance in Data.Balances){
                int width=14+18+7+LunarText.MeasureText(g,balance.Amount.ToString("N0",CultureInfo.InvariantCulture),bold).Width
                    +LunarText.MeasureText(g,balance.Code,small).Width+10;
                chips.Add(new KeyValuePair<StoreBalance,int>(balance,width));
                totalWidth+=width+chipGap;
            }
        int x=alignLeft?area.X:area.Right-totalWidth+chipGap;
        foreach(var chip in chips){
            var rect=new Rectangle(x,area.Y,chip.Value,area.Height);
            Card(g,rect,9,Palette.Panel,Outline);
            var icon=ApiArt.Currency(chip.Key.Code);
            if(icon!=null)Contain(g,icon,new Rectangle(rect.X+12,rect.Y+(rect.Height-17)/2,17,17));
            string amount=chip.Key.Amount.ToString("N0",CultureInfo.InvariantCulture);
            using(var bold=new Font("Segoe UI",10,FontStyle.Bold))
            using(var small=new Font("Segoe UI",8,FontStyle.Bold)){
                int amountWidth=LunarText.MeasureText(g,amount,bold).Width;
                LunarText.DrawText(g,amount,bold,new Rectangle(rect.X+33,rect.Y,amountWidth,rect.Height),Palette.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
                LunarText.DrawText(g,chip.Key.Code,small,new Rectangle(rect.X+30+amountWidth,rect.Y+1,rect.Width-amountWidth-30,rect.Height),Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            }
            x+=chip.Value+chipGap;
        }
    }
    void DrawRotation(Graphics g,Rectangle rect){
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var bg=new SolidBrush(Background))g.FillRectangle(bg,Rectangle.Inflate(rect,1,1));
        Card(g,rect,12,Palette.Panel,Outline);
        bool expired=Data.SavedRotationExpired(DateTime.UtcNow);
        var remaining=Data.ResetsAt.HasValue?Data.ResetsAt.Value-DateTime.UtcNow:TimeSpan.Zero;
        string label=Data.IsSaved?(expired?"SAVED ROTATION · EXPIRED":"SAVED ROTATION"):"NEXT ROTATION";
        string value=!Data.ResetsAt.HasValue?"—":remaining<=TimeSpan.Zero?
            (Data.IsSaved?"Rotation ended":"Updating…"):
            ((int)remaining.TotalHours).ToString("00")+":"+remaining.Minutes.ToString("00")+":"+remaining.Seconds.ToString("00");
        var labelColor=expired?Color.FromArgb(240,185,111):Violet;
        int cy=rect.Y+rect.Height/2;
        using(var pen=new Pen(labelColor,1.6f)){
            g.DrawEllipse(pen,rect.X+18,cy-8,16,16);
            g.DrawLines(pen,new[]{new Point(rect.X+26,cy-4),new Point(rect.X+26,cy),new Point(rect.X+30,cy+2)});
        }
        int labelWidth;
        using(var font=new Font("Segoe UI",8,FontStyle.Bold)){
            labelWidth=LunarText.MeasureText(g,label,font).Width;
            LunarText.DrawText(g,label,font,new Rectangle(rect.X+42,rect.Y,labelWidth+4,rect.Height),labelColor,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        }
        int valueWidth;
        using(var font=new Font("Segoe UI",15,FontStyle.Bold)){
            valueWidth=LunarText.MeasureText(g,"00:00:00",font).Width;
            int actual=LunarText.MeasureText(g,value,font).Width;
            valueWidth=Math.Max(valueWidth,actual);
            LunarText.DrawText(g,value,font,new Rectangle(rect.Right-valueWidth-16,rect.Y,valueWidth,rect.Height),Palette.Text,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        }
        var bar=new Rectangle(rect.X+42+labelWidth+16,cy-3,rect.Width-(42+labelWidth+16)-valueWidth-36,6);
        if(bar.Width>40){
            using(var track=Skin.Round(bar,3))using(var brush=new SolidBrush(Palette.Raised))g.FillPath(brush,track);
            double progress=Data.ResetsAt.HasValue?Math.Max(0,Math.Min(1,1-remaining.TotalSeconds/86400.0)):0;
            int filled=(int)Math.Round(bar.Width*progress);
            if(filled>=6){
                var fill=new Rectangle(bar.X,bar.Y,filled,bar.Height);
                using(var shape=Skin.Round(fill,3))
                using(var brush=new LinearGradientBrush(new Rectangle(fill.X-1,fill.Y,fill.Width+2,fill.Height),Color.FromArgb(124,77,230),Violet,0f))
                    g.FillPath(brush,shape);
            }
        }
    }
    void DrawOffer(Graphics g,StoreOffer offer,Rectangle rect,bool featured){
        var info=SkinCatalog.Find(offer.Id);
        bool hot=Object.ReferenceEquals(hovered,offer);
        bool hasVideo=info!=null&&info.Videos.Count>0;
        var saved=g.Save();
        using(var clip=Skin.Round(rect,13)){
            using(var fill=new LinearGradientBrush(rect,Color.FromArgb(featured?40:26,Violet),Panel,90f)){
                fill.Blend=new Blend{Factors=new[]{0f,.75f,1f},Positions=new[]{0f,.55f,1f}};
                g.FillPath(fill,clip);
            }
            g.SetClip(clip,CombineMode.Intersect);
            var glowRect=new Rectangle(rect.X+rect.Width/2-rect.Width*4/10,rect.Y+22,rect.Width*8/10,rect.Height-70);
            using(var glowPath=new GraphicsPath()){
                glowPath.AddEllipse(glowRect);
                using(var glow=new PathGradientBrush(glowPath){CenterColor=Color.FromArgb(featured?70:46,Violet),SurroundColors=new[]{Color.FromArgb(0,Violet)}})
                    g.FillPath(glow,glowPath);
            }
            using(var stripe=new SolidBrush(featured||hot?Violet:Color.FromArgb(150,Violet)))g.FillRectangle(stripe,rect.X,rect.Y,rect.Width,3);
        }
        g.Restore(saved);
        using(var shape=Skin.Round(rect,13))
        using(var edge=new Pen(featured||hot?Color.FromArgb(featured?200:150,Violet):Outline,featured?1.4f:1f))g.DrawPath(edge,shape);

        int tagX=rect.X+14;
        string tier=info==null?"":info.Tier;
        if(tier!="")tagX=DrawTag(g,tier,ApiArt.ContentTier(info.TierId),tagX,rect.Y+14,false)+6;
        if(featured)DrawTag(g,"Featured",null,tagX,rect.Y+14,true);

        if(hasVideo){
            var play=new Rectangle(rect.Right-44,rect.Y+12,30,30);
            using(var shape=Skin.Round(play,8))
            using(var fill=new SolidBrush(hot?Color.FromArgb(70,Violet):Color.FromArgb(26,255,255,255)))
            using(var edge=new Pen(hot?Color.FromArgb(160,Violet):Color.FromArgb(30,255,255,255))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
            using(var tri=new SolidBrush(hot?Color.White:Palette.Text))
                g.FillPolygon(tri,new[]{new PointF(play.X+12,play.Y+9),new PointF(play.X+12,play.Bottom-9),new PointF(play.Right-9,play.Y+15)});
            videoHits.Add(new KeyValuePair<Rectangle,StoreOffer>(rect,offer));
        }

        var art=new Rectangle(rect.X+26,rect.Y+44,rect.Width-52,rect.Height-104);
        bool knife=info!=null&&info.Weapon=="Melee";
        if(knife)art=new Rectangle(art.X,art.Y-6,art.Width,art.Height+12);
        Image picture;
        if(images.TryGetValue(offer,out picture))Contain(g,picture,art);
        else LunarIcons.Draw(g,LunarGlyph.Shop,new Point(art.X+art.Width/2,art.Y+art.Height/2),Muted,34);

        int priceWidth=150;
        string weapon=info==null||String.IsNullOrEmpty(info.Weapon)?"":info.Weapon;
        ProfileCanvas.T(g,offer.Name,11,Palette.Text,new Rectangle(rect.X+15,rect.Bottom-54,rect.Width-priceWidth-26,26),true);
        if(weapon!="")ProfileCanvas.T(g,weapon,8,Muted,new Rectangle(rect.X+15,rect.Bottom-30,rect.Width-priceWidth-26,20));
        if(offer.Price>=0){
            string amount=offer.Price.ToString("N0",CultureInfo.InvariantCulture);
            using(var font=new Font("Segoe UI",12,FontStyle.Bold)){
                int textWidth=LunarText.MeasureText(g,amount,font).Width;
                int textX=rect.Right-15-textWidth;
                LunarText.DrawText(g,amount,font,new Rectangle(textX,rect.Bottom-56,textWidth,28),Violet,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
                var icon=ApiArt.Currency("VP");
                if(icon!=null)Contain(g,icon,new Rectangle(textX-20,rect.Bottom-49,15,15));
            }
            long vp=WalletVP;
            if(vp>=0&&vp<offer.Price){
                string need="Need "+(offer.Price-vp).ToString("N0",CultureInfo.InvariantCulture)+" VP";
                using(var font=new Font("Segoe UI",8,FontStyle.Bold))
                    LunarText.DrawText(g,need,font,new Rectangle(rect.Right-priceWidth-15,rect.Bottom-30,priceWidth,20),Need,
                        TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            }
        }else ProfileCanvas.T(g,"—",11,Muted,new Rectangle(rect.Right-40,rect.Bottom-54,30,26),true);
    }
    int DrawTag(Graphics g,string text,Image icon,int x,int y,bool star){
        int textWidth;
        using(var font=new Font("Segoe UI",8,FontStyle.Bold))textWidth=LunarText.MeasureText(g,text,font).Width;
        int lead=icon!=null||star?20:8;
        var rect=new Rectangle(x,y,lead+textWidth+4,22);
        using(var shape=Skin.Round(rect,6))
        using(var fill=new SolidBrush(Color.FromArgb(star?52:38,Violet)))g.FillPath(fill,shape);
        if(icon!=null)Contain(g,icon,new Rectangle(rect.X+6,rect.Y+4,14,14));
        else if(star)using(var pen=new Pen(Palette.Cyan,1.2f)){
            var c=new PointF(rect.X+12.5f,rect.Y+11f);
            var pts=new PointF[10];
            for(int i=0;i<10;i++){double a=-Math.PI/2+i*Math.PI/5;float r=i%2==0?5.5f:2.4f;pts[i]=new PointF(c.X+(float)(Math.Cos(a)*r),c.Y+(float)(Math.Sin(a)*r));}
            g.DrawPolygon(pen,pts);
        }
        using(var font=new Font("Segoe UI",8,FontStyle.Bold))
            LunarText.DrawText(g,text,font,new Rectangle(rect.X+lead-2,rect.Y,textWidth+6,rect.Height),Palette.Cyan,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        return rect.Right;
    }
    void DrawBundle(Graphics g,StoreOffer bundle,Rectangle rect){
        var saved=g.Save();
        using(var clip=Skin.Round(rect,13)){
            using(var fill=new SolidBrush(Panel))g.FillPath(fill,clip);
            g.SetClip(clip,CombineMode.Intersect);
            Image picture;
            if(images.TryGetValue(bundle,out picture))TrackArt.Cover(g,picture,rect);
            using(var shade=new LinearGradientBrush(rect,Color.FromArgb(245,14,16,21),Color.FromArgb(0,14,16,21),0f)){
                shade.Blend=new Blend{Factors=new[]{0f,.35f,1f},Positions=new[]{0f,.4f,1f}};
                g.FillRectangle(shade,rect);
            }
        }
        g.Restore(saved);
        using(var shape=Skin.Round(rect,13))using(var edge=new Pen(Outline))g.DrawPath(edge,shape);
        int x=rect.X+24;
        ProfileCanvas.T(g,"FEATURED BUNDLE",8,Palette.Cyan,new Rectangle(x,rect.Y+22,rect.Width/2,20),true);
        ProfileCanvas.T(g,bundle.Name,19,Color.White,new Rectangle(x,rect.Y+42,rect.Width*6/10,40),true);
        if(bundle.Price>0){
            string amount=bundle.Price.ToString("N0",CultureInfo.InvariantCulture);
            using(var font=new Font("Segoe UI",10,FontStyle.Bold)){
                int width=LunarText.MeasureText(g,amount,font).Width+40;
                var chip=new Rectangle(x,rect.Y+94,width,30);
                using(var cs=Skin.Round(chip,8))using(var fill=new SolidBrush(Color.FromArgb(30,255,255,255)))g.FillPath(fill,cs);
                var icon=ApiArt.Currency("VP");
                if(icon!=null)Contain(g,icon,new Rectangle(chip.X+10,chip.Y+8,14,14));
                LunarText.DrawText(g,amount,font,new Rectangle(chip.X+29,chip.Y,width-29,chip.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            }
        }
    }
    StoreOffer HitVideo(Point point){
        return videoHits.Where(hit=>hit.Key.Contains(point)).Select(hit=>hit.Value).FirstOrDefault();
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        var hit=HitVideo(e.Location);
        Cursor=hit!=null?Cursors.Hand:Cursors.Default;
        if(!Object.ReferenceEquals(hit,hovered)){hovered=hit;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){
        base.OnMouseLeave(e);
        if(hovered!=null){hovered=null;Invalidate();}
    }
    protected override void OnMouseClick(MouseEventArgs e){
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left)return;
        var offer=HitVideo(e.Location);
        var info=offer==null?null:SkinCatalog.Find(offer.Id);
        if(info==null||info.Videos.Count==0)return;
        SkinVideoForm.Open(FindForm(),offer.Name,info);
    }
    static void Center(Graphics g,string text,int size,Color color,Rectangle rect,bool bold){
        using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))
            LunarText.DrawText(g,text,font,rect,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
    static void Card(Graphics g,Rectangle rect,int radius,Color fill,Color border){
        using(var shape=Skin.Round(rect,radius))
        using(var brush=new SolidBrush(fill))
        using(var edge=new Pen(border)){
            g.FillPath(brush,shape);
            g.DrawPath(edge,shape);
        }
    }
    static void Contain(Graphics g,Image image,Rectangle rect){
        if(rect.Width<=0||rect.Height<=0)return;
        float ratio=Math.Min((float)rect.Width/image.Width,(float)rect.Height/image.Height);
        float width=image.Width*ratio,height=image.Height*ratio;
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.DrawImage(image,rect.X+(rect.Width-width)/2,rect.Y+(rect.Height-height)/2,width,height);
    }
    void ReleaseArt(){foreach(var image in images.Values)image.Dispose();images.Clear();}
    protected override void Dispose(bool disposing){
        if(disposing)ReleaseArt();
        base.Dispose(disposing);
    }
}

// Plays the skin preview videos from Valorant-API (Riot CDN mp4) inside the app.
sealed class SkinVideoForm:Form {
    readonly IVideoSurface surface=VideoSurfaces.Create();
    readonly FlowLayoutPanel variants=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=false,BackColor=Palette.Panel,Padding=new Padding(0,14,0,0)};
    readonly List<VideoChip> chips=new List<VideoChip>();
    readonly SkinInfo info;
    readonly string title;
    string current;
    const int HeaderHeight=72,FooterHeight=66,Inset=18;

    // Opens the player centered over the owner, with the owner dimmed behind it.
    public static void Open(IWin32Window ownerHandle,string title,SkinInfo info){
        var owner=ownerHandle as Form;
        using(var dim=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,BackColor=Color.Black,Opacity=.58})
        using(var player=new SkinVideoForm(title,info)){
            if(owner!=null&&owner.WindowState!=FormWindowState.Minimized){
                dim.Bounds=owner.RectangleToScreen(owner.ClientRectangle);
                dim.Show(owner);
                var area=dim.Bounds;
                int width=Math.Min(1000,area.Width-60),height=Math.Min(width*9/16+HeaderHeight+FooterHeight+Inset,area.Height-40);
                player.StartPosition=FormStartPosition.Manual;
                player.Bounds=new Rectangle(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height);
                dim.MouseDown+=delegate{player.Close();};
                player.ShowDialog(dim);
            }else player.ShowDialog(ownerHandle);
        }
    }

    public SkinVideoForm(string title,SkinInfo info){
        this.info=info;this.title=title;
        Text=title;
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;KeyPreview=true;
        DoubleBuffered=true;
        ClientSize=new Size(960,HeaderHeight+540+FooterHeight+Inset);BackColor=Palette.Panel;
        var header=new Panel{Dock=DockStyle.Top,Height=HeaderHeight,BackColor=Palette.Panel};
        header.Paint+=PaintHeader;
        var close=new CloseGlyphButton{Size=new Size(38,38)};
        close.Click+=delegate{Close();};
        header.Controls.Add(close);
        header.Resize+=delegate{close.Location=new Point(header.Width-close.Width-Inset,(HeaderHeight-close.Height)/2);};
        header.MouseDown+=Drag;

        var footer=new Panel{Dock=DockStyle.Bottom,Height=FooterHeight,BackColor=Palette.Panel,Padding=new Padding(Inset,0,Inset,0)};
        foreach(var video in info.Videos){
            var choice=video;
            var chip=new VideoChip(choice.Name,choice.Url);
            chip.Click+=delegate{Play(choice.Url);};
            chips.Add(chip);variants.Controls.Add(chip);
        }
        var external=new VideoChip(LunarLanguage.Text("Open in browser","Abrir no navegador")+"  ↗",null){Ghost=true};
        external.Click+=delegate{if(current!=null)try{System.Diagnostics.Process.Start(current);}catch(Exception){}};
        external.Dock=DockStyle.Right;
        var externalHost=new Panel{Dock=DockStyle.Right,Width=external.Width,BackColor=Palette.Panel,Padding=new Padding(0,14,0,16)};
        externalHost.Controls.Add(external);
        footer.Controls.Add(variants);footer.Controls.Add(externalHost);

        var body=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Panel,Padding=new Padding(Inset,0,Inset,0)};
        body.Controls.Add(surface.View);
        surface.CloseRequested+=delegate{if(IsHandleCreated)BeginInvoke((Action)Close);};
        surface.Message+=VideoVolumePreference.Apply;
        Controls.Add(body);Controls.Add(footer);Controls.Add(header);
        KeyDown+=delegate(object s,KeyEventArgs e){
            if(e.KeyCode==Keys.Escape)Close();
            else if(e.KeyCode==Keys.Space){surface.Toggle();e.Handled=true;}
        };
        Shown+=delegate{Play(info.Videos[0].Url);};
        LunarLanguage.Attach(this);
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width>40&&Height>40)using(var shape=Skin.Round(new Rectangle(0,0,Width,Height),16))Region=new Region(shape);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=Skin.Round(new Rectangle(0,0,Width-1,Height-1),16))using(var edge=new Pen(Palette.Border))e.Graphics.DrawPath(edge,shape);
    }
    void PaintHeader(object sender,PaintEventArgs e){
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var header=(Control)sender;
        using(var shape=Skin.Round(new Rectangle(0,0,Width-1,Height-1),16))using(var edge=new Pen(Palette.Border))g.DrawPath(edge,shape);
        var tile=new Rectangle(Inset,(HeaderHeight-42)/2,42,42);
        using(var shape=Skin.Round(tile,11))
        using(var fill=new SolidBrush(Palette.AccentSoft))
        using(var edge=new Pen(Color.FromArgb(80,Palette.Accent))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        var tier=ApiArt.ContentTier(info.TierId);
        if(tier!=null)g.DrawImage(tier,new Rectangle(tile.X+10,tile.Y+10,22,22));
        else using(var tri=new SolidBrush(Palette.Accent))g.FillPolygon(tri,new[]{new Point(tile.X+16,tile.Y+12),new Point(tile.X+16,tile.Bottom-12),new Point(tile.Right-12,tile.Y+21)});
        int x=tile.Right+14;
        ProfileCanvas.T(g,title,13,Palette.Text,new Rectangle(x,14,header.Width-x-80,26),true);
        string sub=(info.Tier==""?"":info.Tier+" Edition")+(String.IsNullOrEmpty(info.Weapon)?"":(info.Tier==""?"":"  ·  ")+info.Weapon)
            +"  ·  "+info.Videos.Count+(info.Videos.Count==1?" video":" videos");
        ProfileCanvas.T(g,sub,9,Palette.Accent,new Rectangle(x,40,header.Width-x-80,20),true);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,int wparam,int lparam);
    void Drag(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}}
    void Play(string url){
        Uri uri;
        if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.Host.EndsWith("riotcdn.net",StringComparison.OrdinalIgnoreCase))return;
        current=url;
        foreach(var chip in chips)chip.Selected=chip.Url==url;
        string safe=System.Net.WebUtility.HtmlEncode(uri.AbsoluteUri);
        // ES5 only: the embedded engine is IE11.
        surface.Load(@"<!doctype html><html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'><style>
html,body{margin:0;height:100%;background:#1d2128;overflow:hidden;font-family:'Segoe UI',sans-serif;-ms-user-select:none}
#wrap{position:absolute;left:0;right:0;top:0;bottom:0;background:#0d0f13;border-radius:12px;overflow:hidden}
video{position:absolute;left:0;top:0;width:100%;height:100%;background:#0d0f13}
#bar{position:absolute;left:0;right:0;bottom:0;height:64px;background:linear-gradient(to top,rgba(10,10,14,.85),rgba(10,10,14,0));
  display:flex;align-items:center;padding:0 16px;transition:opacity .25s;opacity:0}
#wrap:hover #bar,#wrap.paused #bar{opacity:1}
.btn{width:36px;height:36px;border-radius:18px;background:rgba(255,255,255,.12);display:flex;align-items:center;justify-content:center;cursor:pointer;flex:none}
.btn:hover{background:rgba(183,143,246,.45)}
#play{background:#b78ff6}#play:hover{background:#c9a8ff}
#track{flex:1;height:5px;margin:0 14px;border-radius:3px;background:rgba(255,255,255,.18);cursor:pointer;position:relative}
#fill{position:absolute;left:0;top:0;bottom:0;width:0;border-radius:3px;background:#b78ff6}
#time{color:#e8e6f0;font-size:12px;font-weight:600;min-width:84px;text-align:right;margin-right:12px}
#vol{width:84px;height:24px;margin-left:10px;position:relative;cursor:pointer;flex:none}
#volbg{position:absolute;left:0;right:0;top:10px;height:4px;border-radius:2px;background:rgba(255,255,255,.18)}
#volfill{position:absolute;left:0;top:0;bottom:0;width:0;border-radius:2px;background:#b78ff6}
#volknob{position:absolute;top:6px;width:12px;height:12px;margin-left:-6px;border-radius:6px;background:#e8e6f0}
#volpct{color:#e8e6f0;font-size:11px;font-weight:600;width:34px;text-align:right;flex:none}
#load{position:absolute;left:50%;top:50%;width:34px;height:34px;margin:-17px 0 0 -17px;border-radius:50%;border:3px solid rgba(183,143,246,.25);border-top-color:#b78ff6;animation:spin .8s linear infinite}
@keyframes spin{to{transform:rotate(360deg)}}
</style></head><body><div id='wrap'><video id='v' src='"+safe+@"' autoplay loop preload='auto'></video><div id='load'></div>
<div id='bar'><div class='btn' id='play'><svg width='14' height='14' viewBox='0 0 14 14'><g id='ip'><rect x='2' y='1' width='3.4' height='12' rx='1' fill='#14161c'/><rect x='8.6' y='1' width='3.4' height='12' rx='1' fill='#14161c'/></g><path id='ipl' d='M3 1.5v11l9.5-5.5z' fill='#14161c' style='display:none'/></svg></div>
<div id='track'><div id='fill'></div></div><div id='time'>0:00 / 0:00</div>
<div class='btn' id='mute'><svg width='16' height='16' viewBox='0 0 16 16'><path d='M2 6h3l4-3v10l-4-3H2z' fill='#e8e6f0'/><path id='wave' d='M11 5.5c1.2 1.3 1.2 3.7 0 5' stroke='#e8e6f0' stroke-width='1.6' fill='none' stroke-linecap='round'/><path id='x' d='M11 6l4 4M15 6l-4 4' stroke='#e8e6f0' stroke-width='1.6' stroke-linecap='round' style='display:none'/></svg></div>
<div id='vol' title='Volume'><div id='volbg'><div id='volfill'></div></div><div id='volknob'></div></div><div id='volpct'>100%</div></div></div>
<script>
var v=document.getElementById('v'),wrap=document.getElementById('wrap'),fill=document.getElementById('fill'),time=document.getElementById('time');
var vol=document.getElementById('vol'),volfill=document.getElementById('volfill'),volknob=document.getElementById('volknob'),volpct=document.getElementById('volpct'),dragging=false;
v.volume="+VideoVolumePreference.Volume.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+@";v.muted="+(VideoVolumePreference.Muted?"true":"false")+@";
function report(){var s='vol:'+v.volume+':'+(v.muted?1:0);try{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(s);else window.external.Report(s);}catch(e){}}
function drawVol(){var p=v.muted?0:v.volume;volfill.style.width=(p*100)+'%';volknob.style.left=(p*100)+'%';volpct.innerHTML=Math.round(p*100)+'%';}
function setVol(x){x=Math.max(0,Math.min(1,x));v.volume=x;v.muted=x===0;sync();if(!dragging)report();}
function volAt(e){var r=vol.getBoundingClientRect();setVol((e.clientX-r.left)/r.width);}
vol.onmousedown=function(e){e=e||window.event;dragging=true;volAt(e);if(e.preventDefault)e.preventDefault();return false;};
document.onmousemove=function(e){if(dragging)volAt(e||window.event);};
document.onmouseup=function(){if(dragging){dragging=false;report();}};
function wheel(e){e=e||window.event;var d=e.deltaY||-e.wheelDelta||0;setVol((v.muted?0:v.volume)+(d<0?.05:-.05));if(e.preventDefault)e.preventDefault();return false;}
vol.onwheel=wheel;vol.onmousewheel=wheel;volpct.onwheel=wheel;volpct.onmousewheel=wheel;
function fmt(s){s=Math.max(0,Math.floor(s||0));return Math.floor(s/60)+':'+(s%60<10?'0':'')+(s%60);}
function sync(){var p=v.paused;document.getElementById('ip').style.display=p?'none':'';document.getElementById('ipl').style.display=p?'':'none';wrap.className=p?'paused':'';
 var m=v.muted||v.volume===0;document.getElementById('wave').style.display=m?'none':'';document.getElementById('x').style.display=m?'':'none';drawVol();}
function lunarToggle(){if(v.paused)v.play();else v.pause();sync();}
document.getElementById('play').onclick=lunarToggle;v.onclick=lunarToggle;
document.getElementById('mute').onclick=function(){if(v.muted&&v.volume===0)v.volume=.5;v.muted=!v.muted;sync();report();};
document.getElementById('track').onclick=function(e){var r=this.getBoundingClientRect();if(v.duration)v.currentTime=(e.clientX-r.left)/r.width*v.duration;};
v.ontimeupdate=function(){if(v.duration){fill.style.width=(v.currentTime/v.duration*100)+'%';time.innerHTML=fmt(v.currentTime)+' / '+fmt(v.duration);}};
v.onplaying=function(){document.getElementById('load').style.display='none';sync();};v.onwaiting=function(){document.getElementById('load').style.display='';};
v.onpause=sync;v.onplay=sync;sync();
document.onkeydown=function(e){e=e||window.event;var k=e.keyCode;if(k===32){lunarToggle();if(e.preventDefault)e.preventDefault();return false;}
 if(k===38||k===40){setVol((v.muted?0:v.volume)+(k===38?.05:-.05));if(e.preventDefault)e.preventDefault();return false;}
 if(k===27&&window.chrome&&window.chrome.webview)window.chrome.webview.postMessage('close');};
function lunarStats(){var q=v.getVideoPlaybackQuality?v.getVideoPlaybackQuality():null;return (q?q.totalVideoFrames:-1)+','+(q?q.droppedVideoFrames:-1)+','+v.currentTime+','+v.videoWidth+'x'+v.videoHeight;}
</script></body></html>");
    }
    // Diagnostics: "total,dropped,currentTime,size" from the page's video element.
    internal string Stats(){var legacy=surface.View as WebBrowser;if(legacy==null)return "webview2";try{return Convert.ToString(legacy.Document.InvokeScript("lunarStats"));}catch(Exception e){return "error:"+e.Message;}}
    protected override void Dispose(bool disposing){
        if(disposing)surface.View.Dispose();
        base.Dispose(disposing);
    }
}

// Pill button for a video variant; chroma names (Red, Blue…) get a colored dot.
sealed class VideoChip:Control {
    public readonly string Url;
    bool hover,selected;
    public bool Ghost;
    public bool Selected{get{return selected;}set{selected=value;Invalidate();}}
    public VideoChip(string text,string url){
        Text=text;Url=url;
        DoubleBuffered=true;Cursor=Cursors.Hand;Font=new Font("Segoe UI",9,FontStyle.Bold);
        Height=34;Margin=new Padding(0,0,8,0);BackColor=Palette.Panel;
        Width=LunarText.MeasureText(text,Font).Width+(Dot.HasValue?44:28);
    }
    Color? Dot{
        get{
            switch((Text??"").Trim().ToLowerInvariant()){
                case "red":return Color.FromArgb(232,84,92);case "blue":return Color.FromArgb(84,150,240);
                case "yellow":return Color.FromArgb(245,206,80);case "green":return Color.FromArgb(90,200,120);
                case "purple":return Color.FromArgb(170,120,240);case "pink":return Color.FromArgb(240,120,180);
                case "orange":return Color.FromArgb(245,150,70);case "white":return Color.FromArgb(235,235,240);
                case "black":return Color.FromArgb(60,60,66);case "gold":return Color.FromArgb(222,180,90);
                case "teal":return Color.FromArgb(70,200,190);case "silver":return Color.FromArgb(190,195,205);
                default:return null;
            }
        }
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Palette.Panel:Parent.BackColor);
        var r=new Rectangle(0,0,Width-1,Height-1);
        if(!Ghost){
            using(var shape=Skin.Round(r,Height/2))
            using(var fill=new SolidBrush(selected?Palette.AccentSoft:hover?Palette.Border:Palette.Raised))
            using(var edge=new Pen(selected?Palette.Accent:Color.FromArgb(hover?90:40,Palette.Accent))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        }
        int x=14;
        var dot=Dot;
        if(dot.HasValue){
            using(var b=new SolidBrush(dot.Value))g.FillEllipse(b,x,Height/2-5,10,10);
            using(var p=new Pen(Color.FromArgb(70,255,255,255)))g.DrawEllipse(p,x,Height/2-5,10,10);
            x+=16;
        }
        var color=Ghost?(hover?Palette.Text:Palette.Muted):selected?Color.White:hover?Palette.Text:Palette.Muted;
        LunarText.DrawText(g,Text,Font,new Rectangle(x,0,Width-x-8,Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
    }
}

sealed class CloseGlyphButton:Control {
    bool hover;
    public CloseGlyphButton(){DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleName="Close";}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Parent==null?Palette.Panel:Parent.BackColor);
        using(var shape=Skin.Round(new Rectangle(0,0,Width-1,Height-1),10))
        using(var fill=new SolidBrush(hover?Palette.AccentSoft:Palette.Raised))
        using(var edge=new Pen(hover?Palette.Accent:Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        float c=Width/2f,r=5f;
        using(var pen=new Pen(hover?Color.White:Palette.Text,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round}){
            g.DrawLine(pen,c-r,Height/2f-r,c+r,Height/2f+r);g.DrawLine(pen,c+r,Height/2f-r,c-r,Height/2f+r);
        }
    }
}
}
