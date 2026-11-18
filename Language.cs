using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Lunar {
// Multi-language support.
// English is the source language used in the code. Translations live in embedded assets/translations*.json:
//   { "languages": ["pt-BR","es-ES",...], "strings": { "English text": ["Português","Español",...], ... } }
// Keys may contain {0}, {1}… placeholders ("{0} matches") that match numbers, names, ranks, etc. at runtime.
// Translate() accepts text in ANY supported language, so control texts can switch between languages freely.
static class LunarLanguage {
    public sealed class Option {public string Code,Name;}
    sealed class Pattern {public Regex Match;public string[] Templates;public int Literal;}

    static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","language.txt");
    static readonly Dictionary<string,string> Names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
        {"en-US","English"},{"pt-BR","Português (Brasil)"},{"es-ES","Español"},{"fr-FR","Français"},{"de-DE","Deutsch"},
        {"it-IT","Italiano"},{"tr-TR","Türkçe"},{"ru-RU","Русский"},{"pl-PL","Polski"}
    };
    public static readonly Option[] Options;
    // English -> translations, indexed like Options (index 0 = English, always the key itself).
    static readonly Dictionary<string,string[]> Table=new Dictionary<string,string[]>(StringComparer.Ordinal);
    // Any translated text -> English key.
    static readonly Dictionary<string,string> Reverse=new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly List<Pattern> Patterns=new List<Pattern>();
    // Strong separators first (lines, " · "), then sentences inside each piece.
    static readonly Regex Separator=new Regex(@"(\s+·\s+|\r?\n)",RegexOptions.Compiled);
    static readonly Regex Sentence=new Regex(@"((?<=[.!?…])\s+(?=\S))",RegexOptions.Compiled);
    static readonly Dictionary<string,string> Cache=new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly object Gate=new object();
    static int current;

    static LunarLanguage(){
        var codes=new List<string>{"en-US"};
        var rows=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
        var assembly=typeof(LunarLanguage).Assembly;
        foreach(var resource in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("translations",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".json",StringComparison.OrdinalIgnoreCase)).OrderBy(n=>n,StringComparer.Ordinal)){
            try{
                string json;
                using(var stream=assembly.GetManifestResourceStream(resource))using(var reader=new StreamReader(stream,Encoding.UTF8))json=reader.ReadToEnd();
                var root=Json.Codec.DeserializeObject(json);
                var languages=Json.A(Json.Get(root,"languages")).Select(Convert.ToString).ToArray();
                foreach(var code in languages)if(!codes.Contains(code,StringComparer.OrdinalIgnoreCase))codes.Add(code);
                var strings=Json.Get(root,"strings") as Dictionary<string,object>;
                if(strings==null)continue;
                foreach(var pair in strings){
                    var values=Json.A(pair.Value).Select(Convert.ToString).ToArray();
                    Dictionary<string,string> row;
                    if(!rows.TryGetValue(pair.Key,out row))rows[pair.Key]=row=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    for(int i=0;i<languages.Length&&i<values.Length;i++)if(!String.IsNullOrEmpty(values[i]))row[languages[i]]=values[i];
                }
            }catch(Exception){}
        }
        Options=codes.Select(code=>new Option{Code=code,Name=Names.ContainsKey(code)?Names[code]:code}).ToArray();
        foreach(var row in rows){
            var values=new string[codes.Count];
            values[0]=row.Key;
            for(int i=1;i<codes.Count;i++){string value;values[i]=row.Value.TryGetValue(codes[i],out value)?value:row.Key;}
            Table[row.Key]=values;
            if(row.Key.Contains("{0}")){AddPattern(values);continue;}
            for(int i=1;i<values.Length;i++)if(values[i]!=row.Key&&!Reverse.ContainsKey(values[i])&&!Table.ContainsKey(values[i]))Reverse[values[i]]=row.Key;
        }
        // Longer literal text first, so "{0} matches in the current filter" wins over "{0} matches".
        Patterns.Sort((a,b)=>b.Literal.CompareTo(a.Literal));
        current=Load();
    }
    static void AddPattern(string[] templates){
        foreach(var template in templates.Distinct()){
            int literal=Regex.Replace(template,@"\{\d\}","").Trim().Length;
            if(literal<2)continue;
            string regex="^"+Regex.Replace(Regex.Escape(template),@"\\\{(\d)}",m=>"(?<a"+m.Groups[1].Value+">[^·\\r\\n]+?)")+"$";
            try{Patterns.Add(new Pattern{Match=new Regex(regex,RegexOptions.Compiled|RegexOptions.Singleline),Templates=templates,Literal=literal});}catch(ArgumentException){}
        }
    }
    static int Load(){
        try{
            string code=File.ReadAllText(FilePath).Trim();
            for(int i=0;i<Options.Length;i++)if(String.Equals(Options[i].Code,code,StringComparison.OrdinalIgnoreCase))return i;
        }catch(Exception){}
        return 0;
    }

    public static int Index{get{return current;}}
    public static string Code{get{return Options[current].Code;}}
    public static bool Portuguese{get{return Code=="pt-BR";}}
    public static System.Globalization.CultureInfo Culture{get{try{return System.Globalization.CultureInfo.GetCultureInfo(Code);}catch(Exception){return System.Globalization.CultureInfo.InvariantCulture;}}}
    public static event Action Changed;
    public static void Set(int index){
        if(index<0||index>=Options.Length)return;
        current=index;
        lock(Gate)Cache.Clear();
        try{Directory.CreateDirectory(Path.GetDirectoryName(FilePath));File.WriteAllText(FilePath,Options[index].Code,new UTF8Encoding(false));}
        catch(IOException){}catch(UnauthorizedAccessException){}
        if(Changed!=null)Changed();
    }
    // Translation into a given language without changing (or saving) the user's choice. Used by checks.
    internal static string TranslateFor(int index,string text){
        lock(Gate){
            int saved=current;current=index;Cache.Clear();
            try{return Translate(text);}finally{current=saved;Cache.Clear();}
        }
    }
    internal static int Run(){
        string[] samples={"Home","Settings","12 matches","Level 140","Peak · Diamond 2","Today · 5 matches","Saved Nov 17, 23:00",
            "Partial results. Retrying automatically. Some match details are unavailable.","Ascent · In match","Current shortcut: Alt + T\r\nUse Alt, Ctrl or Shift with a key. Esc cancels.",
            "Immortal 3 · last known rank","Riot rate limit reached. Retrying in 42s…","Players · Deathmatch","SAVED ROTATION · EXPIRED","Aurora#BR2","Gold","7 W  | 3 L","Exclusive Edition"};
        int failures=0;
        try{Console.OutputEncoding=Encoding.UTF8;}catch(Exception){}
        for(int i=0;i<Options.Length;i++){
            Console.WriteLine("== "+Options[i].Code+" ("+Options[i].Name+")");
            foreach(var sample in samples)Console.WriteLine("  "+sample.Replace("\r\n"," / ")+"  =>  "+TranslateFor(i,sample).Replace("\r\n"," / "));
            // Round trip: a translated text must translate back to English.
            foreach(var sample in samples){string there=TranslateFor(i,sample),back=TranslateFor(0,there);if(back!=sample&&i>0){Console.WriteLine("  ROUND TRIP: "+sample+" -> "+there+" -> "+back);failures++;}}
        }
        Console.WriteLine(Table.Count+" strings, "+Patterns.Count+" patterns, "+Options.Length+" languages, "+failures+" round-trip differences");
        return 0;
    }
    // Picks the English text; kept for older call sites that also pass Portuguese.
    public static string Text(string english,string brazilian){return Translate(english);}
    public static string Format(string english,params object[] args){return String.Format(Translate(english),args);}

    public static string Translate(string text){
        if(String.IsNullOrEmpty(text)||text.Length>600)return text;
        lock(Gate){
            string cached;
            if(Cache.TryGetValue(text,out cached))return cached;
            string result=TranslateSegments(text);
            if(Cache.Count>6000)Cache.Clear();
            Cache[text]=result;
            return result;
        }
    }
    static string TranslateSegments(string text){
        return Split(text,Separator,true)??text;
    }
    // Composite text: "Ascent  ·  In match", multi-line text, "Partial results. Retrying automatically."…
    static string Split(string text,Regex separator,bool allowSentences){
        string whole=TranslateOne(text,0);
        if(whole!=null)return whole;
        var parts=separator.Split(text);
        if(parts.Length<2)return allowSentences?Split(text,Sentence,false):null;
        var builder=new StringBuilder();
        bool changed=false;
        for(int i=0;i<parts.Length;i++){
            string part=parts[i];
            if(i%2==1||part.Trim().Length==0){builder.Append(part);continue;} // odd indexes are the captured separators
            string translated=allowSentences?Split(part,Sentence,false):TranslateOne(part,0);
            if(translated!=null){builder.Append(translated);changed=true;}
            else builder.Append(part);
        }
        return changed?builder.ToString():null;
    }
    // Returns null when nothing matched.
    static string TranslateOne(string text,int depth){
        string[] row;
        if(Table.TryGetValue(text,out row))return row[current];
        string english;
        if(Reverse.TryGetValue(text,out english))return Table[english][current];
        // Keep surrounding spaces/punctuation used by concatenated UI text: " · recent match", "Level ".
        string trimmed=text.Trim();
        if(trimmed.Length!=text.Length&&trimmed.Length>0){
            string inner=TranslateOne(trimmed,depth);
            if(inner!=null){int start=text.IndexOf(trimmed,StringComparison.Ordinal);return text.Substring(0,start)+inner+text.Substring(start+trimmed.Length);}
        }
        if(depth>2)return null;
        foreach(var pattern in Patterns){
            var match=pattern.Match.Match(text);
            if(!match.Success)continue;
            string result=pattern.Templates[current];
            for(int i=0;i<10;i++){
                var group=match.Groups["a"+i];
                if(!group.Success)continue;
                string value=group.Value;
                string translated=TranslateOne(value,depth+1);
                result=result.Replace("{"+i+"}",translated??value);
            }
            return result;
        }
        return null;
    }

    // Keeps every control's text in the selected language, including texts the code sets later.
    sealed class Watch {public bool Busy;}
    static readonly ConditionalWeakTable<Control,Watch> Watched=new ConditionalWeakTable<Control,Watch>();
    public static void Attach(Control root){
        if(root==null)return;
        Watch watch;
        if(!Watched.TryGetValue(root,out watch)){
            watch=new Watch();
            Watched.Add(root,watch);
            if(!(root is SoftSelect)&&!(root is TextBoxBase)){
                root.TextChanged+=delegate{Refresh(root,watch);};
            }
            root.ControlAdded+=delegate(object sender,ControlEventArgs e){Attach(e.Control);};
        }
        Refresh(root,watch);
        foreach(Control child in root.Controls)Attach(child);
    }
    static void Refresh(Control control,Watch watch){
        if(watch.Busy||control is SoftSelect||control is TextBoxBase||control.IsDisposed)return;
        string text=control.Text;
        if(String.IsNullOrEmpty(text))return;
        string translated=Translate(text);
        if(translated==text)return;
        watch.Busy=true;
        try{control.Text=translated;}finally{watch.Busy=false;}
    }
    public static void RefreshAll(Control root){
        if(root==null)return;
        Watch watch;
        if(Watched.TryGetValue(root,out watch))Refresh(root,watch);
        foreach(Control child in root.Controls)RefreshAll(child);
        root.Invalidate();
    }
}

