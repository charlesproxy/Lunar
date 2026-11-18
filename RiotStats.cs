using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
namespace Lunar {
sealed class RiotStatisticsSession {internal string Access,Entitlement,Version,Region;}
static class RiotSessionCookie {
    static readonly string PathOnDisk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Lunar","riot-ssid.key");
    public static string Value {get;private set;}
    static RiotSessionCookie(){
        Value=Load(PathOnDisk);

    }
    public static bool IsConfigured {get{return !String.IsNullOrWhiteSpace(Value);}}

    internal static string Load(string path){
        try{
            byte[] encrypted=File.ReadAllBytes(path);
            string configured=Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser)).Trim();
            if(configured.Length>0)return configured;
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        catch(CryptographicException){}

        return (Environment.GetEnvironmentVariable("LUNAR_RIOT_SSID")??"").Trim();
    }

    public static bool Prompt(IWin32Window owner){
        using(var form=new Form())using(var input=new TextBox())using(var save=new Button())using(var cancel=new Button()){
            form.Text="Enable Tracker statistics";form.ClientSize=new Size(440,190);form.FormBorderStyle=FormBorderStyle.FixedDialog;
            form.MaximizeBox=false;form.MinimizeBox=false;form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.CenterParent;
            form.BackColor=Palette.Panel;form.ForeColor=Palette.Text;form.Font=new Font("Segoe UI",9);
            var heading=new Label{Text="Connect Riot session",Left=22,Top=18,Width=390,Height=29,Font=new Font("Segoe UI",14,FontStyle.Bold),ForeColor=Palette.Text};
            var details=new Label{Text="Paste your own Riot ssid cookie. Lunar encrypts it for this Windows account. Renew it here when the session expires.",Left=23,Top=54,Width=392,Height=38,ForeColor=Palette.Muted};
            input.Left=23;input.Top=101;input.Width=392;input.Height=27;input.UseSystemPasswordChar=true;input.BorderStyle=BorderStyle.FixedSingle;
            input.BackColor=Palette.Raised;input.ForeColor=Palette.Text;input.Font=new Font("Segoe UI",10);
            save.Text="Save session";save.Left=302;save.Top=145;save.Width=112;save.Height=30;save.DialogResult=DialogResult.OK;
            save.FlatStyle=FlatStyle.Flat;save.FlatAppearance.BorderSize=0;save.BackColor=Palette.Accent;save.ForeColor=Color.White;
            cancel.Text="Later";cancel.Left=213;cancel.Top=145;cancel.Width=78;cancel.Height=30;cancel.DialogResult=DialogResult.Cancel;
            cancel.FlatStyle=FlatStyle.Flat;cancel.FlatAppearance.BorderSize=0;cancel.BackColor=Palette.Raised;cancel.ForeColor=Palette.Text;
            form.Controls.AddRange(new Control[]{heading,details,input,save,cancel});form.AcceptButton=save;form.CancelButton=cancel;
            if(form.ShowDialog(owner)!=DialogResult.OK)return false;
            var key=input.Text.Trim();
            if(key.Length<8||key.Any(c=>Char.IsControl(c)||c==(char)59)){MessageBox.Show(owner,"Enter a valid Riot ssid cookie.","Riot session required",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}
            try{Save(key);return true;}
            catch(Exception error){MessageBox.Show(owner,"Lunar could not save the key securely. "+error.Message,"Could not save session",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
        }
    }

    internal static void Save(string value){
        var directory=Path.GetDirectoryName(PathOnDisk);
        Directory.CreateDirectory(directory);
        byte[] encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser);
        string temporary=PathOnDisk+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            File.WriteAllBytes(temporary,encrypted);
            if(File.Exists(PathOnDisk))File.Replace(temporary,PathOnDisk,null);
            else File.Move(temporary,PathOnDisk);
            Value=value;
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
sealed class RiotStatsException:Exception {
    public readonly int Status;
    public DateTime RetryAfter;
    public RiotStatsException(int status,string message):base(message){Status=status;}
}
static class RiotStatsClient {
    const string Platform="ew0KCSJwbGF0Zm9ybVR5cGUiOiAiUEMiLA0KCSJwbGF0Zm9ybU9TIjogIldpbmRvd3MiLA0KCSJwbGF0Zm9ybU9TVmVyc2lvbiI6ICIxMC4wLjE5MDQyLjEuMjU2LjY0Yml0IiwNCgkicGxhdGZvcm1DaGlwc2V0IjogIlVua25vd24iDQp9";
    const string Authorize="https://auth.riotgames.com/authorize?redirect_uri=https%3A%2F%2Fplayvalorant.com%2Fopt_in&client_id=play-valorant-web-prod&response_type=token%20id_token&nonce=1&scope=account%20openid";
    internal sealed class Session {
        internal string Access,Entitlement,Version,Region,Shard,Cookie;
        internal DateTime Expires;
        internal string Pd {get{return "https://pd."+Shard+".a.pvp.net";}}
    }
    static readonly SemaphoreSlim sessionGate=new SemaphoreSlim(1,1),networkGate=new SemaphoreSlim(8,8),backgroundGate=new SemaphoreSlim(2,2);
    static volatile Session session;
    static int sessionBuilds,sessionReuses;
    internal static int SessionBuildCount {get{return Volatile.Read(ref sessionBuilds);}}
    internal static int SessionReuseCount {get{return Volatile.Read(ref sessionReuses);}}
    internal static bool CanReuseSession(DateTime expires,string sessionCookie,string configuredCookie,DateTime now){return expires>now&&String.Equals(sessionCookie,configuredCookie,StringComparison.Ordinal);}
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,object> matchCache=new System.Collections.Concurrent.ConcurrentDictionary<string,object>();
    static readonly HashSet<string> oldActs=ReadOldActs();
    static HashSet<string> ReadOldActs(){
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var stream=typeof(RiotStatsClient).Assembly.GetManifestResourceStream("seasons.json")){
            if(stream==null)return result;
            using(var reader=new StreamReader(stream))foreach(var act in Json.A(Json.Get(Json.Codec.DeserializeObject(reader.ReadToEnd()),"data"))){
                string path=Json.S(act,"assetPath");
                if(Enumerable.Range(1,4).Any(ep=>path.Contains("Season_Episode"+ep+"_")))result.Add(Json.S(act,"uuid"));
            }
        }
        return result;
    }
    internal static void ApplyRank(PlayerProfile profile,object mmr,string act){
        HistoryLogic.Rank(profile,mmr,act);profile.PeakTier=-1;
        var seasons=Json.Get(Json.Get(Json.Get(mmr,"QueueSkills"),"competitive"),"SeasonalInfoBySeasonID") as Dictionary<string,object>;
        if(seasons==null)return;
        foreach(var entry in seasons){
            int tier=(int)HistoryLogic.N(entry.Value,"CompetitiveTier",0);
            var wins=Json.Get(entry.Value,"WinsByTier") as Dictionary<string,object>;
            if(wins!=null)foreach(var win in wins){int t;long count;if(Int32.TryParse(win.Key,out t)&&Int64.TryParse(Convert.ToString(win.Value),out count)&&count>0)tier=Math.Max(tier,t);}
            if(tier<3)continue;
            if(oldActs.Contains(entry.Key)&&tier>=21)tier+=3;
            profile.PeakTier=Math.Max(profile.PeakTier,tier);
        }
    }
    static readonly SemaphoreSlim[] matchLocks=Enumerable.Range(0,64).Select(i=>new SemaphoreSlim(1,1)).ToArray();
    static async Task<object> Match(string id,CancellationToken cancel){
        var current=await GetSession(cancel).ConfigureAwait(false);
        string key=current.Shard+":"+id;object data;
        if(matchCache.TryGetValue(key,out data))return data;
        var gate=matchLocks[(key.GetHashCode()&Int32.MaxValue)%matchLocks.Length];
        await gate.WaitAsync(cancel).ConfigureAwait(false);
        try{
        if(matchCache.TryGetValue(key,out data))return data;
        data=await Get("/match-details/v1/matches/"+id,cancel).ConfigureAwait(false);
        if(Json.B(Json.Get(data,"matchInfo"),"isCompleted")){if(matchCache.Count>=300)matchCache.Clear();matchCache[key]=data;}
        return data;
        }finally{gate.Release();}
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> names=new System.Collections.Concurrent.ConcurrentDictionary<string,string>();
    static string CurrentAct(){
        using(var stream=typeof(RiotStatsClient).Assembly.GetManifestResourceStream("seasons.json")){
            if(stream==null)return "";
            using(var reader=new StreamReader(stream))foreach(var act in Json.A(Json.Get(Json.Codec.DeserializeObject(reader.ReadToEnd()),"data"))){
                DateTimeOffset start,end;
                if(Json.S(act,"type").EndsWith("::Act")&&DateTimeOffset.TryParse(Json.S(act,"startTime"),out start)&&DateTimeOffset.TryParse(Json.S(act,"endTime"),out end)&&start<=DateTimeOffset.UtcNow&&end>DateTimeOffset.UtcNow)return Json.S(act,"uuid");
            }
        }
        return "";
    }
    static long blockedUntil;
    internal static DateTime RemoteRetryAt {get{return new DateTime(Interlocked.Read(ref blockedUntil),DateTimeKind.Utc);}}
    internal static int RetrySeconds(DateTime until){return (int)Math.Max(1,Math.Ceiling((until-DateTime.UtcNow).TotalSeconds));}
    internal static string Shard(string region){
        switch((region??"").ToLowerInvariant()){
            case "br":case "latam":case "na":return "na";
            case "eu":return "eu";case "ap":return "ap";case "kr":return "kr";
            default:throw new InvalidOperationException("Unsupported Riot region.");
        }
    }
    internal static Dictionary<string,string> Tokens(string location){
        var result=new Dictionary<string,string>();
        int start=(location??"").IndexOf('#');
        if(start<0)return result;
        foreach(var part in location.Substring(start+1).Split('&')){
            int split=part.IndexOf('=');
            if(split>0)result[Uri.UnescapeDataString(part.Substring(0,split))]=Uri.UnescapeDataString(part.Substring(split+1));
        }
        return result;
    }
    static RiotStatsException Failure(int status,string retry){
        if(status==429){
            int seconds;DateTimeOffset date;
            var until=Int32.TryParse(retry,out seconds)?DateTime.UtcNow.AddSeconds(Math.Max(2,Math.Min(3600,seconds))):
                DateTimeOffset.TryParse(retry,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date)?date.UtcDateTime:DateTime.UtcNow.AddSeconds(60);
            long previous;
            do{previous=Interlocked.Read(ref blockedUntil);if(previous>=until.Ticks)break;}
            while(Interlocked.CompareExchange(ref blockedUntil,until.Ticks,previous)!=previous);
            return new RiotStatsException(429,"Riot rate limit reached. Try again shortly."){RetryAfter=RemoteRetryAt};
        }
        return new RiotStatsException(status,status==401||status==403?"Riot session expired or access denied. Renew your ssid cookie.":status==404?"Player or match unavailable in this Riot region.":"Riot request failed (HTTP "+status+").");
    }
    static async Task<object> Http(string url,string method,object body,Session auth,CancellationToken cancel,CookieContainer cookies=null,bool redirect=false){
        await networkGate.WaitAsync(cancel).ConfigureAwait(false);
        try{
            if(RemoteRetryAt>DateTime.UtcNow)throw new RiotStatsException(429,"Riot requests are temporarily paused."){RetryAfter=RemoteRetryAt};
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.Method=method;request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;
            request.UserAgent="Lunar/1.67";request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            request.ServicePoint.ConnectionLimit=8;
            if(cookies!=null)request.CookieContainer=cookies;
            if(auth!=null){
                request.Headers[HttpRequestHeader.Authorization]="Bearer "+auth.Access;
                if(!String.IsNullOrEmpty(auth.Entitlement)){
                    request.Headers["X-Riot-Entitlements-JWT"]=auth.Entitlement;
                    request.Headers["X-Riot-ClientPlatform"]=Platform;
                    if(!String.IsNullOrEmpty(auth.Version))request.Headers["X-Riot-ClientVersion"]=auth.Version;
                }
            }
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                timeout.CancelAfter(15000);
                using(timeout.Token.Register(request.Abort)){
                    try{
                        if(body!=null){
                            byte[] bytes=Encoding.UTF8.GetBytes(Json.Codec.Serialize(body));request.ContentType="application/json";request.ContentLength=bytes.Length;
                            using(var stream=await request.GetRequestStreamAsync().ConfigureAwait(false))await stream.WriteAsync(bytes,0,bytes.Length,timeout.Token).ConfigureAwait(false);
                        }
                        using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)){
                            if(redirect)return response.Headers["Location"]??"";
                            using(var reader=new StreamReader(response.GetResponseStream()))return Json.Codec.DeserializeObject(await reader.ReadToEndAsync().ConfigureAwait(false));
                        }
                    }catch(WebException error){
                        cancel.ThrowIfCancellationRequested();
                        using(var response=error.Response as HttpWebResponse){
                            if(response!=null)throw Failure((int)response.StatusCode,response.Headers["Retry-After"]);
                            throw new RiotStatsException(0,timeout.IsCancellationRequested?"Riot request timed out.":"Could not connect to Riot.");
                        }
                    }catch(IOException){cancel.ThrowIfCancellationRequested();throw new RiotStatsException(0,"Riot response interrupted. Try again.");}
                }
            }
        }finally{networkGate.Release();}
    }
    static async Task<Session> GetSession(CancellationToken cancel){
        cancel.ThrowIfCancellationRequested();
        var cached=session;
        if(cached!=null&&CanReuseSession(cached.Expires,cached.Cookie,RiotSessionCookie.Value,DateTime.UtcNow)){Interlocked.Increment(ref sessionReuses);return cached;}
        await sessionGate.WaitAsync(cancel).ConfigureAwait(false);
        try{
            if(session!=null&&CanReuseSession(session.Expires,session.Cookie,RiotSessionCookie.Value,DateTime.UtcNow)){Interlocked.Increment(ref sessionReuses);return session;}
            string cookie=RiotSessionCookie.Value;
            if(String.IsNullOrWhiteSpace(cookie)){
                var clientSession=new RiotReader().GetStatisticsSession();
                if(clientSession==null)throw new RiotStatsException(401,"Sign in to Riot Client to search profiles. Lunar will reuse that session automatically.");
                var fromClient=new Session{Access=clientSession.Access,Entitlement=clientSession.Entitlement,Version=clientSession.Version,Region=clientSession.Region,Shard=Shard(clientSession.Region),Cookie="",Expires=DateTime.UtcNow.AddMinutes(5)};
                session=fromClient;Interlocked.Increment(ref sessionBuilds);return fromClient;
            }
            if(cookie.Any(c=>Char.IsControl(c)||c==(char)59))throw new RiotStatsException(401,"Invalid saved Riot session.");
            var jar=new CookieContainer();jar.Add(new Uri("https://auth.riotgames.com"),new Cookie("ssid",cookie,"/"));
            var location=(string)await Http(Authorize,"GET",null,null,cancel,jar,true).ConfigureAwait(false);
            var tokens=Tokens(location);
            if(!tokens.ContainsKey("access_token")||!tokens.ContainsKey("id_token"))throw Failure(401,null);
            var next=new Session{Access=tokens["access_token"],Cookie=cookie};
            var renewed=jar.GetCookies(new Uri("https://auth.riotgames.com"))["ssid"];
            if(renewed!=null&&renewed.Value!=cookie){RiotSessionCookie.Save(renewed.Value);next.Cookie=renewed.Value;}
            var ent=await Http("https://entitlements.auth.riotgames.com/api/token/v1","POST",new {},next,cancel).ConfigureAwait(false);
            next.Entitlement=Json.S(ent,"entitlements_token");
            if(next.Entitlement=="")throw Failure(401,null);
            var geo=await Http("https://riot-geo.pas.si.riotgames.com/pas/v1/product/valorant","PUT",new {id_token=tokens["id_token"]},next,cancel).ConfigureAwait(false);
            next.Region=Json.S(Json.Get(geo,"affinities"),"live");next.Shard=Shard(next.Region);
            var version=await Http("https://valorant-api.com/v1/version","GET",null,null,cancel).ConfigureAwait(false);
            next.Version=Json.S(Json.Get(version,"data"),"riotClientVersion");
            if(next.Version=="")throw new InvalidOperationException("Riot client version unavailable.");
            int expires;next.Expires=DateTime.UtcNow.AddSeconds(tokens.ContainsKey("expires_in")&&Int32.TryParse(tokens["expires_in"],out expires)?Math.Max(1,Math.Min(3000,expires-60)):2400);
            session=next;Interlocked.Increment(ref sessionBuilds);return next;
        }finally{sessionGate.Release();}
    }
    static async Task<object> Get(string path,CancellationToken cancel,string method="GET",object body=null){
        for(int attempt=0;;attempt++){
            var current=await GetSession(cancel).ConfigureAwait(false);
            try{return await Http(path.StartsWith("https://",StringComparison.Ordinal)?path:current.Pd+path,method,body,current,cancel).ConfigureAwait(false);}
            catch(RiotStatsException error){
                if(error.Status!=401||attempt>0)throw;
            }
            await sessionGate.WaitAsync(cancel).ConfigureAwait(false);
                try{if(Object.ReferenceEquals(session,current))session=null;}finally{sessionGate.Release();}
        }
    }
    internal static bool HasRiotId(string value){int split=(value??"").LastIndexOf('#');return split>0&&!String.IsNullOrWhiteSpace(value.Substring(0,split))&&!String.IsNullOrWhiteSpace(value.Substring(split+1));}
    internal static string CardId(object card){Guid id;var text=card as string??Json.S(card,"id");if(text=="")text=Json.S(card,"uuid");return Guid.TryParse(text,out id)&&id!=Guid.Empty?id.ToString():"";}
    public static Player Lookup(string id){return LookupAsync(id,CancellationToken.None).GetAwaiter().GetResult();}
    internal static async Task<string> LookupCardIdAsync(Player player,CancellationToken cancel){
        if(player==null||String.IsNullOrEmpty(player.Id))return "";
        var history=await Get("/match-history/v1/history/"+Uri.EscapeDataString(player.Id)+"?startIndex=0&endIndex=1",cancel).ConfigureAwait(false);
        var entry=Json.A(Json.Get(history,"History")).FirstOrDefault();
        Guid matchId;
        if(!Guid.TryParse(Json.S(entry,"MatchID"),out matchId))return "";
        var match=await Match(matchId.ToString(),cancel).ConfigureAwait(false);
        var participant=Json.A(Json.Get(match,"players")).FirstOrDefault(row=>String.Equals(Json.S(row,"subject"),player.Id,StringComparison.OrdinalIgnoreCase));
        object card=Json.Get(participant,"playerCard");
        if(CardId(card)!="")return CardId(card);
        return CardId(Json.Get(participant,"PlayerCardID"));
    }
    internal static async Task<Player> LookupAsync(string riotId,CancellationToken cancel,bool background=false){
        if(!HasRiotId(riotId))throw new ArgumentException("Enter a Riot ID in the Name#TAG format.");
        int split=riotId.LastIndexOf('#');string name=riotId.Substring(0,split).Trim(),tag=riotId.Substring(split+1).Trim();
        var hits=await Get("https://api.account.riotgames.com/aliases/v1/aliases?gameName="+Uri.EscapeDataString(name)+"&tagLine="+Uri.EscapeDataString(tag),cancel).ConfigureAwait(false);
        var hit=Json.A(hits).FirstOrDefault();Guid id;
        if(!Guid.TryParse(Json.S(hit,"puuid"),out id))throw Failure(404,null);
        var current=await GetSession(cancel).ConfigureAwait(false);
        return new Player{Id=id.ToString(),Name=name+"#"+tag,Region=current.Region,CardId=""};
    }
    internal static async Task<Tuple<Player,Task<PlayerProfile>,Task<PlayerProfile>>> SearchAsync(string riotId,string region){
        var player=await LookupAsync(riotId,CancellationToken.None).ConfigureAwait(false);
                var preview=new TaskCompletionSource<PlayerProfile>();
        var pending=LoadLiveAsync(player,CancellationToken.None,new RankProgress(p=>preview.TrySetResult(p)),false);
        var previewCompletion=pending.ContinueWith(t=>preview.TrySetResult(null),TaskScheduler.Default);
        return Tuple.Create(player,pending,preview.Task);
    }
    public static PlayerProfile Load(Player player){return LoadAsync(player,CancellationToken.None).GetAwaiter().GetResult();}
    public static Task<PlayerProfile> LoadAsync(Player player,CancellationToken cancel){return LoadLiveAsync(player,cancel,null,false);}
    sealed class RankProgress:IProgress<PlayerProfile>{readonly Action<PlayerProfile> action;internal RankProgress(Action<PlayerProfile> callback){action=callback;}public void Report(PlayerProfile value){action(value);}}
    internal static Task<PlayerProfile> LoadLiveAsync(Player player,CancellationToken cancel,IProgress<PlayerProfile> rankReady,bool background){
        return LoadProfileCore(player,cancel,rankReady,background);
    }
    static async Task<HistoryPage> BackgroundHistory(Player player,CancellationToken cancel){
        await backgroundGate.WaitAsync(cancel).ConfigureAwait(false);
        try{return await LoadPage(player,0,10,"Competitive",cancel).ConfigureAwait(false);}
        finally{backgroundGate.Release();}
    }
    static async Task<PlayerProfile> LoadProfileCore(Player player,CancellationToken cancel,IProgress<PlayerProfile> rankReady,bool background){        var profile=new PlayerProfile{Id=player.Id,Name=player.Name,CardId=player.CardId,AccountLevel=player.AccountLevel,AccountLevelHidden=player.AccountLevelHidden};
        names[player.Id]=player.Name;
        var history=background?BackgroundHistory(player,cancel):LoadPage(player,0,10,"Competitive",cancel);
        try{
            try{ApplyRank(profile,await Get("/mmr/v1/players/"+PlayerId(player),cancel).ConfigureAwait(false),CurrentAct());}
            catch(RiotStatsException error){AddError(profile,error,"Rank");}
            if(rankReady!=null)rankReady.Report(new PlayerProfile{Id=profile.Id,Name=profile.Name,CardId=profile.CardId,Tier=profile.Tier,PeakTier=profile.PeakTier,RR=profile.RR,RankLabel=profile.RankLabel,RankIsCurrent=profile.RankIsCurrent,AccountLevel=profile.AccountLevel,AccountLevelHidden=profile.AccountLevelHidden,HistoryLoading=true,Message="Loading recent matches…"});
            try{
                var page=await history.ConfigureAwait(false);
                profile.Message+=(page.CompetitiveOnly?"Only competitive history is available for this player. ":"");
                foreach(var item in page.Items.OrderByDescending(m=>HistoryLogic.N(Json.Get(m,"matchInfo"),"gameStartMillis"))){
                    AccountLevels.Apply(profile,item,player.Id);
                    var row=ParseArchivedMatch(item,player.Id);if(row!=null)profile.Matches.Add(row);
                    if(String.IsNullOrEmpty(profile.CardId))profile.CardId=CardId(Json.Get(Json.A(Json.Get(item,"players")).FirstOrDefault(p=>Json.S(p,"subject")==player.Id),"playerCard"));
                }
                profile.Matches=profile.Matches.OrderByDescending(m=>m.Started).ToList();profile.HistoryAvailable=true;
                profile.DayComplete=!page.CompetitiveOnly&&profile.Matches.All(m=>m.Completed)&&(page.Items.Length<10||profile.Matches.Last().Started<DateTime.Today);
                if(profile.Matches.Any(m=>!m.Completed))profile.Message+="Some match details are unavailable. ";
                if(page.CompetitiveOnly&&page.Items.Length==0)profile.Message+="Riot returned no competitive matches for this account. ";
                HistoryLogic.CountDay(profile,DateTime.Today);
            }catch(RiotStatsException error){AddError(profile,error,"History");}
            if(!profile.Limited)profile.RetryAfter=DateTime.UtcNow.AddMinutes(5);
        }finally{
            // Observe the parallel history request even when rank loading is cancelled.
            if(!history.IsCompleted){var observation=history.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
            else if(history.IsFaulted){var ignored=history.Exception;}
        }
        return profile;
    }
    static void AddError(PlayerProfile profile,RiotStatsException error,string section){
        profile.Message+=section+": "+(error.Status==404?"Riot did not return data for this player.":error.Message)+" ";
        if(error.Status==429){profile.Limited=true;profile.RetryAfter=error.RetryAfter;}
    }
    internal sealed class HistoryPage {internal object[] Items;internal bool CompetitiveOnly,EndReached;}
    static async Task<HistoryPage> LoadPage(Player player,int start,int size,string mode,CancellationToken cancel){
        string queue=mode=="Team Deathmatch"?"hurm":Catalog.Modes.Contains(mode)?mode.ToLowerInvariant().Replace(" ",""):"";
        string range="?startIndex="+start+"&endIndex="+(start+size);
        object history=null;bool fallback=false;
        try{history=await Get("/match-history/v1/history/"+PlayerId(player)+range+(queue==""?"":"&queue="+queue),cancel).ConfigureAwait(false);}
        catch(RiotStatsException error){if(error.Status!=404)throw;fallback=true;}
        object[] rows;
        if(fallback){
            if(queue!=""&&queue!="competitive")throw new RiotStatsException(404,"Riot history is unavailable for this mode.");
            history=await Get("/mmr/v1/players/"+PlayerId(player)+"/competitiveupdates"+range+"&queue=competitive",cancel).ConfigureAwait(false);
            rows=Json.Get(history,"Matches") as object[];
        }else rows=Json.Get(history,"History") as object[];
        if(rows==null)throw new InvalidOperationException("Riot match history response was not recognized.");
        return new HistoryPage{CompetitiveOnly=fallback,EndReached=AtHistoryEnd(start,size,rows.Length,HistoryLogic.N(history,"Total",-1)),Items=await FetchDetails(rows,Match,cancel).ConfigureAwait(false)};
    }
    internal static async Task<object[]> FetchDetails(object[] rows,Func<string,CancellationToken,Task<object>> fetch,CancellationToken cancel){
        var tasks=rows.Select(async entry=>{
            Guid id;if(!Guid.TryParse(Json.S(entry,"MatchID"),out id))throw new InvalidOperationException("Invalid Riot match ID.");
            try{return await fetch(id.ToString(),cancel).ConfigureAwait(false);}
            catch(RiotStatsException error){
                if(error.Status!=404&&error.Status!=403&&error.Status!=0&&error.Status<500)throw;
                return (object)new Dictionary<string,object>{{"unavailable",true},{"MatchID",id.ToString()},{"GameStartTime",HistoryLogic.N(entry,"GameStartTime",HistoryLogic.N(entry,"MatchStartTime"))},{"QueueID",Json.S(entry,"QueueID")}};
            }
        }).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    internal static bool AtHistoryEnd(int start,int size,int count,long total){return count<size||(total>=0&&start+count>=total);}
    internal const int ArchivePageSize=25;
    static string PlayerId(Player player){Guid id;if(player==null||!Guid.TryParse(player.Id,out id))throw new ArgumentException("Invalid player ID.");return id.ToString();}
    internal static string ArchivePath(Player player,int page,string mode){
        if(page<1||page>40000)throw new ArgumentOutOfRangeException("page");
        string queue=mode=="Team Deathmatch"?"hurm":Catalog.Modes.Contains(mode)?mode.ToLowerInvariant().Replace(" ",""):"";
        int start=(page-1)*ArchivePageSize;
        return "/match-history/v1/history/"+PlayerId(player)+"?startIndex="+start+"&endIndex="+(start+ArchivePageSize)+(queue==""?"":"&queue="+Uri.EscapeDataString(queue));
    }
    internal static async Task<HistoryPage> LoadArchivePage(Player player,int page,string mode,CancellationToken cancel){
        ArchivePath(player,page,mode);
        return await LoadPage(player,(page-1)*ArchivePageSize,ArchivePageSize,mode,cancel).ConfigureAwait(false);
    }
    internal static MatchRow ParseArchivedMatch(object entry,string subject){
        if(Json.B(entry,"unavailable"))return new MatchRow{Id=Json.S(entry,"MatchID"),Started=HistoryLogic.LocalTime(HistoryLogic.N(entry,"GameStartTime")),Queue=HistoryLogic.QueueName(Json.S(entry,"QueueID")),Map="Details unavailable"};
        if(Json.S(Json.Get(entry,"matchInfo"),"matchId")==""||!Json.A(Json.Get(entry,"players")).Any(p=>String.Equals(Json.S(p,"subject"),subject,StringComparison.OrdinalIgnoreCase)))return null;
        var row=HistoryLogic.ParseMatch(entry,subject);
        ApplyNames(row,names);return row;
    }
    internal static async Task<MatchRow> LoadScoreboard(MatchRow match,Player player,CancellationToken cancel){
        Guid id;if(match==null||!Guid.TryParse(match.Id,out id))return null;
        var row=ParseArchivedMatch(await Match(id.ToString(),cancel).ConfigureAwait(false),player.Id);
        if(row==null)return null;
        var missing=row.Roster.Where(p=>!HasRiotId(p.Name)&&!names.ContainsKey(p.Id)).Select(p=>p.Id).Distinct().ToArray();
        if(missing.Length>0){
            var response=await Get("/name-service/v2/players",cancel,"PUT",missing).ConfigureAwait(false);
            foreach(var item in Json.A(response)){
                string subject=Json.S(item,"Subject"),name=Json.S(item,"GameName"),tag=Json.S(item,"TagLine");
                if(subject!=""&&name!=""&&tag!="")names[subject]=name+"#"+tag;
            }
        }
        ApplyNames(row,names);return row;
    }
    internal static void ApplyNames(MatchRow row,IDictionary<string,string> resolved){
        foreach(var player in row.Roster){string name;if(!HasRiotId(player.Name)&&resolved.TryGetValue(player.Id,out name))player.Name=name;}
    }
 internal static byte[] LoadCard(string card,bool icon=false) {
    Guid id;
    if(!Guid.TryParse(card,out id)||id==Guid.Empty)return null;
    try {
        var request=(HttpWebRequest)WebRequest.Create("https://media.valorant-api.com/playercards/"+id+(icon?"/displayicon.png":"/wideart.png"));
        request.UserAgent="Lunar/1.79";
        request.Accept="image/png,image/*;q=0.9,*/*;q=0.5";
        request.Timeout=6000;
        request.ReadWriteTimeout=6000;
        request.ServicePoint.ConnectionLimit=8;
        request.AllowAutoRedirect=true;
        using(var response=request.GetResponse())
        using(var stream=response.GetResponseStream())
        using(var buffer=new MemoryStream()){
            if(response.ResponseUri==null||response.ResponseUri.Scheme!=Uri.UriSchemeHttps||
                response.ContentType==null||!response.ContentType.StartsWith("image/",StringComparison.OrdinalIgnoreCase))return null;
            byte[] chunk=new byte[8192];
            int count;
            while((count=stream.Read(chunk,0,chunk.Length))>0){
                if(buffer.Length+count>2*1024*1024)return null;
                buffer.Write(chunk,0,count);
            }
            return buffer.ToArray();
        }
    } catch(WebException){return null;}
      catch(IOException){return null;}
 }

}
}
namespace Lunar {
static class RiotStatsChecks {
    static void Check(bool pass,string name){if(!pass)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    static object Data(object value){return Json.Codec.DeserializeObject(Json.Codec.Serialize(value));}
    public static void Run(){
        Check(RiotStatsClient.Shard("br")=="na"&&RiotStatsClient.Shard("latam")=="na"&&RiotStatsClient.Shard("eu")=="eu","Riot routing maps BR and LATAM to NA shard");
        bool rejected=false;try{RiotStatsClient.Shard("invalid");}catch(InvalidOperationException){rejected=true;}Check(rejected,"unknown regions are rejected");
        var tokens=RiotStatsClient.Tokens("https://playvalorant.com/opt_in#access_token=a%2Bb%3D&id_token=c&expires_in=3600");
        Check(tokens["access_token"]=="a+b="&&tokens["id_token"]=="c","redirect fragment preserves encoded token characters");
        Check(RiotStatsClient.Tokens("https://auth.riotgames.com/login").Count==0,"login redirect does not create credentials");
        var player=new Player{Id="11111111-1111-1111-1111-111111111111",Name="Test#BR1"};
        Check(RiotStatsClient.ArchivePath(player,2,"Team Deathmatch").EndsWith("startIndex=25&endIndex=50&queue=hurm"),"Riot history uses index pagination and native queue IDs");
        Check(!RiotStatsClient.ArchivePath(player,1,"All modes").Contains("queue="),"all modes leaves queue unfiltered");
        var mmr=Data(new {QueueSkills=new {competitive=new {SeasonalInfoBySeasonID=new Dictionary<string,object>{{"3f61c772-4560-cd3f-5d3f-a7ab5abda6b3",new {CompetitiveTier=24}},{"current",new {CompetitiveTier=18,RankedRating=42}}}}}});
        var profile=new PlayerProfile();RiotStatsClient.ApplyRank(profile,mmr,"current");
        Check(profile.PeakTier==27&&profile.Tier==18&&profile.RR==42&&profile.RankIsCurrent,"pre-Ascendant peak normalizes independently of current rank");
        var match=Data(new {matchInfo=new {matchId="match",mapId="/Game/Maps/Ascent/Ascent",queueID="competitive",gameStartMillis=1700000000000L,isCompleted=true,completionState="Completed"},players=new object[]{new {subject=player.Id,teamId="Blue",stats=new {kills=1,deaths=0,assists=0,roundsPlayed=1,score=300}},new {subject="enemy",teamId="Red"}},teams=new[]{new {teamId="Blue",roundsWon=1,won=true},new {teamId="Red",roundsWon=0,won=false}},roundResults=new[]{new {playerStats=new[]{new {subject=player.Id,damage=new[]{new {receiver="enemy",damage=150,headshots=1,bodyshots=0,legshots=0}}}}}}});
        var row=RiotStatsClient.ParseArchivedMatch(match,player.Id);
        var summary=new Summary(new[]{row});
        Check(row.Result==Outcome.Win&&summary.ACS==300&&summary.ADR==150&&row.Metrics.HS==100,"native Riot match supplies result ACS ADR and HS without roundDamage");
        Check(RiotStatsClient.ParseArchivedMatch(match,"absent")==null,"missing player does not fabricate a match");
                var validId="22222222-2222-2222-2222-222222222222";
        var missingId="33333333-3333-3333-3333-333333333333";
        var entries=new[]{Data(new {MatchID=validId,GameStartTime=1700000000000L,QueueID="competitive"}),Data(new {MatchID=missingId,GameStartTime=1700000000000L,QueueID="competitive"})};
        Func<string,CancellationToken,Task<object>> partial=(id,cancel)=>{
            if(id==validId)return Task.FromResult(match);
            var failed=new TaskCompletionSource<object>();failed.SetException(new RiotStatsException(404,"missing"));return failed.Task;
        };
        var page=RiotStatsClient.FetchDetails(entries,partial,CancellationToken.None).GetAwaiter().GetResult();
        Check(page.Length==2&&RiotStatsClient.ParseArchivedMatch(page[0],player.Id).Result==Outcome.Win,"one unavailable match preserves successful history records");
        var unavailable=RiotStatsClient.ParseArchivedMatch(page[1],player.Id);
        Check(unavailable.Id==missingId&&!unavailable.Completed&&unavailable.Queue=="Competitive","unavailable detail retains pagination ID date and queue without fabricated metrics");
        var resolved=new Dictionary<string,string>{{player.Id,"Resolved#BR1"},{"enemy","Enemy#BR1"}};
        RiotStatsClient.ApplyNames(row,resolved);
        Check(row.Roster.All(p=>RiotStatsClient.HasRiotId(p.Name)),"batch name results populate scoreboard identities");
        resolved[player.Id]="Replacement#TAG";RiotStatsClient.ApplyNames(row,resolved);
        Check(row.Roster.First(p=>p.Id==player.Id).Name=="Resolved#BR1","resolved names do not overwrite existing identities");
        bool rateLimited=false;
        try{RiotStatsClient.FetchDetails(entries,(id,cancel)=>{var failed=new TaskCompletionSource<object>();failed.SetException(new RiotStatsException(429,"limited"));return failed.Task;},CancellationToken.None).GetAwaiter().GetResult();}
        catch(RiotStatsException error){rateLimited=error.Status==429;}
        Check(rateLimited,"rate limits propagate instead of becoming missing matches");
        bool cancelled=false;
        try{RiotStatsClient.FetchDetails(entries,(id,cancel)=>{var failed=new TaskCompletionSource<object>();failed.SetCanceled();return failed.Task;},CancellationToken.None).GetAwaiter().GetResult();}
        catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"cancelled history stays cancelled");
        var levelProfile=new PlayerProfile();
        AccountLevels.Apply(levelProfile,Data(new {players=new[]{new {subject=player.Id,accountLevel=287}}}),player.Id);
        Check(levelProfile.AccountLevel==287&&levelProfile.AccountLevelFromMatch,"profile reads numeric account level from match player");
        Check(AccountLevels.Value(player,levelProfile)==287,"missing live level falls back to profile level");
        var live=new Player{AccountLevel=290};
        Check(AccountLevels.Value(live,levelProfile)==290,"live level takes precedence over recent match level");
        live.AccountLevelHidden=true;
        Check(AccountLevels.Value(live,levelProfile)==290&&AccountLevels.Label(live,levelProfile)=="Level 290","display flag does not discard a level supplied by Riot");
        Check(AccountLevels.Read(Data(new {PlayerIdentity=new {PlayerCardID="card"},accountLevel=143}))==143,"root level is read when nested identity has no level");
        Check(AccountLevels.Read(Data(new {accountLevel=143,hideAccountLevel="true"}))==143,"account level supplied with string display flag is retained");
        Check(AccountLevels.Read(Data(new {accountLevel=-1}))==0&&AccountLevels.Label(null,new PlayerProfile())=="Level unavailable","unknown level is labeled instead of showing zero");
        AccountLevels.Apply(levelProfile,Data(new {players=new[]{new {subject=player.Id,accountLevel=100}}}),player.Id);
        Check(levelProfile.AccountLevel==287,"older match does not overwrite newest level");
        Check(HistoryLogic.MapName("/Game/Maps/Plummet/Plummet")=="Summit","internal Plummet map name resolves to Summit");
        Guid summitId;Check(Guid.TryParse(TrackArt.MapId("Summit"),out summitId),"Summit resolves to a map id for API artwork");
        Check(RiotStatsClient.AtHistoryEnd(50,25,23,73),"short final history page stops before invalid next offset");
        Check(RiotStatsClient.AtHistoryEnd(50,25,25,75),"full final history page stops at provider total");
        Check(!RiotStatsClient.AtHistoryEnd(0,25,25,73),"full intermediate history page remains loadable");
        Check(AccountLevels.Display(new Player{AccountLevelHidden=true},levelProfile)=="287","missing live number uses returned historical level despite display flag");
        Check(AccountLevels.Display(new Player{AccountLevelHidden=true},null)=="—","no returned account level displays a dash instead of H");
        var levels=Data(new {matchInfo=new {seasonId="test"},players=new[]{new {subject=player.Id,accountLevel=329},new {subject="unknown",accountLevel=0}}});
        var levelRow=new MatchRow();StatsMetrics.Enrich(levels,levelRow,player.Id);
        Check(levelRow.Roster[0].AccountLevel==329&&levelRow.Roster[1].AccountLevel==0,"scoreboard carries per-player account level without inventing missing numbers");
        using(var stream=typeof(MapCatalog).Assembly.GetManifestResourceStream("maps.json"))
        using(var reader=new StreamReader(stream))foreach(var map in Json.A(Json.Codec.DeserializeObject(reader.ReadToEnd()))){
            string name=Json.S(map,"name");
            Check(HistoryLogic.MapName(Json.S(map,"path"))==name&&HistoryLogic.MapName(Json.S(map,"id"))==name,"map path and UUID resolve to display name: "+name);
        }
        Check(HistoryLogic.MapName("Unreleased_Codename")=="Map unavailable","unknown map code is never displayed as a name");
        var now=DateTime.UtcNow;
        Check(RiotStatsClient.CanReuseSession(now.AddMinutes(10),"same","same",now),"valid token session is reusable across players");
        Check(!RiotStatsClient.CanReuseSession(now,"same","same",now),"expired token requires renewal");
        Check(!RiotStatsClient.CanReuseSession(now.AddMinutes(10),"old","new",now),"changed login cookie invalidates old session");
        var deathmatch=new Snapshot();for(int i=0;i<12;i++)deathmatch.Allies.Add(new Player{Id="test"+i});
        Check(TrackerSurface.SingleRoster(deathmatch),"12-player deathmatch uses one roster");
        deathmatch.Enemies.Add(new Player{Id="opponent"});
        Check(!TrackerSurface.SingleRoster(deathmatch),"opposing teams retain the two-team layout");
        Check(TrackerSurface.RosterRowHeight(220,12)>=44&&TrackerSurface.RosterRowHeight(660,12)<=49,"deathmatch rows never collapse below readable height");
        Check(AccountLevels.Label(null,new PlayerProfile{HistoryLoading=true})=="Loading level…","rank preview does not label pending level unavailable");
        Check(MapCatalog.ForMode("Competitive").Contains("Summit")&&!MapCatalog.ForMode("Competitive").Contains("District"),"competitive maps exclude team deathmatch arenas");
        Check(MapCatalog.ForMode("Unrated").SequenceEqual(MapCatalog.ForMode("Competitive")),"unrated uses standard gameplay maps");
        Check(MapCatalog.ForMode("Team Deathmatch").Contains("District")&&!MapCatalog.ForMode("Team Deathmatch").Contains("Ascent"),"team deathmatch uses its own arenas");
        Check(!MapCatalog.ForMode("All modes").Contains("Basic Training")&&!MapCatalog.ForMode("All modes").Contains("The Range"),"map filters exclude training locations");
        Console.WriteLine("Riot integration checks passed.");
    }
}
}
namespace Lunar {
static class AccountLevels {
    internal static bool Hidden(object data){
        var identity=Json.GetAnyCase(data,"PlayerIdentity","player_identity")??data;
        bool hidden;
        return Boolean.TryParse(Convert.ToString(Json.GetAnyCase(identity,"HideAccountLevel","hide_account_level")??Json.GetAnyCase(data,"HideAccountLevel","hide_account_level")),out hidden)&&hidden;
    }
    internal static int Read(object data){

        var identity=Json.GetAnyCase(data,"PlayerIdentity","player_identity")??data;
        var raw=Json.GetAnyCase(identity,"AccountLevel","account_level","Level")??Json.GetAnyCase(data,"AccountLevel","account_level","Level");
        int level;return Int32.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Integer,CultureInfo.InvariantCulture,out level)&&level>0&&level<=10000?level:0;
    }
    internal static void Apply(PlayerProfile profile,object match,string subject){
        if(profile.AccountLevel>0)return;
        var player=Json.A(Json.Get(match,"players")).FirstOrDefault(p=>String.Equals(Json.S(p,"subject"),subject,StringComparison.OrdinalIgnoreCase));
        if(player==null)return;
        if(Hidden(player))profile.AccountLevelHidden=true;
        profile.AccountLevel=Read(player);profile.AccountLevelFromMatch=profile.AccountLevel>0;
    }
    internal static bool IsHidden(Player player,PlayerProfile profile){return player!=null&&player.AccountLevelHidden||profile!=null&&profile.AccountLevelHidden;}
    internal static int Value(Player player,PlayerProfile profile){
        return player!=null&&player.AccountLevel>0?player.AccountLevel:profile==null?0:profile.AccountLevel;
    }
    internal static string Display(Player player,PlayerProfile profile){int level=Value(player,profile);return level>0?level.ToString(CultureInfo.InvariantCulture):"—";}
    internal static string Label(Player player,PlayerProfile profile){int level=Value(player,profile);return level>0?"Level "+level:profile==null||profile.HistoryLoading?"Loading level…":"Level unavailable";}
}
}
