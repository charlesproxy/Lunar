using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace Lunar {
sealed class FriendsSnapshot {
    public Guid Account;
    public Player[] Players=new Player[0];
    public string Error="";
}

static class FriendCardCache {
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lunar","friend-cards");
    static string CardPath(Guid account,string player){
        Guid subject;
        return account!=Guid.Empty&&Guid.TryParse(player,out subject)&&subject!=Guid.Empty
            ?Path.Combine(Root,account.ToString(),subject+".txt"):null;
    }
    public static string Get(Guid account,string player){
        string path=CardPath(account,player);
        if(path==null)return "";
        try{
            var info=new FileInfo(path);
            if(info.Exists&&info.Length<=40&&info.LastWriteTimeUtc>DateTime.UtcNow.AddDays(-1))
                return RiotStatsClient.CardId(File.ReadAllText(path));
        }catch(IOException){}catch(UnauthorizedAccessException){}
        return "";
    }
    public static void Remember(Guid account,string player,string card){
        string path=CardPath(account,player);
        if(path!=null&&RiotStatsClient.CardId(card)!=""&&Get(account,player)!=card)
            Write(path,System.Text.Encoding.ASCII.GetBytes(card));
    }
    public static Bitmap Thumbnail(string card){
        card=RiotStatsClient.CardId(card);
        if(card=="")return null;
        string path=Path.Combine(Root,"icons",card+".png");
        Bitmap art=null;
        try{
            var info=new FileInfo(path);
            if(info.Exists&&info.Length<=65536)art=DecodeThumbnail(File.ReadAllBytes(path));
        }catch(IOException){}catch(UnauthorizedAccessException){}
        if(art!=null)return art;
        art=DecodeThumbnail(RiotStatsClient.LoadCard(card,true));
        if(art!=null){
            using(var buffer=new MemoryStream()){
                art.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);
                Write(path,buffer.ToArray());
            }
        }
        return art;
    }
    static Bitmap DecodeThumbnail(byte[] bytes){
        if(bytes==null)return null;
        using(var stream=new MemoryStream(bytes))
        using(var decoded=TrackArt.Decode(stream)){
            if(decoded==null)return null;
            var thumbnail=new Bitmap(64,64,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(var canvas=Graphics.FromImage(thumbnail))TrackArt.Cover(canvas,decoded,new Rectangle(0,0,64,64));
            return thumbnail;
        }
    }
    static void Write(string path,byte[] bytes){
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(temp,bytes);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }catch(IOException){}catch(UnauthorizedAccessException){}
        finally{
            try{File.Delete(temp);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}

partial class RiotReader {
    internal FriendsSnapshot ReadFriends(){
        const string signIn="Open Riot Client and sign in to see your friends.";
        const string chatPending="Open VALORANT to the main menu to load Riot chat.";
        try{
            if(!ConnectLocalClient())return new FriendsSnapshot{Error=signIn};
            Guid account=ReadChatAccount();
            if(account==Guid.Empty)return new FriendsSnapshot{Error=chatPending};
            var players=ParseFriends(Request(origin+"/chat/v4/friends","GET",null,true,false),account);
            object presences=null;
            try{presences=Request(origin+"/chat/v4/presences","GET",null,true,false,2200);}
            catch(ApiFailure){}catch(WebException){}catch(IOException){}catch(InvalidOperationException){}
            if(presences!=null)ApplyFriendPresence(players,presences);
            if(!ConnectLocalClient()||ReadChatAccount()!=account)
                return new FriendsSnapshot{Error="Riot account changed. Checking again…"};
            foreach(var player in players){
                if(RiotStatsClient.CardId(player.CardId)=="")player.CardId=FriendCardCache.Get(account,player.Id);
                else FriendCardCache.Remember(account,player.Id,player.CardId);
            }
            return new FriendsSnapshot{Account=account,Players=players};
        }catch(FileNotFoundException){return new FriendsSnapshot{Error=signIn};}
         catch(DirectoryNotFoundException){return new FriendsSnapshot{Error=signIn};}
         catch(ApiFailure error){
            return new FriendsSnapshot{Error=error.Code==401||error.Code==403||error.Code==404?chatPending:
                "Riot Client could not load friends (HTTP "+error.Code+")."};
         }
         catch(WebException){return new FriendsSnapshot{Error=chatPending};}
         catch(IOException){return new FriendsSnapshot{Error=chatPending};}
         catch(UnauthorizedAccessException){return new FriendsSnapshot{Error=signIn};}
         catch(InvalidOperationException error){return new FriendsSnapshot{Error=error.Message};}
        finally{basic=access=entitlement=null;}
    }
    Guid ReadChatAccount(){
        var chat=Request(origin+"/chat/v1/session","GET",null,true,false);
        Guid account;
        return Json.B(chat,"loaded")&&Guid.TryParse(Json.S(chat,"puuid"),out account)?account:Guid.Empty;
    }
    internal static Player[] ParseFriends(object payload,Guid account){
        var rows=Json.Get(payload,"friends") as object[];
        if(rows==null)throw new InvalidOperationException("Riot Client did not return a friend list.");
        var friends=new List<Player>();
        var ids=new HashSet<Guid>();
        foreach(var row in rows){
            Guid id;
            if(!Guid.TryParse(Json.S(row,"puuid"),out id)||id==Guid.Empty||id==account||!ids.Add(id))continue;
            string name=Json.S(row,"game_name").Trim(),tag=Json.S(row,"game_tag").Trim().TrimStart('#');
            if(name=="")name=Json.S(row,"gameName").Trim();
            if(tag=="")tag=Json.S(row,"tagLine").Trim().TrimStart('#');
            string card=RiotStatsClient.CardId(Json.Get(row,"playerCardId"));
            if(card=="")card=RiotStatsClient.CardId(Json.Get(row,"player_card_id"));
            if(card=="")card=RiotStatsClient.CardId(Json.Get(row,"card"));
            friends.Add(new Player{Id=id.ToString(),Name=name==""?"Riot friend":name+(tag==""?"":"#"+tag),
                CardId=card,State="Status unavailable"});
        }
        return friends.OrderBy(player=>player.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    internal static void ApplyFriendPresence(Player[] friends,object payload){
        var rows=Json.Get(payload,"presences") as object[];
        if(rows==null)return;
        var byId=rows.Where(row=>Json.S(row,"puuid")!="").ToLookup(row=>Json.S(row,"puuid"),StringComparer.OrdinalIgnoreCase);
        foreach(var friend in friends){
            var entries=byId[friend.Id].OrderByDescending(row=>HistoryLogic.N(row,"time")).ToArray();
            var valorant=entries.FirstOrDefault(row=>String.Equals(Json.S(row,"product"),"valorant",StringComparison.OrdinalIgnoreCase));
            var data=RiotPresenceData(valorant);
            string card=RiotStatsClient.CardId(Json.Get(data,"playerCardId"));
            if(card=="")card=RiotStatsClient.CardId(Json.Get(data,"player_card_id"));
            if(card=="")card=RiotStatsClient.CardId(Json.Get(valorant,"playerCardId"));
            if(card!="")friend.CardId=card;
            var available=entries.FirstOrDefault(row=>{
                string state=Json.S(row,"state").ToLowerInvariant();
                return state=="chat"||state=="online"||state=="dnd"||state=="away"||state=="xa";
            });
            if(available==null){friend.State="Offline";continue;}
            string phase=Json.S(data,"sessionLoopState");
            string presence=Json.S(available,"state").ToLowerInvariant();
            string valState=Json.S(valorant,"state").ToLowerInvariant();
            bool gameOnline=valState!=""&&valState!="offline"&&valState!="mobile";
            if(gameOnline&&phase=="INGAME"){
                string map=HistoryLogic.MapName(Json.S(data,"matchMap"));
                friend.State=map=="Map unavailable"?"In match":"In match · "+map;
            }else if(gameOnline&&phase=="PREGAME")friend.State="Agent select";
            else if(presence=="away"||presence=="xa")friend.State="Away";
            else friend.State="Online";
        }
    }
}

sealed class PlayerRows:Control {
    readonly int height;
    Player[] items=new Player[0];
    int scroll,selected=-1,hover=-1,wheelRemainder;
    public bool OpenOnClick;
    public event Action<Graphics,Player,Rectangle,bool,bool> DrawRow;
    public event Action<Player> OpenPlayer;
    public event Action ViewChanged;
    public Player Selected{get{return selected>=0&&selected<items.Length?items[selected]:null;}}
    public IEnumerable<Player> OnScreen{
        get{return items.Skip(scroll/height).Take(Height/height+2);}
    }
    public PlayerRows(int rowHeight){
        height=rowHeight;
        DoubleBuffered=true;
        ResizeRedraw=true;
        TabStop=true;
        Cursor=Cursors.Hand;
        AccessibleRole=AccessibleRole.List;
    }
    public void SetRows(Player[] entries,bool keepScroll){
        string id=keepScroll&&Selected!=null?Selected.Id:null;
        items=entries;
        selected=id==null?-1:Array.FindIndex(items,player=>player.Id==id);
        if(!keepScroll)scroll=0;
        hover=-1;
        ScrollTo(scroll);
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        ScrollTo(scroll);
    }
    void ScrollTo(int next){
        scroll=Math.Max(0,Math.Min(next,items.Length*height-Height));
        Invalidate();
        if(ViewChanged!=null)ViewChanged();
    }
    int Hit(int y){int i=(scroll+y)/height;return i>=0&&i<items.Length?i:-1;}
    void ActivateRow(){
        if(Selected!=null&&OpenPlayer!=null)OpenPlayer(Selected);
    }
    protected override void OnMouseWheel(MouseEventArgs e){
        wheelRemainder+=e.Delta;
        int ticks=wheelRemainder/SystemInformation.MouseWheelScrollDelta;
        wheelRemainder%=SystemInformation.MouseWheelScrollDelta;
        int lines=SystemInformation.MouseWheelScrollLines;
        ScrollTo(scroll-ticks*(lines<0?Height:lines*height));
        hover=Hit(e.Y);
    }
    protected override void OnMouseMove(MouseEventArgs e){
        int next=Hit(e.Y);
        if(hover!=next){hover=next;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseClick(MouseEventArgs e){
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left)return;
        Focus();
        selected=Hit(e.Y);
        Invalidate();
        if(ViewChanged!=null)ViewChanged();
        if(OpenOnClick)ActivateRow();
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e){
        base.OnMouseDoubleClick(e);
        if(!OpenOnClick&&e.Button==MouseButtons.Left)ActivateRow();
    }
    protected override bool IsInputKey(Keys data){
        switch(data){case Keys.Up:case Keys.Down:case Keys.PageUp:case Keys.PageDown:case Keys.Home:case Keys.End:return true;}
        return base.IsInputKey(data);
    }
    protected override void OnKeyDown(KeyEventArgs e){
        int next;
        switch(e.KeyCode){
            case Keys.Up:next=selected-1;break;
            case Keys.Down:next=selected+1;break;
            case Keys.PageUp:next=selected-Math.Max(1,Height/height);break;
            case Keys.PageDown:next=selected+Math.Max(1,Height/height);break;
            case Keys.Home:next=0;break;
            case Keys.End:next=items.Length-1;break;
            case Keys.Enter:case Keys.Space:ActivateRow();e.SuppressKeyPress=true;return;
            default:base.OnKeyDown(e);return;
        }
        e.SuppressKeyPress=true;
        if(items.Length==0)return;
        selected=Math.Max(0,Math.Min(next,items.Length-1));
        int top=selected*height;
        ScrollTo(Math.Min(top,Math.Max(scroll,top+height-Height)));
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(DrawRow==null)return;
        for(int i=scroll/height;i<items.Length&&i*height-scroll<Height;i++)
            DrawRow(e.Graphics,items[i],new Rectangle(0,i*height-scroll,Width,height),i==selected,i==hover);
    }
}

sealed class FriendsView:UserControl {
    const int ParallelCardLookups=4,ParallelIconDownloads=6;
    readonly TextBox filter=new RiotIdInput("Find a Riot friend");
    readonly PlayerRows list=new PlayerRows(54);
    static readonly Color FriendCardSurface=Palette.Panel;
    readonly SoftButton profile=new SoftButton{SurfaceColor=FriendCardSurface,Text="View profile",FlatStyle=FlatStyle.Flat,BackColor=Palette.Raised,
        ForeColor=Palette.Text,Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter};
    readonly BackgroundWorker worker=new BackgroundWorker();
    readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer{Interval=8000};
    readonly Dictionary<string,string> knownCards=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Image> icons=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,DateTime> failedIcons=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> loadingIcons=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Image> banners=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,DateTime> cardRetry=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,DateTime> bannerRetry=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> loadingBanners=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,CancellationTokenSource> cardLookups=new Dictionary<string,CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
    DateTime cardQueuePausedUntil;
    bool fillingCardQueue;
    readonly bool preview;
    FriendsSnapshot snapshot;
    Guid cardAccount;
    int generation;
    Rectangle rail,detail,friendCard;
    string message="Loading friends…";

    sealed class Result {
        public int Generation;
        public FriendsSnapshot Snapshot;
    }
    public event Action<Player> OpenPlayer;

    public FriendsView(bool preview){
        this.preview=preview;
        BackColor=Palette.Bg;
        DoubleBuffered=true;
        ResizeRedraw=true;
        filter.BorderStyle=BorderStyle.None;
        filter.BackColor=SearchField.Fill;
        filter.ForeColor=Palette.Text;
        filter.Font=new Font("Segoe UI",10);
        filter.MaxLength=70;
        filter.AccessibleName="Filter friends";
        filter.TextChanged+=delegate{UpdateList(false);};
        list.BackColor=Palette.Panel;
        list.AccessibleName="Riot friends";
        list.DrawRow+=DrawFriend;
        list.ViewChanged+=UpdateSelection;
        list.OpenPlayer+=Open;
        list.Visible=false;
        profile.FlatAppearance.BorderSize=0;
        profile.Click+=delegate{Open(list.Selected);};
        profile.Visible=false;
        Controls.AddRange(new Control[]{filter,list,profile});
        worker.DoWork+=delegate(object sender,DoWorkEventArgs e){
            e.Result=new Result{Generation=(int)e.Argument,Snapshot=new RiotReader().ReadFriends()};
        };
        worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){
            if(IsDisposed||Disposing||!Visible)return;
            if(e.Error!=null){ShowSnapshot(new FriendsSnapshot{Error="Could not load friends. Retrying…"});return;}
            var result=(Result)e.Result;
            if(result.Generation!=generation){RequestFriends();return;}
            ShowSnapshot(result.Snapshot);
        };
        refresh.Tick+=delegate{RequestFriends();LoadFriendCards();};
        VisibleChanged+=delegate{
            if(!Visible){refresh.Stop();CancelCardLookups();return;}
            if(preview)return;
            generation++;
            snapshot=null;
            message="Loading friends…";
            filter.Clear();
            UpdateList(false);
            RequestFriends();
            refresh.Start();
        };
    }
    void RequestFriends(){if(Visible&&!worker.IsBusy)worker.RunWorkerAsync(generation);}
    internal void ShowSnapshot(FriendsSnapshot value){
        bool sameAccount=snapshot!=null&&snapshot.Account==value.Account;
        if(value.Account!=Guid.Empty&&value.Account!=cardAccount){
            CancelCardLookups();
            cardAccount=value.Account;
            knownCards.Clear();
            cardRetry.Clear();
        }
        foreach(var player in value.Players){
            Guid id;
            if(Guid.TryParse(player.CardId,out id)&&id!=Guid.Empty)knownCards[player.Id]=id.ToString();
            else{
                string known;
                if(knownCards.TryGetValue(player.Id,out known))player.CardId=known;
            }
        }
        if(sameAccount&&snapshot.Error==value.Error&&snapshot.Players.Length==value.Players.Length&&
            snapshot.Players.Zip(value.Players,(a,b)=>a.Id==b.Id&&a.Name==b.Name&&a.CardId==b.CardId&&a.State==b.State).All(equal=>equal)){
            LoadFriendIcons();
            LoadFriendCards();
            return;
        }
        snapshot=value;
        if(value.Account!=Guid.Empty){
            var currentCards=new HashSet<string>(value.Players.Select(player=>player.CardId),StringComparer.OrdinalIgnoreCase);
            foreach(string previousCard in icons.Keys.Where(id=>!currentCards.Contains(id)).ToArray()){
                icons[previousCard].Dispose();
                icons.Remove(previousCard);
            }
        }
        message=value.Error;
        if(!sameAccount)filter.Clear();
        UpdateList(sameAccount);
    }
    void UpdateList(bool keepScroll){
        var players=snapshot==null?new Player[0]:snapshot.Players;
        string query=filter.Text.Trim();
        var visible=players.Where(player=>player.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)
            .OrderBy(player=>PresenceColor(player.State)==Palette.Muted?1:0).ToArray();
        list.SetRows(visible,keepScroll);
        list.Visible=visible.Length>0;
        UpdateSelection();
    }
    void UpdateSelection(){
        if(IsDisposed||Disposing)return;
        profile.Visible=list.Selected!=null&&RiotStatsClient.HasRiotId(list.Selected.Name);
        Invalidate();
        LoadFriendIcons();
        LoadSelectedBanner();
        LoadFriendCards();
    }
    void CancelCardLookups(){
        var requests=cardLookups.Values.ToArray();
        cardLookups.Clear();
        foreach(var request in requests)request.Cancel();
    }
    void LoadSelectedBanner(){
        if(preview||!Visible||IsDisposed||Disposing)return;
        var player=list.Selected;
        if(player==null)return;
        string id=RiotStatsClient.CardId(player.CardId);
        DateTime retry;
        if(id!=""&&!banners.ContainsKey(id)&&(!bannerRetry.TryGetValue(id,out retry)||retry<=DateTime.UtcNow)
            &&loadingBanners.Count<3&&loadingBanners.Add(id))ReadBanner(id);
    }
    void LoadFriendCards(){
        if(preview||!Visible||IsDisposed||Disposing||snapshot==null||snapshot.Account==Guid.Empty||fillingCardQueue)return;
        fillingCardQueue=true;
        try{
            while(cardLookups.Count<ParallelCardLookups&&DateTime.UtcNow>=cardQueuePausedUntil){
                var player=NextFriendCard(list.Selected,list.OnScreen,snapshot.Players,cardRetry,cardLookups,DateTime.UtcNow);
                if(player==null)break;
                FindFriendCard(player,snapshot.Account);
            }
        }finally{fillingCardQueue=false;}
    }
    static Player NextFriendCard(Player selected,IEnumerable<Player> visible,IEnumerable<Player> all,
        Dictionary<string,DateTime> retries,Dictionary<string,CancellationTokenSource> active,DateTime now){
        return new[]{selected}.Concat(visible).Concat(all).FirstOrDefault(player=>{
            DateTime retry;
            return player!=null&&!active.ContainsKey(player.Id)&&RiotStatsClient.CardId(player.CardId)==""&&
                RiotStatsClient.HasRiotId(player.Name)&&(!retries.TryGetValue(player.Id,out retry)||retry<=now);
        });
    }
    async void FindFriendCard(Player player,Guid account){
        var request=new CancellationTokenSource();
        cardLookups.Add(player.Id,request);
        try{
            var found=await RiotStatsClient.LookupAsync(player.Name,request.Token,true);
            if(request.IsCancellationRequested||IsDisposed||Disposing||account!=cardAccount)return;
            string id=RiotStatsClient.CardId(found.CardId);
            if(id=="")id=await RiotStatsClient.LookupCardIdAsync(found,request.Token).ConfigureAwait(true);
            if(id==""||!String.Equals(player.Id,found.Id,StringComparison.OrdinalIgnoreCase)){
                cardRetry[player.Id]=DateTime.UtcNow.AddMinutes(2);
                return;
            }
            knownCards[player.Id]=id;
            if(snapshot!=null&&snapshot.Account==account)
                foreach(var entry in snapshot.Players.Where(entry=>entry.Id==player.Id&&RiotStatsClient.CardId(entry.CardId)==""))
                    entry.CardId=id;
            LoadFriendIcons();
            await Task.Run(()=>FriendCardCache.Remember(account,player.Id,id));
        }catch(OperationCanceledException){}
         catch(RiotStatsException error){
            if(!request.IsCancellationRequested&&!IsDisposed&&account==cardAccount){
                if(error.Status==429&&error.RetryAfter>cardQueuePausedUntil)cardQueuePausedUntil=error.RetryAfter;
                else if(error.Status==401||error.Status==403||error.Status==0){
                    var retry=DateTime.UtcNow.AddMinutes(2);
                    if(retry>cardQueuePausedUntil)cardQueuePausedUntil=retry;
                }
                cardRetry[player.Id]=error.Status==429?error.RetryAfter:DateTime.UtcNow.AddMinutes(2);
            }
        }catch(InvalidOperationException){
            if(!request.IsCancellationRequested&&!IsDisposed&&account==cardAccount)
                cardRetry[player.Id]=DateTime.UtcNow.AddMinutes(2);
        }catch(ArgumentException){
            if(!request.IsCancellationRequested&&!IsDisposed&&account==cardAccount)
                cardRetry[player.Id]=DateTime.UtcNow.AddMinutes(2);
        }finally{
            CancellationTokenSource active;
            if(cardLookups.TryGetValue(player.Id,out active)&&Object.ReferenceEquals(active,request))cardLookups.Remove(player.Id);
            bool canceled=request.IsCancellationRequested;
            request.Dispose();
            if(!canceled&&!IsDisposed&&!Disposing){
                list.Invalidate();
                Invalidate();
                LoadFriendIcons();
                LoadSelectedBanner();
                LoadFriendCards();
            }
        }
    }
    async void ReadBanner(string id){
        try{
            var picture=await Task.Run(()=>RiotStatsClient.LoadCard(id));
            if(IsDisposed||Disposing)return;
            Image art=null;
            if(picture!=null)using(var stream=new MemoryStream(picture))art=TrackArt.Decode(stream);
            if(art==null){bannerRetry[id]=DateTime.UtcNow.AddMinutes(2);return;}
            if(banners.Count>=16){
                string first=banners.Keys.First();
                banners[first].Dispose();
                banners.Remove(first);
            }
            banners.Add(id,art);
            bannerRetry.Remove(id);
        }catch(IOException){bannerRetry[id]=DateTime.UtcNow.AddMinutes(2);}
         catch(InvalidOperationException){bannerRetry[id]=DateTime.UtcNow.AddMinutes(2);}
        finally{
            loadingBanners.Remove(id);
            if(!IsDisposed&&!Disposing){Invalidate();LoadSelectedBanner();}
        }
    }
    void Open(Player player){
        if(player!=null&&RiotStatsClient.HasRiotId(player.Name)&&OpenPlayer!=null)OpenPlayer(player);
    }
    void LoadFriendIcons(){
        if(preview||!Visible||IsDisposed||Disposing||snapshot==null||snapshot.Account==Guid.Empty||
            loadingIcons.Count>=ParallelIconDownloads)return;
        foreach(var player in list.OnScreen.Concat(new[]{list.Selected}).Concat(snapshot.Players).Where(player=>player!=null)){
            Guid id;
            if(!Guid.TryParse(player.CardId,out id)||id==Guid.Empty||icons.ContainsKey(player.CardId))continue;
            DateTime retry;
            if(failedIcons.TryGetValue(player.CardId,out retry)&&retry>DateTime.UtcNow)continue;
            if(loadingIcons.Add(player.CardId))LoadIcon(player.CardId);
            if(loadingIcons.Count>=ParallelIconDownloads)return;
        }
    }
    async void LoadIcon(string id){
        try{
            var thumbnail=await Task.Run(()=>FriendCardCache.Thumbnail(id));
            if(IsDisposed||Disposing||snapshot==null||!snapshot.Players.Any(player=>
                String.Equals(player.CardId,id,StringComparison.OrdinalIgnoreCase))){
                if(thumbnail!=null)thumbnail.Dispose();
                return;
            }
            if(thumbnail==null){failedIcons[id]=DateTime.UtcNow.AddMinutes(5);return;}
            failedIcons.Remove(id);
            icons.Add(id,thumbnail);
            list.Invalidate();
            Invalidate();
        }catch(IOException){failedIcons[id]=DateTime.UtcNow.AddMinutes(5);}
         catch(InvalidOperationException){failedIcons[id]=DateTime.UtcNow.AddMinutes(5);}
        finally{
            loadingIcons.Remove(id);
            LoadFriendIcons();
        }
    }
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);
        if(ClientSize.Width<300||ClientSize.Height<260)return;
        int left=24,top=88;
        rail=new Rectangle(left,top,Math.Min(352,ClientSize.Width/2-30),ClientSize.Height-top-23);
        detail=new Rectangle(rail.Right+16,top,ClientSize.Width-rail.Right-40,rail.Height);
        filter.SetBounds(rail.X+42,rail.Y+21,rail.Width-60,filter.PreferredHeight);
        list.SetBounds(rail.X+8,rail.Y+60,rail.Width-16,rail.Height-69);
        int cardWidth=Math.Min(608,detail.Width-16);
        friendCard=new Rectangle(detail.X+(detail.Width-cardWidth)/2,detail.Y+Math.Max(12,(detail.Height-292)/2),cardWidth,279);
        profile.SetBounds(friendCard.Right-166,friendCard.Bottom-49,146,32);
    }
    static Color PresenceColor(string state){
        if(state=="Away")return Color.FromArgb(219,174,107);
        if(state=="Agent select"||(state??"").StartsWith("In match",StringComparison.Ordinal))return Color.FromArgb(188,141,252);
        return state=="Online"?Palette.Green:Palette.Muted;
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(rail.Width<=0)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        ProfileCanvas.T(g,"Friends",22,Palette.Text,new Rectangle(rail.X,21,180,46),true);
        if(snapshot!=null&&snapshot.Account!=Guid.Empty)
            ProfileCanvas.T(g,snapshot.Players.Length+" friends",9,Palette.Muted,new Rectangle(rail.X+164,27,rail.Width-164,37));
        using(var path=Skin.Round(rail,13))
        using(var fill=new SolidBrush(list.BackColor))
        using(var outline=new Pen(Palette.Border)){
            g.FillPath(fill,path);
            g.DrawPath(outline,path);
        }
        var search=new Rectangle(rail.X+10,rail.Y+10,rail.Width-20,38);
        using(var path=Skin.Round(search,8))
        using(var fill=new SolidBrush(SearchField.Fill))g.FillPath(fill,path);
        LunarIcons.Draw(g,LunarGlyph.Search,new Point(rail.X+26,rail.Y+28),Palette.Muted,16);
        if(!list.Visible){
            string empty=message!=""?message:snapshot==null||snapshot.Players.Length==0?"No friends on this account.":"No friends match your search.";
            using(var font=new Font("Segoe UI",10))
                TextRenderer.DrawText(g,empty,font,new Rectangle(rail.X+17,rail.Y+75,rail.Width-34,74),Palette.Muted,
                    TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }
        var selected=list.Selected;
        int center=detail.Left+detail.Width/2, y=detail.Top+detail.Height/2-83;
        if(selected!=null){
            DrawFriendCard(g,selected);
        }else{
            using(var fill=new SolidBrush(Palette.AccentSoft))g.FillEllipse(fill,center-26,y,52,52);
            LunarIcons.Draw(g,LunarGlyph.Friends,new Point(center,y+26),Palette.Accent,23);
            Center(g,"Your Riot friends",15,Palette.Text,new Rectangle(detail.X,y+72,detail.Width,35),true);
            Center(g,"Select a friend to see their status and open their profile.",9,Palette.Muted,
                new Rectangle(detail.X+12,y+113,detail.Width-24,47),false);
        }
    }
    void DrawFriendCard(Graphics g,Player player){
        if(friendCard.Width<190)return;
        var banner=new Rectangle(friendCard.X,friendCard.Y,friendCard.Width,159);
        using(var shape=Skin.Round(friendCard,16))
        using(var body=new SolidBrush(FriendCardSurface))
        using(var outline=new Pen(Palette.Border)){
            g.FillPath(body,shape);
            var state=g.Save();
            g.SetClip(shape,CombineMode.Intersect);
            Image art;
            if(!String.IsNullOrEmpty(player.CardId)&&banners.TryGetValue(player.CardId,out art)){
                TrackArt.Cover(g,art,banner);
                using(var shade=new LinearGradientBrush(banner,Color.FromArgb(4,16,10,24),Color.FromArgb(125,14,10,20),90))
                    g.FillRectangle(shade,banner);
            }else{
                using(var gradient=new LinearGradientBrush(banner,Palette.Raised,Palette.Panel,15))
                    g.FillRectangle(gradient,banner);
                bool pending=cardLookups.ContainsKey(player.Id)||(!String.IsNullOrEmpty(player.CardId)&&loadingBanners.Contains(player.CardId));
                ProfileCanvas.T(g,pending?"Loading player card…":"Player card unavailable",9,Palette.Muted,
                    new Rectangle(banner.X+20,banner.Y+17,banner.Width-40,25));
            }
            g.Restore(state);
            g.DrawPath(outline,shape);
        }
        DrawAvatar(g,player,new Rectangle(friendCard.X+22,banner.Bottom-29,57,57));
        ProfileCanvas.T(g,player.Name,17,Palette.Text,new Rectangle(friendCard.X+21,banner.Bottom+34,friendCard.Width-42,34),true);
        ProfileCanvas.T(g,player.State??"Status unavailable",9,PresenceColor(player.State),
            new Rectangle(friendCard.X+21,banner.Bottom+72,friendCard.Width-187,28));
    }
    static void Center(Graphics g,string text,int size,Color color,Rectangle rect,bool bold){
        using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))
            TextRenderer.DrawText(g,text??"",font,rect,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
    void DrawAvatar(Graphics g,Player player,Rectangle avatar){
        Image icon;
        var state=g.Save();
        using(var path=Skin.Round(avatar,Math.Min(12,avatar.Width/4))){
            g.SetClip(path,CombineMode.Intersect);
            using(var fill=new SolidBrush(Palette.Raised))g.FillPath(fill,path);
            if(!String.IsNullOrEmpty(player.CardId)&&icons.TryGetValue(player.CardId,out icon))TrackArt.Cover(g,icon,avatar);
            else LunarIcons.Draw(g,LunarGlyph.Friends,new Point(avatar.X+avatar.Width/2,avatar.Y+avatar.Height/2),
                Palette.Muted,avatar.Width/2);
        }
        g.Restore(state);
        using(var edge=new SolidBrush(list.BackColor))g.FillEllipse(edge,avatar.Right-11,avatar.Bottom-11,12,12);
        using(var dot=new SolidBrush(PresenceColor(player.State)))g.FillEllipse(dot,avatar.Right-9,avatar.Bottom-9,8,8);
    }
    void DrawFriend(Graphics g,Player player,Rectangle bounds,bool selected,bool hover){
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(selected||hover)
            using(var path=Skin.Round(Rectangle.Inflate(bounds,-2,-2),8))
            using(var fill=new SolidBrush(selected?Palette.AccentSoft:Palette.Raised))g.FillPath(fill,path);
        DrawAvatar(g,player,new Rectangle(bounds.X+9,bounds.Y+9,35,35));
        ProfileCanvas.T(g,player.Name,10,Palette.Text,new Rectangle(bounds.X+51,bounds.Y+4,bounds.Width-71,25),true);
        ProfileCanvas.T(g,player.State??"Status unavailable",8,PresenceColor(player.State),
            new Rectangle(bounds.X+51,bounds.Y+27,bounds.Width-68,20));
    }
    protected override void Dispose(bool disposing){
        if(disposing){
            refresh.Dispose();
            worker.Dispose();
            CancelCardLookups();
            foreach(var image in icons.Values.Concat(banners.Values))image.Dispose();
        }
        base.Dispose(disposing);
    }
}
}
