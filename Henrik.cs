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
namespace Lunar {
static class HenrikKey {
    static readonly string PathOnDisk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Lunar","henrik.key");
    public static readonly string Value=Load(PathOnDisk);

    internal static string Load(string path){
        try{
            byte[] encrypted=File.ReadAllBytes(path);
            string configured=Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser)).Trim();
            if(configured.Length>0)return configured;
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        catch(CryptographicException){}

        return (Environment.GetEnvironmentVariable("LUNAR_HENRIK_KEY")??"").Trim();
    }
}
sealed class HenrikRequestException:Exception {
    public readonly int Status;
    public DateTime RetryAfter;
    public HenrikRequestException(int status,string message):base(message){Status=status;}
}
sealed class HenrikRequestBudget {
    const int BackgroundCeiling=22;
    readonly Queue<DateTime> requests=new Queue<DateTime>();
    internal DateTime Admit(DateTime now,bool background){
        lock(requests){
            while(requests.Count>0&&requests.Peek().AddMinutes(1)<=now)requests.Dequeue();
            if(background&&requests.Count>=BackgroundCeiling)
                return requests.ElementAt(requests.Count-BackgroundCeiling).AddMinutes(1);
            requests.Enqueue(now);
            return DateTime.MinValue;
        }
    }
}
static class HenrikClient {
 const string Base="https://api.henrikdev.xyz";
 static long blockedUntil;
 static readonly HenrikRequestBudget budget=new HenrikRequestBudget();
 internal static DateTime RemoteRetryAt {get{return new DateTime(Interlocked.Read(ref blockedUntil),DateTimeKind.Utc);}}

