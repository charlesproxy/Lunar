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
    static readonly object Catalog=Load();

    static object Load(){
        using(var stream=typeof(StoreRarity).Assembly.GetManifestResourceStream("store-tiers.json"))
        using(var reader=new StreamReader(stream))
            return Json.Codec.DeserializeObject(reader.ReadToEnd());
    }
    public static long Rank(string offerId){return HistoryLogic.N(Json.Get(Catalog,"levels"),offerId,-1);}
    public static string Name(string offerId){
        return Json.S(Json.Get(Catalog,"tiers"),Rank(offerId).ToString(CultureInfo.InvariantCulture));
    }
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
                g.Clear(Color.FromArgb(28,22,41));
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
        clientOpen=HasProcess("RiotClientServices");
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
            if(error.Code==429)throw new InvalidOperationException("Riot rate limit reached. Try again in a minute.");
            if(error.Code==401||error.Code==403)throw new InvalidOperationException("Riot rejected the store session. Check that you are signed in to VALORANT and try again.");
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
    public void LoadIfNeeded(){
        if(!Visible||worker.IsBusy||DateTime.UtcNow<nextRequest)return;
        if(canvas.Data==null)canvas.ShowStatus("Loading your daily offers…");
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
        using(var path=Skin.Round(new Rectangle(3,ThumbTop,7,Thumb),3))
        using(var brush=new SolidBrush(Color.FromArgb(128,95,163)))e.Graphics.FillPath(brush,path);
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
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)dragOffset=-1;base.OnMouseCaptureChanged(e);}
}
sealed class StoreCanvas:Control {
    public static readonly Color Background=Color.FromArgb(13,10,20);
    static readonly Color Muted=Color.FromArgb(175,165,193);
    static readonly Color Panel=Color.FromArgb(23,17,34);
    static readonly Color Outline=Color.FromArgb(57,43,76);
    static readonly Color Violet=Color.FromArgb(201,159,255);
    readonly Dictionary<StoreOffer,Image> images=new Dictionary<StoreOffer,Image>();
    readonly Dictionary<string,Image> currencyIcons=new Dictionary<string,Image>{
        {"VP",TrackArt.Asset("currency-vp.png")},
        {"RAD",TrackArt.Asset("currency-rad.png")},
        {"KC",TrackArt.Asset("currency-kc.png")}
    };
    public StoreSnapshot Data{get;private set;}
    string status="Loading your daily offers…";
    const int MaxWidth=1260,Gap=16,TileHeight=203,BundleHeight=116;
    StoreOffer[] Offers{get{return Data==null||Data.Offers==null?new StoreOffer[0]:Data.Offers;}}
    StoreOffer[] Bundles{get{return Data==null||Data.Bundles==null?new StoreOffer[0]:Data.Bundles;}}
    bool HasWallet{get{return Data!=null&&!Data.IsSaved&&Data.Balances!=null&&Data.Balances.Length>0;}}
    bool Stacked{get{return Width<685;}}
    int RotationTop{get{return Stacked?111:98;}}
    int OffersTop{get{return RotationTop+(Stacked&&HasWallet?203:105);}}
    int ContentWidth{get{return Math.Min(MaxWidth,Width-52);}}
    Rectangle RotationBounds{
        get{
            int width=ContentWidth;
            return new Rectangle((Width-width)/2,RotationTop,HasWallet&&!Stacked?Math.Max(254,width*37/100):width,85);
        }
    }
    public void RefreshCountdown(){
        if(Data!=null&&Data.ResetsAt.HasValue&&Width>=245)Invalidate(new Rectangle(RotationBounds.Location, new Size(RotationBounds.Width+1,RotationBounds.Height+1)));
    }
    public StoreCanvas(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Background;}
    public void ShowStatus(string message){status=message;Invalidate();}
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
        int bundleHeight=Bundles.Length==0?0:56+Bundles.Length*(BundleHeight+12)-12;
        return OffersTop+50+offerHeight+bundleHeight+(Data.NightMarket?63:30);
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
        ProfileCanvas.T(g,"Daily Store",25,Palette.Text,new Rectangle(left,23,w,48),true);
        string subtitle=Data!=null&&Data.IsSaved?"Saved "+Data.SavedAt.ToLocalTime().ToString("MMM d · HH:mm",CultureInfo.InvariantCulture)
            :"Your VALORANT skin rotation";
        ProfileCanvas.T(g,subtitle,10,Muted,new Rectangle(left,66,w,27));
        if(Data==null){
            var empty=new Rectangle(left,120,w,215);
            Card(g,empty,16,Panel,Outline);
            var icon=new Rectangle(empty.X+empty.Width/2-23,empty.Y+32,46,46);
            Card(g,icon,13,Color.FromArgb(42,29,61),Outline);
            LunarIcons.Draw(g,LunarGlyph.Shop,new Point(icon.X+23,icon.Y+23),Violet,23);
            Center(g,"Daily offers",14,Palette.Text,new Rectangle(empty.X+16,empty.Y+88,empty.Width-32,30),true);
            Center(g,status,10,Muted,new Rectangle(empty.X+32,empty.Y+125,empty.Width-64,67),false);
            return;
        }
        int split=rotation.Width;
        DrawRotation(g,rotation);
        if(HasWallet){
            var wallet=Stacked?new Rectangle(left,RotationTop+98,w,85):
                new Rectangle(left+split+Gap,RotationTop,w-split-Gap,85);
            DrawWallet(g,wallet);
        }
        ProfileCanvas.T(g,"Daily offers",14,Palette.Text,new Rectangle(left,OffersTop,w-95,31),true);
        using(var font=new Font("Segoe UI",9))
            TextRenderer.DrawText(g,Offers.Length+" offers",font,new Rectangle(left+w-94,OffersTop,94,31),Muted,
                TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        int top=OffersTop+43;
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
            ProfileCanvas.T(g,"Featured bundles",14,Palette.Text,new Rectangle(left,top+20,w,31),true);
            top+=56;
            foreach(var bundle in Bundles){
                DrawBundle(g,bundle,new Rectangle(left,top,w,BundleHeight));
                top+=BundleHeight+12;
            }
            top-=12;
        }
        if(Data.NightMarket)ProfileCanvas.T(g,"Night Market is open",10,Violet,new Rectangle(left,top+15,w,28),true);
    }
    void DrawRotation(Graphics g,Rectangle rect){
        Card(g,rect,14,Color.FromArgb(34,23,51),Color.FromArgb(79,53,109));
        var clock=new Rectangle(rect.X+13,rect.Y+21,39,39);
        Card(g,clock,11,Color.FromArgb(52,34,76),Color.FromArgb(91,66,120));
        using(var pen=new Pen(Violet,1.5f)){g.DrawEllipse(pen,clock.X+10,clock.Y+10,19,19);g.DrawLines(pen,new[]{
            new Point(clock.X+19,clock.Y+14),new Point(clock.X+19,clock.Y+20),new Point(clock.X+24,clock.Y+23)});}
        bool expired=Data.SavedRotationExpired(DateTime.UtcNow);
        var remaining=Data.ResetsAt.HasValue?Data.ResetsAt.Value-DateTime.UtcNow:TimeSpan.Zero;
        string label=Data.IsSaved?(expired?"SAVED ROTATION · EXPIRED":"SAVED ROTATION"):"NEXT ROTATION";
        string value=!Data.ResetsAt.HasValue?"Time unavailable":remaining<=TimeSpan.Zero?
            (Data.IsSaved?"Rotation ended":"Updating rotation…"):
            ((int)remaining.TotalHours).ToString("00")+":"+remaining.Minutes.ToString("00")+":"+remaining.Seconds.ToString("00");
        ProfileCanvas.T(g,label,8,expired?Color.FromArgb(240,185,111):Violet,new Rectangle(rect.X+63,rect.Y+11,rect.Width-76,24),true);
        ProfileCanvas.T(g,value,17,Palette.Text,new Rectangle(rect.X+63,rect.Y+34,rect.Width-76,35),true);
        if(rect.Width>=445&&Data.ResetsAt.HasValue&&remaining>TimeSpan.Zero){
            string local=Data.ResetsAt.Value.ToLocalTime().ToString("MMM d · HH:mm",CultureInfo.InvariantCulture);
            using(var font=new Font("Segoe UI",9))
                TextRenderer.DrawText(g,local,font,new Rectangle(rect.Right-161,rect.Y+38,145,24),Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
        }
    }
    void DrawWallet(Graphics g,Rectangle rect){
        Card(g,rect,14,Panel,Outline);
        ProfileCanvas.T(g,"BALANCE",8,Muted,new Rectangle(rect.X+16,rect.Y+7,rect.Width-32,22),true);
        int width=(rect.Width-24)/Data.Balances.Length;
        for(int i=0;i<Data.Balances.Length;i++){
            var balance=Data.Balances[i];
            int x=rect.X+12+i*width;
            string code=balance.Code;
            Image icon;
            if(currencyIcons.TryGetValue(code,out icon)&&icon!=null)Contain(g,icon,new Rectangle(x+6,rect.Y+41,20,20));
            string amount=balance.Amount.ToString("N0",CultureInfo.InvariantCulture);
            ProfileCanvas.T(g,amount,12,Palette.Text,new Rectangle(x+32,rect.Y+31,width-35,25),true);
            ProfileCanvas.T(g,code,8,Muted,new Rectangle(x+32,rect.Y+53,width-35,20));
        }
    }
    void DrawOffer(Graphics g,StoreOffer offer,Rectangle rect,bool featured){
        var border=featured?Color.FromArgb(130,87,179):Outline;
        Card(g,rect,15,Panel,border);
        var art=new Rectangle(rect.X+1,rect.Y+1,rect.Width-2,rect.Height-58);
        var saved=g.Save();
        using(var clip=Skin.Round(Rectangle.Inflate(rect,-1,-1),14))
        using(var bloom=new LinearGradientBrush(art,featured?Color.FromArgb(53,35,77):Color.FromArgb(41,29,59),Panel,95f)){
            g.SetClip(clip,CombineMode.Intersect);
            g.FillRectangle(bloom,art);
        }
        g.Restore(saved);
        string tier=StoreRarity.Name(offer.Id);
        if(tier!=""){
            string label=featured?tier+"  ·  Featured":tier;
            ProfileCanvas.T(g,label,8,featured?Violet:Muted,new Rectangle(rect.X+13,rect.Y+8,rect.Width-26,21),true);
        }
        Image picture;
        if(images.TryGetValue(offer,out picture))Contain(g,picture,new Rectangle(art.X+22,art.Y+32,art.Width-44,art.Height-42));
        else LunarIcons.Draw(g,LunarGlyph.Shop,new Point(art.X+art.Width/2,art.Y+art.Height/2),Muted,34);
        using(var pen=new Pen(border))g.DrawLine(pen,rect.X+13,art.Bottom,rect.Right-13,art.Bottom);
        int priceWidth=84;
        ProfileCanvas.T(g,offer.Name,11,Palette.Text,new Rectangle(rect.X+14,art.Bottom,rect.Width-priceWidth-33,56),true);
        var price=new Rectangle(rect.Right-priceWidth-12,art.Bottom+13,priceWidth,30);
        Card(g,price,8,Color.FromArgb(36,27,51),Outline);
        if(offer.Price>=0){
            var icon=currencyIcons["VP"];
            if(icon!=null)Contain(g,icon,new Rectangle(price.X+7,price.Y+7,16,16));
            ProfileCanvas.T(g,offer.Price.ToString("N0",CultureInfo.InvariantCulture),10,Violet,
                new Rectangle(price.X+26,price.Y,price.Width-30,30),true);
        }else Center(g,"—",10,Muted,price,false);
    }
    void DrawBundle(Graphics g,StoreOffer bundle,Rectangle rect){
        Card(g,rect,14,Panel,Outline);
        int pictureWidth=Math.Min(310,rect.Width*42/100);
        var art=new Rectangle(rect.X+6,rect.Y+6,pictureWidth,rect.Height-12);
        Card(g,art,9,Color.FromArgb(36,27,51),Outline);
        Image picture;
        if(images.TryGetValue(bundle,out picture)){
            var state=g.Save();
            using(var shape=Skin.Round(Rectangle.Inflate(art,-1,-1),9))g.SetClip(shape,CombineMode.Intersect);
            TrackArt.Cover(g,picture,Rectangle.Inflate(art,-1,-1));
            g.Restore(state);
        }else LunarIcons.Draw(g,LunarGlyph.Shop,new Point(art.X+art.Width/2,art.Y+art.Height/2),Muted,34);
        int x=art.Right+18;
        ProfileCanvas.T(g,bundle.Name,14,Palette.Text,new Rectangle(x,rect.Y+21,rect.Right-x-15,39),true);
        if(bundle.Price>0)ProfileCanvas.T(g,bundle.Price.ToString("N0",CultureInfo.InvariantCulture)+" VP",10,Violet,
            new Rectangle(x,rect.Y+62,rect.Right-x-15,26),true);
    }
    static void Center(Graphics g,string text,int size,Color color,Rectangle rect,bool bold){
        using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))
            TextRenderer.DrawText(g,text,font,rect,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
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
        if(disposing){
            ReleaseArt();
            foreach(var icon in currencyIcons.Values)if(icon!=null)icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
}
