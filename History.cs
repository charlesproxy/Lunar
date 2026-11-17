using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;

namespace Lunar {
enum Outcome { Unknown, Win, Loss, Draw, Ignored }
class MatchRow {
    public string AgentId="",SeasonId="";public Performance Metrics=new Performance();public List<ScorePlayer> Roster=new List<ScorePlayer>();public int? RRChange;
    public string Id="",Map="Map unavailable",Queue="",Score="—",Kda="—";
    public string MapId="",Region="";
    public DateTime Started; public Outcome Result; public bool Completed;
}
class PlayerProfile {
    public int PeakTier=-1;
    public byte[] CardArt;
    public string CardId;
    public string Id,Name,Message="",RankLabel="Rank unavailable";
    public int Tier=-1,RR=-1,Wins,Losses,Draws,Unknown;
    public bool HistoryAvailable,HistoryLoading,DayComplete,RankIsCurrent,Limited;
    public DateTime Day=DateTime.Today,Loaded=DateTime.Now;
    public DateTime RetryAfter=DateTime.UtcNow.AddMinutes(2);
    public List<MatchRow> Matches=new List<MatchRow>();
    public string Record {get {return !HistoryAvailable?"—":Wins+"-"+Losses+(DayComplete?"":"*");}}
    public Color RecordColor {get {return !HistoryAvailable?Palette.Muted:Wins>Losses?Palette.Green:Wins<Losses?Palette.Red:Palette.Muted;}}
}
static class HistoryLogic {
    public static long N(object o,string key,long fallback=0){long n;return Int64.TryParse(Json.S(o,key),out n)?n:fallback;}
    public static DateTime LocalTime(long ms){try{return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();}catch{return DateTime.MinValue;}}
    public static string MapName(string path){
        var parts=(path??"").Split('/');string last=parts.LastOrDefault()??"";
        var maps=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"Ascent","Ascent"},{"Bonsai","Split"},{"Canyon","Fracture"},{"Duality","Bind"},{"Foxtrot","Breeze"},{"HURM_Alley","District"},{"HURM_Bowl","Kasbah"},{"HURM_Yard","Piazza"},{"Jam","Lotus"},{"Juliett","Sunset"},{"Pitt","Pearl"},{"Port","Icebox"},{"Triad","Haven"},{"Infinity","Abyss"},{"Rook","Corrode"}};
        return maps.ContainsKey(last)?maps[last]:String.IsNullOrWhiteSpace(last)?"Map unavailable":last;
    }
    public static string QueueName(string queue){switch(queue){case "competitive":return "Competitive";case "unrated":return "Unrated";case "swiftplay":return "Swiftplay";case "spikerush":return "Spike Rush";case "deathmatch":return "Deathmatch";case "hurm":return "Team Deathmatch";default:return String.IsNullOrEmpty(queue)?"Mode unavailable":queue;}}
    public static MatchRow ParseMatch(object data,string subject){
        var info=Json.Get(data,"matchInfo");var row=new MatchRow {Id=Json.S(info,"matchId"),Map=MapName(Json.S(info,"mapId")),Queue=QueueName(Json.S(info,"queueID")),Started=LocalTime(N(info,"gameStartMillis")),Completed=Json.B(info,"isCompleted")};
        StatsMetrics.Enrich(data,row,subject);
        var p=Json.A(Json.Get(data,"players")).FirstOrDefault(v=>String.Equals(Json.S(v,"subject"),subject,StringComparison.OrdinalIgnoreCase));
        if(p==null)return row;
        var stats=Json.Get(p,"stats");if(Json.Get(stats,"kills")!=null&&Json.Get(stats,"deaths")!=null&&Json.Get(stats,"assists")!=null)row.Kda=N(stats,"kills")+" / "+N(stats,"deaths")+" / "+N(stats,"assists");
        string completion=Json.S(info,"completionState");
        if(!row.Completed||completion.IndexOf("remake",StringComparison.OrdinalIgnoreCase)>=0){row.Result=Outcome.Ignored;return row;}
        string teamId=Json.S(p,"teamId");var teams=Json.A(Json.Get(data,"teams")).ToList();
        var team=teams.FirstOrDefault(t=>Json.S(t,"teamId")==teamId);var enemy=teams.FirstOrDefault(t=>Json.S(t,"teamId")!=teamId);
        if(team==null||enemy==null)return row;
        if(Json.Get(team,"roundsWon")!=null&&Json.Get(enemy,"roundsWon")!=null)row.Score=N(team,"roundsWon")+" : "+N(enemy,"roundsWon");
        if(N(team,"roundsWon")==0&&N(enemy,"roundsWon")==0&&(N(team,"numPoints")>0||N(enemy,"numPoints")>0))row.Score=N(team,"numPoints")+" : "+N(enemy,"numPoints");
        if(Json.B(team,"won"))row.Result=Outcome.Win;
        else if(teams.Any(t=>Json.S(t,"teamId")!=teamId&&Json.B(t,"won")))row.Result=Outcome.Loss;
        else if(completion=="VoteDraw" || (completion=="Completed"&&N(team,"roundsWon",-1)>=0&&N(team,"roundsWon",-1)==N(enemy,"roundsWon",-2)))row.Result=Outcome.Draw;
        return row;
    }
    public static void CountDay(PlayerProfile p,DateTime day){
        p.Day=day.Date;p.Wins=p.Losses=p.Draws=p.Unknown=0;
        foreach(var m in p.Matches.Where(m=>m.Started.Date==day.Date).GroupBy(m=>m.Id).Select(g=>g.First())){
            if(m.Result==Outcome.Win)p.Wins++;else if(m.Result==Outcome.Loss)p.Losses++;else if(m.Result==Outcome.Draw)p.Draws++;else if(m.Result==Outcome.Unknown)p.Unknown++;
        }
        if(p.Unknown>0)p.DayComplete=false;
    }
    public static void Rank(PlayerProfile p,object mmr,string act){
        var queue=Json.Get(Json.Get(mmr,"QueueSkills"),"competitive");var season=String.IsNullOrEmpty(act)?null:Json.Get(Json.Get(queue,"SeasonalInfoBySeasonID"),act);
        var seasons=Json.Get(queue,"SeasonalInfoBySeasonID") as Dictionary<string,object>;
        if(seasons!=null)foreach(var s in seasons.Values){p.PeakTier=Math.Max(p.PeakTier,(int)N(s,"CompetitiveTier",-1));var wins=Json.Get(s,"WinsByTier") as Dictionary<string,object>;if(wins!=null)foreach(var kv in wins){int tier;long count;if(Int32.TryParse(kv.Key,out tier)&&Int64.TryParse(Convert.ToString(kv.Value),out count)&&count>0)p.PeakTier=Math.Max(p.PeakTier,tier);}}
        if(season!=null){p.Tier=(int)N(season,"CompetitiveTier",-1);p.RR=(int)N(season,"RankedRating",-1);p.RankIsCurrent=true;if(N(season,"GamesNeededForRating")>0||N(queue,"CurrentSeasonGamesNeededForRating")>0){p.Tier=0;p.RR=-1;}}
        else {var latest=Json.Get(mmr,"LatestCompetitiveUpdate");if(!String.IsNullOrWhiteSpace(Json.S(latest,"MatchID"))){p.Tier=(int)N(latest,"TierAfterUpdate",-1);p.RR=(int)N(latest,"RankedRatingAfterUpdate",-1);}}
        if(p.Tier<3)p.RR=-1;
        p.RankLabel=RankArt.Name(p.Tier)+(p.Tier>=3&&!p.RankIsCurrent?" · last known rank":"");
    }
}
class HistoryEngine {
    readonly Dictionary<string,object> details=new Dictionary<string,object>();
    string act="",actShard="";DateTime actExpires=DateTime.MinValue;
    public PlayerProfile Load(string id,string name,string shard,Func<string,object> get,CancellationToken cancel){
        var p=new PlayerProfile{Id=id,Name=name};var watch=Stopwatch.StartNew();
        try {
            cancel.ThrowIfCancellationRequested();
            if(actShard!=shard||DateTime.UtcNow>=actExpires){
                act="";actShard=shard;
                try{var content=get("https://shared."+shard+".a.pvp.net/content-service/v3/content");Catalog.SetSeasons(content);var current=Json.A(Json.Get(content,"Seasons")).FirstOrDefault(s=>Json.B(s,"IsActive")&&Json.S(s,"Type")=="act");act=Json.S(current,"ID");actExpires=DateTime.UtcNow.AddMinutes(30);}catch(ApiFailure e){if(e.Code==429)throw;actExpires=DateTime.UtcNow.AddMinutes(1);}catch(System.Net.WebException){actExpires=DateTime.UtcNow.AddMinutes(1);}
            }
            string pd="https://pd."+shard+".a.pvp.net";
            try{HistoryLogic.Rank(p,get(pd+"/mmr/v1/players/"+id),act);}catch(ApiFailure e){if(e.Code==429)throw;p.Message="Rank unavailable (HTTP "+e.Code+"). ";}catch(System.Net.WebException){p.Message="Could not load rank. ";}
            var rr=new Dictionary<string,int>();
            try{var updates=get(pd+"/mmr/v1/players/"+id+"/competitiveupdates?startIndex=0&endIndex=100&queue=competitive");foreach(var u in Json.A(Json.Get(updates,"Matches")))if(Json.S(u,"MatchID")!=""&&Json.Get(u,"RankedRatingEarned")!=null)rr[Json.S(u,"MatchID")]=(int)HistoryLogic.N(u,"RankedRatingEarned");}catch(ApiFailure e){if(e.Code==429)throw;}catch(System.Net.WebException){}
            var entries=new List<object>();bool reachedBoundary=false,malformed=false;var seen=new HashSet<string>();
            for(int start=0;start<100;start+=20){
                cancel.ThrowIfCancellationRequested();
                var response=get(pd+"/match-history/v1/history/"+id+"?startIndex="+start+"&endIndex="+(start+20));
                if(!(Json.Get(response,"History") is object[]))throw new FormatException();
                var page=Json.A(Json.Get(response,"History")).ToList();
                foreach(var entry in page){Guid valid;if(!Guid.TryParse(Json.S(entry,"MatchID"),out valid)||HistoryLogic.N(entry,"GameStartTime")<=0){malformed=true;continue;}if(seen.Add(Json.S(entry,"MatchID")))entries.Add(entry);}
                bool older=page.Any(v=>HistoryLogic.N(v,"GameStartTime")>0&&HistoryLogic.LocalTime(HistoryLogic.N(v,"GameStartTime")).Date<p.Day);
                long total=HistoryLogic.N(response,"Total",-1);
                if(page.Count<20||(total>=0&&start+page.Count>=total)||older){reachedBoundary=true;break;}
                if(watch.Elapsed.TotalSeconds>18)break;
            }
            p.HistoryAvailable=true;p.DayComplete=reachedBoundary&&!malformed;
            var selected=entries.OrderByDescending(v=>HistoryLogic.N(v,"GameStartTime")).Where((v,i)=>i<20||HistoryLogic.LocalTime(HistoryLogic.N(v,"GameStartTime")).Date==p.Day).ToList();
            foreach(var entry in selected){
                cancel.ThrowIfCancellationRequested();string match=Json.S(entry,"MatchID");Guid parsed;
                if(!Guid.TryParse(match,out parsed)){p.DayComplete=false;continue;}
                var date=HistoryLogic.LocalTime(HistoryLogic.N(entry,"GameStartTime"));
                object data;MatchRow row;
                if(!details.TryGetValue(shard+match,out data)){
                    if(watch.Elapsed.TotalSeconds>22){p.DayComplete=false;p.Message+="Partial results. Retrying automatically. ";break;}
                    try{
                        // Spread calls while keeping the UI and roster polling independent.
                        if(cancel.WaitHandle.WaitOne(100))cancel.ThrowIfCancellationRequested();
                        data=get(pd+"/match-details/v1/matches/"+match);
                        if(Json.B(Json.Get(data,"matchInfo"),"isCompleted")){if(details.Count>350)details.Clear();details[shard+match]=data;}
                    }catch(ApiFailure e){if(e.Code==429)throw;p.Matches.Add(new MatchRow{Id=match,Started=date,Queue=HistoryLogic.QueueName(Json.S(entry,"QueueID"))});continue;}
                     catch(System.Net.WebException){p.Matches.Add(new MatchRow{Id=match,Started=date,Queue=HistoryLogic.QueueName(Json.S(entry,"QueueID"))});continue;}
                }
                row=HistoryLogic.ParseMatch(data,id);if(row.Id=="")row.Id=match;if(row.Started==DateTime.MinValue)row.Started=date;if(rr.ContainsKey(match))row.RRChange=rr[match];p.Matches.Add(row);
            }
            p.Matches=p.Matches.OrderByDescending(m=>m.Started).ToList();
        }catch(OperationCanceledException){throw;}
         catch(ApiFailure e){p.DayComplete=false;p.Message+="Request unavailable (HTTP "+e.Code+").";p.RetryAfter=DateTime.UtcNow.AddSeconds(e.Code==429?90:45);}
         catch(Exception){p.DayComplete=false;p.Message+="Match history unavailable. Retrying automatically.";p.RetryAfter=DateTime.UtcNow.AddSeconds(45);}
        HistoryLogic.CountDay(p,p.Day);if(!p.DayComplete&&p.HistoryAvailable&&p.RetryAfter>DateTime.UtcNow.AddSeconds(30))p.RetryAfter=DateTime.UtcNow.AddSeconds(p.Message.Contains("429")?90:30);
        return p;
    }
}
partial class RiotReader {
    readonly HistoryEngine historyEngine=new HistoryEngine();
    public PlayerProfile LoadProfile(Player player,CancellationToken cancel){
        try{
            Guid id;if(!Guid.TryParse(player.Id,out id))throw new FormatException();
            var unavailable=InitializeSession(false);if(unavailable!=null)return new PlayerProfile{Id=player.Id,Name=player.Name,Message=unavailable.Status,RetryAfter=DateTime.UtcNow.AddSeconds(30)};
            Catalog.LoadAgents();
            return historyEngine.Load(player.Id,player.Name,shard,url=>{cancel.ThrowIfCancellationRequested();return Request(url,"GET",null,false,false);},cancel);
        }catch(OperationCanceledException){throw;}
         catch(Exception){return new PlayerProfile{Id=player.Id,Name=player.Name,Message="Sign in to Riot Client to load match history.",RetryAfter=DateTime.UtcNow.AddSeconds(30)};}
        finally{basic=access=entitlement=null;}
    }
}
static class RankArt {
    static readonly Dictionary<int,Image> icons=new Dictionary<int,Image>();
    static RankArt(){for(int i=0;i<=27;i++){using(var stream=typeof(RankArt).Assembly.GetManifestResourceStream("rank-"+i+".png")){if(stream!=null)using(var img=Image.FromStream(stream))icons[i]=new Bitmap(img);}}}

