using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lunar {
static class RiotNames {
    public const string Notice=
        "Lunar resolves the in-game names that VALORANT hides during agent select by calling a "+
        "Riot endpoint with the local, signed-in Riot Client session. That specific call is NOT "+
        "included in this public source. A build made from this repository compiles the stub below "+
        "and shows players without their names. The working implementation ships only in the "+
        "official Lunar release and is kept private on purpose.";
    public static Dictionary<string,string> Resolve(string[] subjects,Func<string,object,object> put){
        return new Dictionary<string,string>(StringComparer.Ordinal);
    }
    public static Task<Dictionary<string,string>> ResolveAsync(string[] subjects,Func<string,object,Task<object>> put){
        return Task.FromResult(new Dictionary<string,string>(StringComparer.Ordinal));
    }
}
}
