using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Threading.Tasks;

[assembly: System.Reflection.AssemblyTitle("Lunar")]
[assembly: System.Reflection.AssemblyProduct("Lunar")]
[assembly: System.Reflection.AssemblyFileVersion("2.3.0.0")]

namespace Lunar {
static class Json {
    public static JavaScriptSerializer Codec { get { return new JavaScriptSerializer { MaxJsonLength = 8388608 }; } }
    public static object Get(object o, string key) { var d=o as Dictionary<string,object>; return d!=null && d.ContainsKey(key)?d[key]:null; }
    public static string S(object o,string key) { return Convert.ToString(Get(o,key)) ?? ""; }
    public static object GetAnyCase(object o,params string[] keys) {
        var values=o as Dictionary<string,object>;
        if(values==null)return null;
        foreach(var key in keys){
            object value;
            if(values.TryGetValue(key,out value))return value;
            var match=values.FirstOrDefault(pair=>String.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase));
            if(match.Key!=null)return match.Value;
        }
        return null;
    }
    public static IEnumerable<object> A(object o) { return (o as object[]) ?? new object[0]; }
    public static bool B(object o,string key) { return Get(o,key) is bool && (bool)Get(o,key); }
}
class ApiFailure:Exception {
    public int Code; public ApiFailure(int c):base("HTTP "+c) { Code=c; }
}
class Player {
    public string Id,Name,Team,State,CardId,AgentId,PartyId,Region;
    public int AccountLevel;public bool AccountLevelHidden;
    public override string ToString(){return Name??"";}
    public Player Copy(){return (Player)MemberwiseClone();}
}
class Snapshot {
    public Player Self;
    public string Status="Waiting for VALORANT",Detail="Open VALORANT and enter agent select. Lunar connects automatically.",Side="Starting side unavailable",Match="",Team="";
    public string Map="";
    public int? AllyScore,EnemyScore;
    public List<Player> Allies=new List<Player>(),Enemies=new List<Player>();
    public int Delay=5000;
    public string Diagnostics="No match data has been requested.";
    public Snapshot CopyRoster(){
        var copy=(Snapshot)MemberwiseClone();
        copy.Allies=Allies.Select(player=>player.Copy()).ToList();
        copy.Enemies=Enemies.Select(player=>player.Copy()).ToList();
        copy.Self=Self==null?null:Self.Copy();
        return copy;
    }

}
partial class RiotReader {
    Player cachedSelf;
    Player GetSelfPlayer(){
        if(cachedSelf!=null&&cachedSelf.Id==self)return cachedSelf;
        var me=new Player{Id=self,Name="Your profile",State="YOU",Region=region};
        try{var response=Request("https://pd."+shard+".a.pvp.net/name-service/v2/players","PUT",new[]{self},false,false);var entry=Json.A(response).FirstOrDefault(x=>Json.S(x,"Subject")==self);if(Json.S(entry,"GameName")!=""){me.Name=Json.S(entry,"GameName")+"#"+Json.S(entry,"TagLine");cachedSelf=me;}}
        catch(ApiFailure e){if(e.Code==429)throw;}catch(WebException){}
        return me;
    }
    string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string basic,origin,access,entitlement,version,shard,region,self;
    string nameMatch=""; Dictionary<string,string> names=new Dictionary<string,string>();
    static string ReadShared(string path,int limit) {
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            if(limit>0 && f.Length>limit) f.Seek(-limit,SeekOrigin.End);
            using(var r=new StreamReader(f)) return r.ReadToEnd();
        }
    }
    object Request(string url,string method,object body,bool isLocal,bool absent,int timeout=6000) {
        var uri=new Uri(url);
        if(isLocal ? uri.Host!="127.0.0.1" : !Regex.IsMatch(uri.Host,@"^((?:pd|shared)\.(na|eu|ap|kr|pbe)|glz-(na|latam|br|eu|ap|kr)-1\.(na|eu|ap|kr|pbe))\.a\.pvp\.net$")) throw new InvalidOperationException();
        var req=(HttpWebRequest)WebRequest.Create(uri);
        req.UserAgent="Lunar/1.67";req.Method=method; req.Timeout=timeout; req.ReadWriteTimeout=timeout; req.AllowAutoRedirect=false;
        req.Accept="application/json"; req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        if(isLocal) {
            req.Proxy=null;
            req.ServerCertificateValidationCallback=delegate { return true; };
            req.Headers[HttpRequestHeader.Authorization]="Basic "+basic;
        } else {
            req.Headers[HttpRequestHeader.Authorization]="Bearer "+access;
            req.Headers["X-Riot-Entitlements-JWT"]=entitlement;
            req.Headers["X-Riot-ClientVersion"]=version;
            req.Headers["X-Riot-ClientPlatform"]=Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"platformType\":\"PC\",\"platformOS\":\"Windows\",\"platformOSVersion\":\"10.0.19042.1.256.64bit\",\"platformChipset\":\"Unknown\"}"));
        }
        if(body!=null) {
            byte[] data=Encoding.UTF8.GetBytes(Json.Codec.Serialize(body)); req.ContentType="application/json"; req.ContentLength=data.Length;
            using(var s=req.GetRequestStream()) s.Write(data,0,data.Length);
        }
        try {
            using(var res=(HttpWebResponse)req.GetResponse()) {
                if((int)res.StatusCode>=300) throw new ApiFailure((int)res.StatusCode);
                using(var sr=new StreamReader(res.GetResponseStream())) return Json.Codec.DeserializeObject(sr.ReadToEnd());
            }
        } catch(WebException e) {
            var res=e.Response as HttpWebResponse;
            if(res==null) throw;
            int code=(int)res.StatusCode; res.Dispose();
            if(code==404 && absent) return null;
            throw new ApiFailure(code);
        }
    }
    static bool HasProcess(string name) {
        var ps=Process.GetProcessesByName(name); bool found=ps.Length>0;
        foreach(var p in ps) p.Dispose(); return found;
    }
    bool ConnectLocalClient(){
        string[] parts=ReadShared(Path.Combine(local,@"Riot Games\Riot Client\Config\lockfile"),4096).Trim().Split(':');
        int port,pid;
        if(parts.Length!=5||parts[4]!="https"||!Int32.TryParse(parts[1],out pid)||!ClientProcessAlive(pid)||
            !Int32.TryParse(parts[2],out port)||port<1||port>65535)return false;
        origin="https://127.0.0.1:"+port;
        basic=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+parts[3]));
        return true;
    }
    bool wasInMatch;
    public Snapshot Poll(){return Poll(null);}
    internal Snapshot Poll(Action<Snapshot> publishRoster) {
        try { return PollInternal(publishRoster); }
        catch(ApiFailure e) {
            var s=new Snapshot();
            s.Status=e.Code==401?"Session expired":e.Code==403?"Request rejected by the service":e.Code==429?"Rate limit reached":"Service temporarily unavailable";
            s.Detail="HTTP "+e.Code+". Retrying automatically.";
            s.Delay=e.Code==429?60000:15000; return s;
        }
        catch(WebException) { return new Snapshot {Status="Client or service unreachable",Detail="Retrying in 15 seconds.",Delay=15000}; }
        catch(IOException) { return new Snapshot {Status="Waiting for Riot Client files",Detail="Open Riot Client and VALORANT. Lunar will retry automatically."}; }
        catch(UnauthorizedAccessException) { return new Snapshot {Status="Cannot access local Riot Client files",Detail="Open Lunar under the same Windows account as the game.",Delay=15000}; }
        catch(Exception) { return new Snapshot {Status="Unexpected client response",Detail="The service may have changed its response format.",Delay=15000}; }
        finally { basic=access=entitlement=null; }
    }
    bool tracing; // log every step (only for profile/tracker session checks, not the 3-second live poll)
    void Trace(string message){if(tracing)LunarLog.Write("session",message);}
    static string ProcessNameOf(int pid){try{using(var p=Process.GetProcessById(pid)){try{return p.ProcessName;}catch(Exception e){return "(running, access denied: "+e.GetType().Name+")";}}}catch(ArgumentException){return "(not running)";}catch(Exception e){return "(unknown: "+e.GetType().Name+")";}}
    Snapshot InitializeSession(bool requireGame=true,bool fetchCurrentVersion=false) {
        if(requireGame&&!HasProcess("VALORANT-Win64-Shipping")) { names.Clear(); nameMatch=""; return new Snapshot(); }
        string lockPath=Path.Combine(local,@"Riot Games\Riot Client\Config\lockfile");
        Trace("lockfile "+(File.Exists(lockPath)?"found":"MISSING")+" (LocalAppData\\Riot Games\\Riot Client\\Config)");
        string[] lf=ReadShared(lockPath,4096).Trim().Split(':');
        int port;
        if(lf.Length!=5 || !Int32.TryParse(lf[2],out port) || port<1 || port>65535 || lf[4]!="https"){Trace("lockfile unreadable: "+lf.Length+" fields, protocol '"+(lf.Length>4?lf[4]:"")+"'");throw new InvalidOperationException();}
        int clientPid;
        bool pidOk=Int32.TryParse(lf[1],out clientPid);
        Trace("lockfile process: "+(pidOk?ProcessNameOf(clientPid):"(no pid)")+" · running Riot processes: "+String.Join(", ",Process.GetProcesses().Select(p=>{try{return p.ProcessName;}catch(Exception){return "";}}).Where(n=>n.IndexOf("riot",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("valorant",StringComparison.OrdinalIgnoreCase)>=0).Distinct()));
        if(fetchCurrentVersion&&(!pidOk||!ClientProcessAlive(clientPid))){
            // The lockfile PID can't be confirmed, but if Riot Client services are running the port may still be valid:
            // try it — the entitlement request below tells whether the session really works.
            if(HasProcess("RiotClientServices")||HasProcess("Riot Client"))Trace("lockfile PID not confirmed, but Riot Client is running → trying its port");
            else{Trace("Riot Client process not running → 'Waiting for Riot Client'");return new Snapshot {Status="Waiting for Riot Client"};}
        }
        basic=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+lf[3])); origin="https://127.0.0.1:"+port;
        if(fetchCurrentVersion&&!ReadSessionAuthorization()){Trace("entitlements: no access/entitlement token (not signed in?)");return new Snapshot {Status="Waiting for Riot Client sign-in"};}
        Trace("entitlements: signed in");
        var sessions=Request(origin+"/product-session/v1/external-sessions","GET",null,true,fetchCurrentVersion) as Dictionary<string,object>;
        version=""; region=""; shard="";
        Trace("external sessions: "+(sessions==null?"none":sessions.Count+" · products: "+String.Join(", ",sessions.Values.Select(v=>Json.S(v,"productId")).Distinct())));
        if(sessions!=null) foreach(var session in sessions.Values) {
            if(Json.S(session,"productId")!="valorant") continue;
            version=Json.S(session,"version");
            string args=String.Join(" ",Json.A(Json.Get(Json.Get(session,"launchConfiguration"),"arguments")).Select(x=>Convert.ToString(x)));
            var r=Regex.Match(args,@"-ares-deployment=([a-z]+)"); if(r.Success) region=r.Groups[1].Value;
        }
        Trace("from VALORANT session: version "+(version==""?"-":version)+" · region "+(region==""?"-":region));
        string logPath=Path.Combine(local,@"VALORANT\Saved\Logs\ShooterGame.log");
        Trace("ShooterGame.log "+(File.Exists(logPath)?"found":"missing"));
        if(File.Exists(logPath)) {
            string log=ReadShared(logPath,4*1024*1024);
            var hosts=Regex.Matches(log,@"https://glz-(na|latam|br|eu|ap|kr)-1\.(na|eu|ap|kr|pbe)\.a\.pvp\.net");
            if(hosts.Count>0) { var h=hosts[hosts.Count-1]; region=h.Groups[1].Value; shard=h.Groups[2].Value; }
        }
        if(fetchCurrentVersion&&!version.StartsWith("release-",StringComparison.OrdinalIgnoreCase))version=RiotStore.CurrentClientVersion();
        if(version==""){try{using(var stream=typeof(RiotReader).Assembly.GetManifestResourceStream("version.json"))using(var rd=new StreamReader(stream))version=Json.S(Json.Get(Json.Codec.DeserializeObject(rd.ReadToEnd()),"data"),"riotClientVersion");}catch{}}
        if(region==""){try{var location=Request(origin+"/riotclient/region-locale","GET",null,true,false);region=RegionFromClient(Json.S(location,"region"),Json.S(location,"webRegion"));Trace("region-locale: region '"+Json.S(location,"region")+"' webRegion '"+Json.S(location,"webRegion")+"' → '"+region+"'");}catch(ApiFailure e){Trace("region-locale failed: HTTP "+e.Code);}catch(WebException e){Trace("region-locale failed: "+e.Status);}}
        Trace("result: region '"+region+"' · shard '"+shard+"' · version '"+version+"'");
        if(shard=="") shard=(region=="br"||region=="latam")?"na":region;
        if((!fetchCurrentVersion&&!Regex.IsMatch(region,@"^(na|latam|br|eu|ap|kr)$")) || !Regex.IsMatch(shard,@"^(na|eu|ap|kr|pbe)$") || version=="")
            return new Snapshot {Status="Detecting region and client version",Detail="Waiting for the client to finish loading."};
        if(!fetchCurrentVersion&&!ReadSessionAuthorization())
            return new Snapshot {Status="Waiting for Riot Client sign-in"};
        return null;
    }
    // Riot Client regions ("BR", "LA1", "EUW", "OC1"…) mapped to VALORANT deployments.
    internal static string RegionFromClient(string region,string webRegion){
        foreach(var raw in new[]{region,webRegion}){
            string r=(raw??"").Trim().ToLowerInvariant();
            if(r=="")continue;
            if(Regex.IsMatch(r,@"^(na|latam|br|eu|ap|kr)$"))return r;
            if(r.StartsWith("br"))return "br";
            if(r.StartsWith("la"))return "latam";
            if(r.StartsWith("na")||r=="us")return "na";
            if(r.StartsWith("kr"))return "kr";
            if(r.StartsWith("euw")||r.StartsWith("eune")||r.StartsWith("tr")||r.StartsWith("ru")||r.StartsWith("me")||r=="eun1"||r=="euw1")return "eu";
            if(r.StartsWith("oc")||r.StartsWith("jp")||r.StartsWith("sg")||r.StartsWith("ph")||r.StartsWith("th")||r.StartsWith("tw")||r.StartsWith("vn")||r.StartsWith("id"))return "ap";
        }
        return "";
    }
    // Why the Riot Client session could not be used (shown instead of a generic "sign in" message).
    internal static string LastSessionProblem="";
    internal RiotStatisticsSession GetStatisticsSession(){
        tracing=true;
        LunarLog.Write("session","--- profile session check ---");
        try{
            var pending=InitializeSession(false,true);
            if(pending!=null){LastSessionProblem=pending.Status=="Waiting for Riot Client"?"Open Riot Client and sign in.":pending.Status=="Waiting for Riot Client sign-in"?"Sign in to Riot Client to search profiles. Lunar will reuse that session automatically.":"Could not detect your Riot region. Open VALORANT once, then try again.";return null;}
            if(String.IsNullOrWhiteSpace(access)||String.IsNullOrWhiteSpace(entitlement)){LastSessionProblem="Sign in to Riot Client to search profiles. Lunar will reuse that session automatically.";return null;}
            if(String.IsNullOrWhiteSpace(region)){LastSessionProblem="Could not detect your Riot region. Open VALORANT once, then try again.";return null;}
            if(String.IsNullOrWhiteSpace(version)){LastSessionProblem="Riot client version unavailable.";return null;}
            LastSessionProblem="";
            LunarLog.Write("session","OK · region "+region+" · version "+version);
            return new RiotStatisticsSession{Access=access,Entitlement=entitlement,Version=version,Region=region};
        }catch(FileNotFoundException e){LastSessionProblem="Open Riot Client and sign in.";LunarLog.Error("session","lockfile not found",e);return null;}
        catch(DirectoryNotFoundException e){LastSessionProblem="Open Riot Client and sign in.";LunarLog.Error("session","Riot Client folder not found",e);return null;}
        catch(InvalidOperationException e){LastSessionProblem="Open Riot Client and sign in.";LunarLog.Error("session","invalid Riot Client state",e);return null;}
        catch(ApiFailure e){LastSessionProblem="Could not connect to Riot Client. Open it and sign in.";LunarLog.Write("session","local Riot Client request failed: HTTP "+e.Code);return null;}
        catch(Exception e){LastSessionProblem="Could not connect to Riot Client. Open it and sign in.";LunarLog.Error("session","unexpected failure",e);return null;}
        finally{if(LastSessionProblem!="")LunarLog.Write("session","FAILED → "+LastSessionProblem);tracing=false;basic=access=entitlement=null;}
    }
    bool ReadSessionAuthorization(){
        var auth=Request(origin+"/entitlements/v1/token","GET",null,true,false);
        access=Json.S(auth,"accessToken"); entitlement=Json.S(auth,"token"); self=Json.S(auth,"subject");
        Guid id;
        return access!=""&&entitlement!=""&&Guid.TryParse(self,out id);
    }
    Snapshot PollInternal(Action<Snapshot> publishRoster) {
        var pending=InitializeSession();if(pending!=null)return pending;Guid id;
        string glz="https://glz-"+region+"-1."+shard+".a.pvp.net";
        bool pregame=!wasInMatch;
        var pointer=Request(glz+(pregame?"/pregame/v1/players/":"/core-game/v1/players/")+self,"GET",null,false,true);
        if(pointer==null){
            pregame=!pregame;
            pointer=Request(glz+(pregame?"/pregame/v1/players/":"/core-game/v1/players/")+self,"GET",null,false,true);
        }
        wasInMatch=pointer!=null&&!pregame;
        if(pointer==null){
            names.Clear();nameMatch="";
            RefreshLobbyParty(glz);
            return new Snapshot{Self=GetSelfPlayer(),Status="Connected · waiting for agent select",Detail="Refreshes automatically.",Delay=3500};
        }
        string match=Json.S(pointer,"MatchID");
        if(!Guid.TryParse(match,out id)) throw new InvalidOperationException();
        var data=Request(glz+(pregame?"/pregame/v1/matches/":"/core-game/v1/matches/")+match,"GET",null,false,true);
        if(data==null) return new Snapshot {Status="Match phase changed",Detail="Waiting for the next game update."};
        var snap=Parse(data,self,pregame);snap.Match=match;snap.Delay=3500;
        BeginMatchParties(snap);
        if(nameMatch!=match) { names.Clear(); nameMatch=match; }
        var all=snap.Allies.Concat(snap.Enemies).ToList();
        foreach(var player in all)player.Region=region;
        var missing=all.Where(p=>p.Id!="" && !names.ContainsKey(p.Id)).Select(p=>p.Id).Distinct().ToArray();
        string remoteStatus="cache",localStatus="not needed";
        if(missing.Length>0&&publishRoster!=null){
            ApplyNames(all,names);
            snap.Self=all.FirstOrDefault(p=>p.Id==self)??new Player{Id=self,Name="Your profile",State="YOU",Region=region};
            if(snap.Self.Name=="Name unavailable"&&cachedSelf!=null&&cachedSelf.Id==self)
                snap.Self.Name=cachedSelf.Name;
            snap.Detail="Loading Riot IDs…";
            publishRoster(snap.CopyRoster());
        }
        if(missing.Length>0) {
            try {
                var result=Request("https://pd."+shard+".a.pvp.net/name-service/v2/players","PUT",missing,false,false);
                remoteStatus="OK; records="+Json.A(result).Count();
                foreach(var n in Json.A(result)) {
                    string subject=Json.S(n,"Subject"),name=Json.S(n,"GameName"),tag=Json.S(n,"TagLine");
                    if(missing.Contains(subject) && name!="") names[subject]=name+(tag==""?"":"#"+tag);
                }
            } catch(ApiFailure e) { remoteStatus="HTTP "+e.Code; if(e.Code==429) snap.Delay=60000; }
              catch(WebException) { remoteStatus="connection failed"; }
              catch(ArgumentException) { remoteStatus="invalid response"; }
        }
        var unresolved=all.Where(p=>!names.ContainsKey(p.Id)).Select(p=>p.Id).Distinct().ToArray();
        if(unresolved.Length>0 && snap.Delay!=60000) {
            try {
                var result=Request(origin+"/player-account/lookup/v2/namesets-for-puuids","POST",new Dictionary<string,object>{{"puuids",unresolved}},true,false);
                var additional=LocalNames(result,unresolved);
                foreach(var entry in additional) names[entry.Key]=entry.Value;
                localStatus="OK; records="+Json.A(Json.Get(result,"namesets")).Count()+"; names="+additional.Count;
            } catch(ApiFailure e) { localStatus="HTTP "+e.Code; if(e.Code==429) snap.Delay=60000; }
              catch(WebException) { localStatus="connection failed"; }
              catch(ArgumentException) { localStatus="invalid response"; }
        } else if(unresolved.Length>0) localStatus="rate limit reached";
        int rawCount=pregame?Json.A(Json.Get(Json.Get(data,"AllyTeam"),"Players")).Count()+Json.A(Json.Get(Json.Get(data,"EnemyTeam"),"Players")).Count():Json.A(Json.Get(data,"Players")).Count();
        int resolvedCount=all.Count(p=>names.ContainsKey(p.Id));
        snap.Diagnostics="Phase: "+snap.Status+"\r\nPlayers in response: "+rawCount+"\r\nAllies: "+snap.Allies.Count+"\r\nOpponents: "+snap.Enemies.Count+"\r\nResolved names: "+resolvedCount+"/"+all.Count+"\r\nRemote service: "+remoteStatus+"\r\nLocal Riot Client: "+localStatus+"\r\nDiagnostics exclude tokens, IDs, names and raw responses.";
        ApplyNames(all,names);
        snap.Self=all.FirstOrDefault(p=>p.Id==self)??GetSelfPlayer();
        snap.Detail=(pregame?"Agent select":"Match in progress")+" · allies: "+snap.Allies.Count+" · opponents: "+snap.Enemies.Count+" · names: "+resolvedCount+"/"+all.Count;
        if(snap.Enemies.Count==0) snap.Detail+=" · opponent roster unavailable";
        if(resolvedCount<all.Count) snap.Detail+=" · some names are still loading";
        if(publishRoster!=null)publishRoster(snap.CopyRoster());
        EnrichParties(snap,glz);
        var parties=LiveMatchArt.Parties(snap.Allies.Concat(snap.Enemies));
        snap.Diagnostics+="\r\nConfirmed parties: "+parties.Count;
        return snap;
    }
    public static Dictionary<string,string> LocalNames(object result,IEnumerable<string> requested) {
        var allowed=new HashSet<string>(requested,StringComparer.OrdinalIgnoreCase);
        var output=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var record in Json.A(Json.Get(result,"namesets"))) {
            string id=Json.S(record,"puuid");
            var alias=Json.Get(record,"alias");
            string name=Json.S(alias,"gameName"),tag=Json.S(alias,"tagLine");
            if(allowed.Contains(id) && String.IsNullOrEmpty(Json.S(record,"error")) && !String.IsNullOrWhiteSpace(name)) output[id]=name+(String.IsNullOrWhiteSpace(tag)?"":"#"+tag);
        }
        return output;
    }
    public static void ApplyNames(IEnumerable<Player> players,Dictionary<string,string> resolved) {
        foreach(var p in players) {
            string name;
            p.Name=resolved.TryGetValue(p.Id,out name) && !String.IsNullOrWhiteSpace(name)?name:"Name unavailable";
        }
    }
    public static string Side(string team,bool pregame) {
        if(!pregame) return "Starting side unavailable in this phase";
        return team=="Red"?"ATTACK · estimated":team=="Blue"?"DEFENSE · estimated":"Starting side unavailable";
    }
    public static Snapshot Parse(object data,string self,bool pregame) {
        var s=new Snapshot {Status=pregame?"AGENT SELECT":"IN MATCH"};
        if(pregame) {
            object ally=Json.Get(data,"AllyTeam"),enemy=Json.Get(data,"EnemyTeam");
            // Never classify teams by array position. The user's ID must anchor a fallback.
            if(ally==null) ally=Json.A(Json.Get(data,"Teams")).FirstOrDefault(t=>Json.A(Json.Get(t,"Players")).Any(p=>Json.S(p,"Subject")==self));
            s.Team=Json.S(ally,"TeamID");
            s.Allies=Players(Json.Get(ally,"Players"),s.Team,self);
            if(enemy!=null && s.Team!="" && Json.S(enemy,"TeamID")!=s.Team) s.Enemies=Players(Json.Get(enemy,"Players"),Json.S(enemy,"TeamID"),self);
            if(enemy==null && s.Team!="") foreach(var t in Json.A(Json.Get(data,"Teams"))) {
                string team=Json.S(t,"TeamID");
                if(team!="" && team!=s.Team) s.Enemies.AddRange(Players(Json.Get(t,"Players"),team,self));
            }
        } else {
            var all=Players(Json.Get(data,"Players"),"",self);
            var me=all.FirstOrDefault(p=>p.Id==self); s.Team=me==null?"":me.Team;
            if(s.Team!="") { s.Allies=all.Where(p=>p.Team==s.Team).ToList(); s.Enemies=all.Where(p=>p.Team!=""&&p.Team!=s.Team).ToList(); }
        }
        s.Map=HistoryLogic.MapName(Json.S(data,"MapID"));
        s.Side=Side(s.Team,pregame); return s;
    }
    static List<Player> Players(object array,string team,string self) {
        var result=new List<Player>(); var seen=new HashSet<string>();
        foreach(var p in Json.A(array)) {
            string subject=Json.S(p,"Subject"); Guid id;
            if(!Guid.TryParse(subject,out id) || !seen.Add(subject) || Json.B(p,"IsCoach")) continue;
            string selection=Json.S(p,"CharacterSelectionState");
            var identity=Json.GetAnyCase(p,"PlayerIdentity","playerIdentity","player_identity")??p;
            bool hide=AccountLevels.Hidden(p);int level=AccountLevels.Read(p);
            result.Add(new Player {Id=subject,Name="Name unavailable",AgentId=Json.S(p,"CharacterID"),PartyId=ReadPartyId(p),CardId=Json.S(identity,"PlayerCardID"),AccountLevel=level,AccountLevelHidden=hide,Team=team==""?Json.S(p,"TeamID"):team,
                State=subject==self?"YOU":selection=="locked"?"LOCKED IN":selection=="selected"?"SELECTING":"PLAYER"});
        } return result;
    }
}
static class Palette {
    public static readonly Color Bg=Color.FromArgb(19,22,28),Sidebar=Color.FromArgb(15,18,23),Panel=Color.FromArgb(29,33,40),Raised=Color.FromArgb(36,40,48),Border=Color.FromArgb(53,59,69),Text=Color.FromArgb(239,241,244),Muted=Color.FromArgb(158,164,174),Accent=Color.FromArgb(183,143,246),AccentSoft=Color.FromArgb(53,43,68),Cyan=Color.FromArgb(198,170,255),Green=Color.FromArgb(73,190,157),Red=Color.FromArgb(183,143,246);
    // Match results: easy to tell apart at a glance (win green, loss red, draw grey).
    public static readonly Color Win=Color.FromArgb(67,205,150),Loss=Color.FromArgb(240,86,106),Draw=Color.FromArgb(176,182,194);
    public static Color ForRate(double? rate){return !rate.HasValue?Muted:rate.Value>50.0001?Win:rate.Value<49.9999?Loss:Draw;}
}
class TrackerSurface:Control {
    public Dictionary<string,PlayerProfile> Profiles=new Dictionary<string,PlayerProfile>();
    public event Action<Player> PlayerClicked;
    readonly List<KeyValuePair<Rectangle,Player>> targets=new List<KeyValuePair<Rectangle,Player>>();
    readonly ToolTip tip=new ToolTip();string hover="";
    int rosterScroll;
    internal static bool SingleRoster(Snapshot data){return data.Enemies.Count==0&&data.Allies.Count>5;}
    internal static int RosterRowHeight(int available,int count){return Math.Max(44,Math.Min(49,available/Math.Max(1,count)));}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(SingleRoster(Data)){rosterScroll=Math.Max(0,rosterScroll-Math.Sign(e.Delta)*88);Invalidate();}}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Focus();}
    public Player At(Point point){return targets.Where(t=>t.Key.Contains(point)).Select(t=>t.Value).FirstOrDefault();}
    public void ActivateFirstPlayer(){if(targets.Count==0)throw new InvalidOperationException("No player hit regions");var r=targets[0].Key;OnMouseClick(new MouseEventArgs(MouseButtons.Left,1,r.X+3,r.Y+3,0));}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);var p=At(e.Location);if(e.Button==MouseButtons.Left&&p!=null&&PlayerClicked!=null)PlayerClicked(p);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var p=At(e.Location);Cursor=p==null?Cursors.Default:Cursors.Hand;string id=p==null?"":p.Id??p.Name;if(id!=hover){
        hover=id;
        PlayerProfile profile;
        string peak=p!=null&&p.Id!=null&&Profiles.TryGetValue(p.Id,out profile)&&profile.PeakTier>=3
            ?" · Peak: "+RankArt.Name(profile.PeakTier):"";
        tip.SetToolTip(this,p==null?"":LunarLanguage.Translate("Click to view match history for "+p.Name)+LunarLanguage.Translate(peak));
    }}
    protected override void Dispose(bool disposing){if(disposing){tip.Dispose();if(card!=null)card.Dispose();}base.Dispose(disposing);}
    public Snapshot Data=new Snapshot(); public string Updated="Automatic connection"; public string Warning="";
    Image card;
    public void SetCard(byte[] data){
        if(card!=null){card.Dispose();card=null;}
        if(data!=null)using(var stream=new MemoryStream(data))card=TrackArt.Decode(stream);
        Invalidate();
    }
    public TrackerSurface(){DoubleBuffered=true;BackColor=Palette.Bg;ResizeRedraw=true;}
    void T(Graphics g,string text,float size,Color color,Rectangle r,bool bold=false){using(var f=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))LunarText.DrawText(g,text,f,r,color,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);}
    System.Drawing.Drawing2D.GraphicsPath Shape(Rectangle r,int radius){var p=new System.Drawing.Drawing2D.GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    void Box(Graphics g,Rectangle r,Color fill,int radius=12){using(var p=Shape(r,radius))using(var b=new SolidBrush(fill))using(var pen=new Pen(Palette.Border)){g.FillPath(b,p);g.DrawPath(pen,p);}}
    void L(Graphics g,int x,int y,int w){using(var pen=new Pen(Palette.Border))g.DrawLine(pen,x,y,x+w,y);}
    void Badge(Graphics g,string text,Rectangle r,Color color){using(var p=Shape(r,5))using(var b=new SolidBrush(Color.FromArgb(35,color)))g.FillPath(b,p);T(g,text,8,color,r,true);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);targets.Clear();var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(Data.Self==null){
            HomeWelcome.Draw(g,ClientRectangle,LunarGlyph.Live,"LIVE MATCH","Your live board loads automatically when VALORANT is running.");
            return;
        }
        int m=24,pw=236,x=m+pw+16,w=Width-x-m;
        var me=Data.Self;
        var parties=LiveMatchArt.RosterParties(Data,Profiles,DateTime.Now);
        Box(g,new Rectangle(m,24,pw,300),Palette.Panel);
        var banner=new Rectangle(m+1,25,pw-2,108);
        var state=g.Save();
        using(var shape=Shape(banner,11)){
            g.SetClip(shape,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var fill=new System.Drawing.Drawing2D.LinearGradientBrush(banner,Palette.Raised,Palette.Panel,15f))g.FillPath(fill,shape);
            if(card!=null){
                TrackArt.Cover(g,card,banner);
                using(var shade=new SolidBrush(Color.FromArgb(66,15,10,23)))g.FillRectangle(shade,banner);
            }
        }
        g.Restore(state);
        Box(g,new Rectangle(m+18,91,48,48),Palette.Raised,10);
        AgentArt.Draw(g,me.AgentId,new Rectangle(m+20,93,44,44));
        string nick=me==null?"Waiting for player":me.Name;T(g,nick,11,Palette.Text,new Rectangle(m+17,153,pw-30,28),true);
        PlayerProfile mine=null;if(me!=null&&me.Id!=null)Profiles.TryGetValue(me.Id,out mine);
        RankArt.Draw(g,mine==null?-1:mine.Tier,new Rectangle(m+18,184,22,22));T(g,mine==null?"Loading rank...":mine.RankLabel,8,Palette.Muted,new Rectangle(m+47,183,pw-60,24));
        RankArt.Draw(g,mine==null?-1:mine.PeakTier,new Rectangle(m+18,210,22,22));
        T(g,mine==null||mine.PeakTier<3?"Peak rank unavailable":"Peak · "+RankArt.Name(mine.PeakTier),
            8,mine==null||mine.PeakTier<3?Palette.Muted:RankArt.ColorFor(mine.PeakTier),new Rectangle(m+47,209,pw-60,24));
        L(g,m+18,239,pw-36);
        T(g,"STARTING SIDE",8,Palette.Muted,new Rectangle(m+18,244,pw-36,20));
        string side=LiveMatchArt.StartingSide(Data);
        Color sideColor=LiveMatchArt.SideColor(side);
        T(g,side,15,sideColor,new Rectangle(m+18,263,pw-36,29),true);
        LiveMatchArt.Recent(g,mine,m+18,298);
        int ph=Height-348-24;Box(g,new Rectangle(m,340,pw,ph),Palette.Panel);T(g,"Your session",10,Palette.Text,new Rectangle(m+18,352,pw-36,28),true);L(g,m,394,pw);
        T(g,"●",20,Palette.Accent,new Rectangle(m+96,419,45,40));T(g,Data.Status=="IN MATCH"?"Match in progress":Data.Status=="AGENT SELECT"?"Agent select":"Waiting for connection",10,Palette.Text,new Rectangle(m+18,469,pw-36,30),true);
        if(Height>=650){T(g,"Click a player to view",9,Palette.Muted,new Rectangle(m+18,507,pw-36,24));T(g,"their match history.",9,Palette.Muted,new Rectangle(m+18,531,pw-36,24));}if(Height>=690)T(g,"Today: wins and losses",9,Palette.Muted,new Rectangle(m+18,569,pw-36,24));
        T(g,Updated,8,Palette.Muted,new Rectangle(m+18,Height-65,pw-36,24));
        Box(g,new Rectangle(x,24,w,Height-48),Palette.Panel);
        T(g,Data.Map==""||Data.Map=="Map unavailable"?"Live Match":Data.Map+"  ·  Live Match",12,Palette.Text,new Rectangle(x+18,34,w-210,32),true);
        Badge(g,"  "+(Data.Status=="IN MATCH"?"IN MATCH":Data.Status=="AGENT SELECT"?"AGENT SELECT":"WAITING"),new Rectangle(x+w-150,40,130,22),Palette.Muted);
        L(g,x,79,w);
        int bannerH=Warning==""?0:54;
        if(bannerH>0)DrawWarning(g,new Rectangle(x+12,Height-24-bannerH,w-24,bannerH-8));
        if(SingleRoster(Data)){
            Team(g,new Rectangle(x,80,w,Height-104-bannerH),"Players · Deathmatch",Data.Allies,"",Palette.Muted,parties);
            return;
        }
        rosterScroll=0;int sectionH=(Height-128-bannerH)/2;
        Team(g,new Rectangle(x,80,w,sectionH),"Your team",Data.Allies,side,sideColor,parties);
        string enemySide=side=="Defense"?"Attack":side=="Attack"?"Defense":"";
        Team(g,new Rectangle(x,80+sectionH,w,sectionH),"Enemy team",Data.Enemies,enemySide,side=="Defense"?Palette.Red:Color.FromArgb(94,158,215),parties);
    }
    // Amber strip at the bottom of the live board while VALORANT runs in exclusive Fullscreen.
    void DrawWarning(Graphics g,Rectangle r){
        var smooth=g.SmoothingMode;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var shape=Skin.Round(r,10))
        using(var fill=new SolidBrush(Color.FromArgb(46,245,190,95)))
        using(var edge=new Pen(Color.FromArgb(120,245,190,95))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        g.SmoothingMode=smooth;
        using(var font=new Font("Segoe UI",8.5f,FontStyle.Bold))
            LunarText.DrawText(g,Warning,font,new Rectangle(r.X+14,r.Y+2,r.Width-28,r.Height-4),Color.FromArgb(250,214,150),TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
    void Team(Graphics g,Rectangle r,string title,List<Player> players,string side,Color color,Dictionary<string,Color> parties){
        T(g,title,10,Palette.Text,new Rectangle(r.X+18,r.Y+5,190,30),true);
        int badgeX=131;
        using(var titleFont=new Font("Segoe UI",10,FontStyle.Bold))badgeX=Math.Max(131,18+LunarText.MeasureText(title,titleFont).Width+10);
        if(side!=""&&side!="Waiting")Badge(g,"  "+side.ToUpperInvariant(),new Rectangle(r.X+badgeX,r.Y+11,73,19),color);
        T(g,players.Count+" players",8,Palette.Muted,new Rectangle(r.Right-96,r.Y+7,83,25));L(g,r.X,r.Y+40,r.Width);
        bool showPeakName=r.Width>=690;
        int peakX=r.Right-(showPeakName?307:238),rankX=peakX-145;
        T(g,"PLAYER",7,Palette.Muted,new Rectangle(r.X+18,r.Y+43,110,22));
        T(g,"RANK",7,Palette.Muted,new Rectangle(rankX,r.Y+43,100,22));
        T(g,"PEAK",7,Palette.Muted,new Rectangle(peakX,r.Y+43,showPeakName?103:43,22));T(g,"RECENT FORM",7,Palette.Muted,new Rectangle(r.Right-191,r.Y+43,100,22));T(g,"TODAY  W–L",7,Palette.Muted,new Rectangle(r.Right-88,r.Y+43,76,22));L(g,r.X,r.Y+68,r.Width);
        if(players.Count==0){T(g,"Waiting for players...",10,Palette.Muted,new Rectangle(r.X+30,r.Y+110,r.Width-60,42));return;}
        bool single=SingleRoster(Data);
        int rowH=single?RosterRowHeight(r.Height-72,players.Count):Math.Min(49,(r.Height-72)/Math.Max(5,players.Count));
        var viewport=new Rectangle(r.X+1,r.Y+70,r.Width-2,Math.Max(0,r.Height-72));
        if(single)rosterScroll=Math.Min(rosterScroll,Math.Max(0,players.Count*rowH-viewport.Height));
        var clip=g.Save();g.SetClip(viewport,System.Drawing.Drawing2D.CombineMode.Intersect);
        for(int i=0;i<players.Count;i++){
            var p=players[i];int y=r.Y+70+i*rowH-(single?rosterScroll:0);bool me=p.State=="YOU";
            var hit=Rectangle.Intersect(viewport,new Rectangle(r.X+1,y,r.Width-2,rowH));
            if(hit.IsEmpty)continue;targets.Add(new KeyValuePair<Rectangle,Player>(hit,p));
            if(me){using(var b=new SolidBrush(Palette.AccentSoft))g.FillRectangle(b,r.X+1,y,r.Width-2,rowH);using(var b=new SolidBrush(Palette.Accent))g.FillRectangle(b,r.X+1,y,2,rowH);}
            Box(g,new Rectangle(r.X+17,y+6,30,30),Palette.Raised,6);
            string name=p.Name??"Name unavailable";
            AgentArt.Draw(g,p.AgentId,new Rectangle(r.X+18,y+7,28,28));
            LiveMatchArt.Stripe(g,parties,p,new Rectangle(r.X+11,y+7,3,28));
            T(g,name,9,Palette.Text,new Rectangle(r.X+58,y+1,Math.Max(20,rankX-r.X-69),23),true);
            string agent=Catalog.Agent(p.AgentId);
            if(agent=="Agent unavailable")agent=Data.Status=="AGENT SELECT"?"Selecting agent":"Agent unavailable";
            PlayerProfile profile=null;if(p.Id!=null)Profiles.TryGetValue(p.Id,out profile);
            int labelWidth=Math.Max(20,rankX-r.X-69);
            T(g,AccountLevels.Label(p,profile)+" · "+agent,8,me?Palette.Accent:Palette.Muted,
                new Rectangle(r.X+58,y+22,labelWidth,18));
            bool fresh=profile!=null&&profile.Day==DateTime.Today;
            RankArt.Draw(g,profile==null?-1:profile.Tier,new Rectangle(rankX,y+10,25,25));
            T(g,profile==null?"...":RankArt.Name(profile.Tier)+(profile.Tier>=3&&!profile.RankIsCurrent?"*":""),8,profile==null?Palette.Muted:RankArt.ColorFor(profile.Tier),new Rectangle(rankX+31,y+7,118,24));
            int peak=profile==null?-1:profile.PeakTier;
            if(peak>=3){
                RankArt.Draw(g,peak,new Rectangle(peakX,y+10,25,25));
                if(showPeakName)T(g,RankArt.Name(peak),8,RankArt.ColorFor(peak),new Rectangle(peakX+29,y+7,82,24));
            }else T(g,"—",9,Palette.Muted,new Rectangle(peakX+5,y+7,25,26));
            LiveMatchArt.Recent(g,profile,r.Right-190,y+14);
            T(g,!fresh?"...":profile.Record,11,!fresh?Palette.Muted:profile.RecordColor,new Rectangle(r.Right-86,y+7,75,30),true);
            if(i<players.Count-1)L(g,r.X+17,y+rowH,r.Width-34);
        }
        g.Restore(clip);
        if(single&&players.Count*rowH>viewport.Height)T(g,"Scroll to see all players",8,Palette.Muted,new Rectangle(r.X+220,r.Y+8,220,23));
        L(g,r.X,r.Bottom,r.Width);
    }
}

class LunarForm:Form {
    const int ImmersiveDarkMode=20, LegacyImmersiveDarkMode=19, CaptionColor=35, CaptionTextColor=36;

    [DllImport("dwmapi.dll",ExactSpelling=true)]
    static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int valueSize);

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if(SystemInformation.HighContrast)return;

        int enabled=1;
        if(DwmSetWindowAttribute(Handle,ImmersiveDarkMode,ref enabled,sizeof(int))!=0)
            DwmSetWindowAttribute(Handle,LegacyImmersiveDarkMode,ref enabled,sizeof(int));

        int caption=ColorTranslator.ToWin32(Palette.Sidebar);
        int text=ColorTranslator.ToWin32(Palette.Text);
        DwmSetWindowAttribute(Handle,CaptionColor,ref caption,sizeof(int));
        DwmSetWindowAttribute(Handle,CaptionTextColor,ref text,sizeof(int));
    }
}