    public static string Name(int tier){if(tier<0)return "Rank unavailable";if(tier<3)return "Unranked";if(tier==27)return "Radiant";string[] groups={"Iron","Bronze","Silver","Gold","Platinum","Diamond","Ascendant","Immortal"};int group=(tier-3)/3;return group<groups.Length?groups[group]+" "+((tier-3)%3+1):"Unknown rank";}
    public static Color ColorFor(int tier){if(tier<3)return Palette.Muted;Color[] colors={Color.FromArgb(147,151,153),Color.FromArgb(177,130,91),Color.FromArgb(207,216,219),Color.FromArgb(227,192,97),Color.FromArgb(91,190,194),Color.FromArgb(171,141,229),Color.FromArgb(101,195,150),Color.FromArgb(218,103,131),Color.FromArgb(244,227,169)};return colors[Math.Min(8,(tier-3)/3)];}
    public static void Draw(Graphics g,int tier,Rectangle r){
        Image icon;if(icons.TryGetValue(tier<3?0:tier,out icon)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(icon,r);return;}
        var c=ColorFor(tier);int cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
        Point[] outer={new Point(cx,r.Y+1),new Point(r.Right-2,cy),new Point(cx,r.Bottom-1),new Point(r.X+2,cy)};
        using(var b=new SolidBrush(Color.FromArgb(55,c)))using(var pen=new Pen(c,1.5f)){g.FillPolygon(b,outer);g.DrawPolygon(pen,outer);}
        if(tier>=3){int n=tier>=27?3:(tier-3)%3+1;using(var b=new SolidBrush(c))for(int i=0;i<n;i++)g.FillRectangle(b,cx-n*2+i*4,cy-3,2,6);}
        else using(var pen=new Pen(c))g.DrawLine(pen,cx-4,cy,cx+4,cy);
    }
}
static class HistoryChecks {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
    static object D(object value){return Json.Codec.DeserializeObject(Json.Codec.Serialize(value));}
    static long Millis(DateTime date){return (long)(date.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
    static object Match(string id,string who,bool won,bool complete,string completion,DateTime start){return D(new {matchInfo=new {matchId=id,mapId="/Game/Maps/Ascent/Ascent",gameStartMillis=Millis(start),isCompleted=complete,completionState=completion,queueID="competitive"},players=new[]{new {subject=who,teamId="Blue",stats=new {kills=20,deaths=12,assists=7}}},teams=new[]{new {teamId="Blue",won=won,roundsWon=won?13:9},new {teamId="Red",won=!won,roundsWon=won?9:13}}});}
    public static int Run(){
        count=0;string id="11111111-1111-1111-1111-111111111111",match="22222222-2222-2222-2222-222222222222";DateTime now=DateTime.Today.AddHours(12);
        var row=HistoryLogic.ParseMatch(Match(match,id,true,true,"Completed",now),id);
        Check(row.Result==Outcome.Win&&row.Score=="13 : 9"&&row.Kda=="20 / 12 / 7","match result and KDA come from player's team");
        Check(HistoryLogic.ParseMatch(Match(match,id,false,true,"Completed",now),id).Result==Outcome.Loss,"loss uses opposing team winner");
        Check(HistoryLogic.ParseMatch(Match(match,id,false,false,"",now),id).Result==Outcome.Ignored,"unfinished matches do not count as losses");
        Check(HistoryLogic.ParseMatch(Match(match,id,false,true,"Remake",now),id).Result==Outcome.Ignored,"remakes excluded");
        Check(HistoryLogic.ParseMatch(Match(match,id,true,true,"Completed",now),"other").Result==Outcome.Unknown,"missing player never guessed");
        var draw=D(new {matchInfo=new {matchId=match,isCompleted=true,completionState="VoteDraw",gameStartMillis=Millis(now)},players=new[]{new {subject=id,teamId="Blue"}},teams=new[]{new {teamId="Blue",won=false,roundsWon=14},new {teamId="Red",won=false,roundsWon=14}}});
        Check(HistoryLogic.ParseMatch(draw,id).Result==Outcome.Draw,"draw is separate from loss");
        var profile=new PlayerProfile {HistoryAvailable=true,DayComplete=true};
        for(int i=0;i<5;i++)profile.Matches.Add(new MatchRow {Id="match"+i,Started=now,Result=i==4?Outcome.Loss:Outcome.Win});
        profile.Matches.Add(new MatchRow{Id="yesterday",Started=DateTime.Today.AddTicks(-1),Result=Outcome.Loss});
        profile.Matches.Add(profile.Matches[0]);HistoryLogic.CountDay(profile,DateTime.Today);
        Check(profile.Record=="4-1"&&profile.RecordColor==Palette.Green,"daily wins green; previous day and duplicates excluded");
        profile.Matches=Enumerable.Range(0,5).Select(i=>new MatchRow{Id="l"+i,Started=now,Result=i==0?Outcome.Win:Outcome.Loss}).ToList();HistoryLogic.CountDay(profile,DateTime.Today);
        Check(profile.Record=="1-4"&&profile.RecordColor==Palette.Red,"negative daily balance red");
        profile.Matches.Clear();HistoryLogic.CountDay(profile,DateTime.Today);Check(profile.Record=="0-0"&&profile.RecordColor==Palette.Muted,"empty day neutral");
        profile.Matches.Add(new MatchRow{Id="unknown",Started=now,Result=Outcome.Unknown});HistoryLogic.CountDay(profile,DateTime.Today);Check(profile.Record=="0-0*"&&!profile.DayComplete,"unknown results label balance partial");
        Check(HistoryLogic.LocalTime(Millis(DateTime.Today.AddMinutes(-1))).Date==DateTime.Today.AddDays(-1)&&HistoryLogic.LocalTime(Millis(DateTime.Today.AddMinutes(1))).Date==DateTime.Today,"local midnight boundary respected");
        var mmr=D(new {QueueSkills=new {competitive=new {CurrentSeasonGamesNeededForRating=0,SeasonalInfoBySeasonID=new Dictionary<string,object>{{"old",new {CompetitiveTier=27,RankedRating=90}},{"active",new {CompetitiveTier=20,RankedRating=42,GamesNeededForRating=0}}}}},LatestCompetitiveUpdate=new {MatchID=match,TierAfterUpdate=27,RankedRatingAfterUpdate=90}});
        var rank=new PlayerProfile();HistoryLogic.Rank(rank,mmr,"active");Check(rank.Tier==20&&rank.RR==42&&rank.RankIsCurrent,"current act chosen explicitly rather than highest historical rank");
        rank=new PlayerProfile();HistoryLogic.Rank(rank,mmr,"");Check(rank.Tier==27&&!rank.RankIsCurrent&&rank.RankLabel.Contains("last known rank"),"fallback rank labelled historical");
        int requests=0;
        Func<string,object> fake=url=>{
            requests++;
            if(url.Contains("content-service"))return D(new {Seasons=new[]{new {ID="active",Type="act",IsActive=true}}});
            if(url.Contains("/mmr/"))return mmr;
            if(url.Contains("match-history"))return D(new {Total=1,History=new[]{new {MatchID=match,GameStartTime=Millis(now),QueueID="competitive"}}});
            if(url.Contains("match-details"))return Match(match,id,true,true,"Completed",now);
            throw new Exception("Unexpected request");
        };
        var engine=new HistoryEngine();var loaded=engine.Load(id,"Test","na",fake,CancellationToken.None);
        Check(loaded.Record=="1-0"&&loaded.Tier==20&&loaded.Matches.Count==1,"integrated rank/history/details loading with mock service");
        int before=requests;engine.Load(id,"Test","na",fake,CancellationToken.None);Check(requests-before==3,"act and completed match details cached");
        var unavailable=new HistoryEngine().Load(id,"Test","na",url=>{throw new ApiFailure(403);},CancellationToken.None);Check(!unavailable.HistoryAvailable&&unavailable.Record=="—","403 never displayed as zero wins/losses");
        var limited=new HistoryEngine().Load(id,"Test","na",url=>{throw new ApiFailure(429);},CancellationToken.None);Check(limited.Message.Contains("429")&&limited.RetryAfter>DateTime.UtcNow.AddSeconds(60),"rate limits back off");
        int pages=0;string oldMatch="33333333-3333-3333-3333-333333333333";
        Func<string,object> paged=url=>{
            if(url.Contains("match-history")){pages++;return url.Contains("startIndex=0&")?D(new {Total=21,History=Enumerable.Range(0,20).Select(i=>new {MatchID=match,GameStartTime=Millis(now),QueueID="competitive"}).ToArray()}):D(new {Total=21,History=new[]{new {MatchID=oldMatch,GameStartTime=Millis(now.AddDays(-1)),QueueID="competitive"}}});}
            if(url.Contains(oldMatch))return Match(oldMatch,id,false,true,"Completed",now.AddDays(-1));
            return fake(url);
        };
        var paginated=new HistoryEngine().Load(id,"Test","na",paged,CancellationToken.None);
        Check(pages==2&&paginated.Record=="1-0"&&paginated.Matches.Count==2,"pagination reaches prior day and deduplicates matches");
        var incomplete=new HistoryEngine().Load(id,"Test","na",url=>{if(url.Contains("match-details"))throw new ApiFailure(403);return fake(url);},CancellationToken.None);
        Check(incomplete.Record=="0-0*"&&incomplete.Unknown==1,"denied match detail marks daily count incomplete");
        var malformed=new HistoryEngine().Load(id,"Test","na",url=>{if(url.Contains("match-history"))return D(new {History="not-an-array"});return fake(url);},CancellationToken.None);
        Check(!malformed.HistoryAvailable&&malformed.Record=="—","malformed history is not mistaken for empty history");
        rank=new PlayerProfile();HistoryLogic.Rank(rank,D(new {QueueSkills=new {competitive=new {CurrentSeasonGamesNeededForRating=1,SeasonalInfoBySeasonID=new Dictionary<string,object>{{"active",new {CompetitiveTier=20,RankedRating=42,GamesNeededForRating=1}}}}}}),"active");
        Check(rank.Tier==0&&rank.RR==-1,"placements do not present an old rank as current");
        using(var c=new CancellationTokenSource()){c.Cancel();bool canceled=false;try{engine.Load(id,"Test","na",fake,c.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled,"cancellation exits before requests");}
        Console.WriteLine(count+" history checks passed.");return 0;
    }
}
}