 static Task<object> GetAsync(string path,CancellationToken cancellation){return RequestAsync(path,cancellation,false);}
 static Task<object> GetBackgroundAsync(string path,CancellationToken cancellation){return RequestAsync(path,cancellation,true);}
 static async Task<object> RequestAsync(string path,CancellationToken cancellation,bool background){
    cancellation.ThrowIfCancellationRequested();
    if(String.IsNullOrWhiteSpace(HenrikKey.Value))throw new InvalidOperationException("HenrikDev API key unavailable.");
    var now=DateTime.UtcNow;
    var blocked=RemoteRetryAt;
    if(blocked>now)throw RateLimit(blocked);
    var deferred=budget.Admit(now,background);
    if(deferred>now)
        throw new HenrikRequestException(429,"Background updates paused to leave room for Tracker searches."){RetryAfter=deferred};
    var request=(HttpWebRequest)WebRequest.Create(Base+path);
    request.Method="GET";
    request.ReadWriteTimeout=15000;
    request.AllowAutoRedirect=false;
    request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
    request.UserAgent="Lunar/1.58";
    request.ServicePoint.ConnectionLimit=24;
    request.Headers[HttpRequestHeader.Authorization]=HenrikKey.Value;
    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
        timeout.CancelAfter(15000);
        using(timeout.Token.Register(request.Abort)){
            try{
                using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using(var reader=new StreamReader(response.GetResponseStream())){
                    string body=await reader.ReadToEndAsync().ConfigureAwait(false);
                    return Json.Codec.DeserializeObject(body);
                }
            }catch(WebException error){
                using(var response=error.Response as HttpWebResponse){
                    cancellation.ThrowIfCancellationRequested();
                    if(timeout.IsCancellationRequested)
                        throw new HenrikRequestException(0,"HenrikDev timed out. Try again shortly.");
                    if(response==null)
                        throw new HenrikRequestException(0,"Could not connect to HenrikDev. Check your internet connection.");
                    throw ResponseFailure((int)response.StatusCode,response.Headers["Retry-After"]);
                }
            }catch(IOException){
                cancellation.ThrowIfCancellationRequested();
                throw new HenrikRequestException(0,"The HenrikDev response was interrupted. Try again shortly.");
            }
        }
    }
 }
 static HenrikRequestException ResponseFailure(int status,string retryAfter){
    if(status==429){
        DateTime until=RetryTime(retryAfter);
        long previous;
        do {
            previous=Interlocked.Read(ref blockedUntil);
            if(previous>=until.Ticks)return RateLimit(new DateTime(previous,DateTimeKind.Utc));
        } while(Interlocked.CompareExchange(ref blockedUntil,until.Ticks,previous)!=previous);
        return RateLimit(until);
    }
    string message=status==401||status==403?"HenrikDev rejected the API key."
        :status==404?"Profile not found or private."
        :"HenrikDev returned HTTP "+status+".";
    return new HenrikRequestException(status,message);
 }
 static HenrikRequestException RateLimit(DateTime until){
    return new HenrikRequestException(429,"HenrikDev rate limit reached. Try again in "+RetrySeconds(until)+"s."){RetryAfter=until};
 }
 internal static int RetrySeconds(DateTime until){return (int)Math.Max(1,Math.Ceiling((until-DateTime.UtcNow).TotalSeconds));}
 static DateTime RetryTime(string value){
    DateTime now=DateTime.UtcNow;
    int seconds;
    if(Int32.TryParse(value,out seconds)&&seconds>=0)return now.AddSeconds(Math.Max(2,seconds));
    DateTimeOffset date;
    if(DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date)&&date.UtcDateTime>now)
        return date.UtcDateTime;
    return now.AddSeconds(90);
 }
 static string Esc(string value){return Uri.EscapeDataString(value??"");}
 internal static bool HasRiotId(string value){
    if(String.IsNullOrWhiteSpace(value))return false;
    int split=value.LastIndexOf('#');
    return split>0&&!String.IsNullOrWhiteSpace(value.Substring(0,split))
        &&!String.IsNullOrWhiteSpace(value.Substring(split+1));
 }
 internal const int ArchivePageSize=25;
 internal static string ArchivePath(Player player,int page,string mode){
    if(player==null||!HasRiotId(player.Name))throw new InvalidOperationException("The Riot ID is not available yet.");
    string region=Region(player.Region);
    Guid id;
    string path;
    if(Guid.TryParse(player.Id,out id)&&id!=Guid.Empty)path="/valorant/v1/by-puuid/lifetime/matches/"+region+"/"+id;
    else{
        int split=player.Name.LastIndexOf('#');
        path="/valorant/v1/lifetime/matches/"+region+"/"+Esc(player.Name.Substring(0,split).Trim())+"/"+Esc(player.Name.Substring(split+1).Trim());
    }
    string filter=Catalog.Modes.Contains(mode)?"&mode="+Esc(mode.ToLowerInvariant().Replace(" ","")):"";
    return path+"?page="+page+"&size="+ArchivePageSize+filter;
 }
 internal static async Task<object[]> LoadArchivePage(Player player,int page,string mode,CancellationToken cancellation){
    var response=await GetAsync(ArchivePath(player,page,mode),cancellation).ConfigureAwait(false);
    var rows=Json.Get(response,"data") as object[];
    if(rows==null)throw new InvalidOperationException("The archived match response was not recognized.");
    return rows;
 }
 internal static async Task<MatchRow> LoadScoreboard(MatchRow match,Player player,CancellationToken cancellation){
    Guid id;
    if(player==null||!Guid.TryParse(match.Id,out id))return null;
    string region=Region(String.IsNullOrEmpty(match.Region)?player.Region:match.Region);
    var response=await GetAsync("/valorant/v4/match/"+region+"/"+id,cancellation).ConfigureAwait(false);
    return ParseMatch(Json.Get(response,"data"),player.Id);
 }
 internal static MatchRow ParseArchivedMatch(object entry,string subject){
    var meta=Json.Get(entry,"meta");
    var stats=Json.Get(entry,"stats");
    var teams=Json.Get(entry,"teams");
    string id=Json.S(meta,"id");
    string owner=Json.S(stats,"puuid");
    DateTimeOffset started;
    if(String.IsNullOrWhiteSpace(id)||(owner!=""&&!String.Equals(owner,subject,StringComparison.OrdinalIgnoreCase))||
       !DateTimeOffset.TryParse(Json.S(meta,"started_at"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out started))return null;
    var map=Json.Get(meta,"map");
    var season=Json.Get(meta,"season");
    string rawMode=Json.S(meta,"mode");
    string normalizedMode=rawMode.ToLowerInvariant().Replace(" ","");
    string queue=HistoryLogic.QueueName(normalizedMode);
    if(queue=="teamdeathmatch")queue="Team Deathmatch";
    if(queue==normalizedMode&&rawMode!="")queue=rawMode;
    var row=new MatchRow{
        Id=id,Started=started.LocalDateTime,Completed=true,Region=Json.S(meta,"region"),
        MapId=Json.S(map,"id"),Map=Json.S(map,"name"),Queue=queue,
        SeasonId=Json.S(season,"id"),AgentId=Json.S(Json.Get(stats,"character"),"id")
    };
    if(row.Map=="")row.Map="Map unavailable";
    if(row.AgentId=="")row.AgentId=Json.S(Json.Get(stats,"character"),"name");
    string team=Json.S(stats,"team").ToLowerInvariant();
    long red=HistoryLogic.N(teams,"red",-1),blue=HistoryLogic.N(teams,"blue",-1);
    if((team=="red"||team=="blue")&&red>=0&&blue>=0&&(red+blue)>0){
        long won=team=="red"?red:blue,lost=team=="red"?blue:red;
        row.Score=won+" : "+lost;
        row.Result=won==lost?Outcome.Draw:won>lost?Outcome.Win:Outcome.Loss;
        row.Metrics.Rounds=red+blue;
    }
    var p=row.Metrics;
    p.HasKda=new[]{"kills","deaths","assists"}.All(k=>Json.Get(stats,k)!=null);
    p.Kills=HistoryLogic.N(stats,"kills");p.Deaths=HistoryLogic.N(stats,"deaths");p.Assists=HistoryLogic.N(stats,"assists");
    if(p.HasKda)row.Kda=p.Kills+" / "+p.Deaths+" / "+p.Assists;
    var shots=Json.Get(stats,"shots");
    p.HasHits=new[]{"head","body","leg"}.All(k=>Json.Get(shots,k)!=null);
    p.Head=HistoryLogic.N(shots,"head");p.Body=HistoryLogic.N(shots,"body");p.Leg=HistoryLogic.N(shots,"leg");
    p.HasScore=Json.Get(stats,"score")!=null;p.Score=HistoryLogic.N(stats,"score");
    var damage=Json.Get(stats,"damage");
    p.HasDamage=Json.Get(damage,"made")!=null;
    p.Damage=StatsMetrics.Number(damage,"made");
    return row;
 }

 public static Player Lookup(string riotId) {
    return LookupAsync(riotId,CancellationToken.None).GetAwaiter().GetResult();
 }
 internal static async Task<Player> LookupAsync(string riotId,CancellationToken cancellation,bool background=false){
    if(!HasRiotId(riotId))throw new ArgumentException("Enter a Riot ID in the Name#TAG format.");
    int split=riotId.LastIndexOf('#');
    string name=riotId.Substring(0,split).Trim(),tag=riotId.Substring(split+1).Trim();
    var account=Json.Get(await RequestAsync("/valorant/v2/account/"+Esc(name)+"/"+Esc(tag),cancellation,background).ConfigureAwait(false),"data");
    string id=Json.S(account,"puuid");
    if(id=="")throw new InvalidOperationException("HenrikDev did not return a player ID for this profile.");
    return new Player{Id=id,Name=Json.S(account,"name")+"#"+Json.S(account,"tag"),CardId=CardId(Json.Get(account,"card")),Region=Json.S(account,"region")};
 }
 internal static string CardId(object card){
    Guid id;
    var text=card as string;
    if(text==null){
        text=Json.S(card,"id");
        if(text=="")text=Json.S(card,"uuid");
    }
    return Guid.TryParse(text,out id)&&id!=Guid.Empty?id.ToString():"";
 }
 public static PlayerProfile Load(Player player){
    var profile=LoadAsync(player,CancellationToken.None).GetAwaiter().GetResult();
    profile.CardArt=LoadCard(profile.CardId);
    return profile;
 }
 public static Task<PlayerProfile> LoadAsync(Player player,CancellationToken cancellation){
    return LoadAsync(player,GetAsync,cancellation);
 }
 internal static async Task<Tuple<Player,Task<PlayerProfile>,Task<PlayerProfile>>> SearchAsync(string riotId,string region){
    using(var warmup=new CancellationTokenSource()){
        var account=LookupAsync(riotId,CancellationToken.None);
        string rankPath,historyPath;
        ProfilePaths(new Player{Name=riotId,Region=region},out rankPath,out historyPath);
        var rank=GetAsync(rankPath,warmup.Token);
        var history=GetAsync(historyPath,warmup.Token);
        // Observe speculative responses even if the account is missing or belongs to another region.
        ObserveWarmup(rank,history);
        Player player;
        try{player=await account.ConfigureAwait(false);}
        catch{warmup.Cancel();throw;}
        if(Region(player.Region)!=Region(region)||!String.Equals(player.Name,riotId,StringComparison.OrdinalIgnoreCase)){
            warmup.Cancel();
            ProfilePaths(player,out rankPath,out historyPath);
            rank=GetAsync(rankPath,CancellationToken.None);
            history=GetAsync(historyPath,CancellationToken.None);
        }
        return Tuple.Create(player,ReadProfile(player,rank,history,CancellationToken.None,null),PreviewRank(player,rank));
    }
 }
 static void ObserveWarmup(Task rank,Task history){
    Task.WhenAll(rank,history).ContinueWith(task=>task.Exception.Handle(error=>true),TaskContinuationOptions.OnlyOnFaulted);
 }
 static async Task<PlayerProfile> PreviewRank(Player player,Task<object> rank){
    try{
        var data=Json.Get(await rank.ConfigureAwait(false),"data");
        var preview=RankPreview(player,data);
        return preview.RankIsCurrent?preview:null;
    }catch(Exception){return null;}
 }
 static PlayerProfile RankPreview(Player player,object data){
    var preview=new PlayerProfile{Id=player.Id,Name=player.Name,CardId=player.CardId,HistoryLoading=true,Message="Loading match history…"};
    ApplyRank(preview,data);
    return preview;
 }
 internal static Task<PlayerProfile> LoadLiveAsync(Player player,CancellationToken cancellation,IProgress<PlayerProfile> rankReady,bool background){
    Func<string,CancellationToken,Task<object>> fetch=background?(Func<string,CancellationToken,Task<object>>)GetBackgroundAsync:GetAsync;
    return LoadAsync(player,fetch,cancellation,rankReady);
 }
 internal static async Task<PlayerProfile> LoadAsync(Player player,Func<string,CancellationToken,Task<object>> fetch,
    CancellationToken cancellation,IProgress<PlayerProfile> rankReady=null){
    if(!HasRiotId(player.Name)){
        return new PlayerProfile{Id=player.Id,Name=player.Name,CardId=player.CardId,Message="The Riot ID is not available yet."};
    }
    string rankPath,historyPath;
    ProfilePaths(player,out rankPath,out historyPath);
    return await ReadProfile(player,fetch(rankPath,cancellation),fetch(historyPath,cancellation),cancellation,rankReady)
        .ConfigureAwait(false);
 }
 static async Task<PlayerProfile> ReadProfile(Player player,Task<object> rank,Task<object> history,
    CancellationToken cancellation,IProgress<PlayerProfile> rankReady){
    var profile=new PlayerProfile{Id=player.Id,Name=player.Name,CardId=player.CardId};
    var pending=new List<Task<object>>{rank,history};
    while(pending.Count!=0){
        var finished=await Task.WhenAny(pending).ConfigureAwait(false);
        pending.Remove(finished);
        try{
            var data=Json.Get(await finished.ConfigureAwait(false),"data");
            cancellation.ThrowIfCancellationRequested();
            if(finished==rank){
                ApplyRank(profile,data);
                if(rankReady!=null&&profile.RankIsCurrent&&!history.IsCompleted)
                    rankReady.Report(RankPreview(player,data));
            }
            else ApplyHistory(profile,data,player.Id);
        }catch(Exception error){
            cancellation.ThrowIfCancellationRequested();
            AddFailure(profile,error,finished==rank?"Rank":"Matches");
        }
    }
    if(!profile.Limited&&profile.HistoryAvailable&&profile.RankIsCurrent)
        profile.RetryAfter=DateTime.UtcNow.AddMinutes(5);
    return profile;
 }
 static string Region(string region){
    region=(region??"").ToLowerInvariant();
    return new[]{"br","na","eu","ap","kr","latam"}.Contains(region)?region:"br";
 }
 internal static void ProfilePaths(Player player,out string rank,out string history){
    string region=Region(player.Region);
    Guid id;
    if(Guid.TryParse(player.Id,out id)&&id!=Guid.Empty){
        rank="/valorant/v3/by-puuid/mmr/"+region+"/pc/"+id;
        history="/valorant/v4/by-puuid/matches/"+region+"/pc/"+id+"?mode=competitive&size=10";
        return;
    }
    int split=player.Name.LastIndexOf('#');
    string path=Esc(player.Name.Substring(0,split).Trim())+"/"+Esc(player.Name.Substring(split+1).Trim());
    rank="/valorant/v3/mmr/"+region+"/pc/"+path;
    history="/valorant/v4/matches/"+region+"/pc/"+path+"?mode=competitive&size=10";
 }
 static void ApplyHistory(PlayerProfile profile,object data,string subject){
    var matches=data as object[];
    if(matches==null)throw new InvalidOperationException("The match history response was not recognized.");
    foreach(var match in matches){
        var row=ParseMatch(match,subject);
        if(row==null)continue;
        if(String.IsNullOrEmpty(profile.CardId))profile.CardId=CardId(Json.Get(Json.Get(FindPlayer(match,subject),"customization"),"card"));
        profile.Matches.Add(row);
    }
    profile.Matches=profile.Matches.GroupBy(x=>x.Id).Select(g=>g.First()).OrderByDescending(x=>x.Started).ToList();
    profile.HistoryAvailable=true;
    profile.DayComplete=profile.Matches.Count==matches.Length&&(profile.Matches.Count<10||profile.Matches.Last().Started<DateTime.Today);
    HistoryLogic.CountDay(profile,DateTime.Today);
 }
 static void AddFailure(PlayerProfile profile,Exception error,string section){
    var api=error as HenrikRequestException;
    if(api!=null&&api.Status==429){
        if(!profile.Limited||api.RetryAfter>profile.RetryAfter)profile.RetryAfter=api.RetryAfter;
        profile.Limited=true;
    }
    profile.Message=(profile.Message+" "+section+": "+error.Message).Trim();
 }
 internal static byte[] LoadCard(string card,bool icon=false) {
    Guid id;
    if(!Guid.TryParse(card,out id)||id==Guid.Empty)return null;
    try {
        var request=(HttpWebRequest)WebRequest.Create("https://media.valorant-api.com/playercards/"+id+(icon?"/displayicon.png":"/wideart.png"));
        request.Timeout=6000;
        request.ReadWriteTimeout=6000;
        request.ServicePoint.ConnectionLimit=8;
        request.AllowAutoRedirect=false;
        using(var response=request.GetResponse())
        using(var stream=response.GetResponseStream())
        using(var buffer=new MemoryStream()){
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
 internal static void ApplyRank(PlayerProfile profile,object data) {
    var current=Json.Get(data,"current");
    profile.Tier=Tier(current);
    profile.PeakTier=Tier(Json.Get(data,"peak"));
    profile.RR=(int)HistoryLogic.N(current,"rr",-1);
    profile.RankIsCurrent=profile.Tier>=0;
    profile.RankLabel=profile.Tier<0?"Rank unavailable":RankArt.Name(profile.Tier);
 }
 static int Tier(object data) {
    return (int)HistoryLogic.N(Json.Get(data,"tier"),"id",HistoryLogic.N(data,"tier",-1));
 }
 static object FindPlayer(object match,string puuid) {
    return Json.A(Json.Get(match,"players")).FirstOrDefault(p=>Json.S(p,"puuid")==puuid);
 }
 internal static MatchRow ParseMatch(object match,string puuid) {
    var meta=Json.Get(match,"metadata");
    var me=FindPlayer(match,puuid);
    var date=Json.S(meta,"started_at");
    DateTimeOffset started;
    string id=Json.S(meta,"match_id");
    if(me==null||id==""||!DateTimeOffset.TryParse(date,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out started))return null;
    var map=Json.Get(meta,"map");
    var row=new MatchRow{
        Id=id,MapId=Json.S(map,"id"),Map=Json.S(map,"name"),Started=started.LocalDateTime,Region=Json.S(meta,"region"),
        Queue=HistoryLogic.QueueName(Json.S(Json.Get(meta,"queue"),"id")),
        SeasonId=Json.S(Json.Get(meta,"season"),"id"),
        AgentId=Json.S(Json.Get(me,"agent"),"id"),Completed=Json.B(meta,"is_completed")
    };
    var teams=Json.A(Json.Get(match,"teams")).ToList();
    string team=Json.S(me,"team_id");
    var own=teams.FirstOrDefault(x=>team!=""&&String.Equals(Json.S(x,"team_id"),team,StringComparison.OrdinalIgnoreCase));
    long won=HistoryLogic.N(Json.Get(own,"rounds"),"won",-1);
    long lost=HistoryLogic.N(Json.Get(own,"rounds"),"lost",-1);
    long rounds=won>=0&&lost>=0?won+lost:0;
    if(rounds>0)row.Score=won+" : "+lost;
    if(!row.Completed)row.Result=Outcome.Ignored;
    else if(teams.Count==2&&rounds>0&&won==lost)row.Result=Outcome.Draw;
    else if(teams.Count==2&&rounds>0)row.Result=won>lost?Outcome.Win:Outcome.Loss;
    else if(Json.Get(own,"won") is bool)row.Result=Json.B(own,"won")?Outcome.Win:Outcome.Unknown;
    foreach(var player in Json.A(Json.Get(match,"players"))){
        var stats=ReadStats(Json.Get(player,"stats"),rounds);
        row.Roster.Add(new ScorePlayer{
            Id=Json.S(player,"puuid"),Name=Json.S(player,"name")+"#"+Json.S(player,"tag"),
            Team=Json.S(player,"team_id"),Agent=Json.S(Json.Get(player,"agent"),"id"),PartyId=RiotReader.ReadPartyId(player),Tier=Tier(player),Stats=stats
        });
        if(Json.S(player,"puuid")!=puuid)continue;
        row.Metrics=stats;
        if(stats.HasKda)row.Kda=stats.Kills+" / "+stats.Deaths+" / "+stats.Assists;
    }
    return row;
 }
 static Performance ReadStats(object stats,long rounds) {
    return new Performance{
        HasKda=new[]{"kills","deaths","assists"}.All(k=>Json.Get(stats,k)!=null),
        Kills=HistoryLogic.N(stats,"kills"),Deaths=HistoryLogic.N(stats,"deaths"),Assists=HistoryLogic.N(stats,"assists"),
        HasHits=new[]{"headshots","bodyshots","legshots"}.All(k=>Json.Get(stats,k)!=null),
        Head=HistoryLogic.N(stats,"headshots"),Body=HistoryLogic.N(stats,"bodyshots"),Leg=HistoryLogic.N(stats,"legshots"),
        HasDamage=Json.Get(Json.Get(stats,"damage"),"dealt")!=null,
        Damage=StatsMetrics.Number(Json.Get(stats,"damage"),"dealt"),Rounds=rounds,
        HasScore=Json.Get(stats,"score")!=null,Score=HistoryLogic.N(stats,"score")
    };
 }
}
}