class Window:LunarForm,IMessageFilter {
    HistoryView home;SearchView tracker;StoreView storeView;FriendsView friendsView;CrosshairView crosshairView;Panel settingsView;
    NavButton trackerNav,storeNav,homeNav,liveNav,friendsNav,crosshairNav,settingsNav;
    DesktopIntegration desktop;
    const int ParallelLiveProfiles=16;
    readonly HashSet<string> statsLoading=new HashSet<string>();
    readonly HashSet<string> artLoading=new HashSet<string>();
    readonly Dictionary<string,byte[]> cardCache=new Dictionary<string,byte[]>();
    readonly Timer statsTimer=new Timer();readonly System.Threading.CancellationTokenSource statsCancel=new System.Threading.CancellationTokenSource();
    readonly Dictionary<string,PlayerProfile> profiles=new Dictionary<string,PlayerProfile>();
    readonly Dictionary<string,PlayerProfile> liveProfiles=new Dictionary<string,PlayerProfile>();
    readonly Timer liveBatchTimer=new Timer{Interval=550};
    readonly Dictionary<string,PlayerProfile> liveRanks=new Dictionary<string,PlayerProfile>();
    Snapshot current=new Snapshot();Player selected;Panel profilePage;HistoryView historyView;
    readonly List<KeyValuePair<Control,NavButton>> previousPages=new List<KeyValuePair<Control,NavButton>>();
    KeyValuePair<Control,NavButton> activePage;
    bool isPreview;DateTime statsHold=DateTime.MinValue;
    TrackerSurface surface;bool busy;
    UpdateReadyCard updateNotice;
    readonly FullscreenNoticeCard fullscreenNotice=new FullscreenNoticeCard();
    Action restartForUpdate;
    Label languageTitle,languageDescription,languageHelp,hotkeyTitle,hotkeyCurrent,hotkeyHelp,versionFooter;
    SoftSelect languageSelect;
    Button changeHotkeyButton;
    string diagnostics="Waiting for data.";
    readonly Timer timer=new Timer();readonly BackgroundWorker worker=new BackgroundWorker();readonly RiotReader reader=new RiotReader();
    public Window(bool preview=false){
        isPreview=preview;
        Icon=AppIcon.Load();Text="Lunar";ClientSize=new Size(1220,790);MinimumSize=new Size(1060,750);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Palette.Bg;ForeColor=Palette.Text;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=198,BackColor=Palette.Sidebar,Padding=new Padding(18)};
        sidebar.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Palette.Border))e.Graphics.DrawLine(pen,sidebar.Width-1,0,sidebar.Width-1,sidebar.Height);};
        var mark=new PictureBox{Image=TrackArt.Asset("lunar.png"),Location=new Point(17,21),Size=new Size(44,44),SizeMode=PictureBoxSizeMode.Zoom};
        mark.Disposed+=delegate{if(mark.Image!=null)mark.Image.Dispose();};
        sidebar.Controls.Add(mark);
        var brand=new Label{Text="Lunar",Location=new Point(68,20),Size=new Size(116,30),Font=new Font("Segoe UI",16,FontStyle.Bold),ForeColor=Palette.Text};sidebar.Controls.Add(brand);
        sidebar.Controls.Add(new Label{Text="VALORANT TRACKER",Location=new Point(70,51),Size=new Size(120,16),Font=new Font("Segoe UI",7),ForeColor=Palette.Muted});
        var brandDivider=new Panel{
            Location=new Point(13,83),Size=new Size(172,1),BackColor=Palette.Border,
            Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,TabStop=false
        };
        sidebar.Controls.Add(brandDivider);
        homeNav=Navigation("Home",LunarGlyph.Home,111);homeNav.Click+=delegate{SwitchPage(true);};sidebar.Controls.Add(homeNav);
        liveNav=Navigation("Live Match",LunarGlyph.Live,158);liveNav.Click+=delegate{SwitchPage(false);};sidebar.Controls.Add(liveNav);
        friendsNav=Navigation("Friends",LunarGlyph.Friends,205);friendsNav.Click+=delegate{ShowFriends();};sidebar.Controls.Add(friendsNav);
        trackerNav=Navigation("Tracker",LunarGlyph.Search,252);trackerNav.Click+=delegate{ShowTracker();};sidebar.Controls.Add(trackerNav);
        storeNav=Navigation("Daily Store",LunarGlyph.Shop,299);storeNav.Click+=delegate{ShowStore();};sidebar.Controls.Add(storeNav);
        crosshairNav=Navigation("Crosshair",LunarGlyph.Crosshair,346);crosshairNav.Click+=delegate{ShowCrosshair();};sidebar.Controls.Add(crosshairNav);
        settingsNav=Navigation("Settings",LunarGlyph.Settings,393);settingsNav.Click+=delegate{ShowSettings();};sidebar.Controls.Add(settingsNav);
        var bottom=new Panel{Dock=DockStyle.Bottom,Height=110};
        versionFooter=new Label{Text="LUNAR   /   "+LunarUpdates.DisplayVersion,Dock=DockStyle.Bottom,Height=26,ForeColor=Palette.Muted,Font=new Font("Segoe UI",8)};bottom.Controls.Add(versionFooter);sidebar.Controls.Add(bottom);
        surface=new TrackerSurface{Dock=DockStyle.Fill,Profiles=liveProfiles};surface.PlayerClicked+=OpenHistory;
        home=new HistoryView{Dock=DockStyle.Fill,HomeMode=true};
        home.OpenPlayer+=OpenHistory;
        var content=new Panel{Dock=DockStyle.Fill};
        tracker=new SearchView{Dock=DockStyle.Fill};
        tracker.OpenPlayer+=OpenHistory;
        tracker.FindKnownPlayer=FindTrackerPlayer;
        tracker.PreferredRegion=()=>current.Self==null?null:current.Self.Region;
        tracker.OpenResolvedPlayer+=OpenTrackerHistory;
        storeView=new StoreView(preview){Dock=DockStyle.Fill};
        friendsView=new FriendsView(preview){Dock=DockStyle.Fill};
        crosshairView=new CrosshairView{Dock=DockStyle.Fill};
        friendsView.OpenPlayer+=OpenHistory;
        settingsView=BuildSettingsView();
        var pages=new Control[]{storeView,tracker,surface,friendsView,crosshairView,home,settingsView};
        foreach(var page in pages)page.Visible=false;
        content.Controls.AddRange(pages);
        Controls.Add(content);
        Controls.Add(sidebar);
        updateNotice=BuildUpdateNotice();
        ApplyLanguage();
        Resize+=delegate{if(updateNotice.Visible)updateNotice.Present(ClientSize,false);};
        Controls.Add(updateNotice);
        Controls.Add(fullscreenNotice);
        fullscreenNotice.VisibleChanged+=delegate{if(!fullscreenNotice.Visible)TaskbarAlert.Clear(this);};
        Resize+=delegate{if(fullscreenNotice.Visible)PlaceFullscreenNotice();};
        SwitchPage(true);
        Activated+=delegate{if(updateNotice.Visible)updateNotice.Present(ClientSize,false);};
        var discord=new DiscordToggle{Checked=true,Location=new Point(0,36),Size=new Size(162,37)};
        discord.Hint.SetBounds(0,1,162,30);
        bottom.Controls.Add(discord.Hint);
        bottom.Controls.Add(discord);
        if(!preview)desktop=new DesktopIntegration(this,liveProfiles,discord,OpenHistory);
        liveBatchTimer.Tick+=delegate{PublishLiveProfiles();};
        statsTimer.Interval=1500;statsTimer.Tick+=delegate{ScheduleStats();};if(!preview)statsTimer.Start();
        worker.WorkerReportsProgress=true;
        worker.DoWork+=delegate(object sender,DoWorkEventArgs e){e.Result=reader.Poll(snapshot=>worker.ReportProgress(0,snapshot));};
        worker.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e){
            if(IsDisposed||Disposing)return;
            var roster=(Snapshot)e.UserState;
            bool changed=current.Match!=roster.Match||current.Status!=roster.Status;
            int visibleNames=current.Allies.Concat(current.Enemies).Count(player=>RiotStatsClient.HasRiotId(player.Name));
            int fetchedNames=roster.Allies.Concat(roster.Enemies).Count(player=>RiotStatsClient.HasRiotId(player.Name));
            if(changed||fetchedNames>visibleNames)ShowSnapshot(roster);
        };
        worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){busy=false;if(IsDisposed||Disposing)return;var s=e.Error==null?(Snapshot)e.Result:new Snapshot{Status="Temporary connection issue",Delay=15000};ShowSnapshot(s);timer.Interval=s.Delay;timer.Start();};
        timer.Interval=5000;timer.Tick+=delegate{RefreshData();};if(!preview)Shown+=delegate{RefreshData();};FormClosed+=delegate{if(desktop!=null)desktop.Dispose();timer.Stop();timer.Dispose();statsTimer.Stop();statsTimer.Dispose();liveBatchTimer.Stop();liveBatchTimer.Dispose();statsCancel.Cancel();};
    }
    UpdateReadyCard BuildUpdateNotice(){
        var card=new UpdateReadyCard();
        card.UpdateClicked+=delegate{if(restartForUpdate!=null)restartForUpdate();};
        return card;
    }
    internal void ShowUpdateReady(Action restart,string version){
        restartForUpdate=restart;
        if(updateNotice==null||IsDisposed)return;
        updateNotice.Version=version??"";
        updateNotice.Present(ClientSize,true);
    }
    int NoticeOffset{get{return updateNotice!=null&&updateNotice.Visible?updateNotice.Height+12:0;}}
    void PlaceFullscreenNotice(){fullscreenNotice.Present(ClientSize,NoticeOffset);}
    // Explains a missed overlay (exclusive Fullscreen). Stacks above the Update ready card when both are shown.
    internal void ShowFullscreenNotice(){
        if(IsDisposed||FullscreenNoticeCard.Disabled)return;
        PlaceFullscreenNotice();
    }
    DateTime lastAlertSound=DateTime.MinValue;
    // Sound + yellow taskbar highlight when the overlay could not appear (exclusive Fullscreen). Cleared when the
    // notice card is closed.
    internal void AlertFullscreen(){
        if(IsDisposed||FullscreenNoticeCard.Disabled)return;
        bool sound=DateTime.UtcNow-lastAlertSound>TimeSpan.FromSeconds(4);
        if(sound)lastAlertSound=DateTime.UtcNow;
        TaskbarAlert.Raise(this,"The overlay did not appear",sound);
    }
    internal void SetFullscreenWarning(bool active){
        if(surface==null)return;
        surface.Warning=active?"⚠ VALORANT is in exclusive Fullscreen: the overlay and the hotkey can't appear over the game. Use Windowed Fullscreen.":"";
        surface.Invalidate();
    }
    internal void HideUpdateReady(){if(updateNotice!=null)updateNotice.Visible=false;restartForUpdate=null;}
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);
    }
    protected override void OnHandleDestroyed(EventArgs e){
        Application.RemoveMessageFilter(this);
        base.OnHandleDestroyed(e);
    }
    internal static bool IsMouseBack(Message message){
        const int XButtonUp=0x020C,AppCommand=0x0319,BrowserBack=1;
        return message.Msg==XButtonUp&&((message.WParam.ToInt64()>>16)&0xffff)==1||
            message.Msg==AppCommand&&((message.LParam.ToInt64()>>16)&0x0fff)==BrowserBack;
    }
    bool IMessageFilter.PreFilterMessage(ref Message message){
        if(!IsMouseBack(message))return false;
        var control=Control.FromChildHandle(message.HWnd);
        return control!=null&&control.FindForm()==this&&GoBack();
    }
    protected override void WndProc(ref Message message){
        if(message.Msg==0x0319&&IsMouseBack(message)&&GoBack()){message.Result=new IntPtr(1);return;}
        if(desktop!=null)desktop.HandleMessage(message);
        base.WndProc(ref message);
    }
    NavButton Navigation(string title,LunarGlyph glyph,int top){
        return new NavButton(title,glyph){Location=new Point(12,top),Size=new Size(174,42)};
    }
    void MarkPage(NavButton active){
        foreach(var button in new[]{homeNav,liveNav,friendsNav,trackerNav,storeNav,crosshairNav,settingsNav})button.Active=button==active;
    }
    internal DesktopIntegration Desktop{get{return desktop;}}
    internal void ShowFromOtherInstance(){if(desktop!=null)desktop.ShowWindow();else{Show();Activate();}}
    internal string DiagnosticsText{get{return "Lunar "+LunarUpdates.DisplayVersion+"\r\n"+diagnostics;}}
    void ShowInfo(string title,string text,bool copyable){
        using(var dialog=new Form{Text=title,Size=new Size(640,440),StartPosition=FormStartPosition.CenterParent,BackColor=Palette.Panel,ForeColor=Palette.Text,Padding=new Padding(22)}){
            var box=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,Text=text,ScrollBars=ScrollBars.None,BackColor=Palette.Panel,ForeColor=Palette.Text,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11)};
            dialog.Controls.Add(box);if(copyable){var copy=new SoftButton{Text="Copy diagnostics",Dock=DockStyle.Bottom,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Palette.Panel,ForeColor=Palette.Text};copy.Click+=delegate{try{Clipboard.SetText(text);copy.Text="Copied";}catch(System.Runtime.InteropServices.ExternalException){copy.Text="Try again";}};dialog.Controls.Add(copy);}dialog.ShowDialog(this);
        }
    }
    void RefreshData(){if(busy)return;timer.Stop();busy=true;worker.RunWorkerAsync();}
    void ShowSnapshot(Snapshot s){
        var previousSelf=current.Self;
        bool newRoster=current.Match!=s.Match;
        bool finished=current.Status=="IN MATCH"&&s.Self!=null&&s.Match=="";
        current=s;
        if(newRoster)liveRanks.Clear();
        if(newRoster||isPreview)PublishLiveProfiles();
        if(finished){
            PlayerProfile cached;
            if(profiles.TryGetValue(s.Self.Id,out cached))cached.RetryAfter=DateTime.MinValue;
        }
        home.Player=s.Self;
        PlayerProfile own=null;
        if(s.Self!=null)profiles.TryGetValue(s.Self.Id,out own);
        if(!Object.ReferenceEquals(home.Profile,own)||!String.Equals(previousSelf==null?null:previousSelf.Name,s.Self==null?null:s.Self.Name))
            home.UpdateProfile(own);
        if(profiles.Count>60){var active=new HashSet<string>(s.Allies.Concat(s.Enemies).Select(p=>p.Id));foreach(var key in profiles.Keys.Where(k=>!active.Contains(k)&&(selected==null||selected.Id!=k)).ToList())profiles.Remove(key);}
        diagnostics=s.Diagnostics+"\r\nStatus: "+s.Status+"\r\n"+s.Detail+"\r\n"+RiotReader.PresenceSummary;surface.Data=s;surface.Updated="Updated at "+DateTime.Now.ToString("HH:mm:ss");surface.Invalidate();if(desktop!=null)desktop.Update(s);RefreshLiveCard();ScheduleStats();
        if(!isPreview)QueueLiveProfiles();
    }
    void ScheduleStats(){
        if(isPreview||statsCancel.IsCancellationRequested)return;
        var focusPlayer=profilePage!=null&&profilePage.Visible?selected:null;
        if(focusPlayer!=null&&!String.IsNullOrEmpty(focusPlayer.Id)&&RiotStatsClient.HasRiotId(focusPlayer.Name)&&
            !statsLoading.Contains(focusPlayer.Id)&&(!profiles.ContainsKey(focusPlayer.Id)||
                profiles[focusPlayer.Id].Day!=DateTime.Today||profiles[focusPlayer.Id].RetryAfter<=DateTime.UtcNow)&&
            DateTime.UtcNow>=RiotStatsClient.RemoteRetryAt)
            LoadStats(focusPlayer,null,true);
        if(DateTime.UtcNow<statsHold)return;
        var priority=new[]{profilePage!=null&&profilePage.Visible?selected:null,current.Self};
        var candidates=priority.Concat(AlternateTeams(current.Allies,current.Enemies))
            .Where(p=>p!=null&&!String.IsNullOrEmpty(p.Id)&&RiotStatsClient.HasRiotId(p.Name))
            .GroupBy(p=>p.Id).Select(group=>group.First()).ToList();
        Func<Player,bool> due=p=>!statsLoading.Contains(p.Id)&&(!profiles.ContainsKey(p.Id)||
            profiles[p.Id].Day!=DateTime.Today||profiles[p.Id].RetryAfter<=DateTime.UtcNow);
        foreach(var player in candidates.Where(p=>priority.Any(focus=>focus!=null&&focus.Id==p.Id)).Where(due)){
            if(statsLoading.Count>=ParallelLiveProfiles+1)break;
            LoadStats(player);
        }
        foreach(var player in candidates.Where(due).OrderBy(p=>profiles.ContainsKey(p.Id))){
            if(statsLoading.Count>=ParallelLiveProfiles)break;
            LoadStats(player);
        }
    }
    internal static IEnumerable<Player> AlternateTeams(IList<Player> allies,IList<Player> enemies){
        for(int i=0;i<Math.Max(allies.Count,enemies.Count);i++){
            if(i<enemies.Count)yield return enemies[i];
            if(i<allies.Count)yield return allies[i];
        }
    }
    async void LoadStats(Player player,Task<PlayerProfile> pending=null,bool interactive=false){
        if(!statsLoading.Add(player.Id))return;
        try{
            if(pending==null){
                pending=RiotStatsClient.LoadLiveAsync(player,statsCancel.Token,new Progress<PlayerProfile>(rank=>{
                    if(IsDisposed||Disposing||statsCancel.IsCancellationRequested||profiles.ContainsKey(rank.Id))return;
                    if(selected!=null&&selected.Id==rank.Id&&historyView!=null&&!historyView.IsDisposed&&historyView.Profile==null)
                        historyView.UpdateProfile(rank);
                    if(!LiveRoster().Any(member=>member.Id==rank.Id))return;
                    liveRanks[rank.Id]=rank;
                    QueueLiveProfiles();
                }),!interactive);
            }
            var profile=await pending;
            if(IsDisposed||Disposing||statsCancel.IsCancellationRequested)return;
            PlayerProfile previous;
            profiles.TryGetValue(profile.Id,out previous);
            if(profile.Limited){
                if(profile.RetryAfter>statsHold)statsHold=profile.RetryAfter;
                if(previous!=null&&previous.Day==DateTime.Today&&previous.HistoryAvailable&&previous.RankIsCurrent
                    &&(!profile.HistoryAvailable||!profile.RankIsCurrent)){
                    previous.RetryAfter=statsHold;
                    return;
                }
            }
            byte[] picture;
            if(!String.IsNullOrEmpty(profile.CardId)&&cardCache.TryGetValue(profile.CardId,out picture))
                profile.CardArt=picture;
            profiles[profile.Id]=profile;
            RenderProfile(profile);
            if(WantsCard(profile.Id))LoadProfileArt(profile);
        }catch(OperationCanceledException){}
         catch(Exception){
            if(!IsDisposed&&!statsCancel.IsCancellationRequested){
                PlayerProfile cached;
                if(profiles.TryGetValue(player.Id,out cached))cached.RetryAfter=DateTime.UtcNow.AddMinutes(2);
                else{
                    var unavailable=new PlayerProfile{Id=player.Id,Name=player.Name,Message="Profile temporarily unavailable. Retrying automatically."};
                    profiles[player.Id]=unavailable;
                    RenderProfile(unavailable);
                }
            }
        }finally{
            statsLoading.Remove(player.Id);
            liveRanks.Remove(player.Id);
            ScheduleStats();
            if(!IsDisposed&&!Disposing&&!statsCancel.IsCancellationRequested)QueueLiveProfiles();
        }
    }
    IEnumerable<Player> LiveRoster(){
        return current.Allies.Concat(current.Enemies).Where(player=>!String.IsNullOrEmpty(player.Id));
    }
    PlayerProfile LiveProfile(string id){
        PlayerProfile profile;
        return profiles.TryGetValue(id,out profile)||liveRanks.TryGetValue(id,out profile)?profile:null;
    }
    void QueueLiveProfiles(){
        var roster=LiveRoster().ToArray();
        if(roster.Length==0)return;
        bool changed=roster.Any(player=>{
            var newest=LiveProfile(player.Id);
            PlayerProfile displayed;
            liveProfiles.TryGetValue(player.Id,out displayed);
            return !Object.ReferenceEquals(newest,displayed);
        });
        if(!changed)return;
        bool stillLoading=roster.Any(player=>RiotStatsClient.HasRiotId(player.Name)&&
            (statsLoading.Contains(player.Id)||!profiles.ContainsKey(player.Id)));
        if(!stillLoading)PublishLiveProfiles();
        else if(!liveBatchTimer.Enabled)liveBatchTimer.Start();
    }
    void PublishLiveProfiles(){
        liveBatchTimer.Stop();
        liveProfiles.Clear();
        foreach(var player in LiveRoster()){
            var profile=LiveProfile(player.Id);
            if(profile!=null)liveProfiles[player.Id]=profile;
        }
        surface.Invalidate();
        if(desktop!=null)desktop.RefreshStats();
    }
    void RenderProfile(PlayerProfile profile){
        if(current.Self!=null&&current.Self.Id==profile.Id){
            RefreshLiveCard();
            home.Player=current.Self;
            home.UpdateProfile(profile);
        }
        if(selected!=null&&selected.Id==profile.Id&&historyView!=null&&!historyView.IsDisposed)
            historyView.UpdateProfile(profile);
    }
    string liveCardId="";
    async void RefreshLiveCard(){
        var player=current.Self;
        if(isPreview)return;
        string card=player==null?"":player.CardId??"";
        if(card==""){
            PlayerProfile profile;
            if(player!=null&&player.Id!=null&&profiles.TryGetValue(player.Id,out profile))card=profile.CardId??"";
        }
        if(liveCardId==card)return;
        liveCardId=card;
        SetLiveCard(null);
        if(card=="")return;
        try{
            byte[] picture;
            if(!cardCache.TryGetValue(card,out picture)){
                picture=await Task.Run(()=>RiotStatsClient.LoadCard(card),statsCancel.Token);
                if(picture!=null&&!IsDisposed&&!statsCancel.IsCancellationRequested){
                    if(cardCache.Count>=32)cardCache.Clear();
                    cardCache[card]=picture;
                }
            }
            if(!IsDisposed&&!statsCancel.IsCancellationRequested&&liveCardId==card){
                if(picture!=null)SetLiveCard(picture);
                else liveCardId=null;
            }
        }catch(OperationCanceledException){}
    }
    void SetLiveCard(byte[] picture){
        surface.SetCard(picture);

    }
    bool WantsCard(string id){
        return (home.Visible&&current.Self!=null&&current.Self.Id==id)
            ||(profilePage!=null&&profilePage.Visible&&selected!=null&&selected.Id==id);
    }
    async void LoadProfileArt(PlayerProfile profile){
        string card=profile.CardId;
        if(isPreview||String.IsNullOrEmpty(card))return;
        byte[] cachedArt;
        if(cardCache.TryGetValue(card,out cachedArt)){
            if(profile.CardArt!=cachedArt){
                profile.CardArt=cachedArt;
                RenderProfile(profile);
            }
            return;
        }
        if(!artLoading.Add(card))return;
        try{
            byte[] picture=await Task.Run(()=>RiotStatsClient.LoadCard(card),statsCancel.Token);
            if(picture==null||IsDisposed||statsCancel.IsCancellationRequested)return;
            if(cardCache.Count>=32)cardCache.Clear();
            cardCache[card]=picture;
            foreach(var cached in profiles.Values.Where(p=>p.CardId==card&&WantsCard(p.Id)).ToArray()){
                cached.CardArt=picture;
                RenderProfile(cached);
            }
        }catch(OperationCanceledException){}
         catch(Exception){}
        finally{artLoading.Remove(card);}
    }
    Player FindTrackerPlayer(string riotId){
        var player=new[]{current.Self}.Concat(LiveRoster()).FirstOrDefault(entry=>entry!=null&&
            String.Equals(entry.Name,riotId,StringComparison.OrdinalIgnoreCase));
        if(player!=null)return player;
        var cached=profiles.Values.FirstOrDefault(entry=>entry.Day==DateTime.Today&&
            String.Equals(entry.Name,riotId,StringComparison.OrdinalIgnoreCase));
        return cached==null?null:new Player{Id=cached.Id,Name=cached.Name,CardId=cached.CardId};
    }
    async void OpenTrackerHistory(Player player,Task<PlayerProfile> pending,Task<PlayerProfile> rankPreview){
        OpenHistory(player,pending);
        var target=historyView;
        var rank=await rankPreview;
        if(rank!=null&&!IsDisposed&&!Disposing&&target==historyView&&!target.IsDisposed&&target.Profile==null)
            target.UpdateProfile(rank);
    }
    void OpenHistory(Player player){OpenHistory(player,null);}
    void OpenHistory(Player player,Task<PlayerProfile> pending){
        tracker.Remember(player);
        PlayerProfile cached;profiles.TryGetValue(player.Id??"",out cached);if(cached!=null&&cached.Day!=DateTime.Today)cached=null;
        var view=new HistoryView{Dock=DockStyle.Fill,Player=player};
        view.OpenPlayer+=OpenHistory;
        view.UpdateProfile(cached);
        var page=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Bg,Visible=false,Tag=view};
        page.Controls.Add(view);
        home.Parent.Controls.Add(page);
        ShowPage(page,trackerNav);
        if(pending!=null&&(cached==null||cached.RetryAfter<=DateTime.UtcNow||cached.Limited))LoadStats(player,pending,true);
        else if(!isPreview&&cached!=null&&cached.Limited&&RiotStatsClient.RemoteRetryAt<=DateTime.UtcNow)
            LoadStats(player,null,true);
        ScheduleStats();
        if(cached!=null)LoadProfileArt(cached);

    }
    public void Demo(){
        var s=new Snapshot{Status="AGENT SELECT",Map="Lotus",Match="demo-select",Side="DEFENSE · estimated",Diagnostics="Preview with sample players."};
        for(int i=0;i<5;i++){s.Allies.Add(new Player{Id="demo-a"+i,Name=new[]{"Lunar#BR","Brisa#BR1","Aurora#BR2","Café#123","Domingo#BR1"}[i],State=i==0?"YOU":"LOCKED IN"});s.Enemies.Add(new Player{Id="demo-e"+i,Name=new[]{"Neblina#BR1","Horizonte#BR2","Maré#222","Intervalo#BR1","Lua#999"}[i],State="PLAYER"});}
        int n=0;foreach(var player in s.Allies.Concat(s.Enemies)){
            player.AccountLevel=n%3==0?240+n:0;player.AccountLevelHidden=n%3==2;
            var p=new PlayerProfile{AccountLevel=240+n,AccountLevelHidden=player.AccountLevelHidden,Id=player.Id,Name=player.Name,Tier=new[]{23,20,17,15,12}[n%5],RR=42,RankIsCurrent=true,HistoryAvailable=true,DayComplete=true,Message="PREVIEW · sample matches"};p.RankLabel=RankArt.Name(p.Tier);
            for(int j=0;j<5;j++)p.Matches.Add(new MatchRow{Id="demo-match-"+j,Map=new[]{"Ascent","Haven","Lotus","Icebox","Bind"}[j],Queue="Competitive",Started=DateTime.Now.AddHours(-j-1),Completed=true,Result=n%3==0?(j==3?Outcome.Loss:Outcome.Win):n%3==1?(j==0?Outcome.Win:Outcome.Loss):(j<2?Outcome.Win:j<4?Outcome.Loss:Outcome.Draw),Score=j==3?"9 : 13":"13 : 8",Kda="21 / 14 / 6"});
            HistoryLogic.CountDay(p,DateTime.Today);profiles[p.Id]=p;n++;
        }
        for(int i=0;i<5;i++){
            s.Allies[i].AgentId=i%2==0?"add6443a-41bd-e414-f6ad-e58d267f4e95":"320b2a48-4d9b-a075-30f1-1f93a9b638fa";
            s.Enemies[i].AgentId=i%2==0?"320b2a48-4d9b-a075-30f1-1f93a9b638fa":"add6443a-41bd-e414-f6ad-e58d267f4e95";
        }
        s.Allies[0].PartyId=s.Allies[1].PartyId="demo-party-1";
        s.Enemies[2].PartyId=s.Enemies[3].PartyId="demo-party-2";
        PopulateDemo(s);s.Self=s.Allies[0];ShowSnapshot(s);surface.Updated="PREVIEW · sample data";
    }
    Panel BuildSettingsView(){
        var panel=new SettingsSurface{Dock=DockStyle.Fill,BackColor=Palette.Bg};
        var labelFont=new Font("Segoe UI",11,FontStyle.Bold);var bodyFont=new Font("Segoe UI",9);
        languageTitle=new Label{Text="Language",AutoSize=false,Size=new Size(360,24),ForeColor=Palette.Text,BackColor=Color.Transparent,Font=labelFont};
        languageDescription=new Label{Text="Choose the language used by Lunar.",AutoSize=false,Size=new Size(380,22),ForeColor=Palette.Muted,BackColor=Color.Transparent,Font=bodyFont};
        languageSelect=new SoftSelect{Size=new Size(230,38),Font=new Font("Segoe UI",10),ForeColor=Palette.Text,BackColor=Palette.Raised};
        foreach(var option in LunarLanguage.Options)languageSelect.Items.Add(option.Name);
        languageSelect.SelectedIndex=LunarLanguage.Index;
        languageSelect.SelectedIndexChanged+=delegate{if(languageSelect.SelectedIndex>=0&&languageSelect.SelectedIndex!=LunarLanguage.Index){LunarLanguage.Set(languageSelect.SelectedIndex);ApplyLanguage();}};
        hotkeyTitle=new Label{Text="Live Match hotkey",AutoSize=false,Size=new Size(360,24),ForeColor=Palette.Text,BackColor=Color.Transparent,Font=labelFont};
        hotkeyHelp=new Label{Text="Change the shortcut used to open the live overlay.",AutoSize=false,Size=new Size(380,22),ForeColor=Palette.Muted,BackColor=Color.Transparent,Font=bodyFont};
        hotkeyCurrent=new Label{Text="Current shortcut: "+HotkeyPreference.Current.Display,Visible=false};
        changeHotkeyButton=new SoftButton{Text="Change hotkey",Size=new Size(150,38),FlatStyle=FlatStyle.Flat,BackColor=Palette.AccentSoft,ForeColor=Color.White,Font=new Font("Segoe UI",9,FontStyle.Bold),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter};
        changeHotkeyButton.FlatAppearance.BorderSize=0;
        changeHotkeyButton.Click+=delegate{
            string current=desktop==null?HotkeyPreference.Current.Display:desktop.HotkeyDisplay;
            using(var dialog=new HotkeyCaptureDialog(current)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                bool saved=desktop==null?SavePreviewHotkey(dialog.CapturedKey,dialog.CapturedModifiers):desktop.SetHotkey(dialog.CapturedKey,dialog.CapturedModifiers);
                if(saved){hotkeyCurrent.Text="Current shortcut: "+(desktop==null?HotkeyPreference.Current.Display:desktop.HotkeyDisplay);panel.Invalidate();}
                else MessageBox.Show(this,LunarLanguage.Translate("That key combination is already in use by another application."),"Lunar",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        };
        var updatesTitle=new Label{Text="Updates",AutoSize=false,Size=new Size(360,24),ForeColor=Palette.Text,BackColor=Color.Transparent,Font=labelFont};
        languageHelp=new Label{Text="Updates are checked automatically. When ready, an Update button appears at the bottom right.",AutoSize=false,Size=new Size(420,40),ForeColor=Palette.Muted,BackColor=Color.Transparent,Font=bodyFont};
        var checkButton=new SoftButton{Text="Check now",Size=new Size(150,38),FlatStyle=FlatStyle.Flat,BackColor=Palette.Raised,ForeColor=Palette.Text,Font=new Font("Segoe UI",9,FontStyle.Bold),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter};
        checkButton.FlatAppearance.BorderSize=0;
        checkButton.Click+=delegate{
            if(desktop==null)return;
            if(desktop.UpdateReady){checkButton.Text="Update ready";return;}
            desktop.CheckUpdatesNow();
            checkButton.Text="Checking…";
            var reset=new Timer{Interval=4000};
            reset.Tick+=delegate{reset.Stop();reset.Dispose();if(!checkButton.IsDisposed)checkButton.Text=desktop.UpdateReady?"Update ready":"You're up to date";};
            reset.Start();
        };
        panel.Cards=new[]{
            new SettingsCard{Glyph=SettingsGlyph.Language,Title=languageTitle,Body=new[]{languageDescription},Action=languageSelect},
            new SettingsCard{Glyph=SettingsGlyph.Keyboard,Title=hotkeyTitle,Body=new[]{hotkeyHelp},Action=changeHotkeyButton,Keys=()=>desktop==null?HotkeyPreference.Current.Display:desktop.HotkeyDisplay},
            new SettingsCard{Glyph=SettingsGlyph.Update,Title=updatesTitle,Body=new[]{languageHelp},Action=checkButton,Badge=LunarUpdates.DisplayVersion}
        };
        foreach(var card in panel.Cards){panel.Controls.Add(card.Title);foreach(var body in card.Body)panel.Controls.Add(body);panel.Controls.Add(card.Action);}
        panel.Controls.Add(hotkeyCurrent);
        panel.Layout+=delegate{panel.Arrange();};
        return panel;
    }
    static bool SavePreviewHotkey(Keys key,Keys modifiers){if(!HotkeyPreference.Valid(key,modifiers))return false;HotkeyPreference.Save(new HotkeyBinding{Key=key,Modifiers=modifiers});return true;}
    void ApplyLanguage(){
        // Texts are set in English; LunarLanguage.Attach translates every control (and later changes) automatically.
        if(versionFooter!=null)versionFooter.Text="LUNAR   /   "+LunarUpdates.DisplayVersion;
        if(hotkeyCurrent!=null)hotkeyCurrent.Text="Current shortcut: "+(desktop==null?HotkeyPreference.Current.Display:desktop.HotkeyDisplay);
        LunarLanguage.Attach(this);
        LunarLanguage.RefreshAll(this);
        if(updateNotice!=null)updateNotice.Invalidate();
        if(languageSelect!=null)languageSelect.Invalidate();
        if(settingsView!=null)settingsView.Invalidate();
    }
    void ShowSettings(){ShowPage(settingsView,settingsNav);}    public void ShowStore(){ShowPage(storeView,storeNav);}
    public void ShowFriends(){ShowPage(friendsView,friendsNav);}
    public void ShowCrosshair(){ShowPage(crosshairView,crosshairNav);}
    public void ShowTracker(){ShowPage(tracker,trackerNav);}
    void SwitchPage(bool showHome){
        if(showHome)ShowPage(home,homeNav);else ShowPage(surface,liveNav);
    }
    void ShowPage(Control page,NavButton navigation,bool record=true){
        var leaving=activePage.Key;
        if(leaving==page)return;
        if(leaving!=null){
            if(record)previousPages.Add(activePage);
            leaving.Visible=false;
        }
        activePage=new KeyValuePair<Control,NavButton>(page,navigation);
        historyView=page.Tag as HistoryView;
        profilePage=historyView==null?null:(Panel)page;
        selected=historyView==null?null:historyView.Player;
        page.Visible=true;
        page.BringToFront();
        if(updateNotice!=null&&updateNotice.Visible)updateNotice.BringToFront();
        MarkPage(navigation);
        if(page==storeView&&!isPreview)storeView.CheckOnOpen();
        if(page==home&&home.Profile!=null)LoadProfileArt(home.Profile);
        if(selected!=null&&!record){
            PlayerProfile cached;
            if(profiles.TryGetValue(selected.Id??"",out cached)&&cached.Day==DateTime.Today&&cached!=historyView.Profile)
                historyView.UpdateProfile(cached);
            ScheduleStats();
        }
        if(previousPages.Count>24){
            var oldest=previousPages[0].Key;
            previousPages.RemoveAt(0);
            ReleaseProfilePage(oldest);
        }
    }
    void ReleaseProfilePage(Control page){
        if(page.Tag is HistoryView&&page!=activePage.Key&&!previousPages.Any(entry=>entry.Key==page))page.Dispose();
    }
    internal bool GoBack(){
        var profile=historyView??(activePage.Key==home?home:null);
        if(profile!=null&&profile.BackFromScoreboard())return true;
        if(previousPages.Count==0)return false;
        var leaving=activePage.Key;
        var previous=previousPages[previousPages.Count-1];
        previousPages.RemoveAt(previousPages.Count-1);
        ShowPage(previous.Key,previous.Value,false);
        ReleaseProfilePage(leaving);
        return true;
    }
    void PopulateDemo(Snapshot s){
        Catalog.Demo();foreach(var p in profiles.Values){p.PeakTier=26;p.PeakRR=412;int i=0;foreach(var m in p.Matches){m.AgentId=i%3==0?"8e253930-4c05-31dd-1b6c-968525494517":"add6443a-41bd-e414-f6ad-e58d267f4e95";m.SeasonId="demo-act";m.RRChange=m.Result==Outcome.Win?18:-16;m.Mvp=i==0?MvpKind.Match:i==3?MvpKind.Team:MvpKind.None;m.Metrics=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=25-i*2,Deaths=12+i,Assists=6,Rounds=21,Damage=3500-i*100,Head=30,Body=60,Leg=10};m.Kda=m.Metrics.Kills+" / "+m.Metrics.Deaths+" / 6";int j=0;foreach(var player in s.Allies.Concat(s.Enemies)){m.Roster.Add(new ScorePlayer{Id=player.Id,Name=player.Name,Team=j<5?"Blue":"Red",Tier=20,Agent=j%2==0?"add6443a-41bd-e414-f6ad-e58d267f4e95":"320b2a48-4d9b-a075-30f1-1f93a9b638fa",Stats=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=25-j,Deaths=12+j,Assists=4,Rounds=21,Damage=3000-j*50,Head=25,Body=70,Leg=5}});j++;}i++;}}
    }
    public void RenderTab(string path,int tab){SwitchPage(true);home.ActivateTab(tab);home.Refresh();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}}
    public void RenderDetail(string path){home.RenderMatch(path);}
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
    public void RenderFullscreen(string path){
        SwitchPage(false);SetFullscreenWarning(true);ShowUpdateReady(delegate{},"1.83");
        fullscreenNotice.Present(ClientSize,NoticeOffset);
        var stop=DateTime.UtcNow.AddSeconds(1.5);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}
        using(var b=new Bitmap(Width,Height)){using(var g=Graphics.FromImage(b)){var hdc=g.GetHdc();try{PrintWindow(Handle,hdc,2);}finally{g.ReleaseHdc(hdc);}}b.Save(path);}
    }
    public void RenderSettings(string path){ShowSettings();settingsView.PerformLayout();Application.DoEvents();Refresh();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}}
    public void RenderStore(string path){
        ShowStore();
        storeView.ShowDemo();
        var until=DateTime.UtcNow.AddSeconds(12);
        while(DateTime.UtcNow<until){Application.DoEvents();System.Threading.Thread.Sleep(50);storeView.Refresh();if(storeView.DemoReady)break;}
        Application.DoEvents();storeView.Refresh();
        using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}
    }
    public void RenderHistory(string path){SwitchPage(false);surface.Refresh();surface.ActivateFirstPlayer();Application.DoEvents();if(profilePage==null||historyView.Player.Id!=current.Allies[0].Id)throw new InvalidOperationException("Profile navigation failed");using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}}
}