// Drop-in for TextRenderer.DrawText that translates the text first.
static class LunarText {
    public static void DrawText(IDeviceContext dc,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags){TextRenderer.DrawText(dc,LunarLanguage.Translate(text),font,bounds,color,flags);}
    public static void DrawText(IDeviceContext dc,string text,Font font,Rectangle bounds,Color color){TextRenderer.DrawText(dc,LunarLanguage.Translate(text),font,bounds,color);}
    public static void DrawText(IDeviceContext dc,string text,Font font,Rectangle bounds,Color color,Color back,TextFormatFlags flags){TextRenderer.DrawText(dc,LunarLanguage.Translate(text),font,bounds,color,back,flags);}
    public static void DrawText(IDeviceContext dc,string text,Font font,Point point,Color color){TextRenderer.DrawText(dc,LunarLanguage.Translate(text),font,point,color);}
    public static void DrawText(IDeviceContext dc,string text,Font font,Point point,Color color,TextFormatFlags flags){TextRenderer.DrawText(dc,LunarLanguage.Translate(text),font,point,color,flags);}
    public static Size MeasureText(IDeviceContext dc,string text,Font font,Size proposed,TextFormatFlags flags){return TextRenderer.MeasureText(dc,LunarLanguage.Translate(text),font,proposed,flags);}
    public static Size MeasureText(IDeviceContext dc,string text,Font font){return TextRenderer.MeasureText(dc,LunarLanguage.Translate(text),font);}
    public static Size MeasureText(string text,Font font){return TextRenderer.MeasureText(LunarLanguage.Translate(text),font);}
    public static Size MeasureText(string text,Font font,Size proposed,TextFormatFlags flags){return TextRenderer.MeasureText(LunarLanguage.Translate(text),font,proposed,flags);}
    public static Size MeasureText(IDeviceContext dc,string text,Font font,Size proposed){return TextRenderer.MeasureText(dc,LunarLanguage.Translate(text),font,proposed);}
}
}
