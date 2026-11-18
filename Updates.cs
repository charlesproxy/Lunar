using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lunar {
sealed class UpdatePackage {
    public Version Version;
    public string Name, Hash;
    public long Size;
    public bool Installed;
}

static class LunarUpdates {
    const string Feed="https://github.com/mucalol640-glitch/Lunar/releases";
    const int MaxManifest=32*1024;
    const int MaxPackage=64*1024*1024;
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","updates");
    static readonly string InstalledExe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Lunar","Lunar.exe");
    internal static Version CurrentVersion{get{return ReadVersion(Application.ExecutablePath);}}
    internal static string DisplayVersion{get{return ReleaseVersion(CurrentVersion);}}
    // Version of the executable staged in an update folder (for the Update ready card), or "" if unknown.
    internal static string StagedVersion(string folder){
        try{
            foreach(var file in Directory.GetFiles(folder,"Lunar-*.exe"))return ReleaseVersion(ReadVersion(file));
        }catch(Exception){}
        return "";
    }
    static Version ReadVersion(string path){return new Version(FileVersionInfo.GetVersionInfo(path).FileVersion);}
    static bool IsInstalled(string path){return String.Equals(path,InstalledExe,StringComparison.OrdinalIgnoreCase);}
    static RSACryptoServiceProvider Verifier(){
        var rsa=new RSACryptoServiceProvider(new CspParameters(24)){PersistKeyInCsp=false};
        try{
            using(var resource=typeof(LunarUpdates).Assembly.GetManifestResourceStream("lunar-update-public.xml"))
            using(var reader=new StreamReader(resource))rsa.FromXmlString(reader.ReadToEnd());
            return rsa;
        }catch{rsa.Dispose();throw;}
    }
    internal static UpdatePackage VerifyManifest(byte[] bytes,bool installed){
        if(bytes==null||bytes.Length>MaxManifest)throw new InvalidDataException("Invalid update manifest.");
        var envelope=Json.Codec.DeserializeObject(Encoding.UTF8.GetString(bytes));
        var payload=Convert.FromBase64String(Json.S(envelope,"payload"));
        var signature=Convert.FromBase64String(Json.S(envelope,"signature"));
        using(var rsa=Verifier()){
            if(!rsa.VerifyData(payload,CryptoConfig.MapNameToOID("SHA256"),signature))
                throw new CryptographicException("Invalid update signature.");
        }
        var manifest=Json.Codec.DeserializeObject(Encoding.UTF8.GetString(payload));
        Version version;
        if(!Version.TryParse(Json.S(manifest,"version"),out version)||version.Build<0||version.Revision<0)
            throw new InvalidDataException("Invalid update version.");
        var package=Json.Get(manifest,"application");
        string release=ReleaseVersion(version);
        var update=new UpdatePackage{Version=version,Installed=installed,Name=Json.S(package,"name"),
            Hash=Json.S(package,"sha256"),Size=HistoryLogic.N(package,"size",-1)};
        if(update.Name!="Lunar-"+release+".exe"||
            update.Size<=0||update.Size>MaxPackage||!Regex.IsMatch(update.Hash,@"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("Invalid update package.");
        return update;
    }
    static string ReleaseVersion(Version version){return version.ToString(version.Build==0?2:3);}
    internal static void VerifyFile(string path,UpdatePackage package){
        if(new FileInfo(path).Length!=package.Size)throw new InvalidDataException("Incorrect update size.");
        using(var stream=File.OpenRead(path))
        using(var sha=SHA256.Create()){
            string hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
            if(!String.Equals(hash,package.Hash,StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Incorrect update file hash.");
        }
    }
    static async Task Download(string url,Stream buffer,long limit,CancellationToken cancellation){
        var request=(HttpWebRequest)WebRequest.Create(url);
        request.UserAgent="Lunar Update";
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            using(timeout.Token.Register(request.Abort))
            using(var response=await request.GetResponseAsync().ConfigureAwait(false)){
                if(response.ResponseUri.Scheme!=Uri.UriSchemeHttps||response.ContentLength>limit)
                    throw new InvalidDataException("Invalid update response.");
                using(var source=response.GetResponseStream()){
                    var bytes=new byte[32768];
                    int count;
                    while((count=await source.ReadAsync(bytes,0,bytes.Length,timeout.Token).ConfigureAwait(false))!=0){
                        if(buffer.Length+count>limit)throw new InvalidDataException("Update download exceeded its limit.");
                        await buffer.WriteAsync(bytes,0,count,timeout.Token).ConfigureAwait(false);
                    }
                }
            }
        }
    }
    const string LatestApi="https://api.github.com/repos/mucalol640-glitch/Lunar/releases/latest";
    static string latestTag,latestEtag;
    // Asks the GitHub API for the newest release tag. Uses ETag: an unchanged answer is a 304, which is fast and does
    // not count against GitHub's rate limit, so Lunar can check every few minutes. The API is not CDN-cached like the
    // releases/latest/download redirect, so a new release is seen right away.
    static async Task<string> LatestTag(CancellationToken cancellation){
        var request=(HttpWebRequest)WebRequest.Create(LatestApi);
        request.UserAgent="Lunar Update";
        request.Accept="application/vnd.github+json";
        if(latestEtag!=null&&latestTag!=null)request.Headers[HttpRequestHeader.IfNoneMatch]=latestEtag;
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using(timeout.Token.Register(request.Abort)){
                try{
                    using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){
                        string json=await reader.ReadToEndAsync().ConfigureAwait(false);
                        if(json.Length>256*1024)throw new InvalidDataException("Invalid release response.");
                        string tag=Json.S(Json.Codec.DeserializeObject(json),"tag_name");
                        if(!Regex.IsMatch(tag,@"\Av\d+\.\d+(\.\d+)?\z"))throw new InvalidDataException("Invalid release tag.");
                        latestTag=tag;latestEtag=response.Headers[HttpResponseHeader.ETag];
                        return tag;
                    }
                }catch(WebException error){
                    var response=error.Response as HttpWebResponse;
                    if(response!=null&&response.StatusCode==HttpStatusCode.NotModified&&latestTag!=null){response.Close();return latestTag;}
                    if(response!=null)response.Close();
                    throw;
                }
            }
        }
    }
    // Diagnostics: "--update-probe" prints what the update check sees (does not download or apply anything).
    internal static int Probe(){
        try{
            System.Net.ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            var clock=Stopwatch.StartNew();
            string first=LatestTag(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("latest="+first+" etag="+(latestEtag??"-")+" in "+clock.ElapsedMilliseconds+" ms");
            clock.Restart();
            string second=LatestTag(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("again="+second+" (cached via ETag) in "+clock.ElapsedMilliseconds+" ms");
            Console.WriteLine("current="+DisplayVersion+" newer="+(Normalize(new Version(second.Substring(1)))>Normalize(CurrentVersion)));
            var staged=Prepare(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("prepare="+(staged??"nothing newer"));
            return 0;
        }catch(Exception e){Console.WriteLine("error: "+e.Message);return 1;}
    }
    static Version Normalize(Version v){return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
    internal static async Task<string> Prepare(CancellationToken cancellation){
        string manifestUrl;
        try{
            string tag=await LatestTag(cancellation).ConfigureAwait(false);
            Version tagged;
            // Nothing newer: stop here without downloading anything else.
            if(Version.TryParse(tag.Substring(1),out tagged)&&Normalize(tagged)<=Normalize(CurrentVersion))return null;
            manifestUrl=Feed+"/download/"+tag+"/lunar-update.json";
        }catch(OperationCanceledException){throw;}
        catch(Exception){
            // API unavailable or rate-limited: fall back to the latest redirect, with a cache-buster.
            manifestUrl=Feed+"/latest/download/lunar-update.json?t="+DateTime.UtcNow.Ticks;
        }
        byte[] bytes;
        using(var buffer=new MemoryStream()){
            await Download(manifestUrl,buffer,MaxManifest,cancellation).ConfigureAwait(false);
            bytes=buffer.ToArray();
        }
        var package=VerifyManifest(bytes,IsInstalled(Application.ExecutablePath));
        if(package.Version<=CurrentVersion)return null;
        string folder=Path.Combine(Root,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,package.Name);
        string release=ReleaseVersion(package.Version);
        try{
            // The executable is shipped inside a zip (no raw .exe on GitHub); the signed manifest
            // still describes the executable, so the extracted file is hash-verified below.
            using(var archive=new MemoryStream()){
                await Download(Feed+"/download/v"+release+"/Lunar-"+release+".zip",archive,MaxPackage,cancellation).ConfigureAwait(false);
                archive.Position=0;
                using(var zip=new ZipArchive(archive,ZipArchiveMode.Read)){
                    var entry=zip.GetEntry(package.Name);
                    if(entry==null||entry.Length!=package.Size)throw new InvalidDataException("Invalid update archive.");
                    using(var input=entry.Open())
                    using(var file=File.Create(path))
                        await input.CopyToAsync(file,81920,cancellation).ConfigureAwait(false);
                }
            }
            VerifyFile(path,package);
            File.WriteAllBytes(Path.Combine(folder,"lunar-update.json"),bytes);
            return folder;
        }catch{
            DeleteFile(path);
            throw;
        }
    }
    internal static void Start(string folder,bool restart){
        string target=Application.ExecutablePath;
        string tool=Path.Combine(folder,"LunarUpdate.exe");
        File.Copy(target,tool,true);
        using(var parent=Process.GetCurrentProcess()){
            File.WriteAllText(Path.Combine(folder,"job.json"),Json.Codec.Serialize(new{
                target=target,pid=parent.Id,started=parent.StartTime.ToUniversalTime().Ticks.ToString(),restart=restart
            }));
        }
        Process.Start(new ProcessStartInfo(tool,"--apply-update"){UseShellExecute=false,CreateNoWindow=true});
    }
    internal static int Apply(){
        string folder=Path.GetDirectoryName(Application.ExecutablePath);
        try{
            Guid id;
            if(!String.Equals(Path.GetDirectoryName(folder),Root,StringComparison.OrdinalIgnoreCase)||
                !Guid.TryParseExact(Path.GetFileName(folder),"N",out id))return 1;
            var job=Json.Codec.DeserializeObject(File.ReadAllText(Path.Combine(folder,"job.json")));
            string target=Path.GetFullPath(Json.S(job,"target"));
            if(!File.Exists(target)||!String.Equals(Path.GetExtension(target),".exe",StringComparison.OrdinalIgnoreCase))return 1;
            int pid=(int)HistoryLogic.N(job,"pid",-1);
            if(pid<=0)return 1;
            var package=VerifyManifest(File.ReadAllBytes(Path.Combine(folder,"lunar-update.json")),IsInstalled(target));
            if(package.Version<=ReadVersion(target))return 0;
            try{
                using(var parent=Process.GetProcessById(pid)){
                    if(parent.StartTime.ToUniversalTime().Ticks.ToString()==Json.S(job,"started")&&!parent.WaitForExit(60000))return 1;
                }
            }catch(ArgumentException){}
            string source=Path.Combine(folder,package.Name);
            VerifyFile(source,package);
            if(!Install(source,target,package))return 1;
            DeleteFile(source);
            DeleteFile(Path.Combine(folder,"job.json"));
            DeleteFile(Path.Combine(folder,"lunar-update.json"));
            if(Json.B(job,"restart"))
                Process.Start(new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)});
            return 0;
        }catch(Exception){return 1;}
    }
    static bool Install(string source,string target,UpdatePackage package){
        if(!String.Equals(Path.GetExtension(source),".exe",StringComparison.OrdinalIgnoreCase)||
            ReadVersion(source)!=package.Version)return false;
        string temporary=target+"."+Guid.NewGuid().ToString("N")+".update";
        try{
            File.Copy(source,temporary);
            for(int attempt=0;attempt<20;attempt++){
                try{File.Replace(temporary,target,null);return true;}
                catch(IOException){if(attempt==19)return false;Thread.Sleep(250);}
                catch(UnauthorizedAccessException){return false;}
            }
            return false;
        }finally{DeleteFile(temporary);}
    }
    static void DeleteFile(string path){
        try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
}