static class Program {
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [STAThread] static int Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Length==1&&args[0]=="--apply-update")return LunarUpdates.Apply();
        if(args.Contains("--self-test")) {SearchChecks.Run();Tests.Run();HistoryChecks.Run();RiotStatsChecks.Run();return ProfileChecks.Run();}
        if(args.Contains("--language-test"))return LunarLanguage.Run();
        if(args.Contains("--export-log")){new RiotReader().GetStatisticsSession();Console.WriteLine(LunarLog.Export());return 0;}
        if(args.Contains("--session-probe")){var probe=new RiotReader().GetStatisticsSession();Console.WriteLine(probe==null?"session: unavailable — "+RiotReader.LastSessionProblem:"session: ok · region "+probe.Region+" · version "+probe.Version);return 0;}
        if(args.Length==2&&args[0]=="--dump-tile"){
            var row=new MatchRow{Map="Ascent",MapId=TrackArt.MapId("Ascent")};
            for(int i=0;i<200&&TrackArt.MapReady(row)==false;i++){System.Threading.Thread.Sleep(50);Application.DoEvents();}
            using(var b=new Bitmap(420,80)){using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(255,0,255));TrackArt.DrawMap(g,row,new Rectangle(10,9,400,62),Palette.Win);}
                foreach(int y in new[]{8,9,10,11,12,69,70,71})Console.WriteLine("y="+y+" x=300 "+b.GetPixel(300,y));
                b.Save(args[1]);}
            return 0;
        }
        if(args.Contains("--update-probe"))return LunarUpdates.Probe();
        if(args.Contains("--probe")) {var s=new RiotReader().Poll();Console.WriteLine(s.Status+" | "+s.Detail);return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0]=="--render-skeleton") {
            using(var form=new Form{ClientSize=new Size(1020,760),StartPosition=FormStartPosition.CenterScreen,BackColor=Palette.Bg}){
                var sk=new ProfileSkeleton{Dock=DockStyle.Fill};form.Controls.Add(sk);form.Show();
                var stop=DateTime.UtcNow.AddSeconds(1.2);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}
                using(var b=new Bitmap(sk.Width,sk.Height)){sk.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}
            }
            return 0;
        }
        if(args.Length==2 && args[0]=="--render-video") {
            var info=SkinCatalog.Find("9f09d408-4e55-b9bb-580a-9fa37e3730b5");
            if(info==null){SkinCatalog.Ensure(new[]{"9f09d408-4e55-b9bb-580a-9fa37e3730b5"});info=SkinCatalog.Find("9f09d408-4e55-b9bb-580a-9fa37e3730b5");}
            if(info==null||info.Videos.Count==0)return 2;
            using(var form=new SkinVideoForm("RGX 11z Pro Karambit",info)){
                form.StartPosition=FormStartPosition.Manual;var wa=Screen.PrimaryScreen.WorkingArea;form.Location=new Point(wa.X+(wa.Width-form.Width)/2,wa.Y+(wa.Height-form.Height)/2);form.TopMost=true;form.Show();
                Action<double> pump=seconds=>{var stop=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(5);}};
                pump(5);var a=form.Stats();var t0=DateTime.UtcNow;pump(5);var b2=form.Stats();double wall=(DateTime.UtcNow-t0).TotalSeconds;
                try{var x=a.Split(',');var y=b2.Split(',');double total=Double.Parse(y[0])-Double.Parse(x[0]),dropped=Double.Parse(y[1])-Double.Parse(x[1]);
                    File.WriteAllText(Path.ChangeExtension(args[1],".txt"),"before="+a+" | after="+b2+" | wall="+wall.ToString("F2")+" | decoded_fps="+(total/wall).ToString("F1")+" | shown_fps="+((total-dropped)/wall).ToString("F1")+" | dropped="+dropped);}
                catch(Exception e){File.WriteAllText(Path.ChangeExtension(args[1],".txt"),"before="+a+" | after="+b2+" | "+e.Message);}
                using(var b=new Bitmap(form.Width,form.Height)){using(var g=Graphics.FromImage(b)){var hdc=g.GetHdc();try{PrintWindow(form.Handle,hdc,2);}finally{g.ReleaseHdc(hdc);}}b.Save(args[1]);}
            }
            return 0;
        }
        if(args.Length==3 && args[0]=="--render-store-empty") {
            using(var form=new Form{ClientSize=new Size(980,620),BackColor=Palette.Bg,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,0)}){
                var canvas=new StoreCanvas{Location=Point.Empty,Width=980};form.Controls.Add(canvas);LunarLanguage.Attach(form);form.Show();canvas.ShowStatus(args[2]);canvas.Height=canvas.HeightForLayout();
                var stop=DateTime.UtcNow.AddSeconds(.8);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}
                using(var b=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){using(var g=Graphics.FromImage(b)){var hdc=g.GetHdc();try{PrintWindow(form.Handle,hdc,1);}finally{g.ReleaseHdc(hdc);}}b.Save(args[1]);}
            }
            return 0;
        }
        if(args.Length==2 && args[0]=="--render-tray") {
            using(var window=new Window(false)){
                var desktop=window.Desktop;
                int n=0;
                foreach(var phase in new[]{"WAITING","AGENT SELECT","IN MATCH"}){
                    desktop.PreviewTray(phase,phase=="IN MATCH");
                    var menu=desktop.TrayMenu;
                    menu.Show(new Point(-2000,-2000));
                    var stop=DateTime.UtcNow.AddSeconds(.8);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}
                    using(var b=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(System.IO.Path.ChangeExtension(args[1],"."+(n++)+".png"));}
                    menu.Close();
                }
                window.Close();
            }
            return 0;
        }
        if(args.Length==2 && args[0]=="--test-alert") {
            using(var window=new Window(true)){
                Action<double> pump=seconds=>{var stop=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}};
                window.Show();pump(1);window.Hide();pump(.5); // like Lunar sitting in the tray during a game
                window.AlertFullscreen();pump(2);
                var screen=Screen.PrimaryScreen.Bounds;
                using(var b=new Bitmap(screen.Width,screen.Height)){using(var g=Graphics.FromImage(b))g.CopyFromScreen(screen.Location,Point.Empty,screen.Size);b.Save(args[1]);}
                Console.WriteLine("after alert: visible="+window.Visible+" state="+window.WindowState+" alert="+TaskbarAlert.Active);
                TaskbarAlert.ShowRestored(window);pump(1.5); // what clicking the taskbar button does
                Console.WriteLine("after click: visible="+window.Visible+" state="+window.WindowState+" bounds="+window.Bounds);
                window.Hide();pump(.5);
                Console.WriteLine("after close-to-tray: visible="+window.Visible+" nativeVisible="+IsWindowVisible(window.Handle));
                window.Show();pump(.5);window.Hide();
                using(var b=new Bitmap(window.Width,window.Height)){using(var g=Graphics.FromImage(b)){var hdc=g.GetHdc();try{PrintWindow(window.Handle,hdc,2);}finally{g.ReleaseHdc(hdc);}}b.Save(System.IO.Path.ChangeExtension(args[1],".window.png"));}
                window.Close();
            }
            return 0;
        }
        if(args.Length==2 && args[0]=="--render-update") {
            using(var window=new Window(true)){
                window.Show();window.ShowUpdateReady(delegate{},"1.81");
                var stop=DateTime.UtcNow.AddSeconds(2.5);while(DateTime.UtcNow<stop){Application.DoEvents();System.Threading.Thread.Sleep(15);}
                using(var b=new Bitmap(window.Width,window.Height)){using(var g=Graphics.FromImage(b)){var hdc=g.GetHdc();try{PrintWindow(window.Handle,hdc,2);}finally{g.ReleaseHdc(hdc);}}b.Save(args[1]);}
                window.Close();
            }
            return 0;
        }
        if(args.Length==2 && args[0].StartsWith("--render")) {
            using(var window=new Window(true)) { if(args[0]!="--render")window.Demo();if(args[0]=="--render-small")window.Size=window.MinimumSize;window.Show();Application.DoEvents();{var warm=DateTime.UtcNow.AddSeconds(6);while(DateTime.UtcNow<warm){Application.DoEvents();System.Threading.Thread.Sleep(40);}}if(args[0]=="--render-tracker"){window.ShowTracker();Application.DoEvents();using(var b=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}}else if(args[0]=="--render-history")window.RenderHistory(args[1]);else if(args[0]=="--render-matches")window.RenderTab(args[1],1);else if(args[0]=="--render-agents")window.RenderTab(args[1],2);else if(args[0]=="--render-maps")window.RenderTab(args[1],3);else if(args[0]=="--render-detail")window.RenderDetail(args[1]);else if(args[0]=="--render-store")window.RenderStore(args[1]);else if(args[0]=="--render-settings")window.RenderSettings(args[1]);else if(args[0]=="--render-fullscreen")window.RenderFullscreen(args[1]);else using(var bitmap=new Bitmap(window.Width,window.Height)) {window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);}window.Close();}return 0;
        }
        DesktopShortcut.Ensure();
        LunarLog.Start();
        AppIcon.RefreshShellCacheIfChanged();
        // Single instance: opening Lunar again just brings the running window to the front (no second tray icon).
        bool first;
        var instance=new System.Threading.Mutex(true,@"Local\LunarSingleInstance",out first);
        if(!first){
            try{using(var signal=System.Threading.EventWaitHandle.OpenExisting(@"Local\LunarShowWindow"))signal.Set();}catch(Exception){}
            return 0;
        }
        var show=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,@"Local\LunarShowWindow");
        var mainWindow=new Window();
        var listener=new System.Threading.Thread(()=>{
            while(show.WaitOne()){
                try{if(mainWindow.IsDisposed)break;mainWindow.BeginInvoke((Action)mainWindow.ShowFromOtherInstance);}catch(Exception){break;}
            }
        }){IsBackground=true,Name="Lunar single instance"};
        listener.Start();
        Application.Run(mainWindow);
        GC.KeepAlive(instance);
        return 0;
    }
}
// The app icon, read from the icon embedded in Lunar itself. Icon.ExtractAssociatedIcon goes through the Windows
// icon cache, which keeps showing the previous icon after an update (same .exe path).
static class AppIcon {
    public static Icon Load(){
        try{
            using(var stream=typeof(AppIcon).Assembly.GetManifestResourceStream("lunar.ico"))
                if(stream!=null)return new Icon(stream);
        }catch(Exception e){LunarLog.Error("app","embedded icon failed",e);}
        return Icon.ExtractAssociatedIcon(typeof(AppIcon).Assembly.Location);
    }
    // When the icon changes (an update with a new logo), ask Windows to refresh its icon cache once so the
    // taskbar pin, Start menu and desktop shortcut show the new icon too.
    public static void RefreshShellCacheIfChanged(){
        try{
            string hash;
            using(var stream=typeof(AppIcon).Assembly.GetManifestResourceStream("lunar.ico"))
            using(var sha=System.Security.Cryptography.SHA256.Create()){
                if(stream==null)return;
                hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Substring(0,16);
            }
            string marker=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","icon.txt");
            if(File.Exists(marker)&&File.ReadAllText(marker).Trim()==hash)return;
            Directory.CreateDirectory(Path.GetDirectoryName(marker));
            File.WriteAllText(marker,hash);
            string tool=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"ie4uinit.exe");
            if(File.Exists(tool))Process.Start(new ProcessStartInfo(tool,"-show"){UseShellExecute=false,CreateNoWindow=true});
            SHChangeNotify(0x08000000,0x0000,IntPtr.Zero,IntPtr.Zero); // SHCNE_ASSOCCHANGED: shell reloads icons
            LunarLog.Write("app","icon changed → Windows icon cache refreshed");
        }catch(Exception e){LunarLog.Error("app","icon cache refresh failed",e);}
    }
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId,int flags,IntPtr item1,IntPtr item2);
}
static class DesktopShortcut {
    public static void Ensure(){
        object shell=null,shortcut=null;
        try{
            var shellType=Type.GetTypeFromProgID("WScript.Shell");
            if(shellType==null)return;
            shell=Activator.CreateInstance(shellType);
            string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Lunar.lnk");
            shortcut=shellType.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{link});
            var linkType=shortcut.GetType();
            Action<string,string> set=delegate(string name,string value){linkType.InvokeMember(name,System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{value});};
            set("TargetPath",Application.ExecutablePath);
            set("WorkingDirectory",Path.GetDirectoryName(Application.ExecutablePath));
            set("IconLocation",Application.ExecutablePath+",0");
            set("Description","Lunar - VALORANT Tracker");
            linkType.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null);
        }catch{}
        finally{
            if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);
            if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);
        }
    }
}
static class Tests {
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    public static int Run(){
        string me="11111111-1111-1111-1111-111111111111",other="22222222-2222-2222-2222-222222222222";
        Func<string,object> parse=Json.Codec.DeserializeObject;
        // Score from the 2024+ nested presence format and from the older flat one.
        Func<string,object> presenceEntry=json=>new Dictionary<string,object>{{"private",Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}};
        var live=new Snapshot{Status="IN MATCH",Match="m-1",Map="Summit"};
        RiotReader.ApplyDiscordScore(live,RiotReader.RiotPresenceData(presenceEntry("{\"matchPresenceData\":{\"sessionLoopState\":\"INGAME\",\"matchMap\":\"\"},\"partyPresenceData\":{\"isPartyOwner\":true,\"partyOwnerSessionLoopState\":\"INGAME\",\"partyOwnerMatchScoreAllyTeam\":7,\"partyOwnerMatchScoreEnemyTeam\":5}}")));
        Check(live.AllyScore==7&&live.EnemyScore==5,"live score read from nested presence format");
        live=new Snapshot{Status="IN MATCH",Match="m-1",Map="Summit"};
        RiotReader.ApplyDiscordScore(live,RiotReader.RiotPresenceData(presenceEntry("{\"sessionLoopState\":\"INGAME\",\"isPartyOwner\":true,\"partyOwnerMatchScoreAllyTeam\":3,\"partyOwnerMatchScoreEnemyTeam\":9}")));
        Check(live.AllyScore==3&&live.EnemyScore==9,"live score read from flat presence format");
        Check(RiotReader.RegionFromClient("BR","")=="br"&&RiotReader.RegionFromClient("LA1","")=="latam"&&RiotReader.RegionFromClient("EUW","")=="eu"&&RiotReader.RegionFromClient("OC1","")=="ap"&&RiotReader.RegionFromClient("","na")=="na"&&RiotReader.RegionFromClient("XYZ","")=="","Riot Client region codes map to VALORANT regions");
        Check(RiotReader.IsRiotClientName("Riot Client")&&RiotReader.IsRiotClientName("RiotClientServices")&&!RiotReader.IsRiotClientName("VALORANT"),"new and old Riot Client process names are recognised");
        Check(DiscordActivity.From(new Snapshot{Status="IN MATCH",Map="Ascent"},0).Visible,"Discord activity shown while in a match");
        Check(DiscordActivity.From(new Snapshot{Status="AGENT SELECT",Map="Ascent"},0).Visible&&!DiscordActivity.From(new Snapshot{Status="AGENT SELECT",Map="Ascent"},0).InMatch,"Discord activity shown in agent select");
        Check(DiscordActivity.From(new Snapshot{Status="IN MATCH",Map="Ascent"},0).InMatch,"in-match activity is marked as a match");
        Check(DiscordActivity.From(new Snapshot{Status="IN MATCH",Map="Ascent",Self=new Player{AgentId="add6443a-41bd-e414-f6ad-e58d267f4e95"}},0).InMatch,"a match without a score is still marked as a match");
        Check(!DiscordActivity.From(new Snapshot(),0).Visible,"Discord activity hidden in the lobby and when VALORANT is closed");
        using(var appIcon=AppIcon.Load())Check(appIcon!=null&&appIcon.Width>0,"app icon loads from the embedded resource");
        Check(!RiotReader.ClientProcessAlive(Int32.MaxValue),"a process that does not exist is not running");
        var guarded=Process.GetProcessesByName("csrss").FirstOrDefault();
        if(guarded!=null)Check(!RiotReader.ClientProcessAlive(guarded.Id),"a protected non-Riot process is checked by name without access errors");
        var s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\"}]},\"EnemyTeam\":null}"),me,true);
        Check(s.Allies.Count==1&&s.Enemies.Count==0,"enemy null does not fabricate identities");
        Check(s.Side.Contains("estimated"),"starting side labelled as inference");
        Check(RiotReader.Side("Unknown",true).Contains("unavailable"),"unknown side is not mapped to defense");
        Check(RiotReader.Side("Red",false).Contains("unavailable"),"ingame team does not claim starting side");
        s=RiotReader.Parse(parse("{\"Teams\":[{\"TeamID\":\"Red\",\"Players\":[{\"Subject\":\""+other+"\"}]},{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\"}]}]}"),me,true);
        Check(s.Allies[0].Id==me&&s.Enemies[0].Id==other,"fallback anchors team to local player");
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>{{other,"Teste#BR1"}});
        Check(s.Enemies[0].Name=="Teste#BR1","missing privacy flag does not suppress returned name");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\"},{\"Subject\":\""+other+"\",\"TeamID\":\"Blue\",\"PlayerIdentity\":{\"Incognito\":true}}]}"),me,false);
        Check(s.Allies.Count==1&&s.Enemies.Count==1,"ingame grouping");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"PlayerIdentity\":{\"AccountLevel\":276}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==276,"account level parsed from the Riot identity");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"player_identity\":{\"account_level\":\"143\"}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==143,"account level parsed from alternate field casing");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"PlayerIdentity\":{\"HideAccountLevel\":true,\"AccountLevel\":276}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==276,"account level returned with hidden display flag remains available");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\",\"playerIdentity\":{\"Level\":52}}]}"),me,false);
        Check(s.Allies[0].AccountLevel==52,"account level parsed from lowercase live response");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\"},{\"Subject\":\""+other+"\",\"TeamID\":\"Blue\",\"PlayerIdentity\":{\"Incognito\":true}}]}"),me,false);
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>{{other,"Teste#BR1"}});
        Check(s.Enemies[0].Name=="Teste#BR1","Incognito does not suppress returned name");
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>());
        Check(s.Enemies[0].Name=="Name unavailable","missing name is not fabricated or labelled hidden");
        s=RiotReader.Parse(parse("{}"),me,true);Check(s.Allies.Count==0&&s.Enemies.Count==0,"empty response produces empty state");
        var local=RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":{\"gameName\":\"Local\",\"tagLine\":\"BR1\"}}]}"),new[]{other});
        Check(local.ContainsKey(other)&&local[other]=="Local#BR1","local V2 alias resolves requested player");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":{\"gameName\":\"Local\"}}]}"),new[]{me}).Count==0,"unrequested identities discarded");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"error\":\"unavailable\",\"alias\":{\"gameName\":\"Local\"}}]}"),new[]{other}).Count==0,"local entry error not cached as a name");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":null}]}"),new[]{other}).Count==0,"null local alias tolerated");
        Console.WriteLine("18 checks passed.");return 0;
    }
}
}
