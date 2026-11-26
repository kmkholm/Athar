using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Athar
{
    public class Label { public string Name = "", Cat = "", Source = ""; }

    /// <summary>Address labels: the user's own, a small built-in list of well-known services, and tags returned by the explorer.</summary>
    public static class Labels
    {
        public static readonly string[] Cats = { "exchange", "mixer", "bridge", "dex", "threat", "contract", "user", "other" };
        static readonly Dictionary<string, Label> Builtin = new();
        public static readonly ConcurrentDictionary<string, Label> Seen = new();
        public static int BuiltinCount => Builtin.Count;
        public static IEnumerable<KeyValuePair<string, Label>> BuiltinAll => Builtin;

        static void B(string cat, string name, string addr) => Builtin[addr.ToLowerInvariant()] = new Label { Name = name, Cat = cat, Source = "built-in" };
        static Labels()
        {
            B("mixer", "Tornado Cash: 0.1 ETH", "0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc");
            B("mixer", "Tornado Cash: 1 ETH", "0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936");
            B("mixer", "Tornado Cash: 10 ETH", "0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF");
            B("mixer", "Tornado Cash: 100 ETH", "0xA160cdAB225685dA1d56aa342Ad8841c3b53f291");
            B("mixer", "Tornado Cash: Router", "0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b");
            B("mixer", "Railgun: Relay", "0xFA7093CDD9EE6932B4eb2c9e1cde7CE00B1FA4b9");
            B("exchange", "Binance 7", "0xBE0eB53F46cd790Cd13851d5EFf43D12404d33E8");
            B("exchange", "Binance 8", "0xF977814e90dA44bFA03b6295A0616a897441aceC");
            B("exchange", "Binance 14", "0x28C6c06298d514Db089934071355E5743bf21d60");
            B("exchange", "Binance 15", "0x21a31Ee1afC51d94C2eFcCAa2092aD1028285549");
            B("exchange", "Binance 16", "0xDFd5293D8e347dFe59E90eFd55b2956a1343963d");
            B("exchange", "Coinbase 1", "0x71660c4005BA85c37ccec55d0C4493E66Fe775d3");
            B("exchange", "Kraken 4", "0x267be1C1D684F78cb4F6a176C4911b741E4Ffdc0");
            B("exchange", "OKX", "0x6cC5F688a315f3dC28A7781717a9A798a59fDA7b");
            B("exchange", "Bybit: Hot Wallet", "0xf89d7b9c864f589bbF53a82105107622B35EaA40");
            B("exchange", "KuCoin", "0x2B5634C42055806a59e9107ED44D43c426E58258");
            B("bridge", "Relay: Depository (Tron)", "TXtEs6t2oUWQsNos7m68gbHdE9Q5n6x2oN");
            B("bridge", "THORChain: Router", "0xD37BbE5744D730a1d98d8DC97c42F0Ca46aD7146");
            B("bridge", "Wormhole: Token Bridge", "0x3ee18B2214AFF97000D974cf647E7C347E8fa585");
            B("bridge", "Polygon: ERC20 Bridge", "0x40ec5B33f54e0E8A33A975908C5BA1c14e5BbbDF");
            B("bridge", "Arbitrum One: Bridge", "0x8315177aB297bA92A06054cE80a67Ed4DBd7ed3a");
            B("bridge", "Optimism: Gateway", "0x99C9fc46f92E8a1c0deC1b1747d010903E884bE1");
            B("dex", "Uniswap V2: Router", "0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D");
            B("dex", "Uniswap V3: Router", "0xE592427A0AEce92De3Edee1F18E0157C05861564");
            B("dex", "Uniswap V3: Router 2", "0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45");
            B("dex", "1inch v5: Router", "0x1111111254EEB25477B68fb85Ed929f73A960582");
            B("contract", "Wrapped Ether (WETH)", "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2");
            B("contract", "Beacon Deposit Contract", "0x00000000219ab540356cBB839Cbe05303d7705Fa");
            B("other", "Null address (burn / mint)", "0x0000000000000000000000000000000000000000");
            B("other", "Burn address", "0x000000000000000000000000000000000000dEaD");
            B("threat", "Bybit Exploiter (Feb 2025)", "0x47666Fab8bd0Ac7003bce3f5C3585383F09486E2");
        }

        public static bool IsService(string cat) => cat == "exchange" || cat == "mixer" || cat == "bridge" || cat == "dex";

        public static string CatFromText(string text, bool scam, bool contract)
        {
            string t = (text ?? "").ToLowerInvariant();
            bool Has(params string[] w) => w.Any(t.Contains);
            if (scam || Has("exploit", "hack", "phish", "scam", "sanction", "ofac", "lazarus", "drainer", "heist", "stolen")) return "threat";
            if (Has("tornado", "mixer", "railgun", "sinbad", "blender", "wasabi")) return "mixer";
            if (Has("bridge", "wormhole", "stargate", "across", "thorchain", "synapse", "hop protocol")) return "bridge";
            if (Has("exchange", "hot wallet", "cex", "binance", "coinbase", "kraken", "okx", "bybit", "kucoin", "huobi", "htx", "gate.io", "bitfinex", "bitget", "mexc", "crypto.com")) return "exchange";
            if (Has("dex", "uniswap", "1inch", "router", "swap", "aggregator", "curve", "sushi", "pancake", "cow protocol", "0x protocol")) return "dex";
            if (contract) return "contract";
            return t.Trim().Length > 0 ? "other" : "";
        }

        public static Label FromInfo(AddrInfo i)
        {
            if (i == null) return null;
            string text = i.Name + " " + string.Join(" ", i.Tags);
            string cat = CatFromText(text, i.IsScam, i.IsContract);
            if (cat == "") return null;
            string name = i.Name != "" ? i.Name : i.Tags.FirstOrDefault() ?? (i.IsScam ? "Flagged as scam" : i.IsContract ? "Contract" : "");
            return new Label { Name = name, Cat = cat, Source = "explorer" };
        }

        public static void Learn(AddrInfo i)
        {
            var l = FromInfo(i);
            if (l != null) Seen[i.Address] = l;
        }

        public static Label Find(string addr)
        {
            if (string.IsNullOrEmpty(addr)) return null;
            addr = Addr.Bare(addr);
            var u = Store.UserLabels.FirstOrDefault(x => string.Equals(x.Address, addr, StringComparison.OrdinalIgnoreCase));
            if (u != null) return new Label { Name = u.Name, Cat = string.IsNullOrEmpty(u.Cat) ? "user" : u.Cat, Source = "user" };
            if (Builtin.TryGetValue(addr.ToLowerInvariant(), out var b)) return b;
            return Seen.TryGetValue(addr, out var s) ? s : null;
        }
        /// <summary>Label, or the shortened address. Generic names the explorer gives to contracts keep the address next to them.</summary>
        public static string Show(string addr)
        {
            var l = Find(addr);
            if (l == null || l.Name == "") return Fmt.Short(addr);
            if (addr != Addr.Bare(addr)) return addr.Substring(0, addr.IndexOf(':') + 1) + l.Name;
            return l.Source == "explorer" && (l.Cat == "contract" || l.Cat == "other" || l.Cat == "dex") ? l.Name + " · " + Fmt.Short(addr) : l.Name;
        }
    }

    public class TraceOptions
    {
        public string Chain = "eth", Start = "", Asset = "";
        public bool Forward = true, StopAtServices = true, AllTokens = false, FollowBridges = true;
        public int MaxHops = 3, MaxNodes = 150, Branch = 25, PerNode = 1000, RootPerNode = 5000, WindowDays = 0, BusyTx = 5000;
        public double MinSharePct = 3;
        public long FromTime;
    }

    public class GNode
    {
        public string Id { get; set; }
        public string Label { get; set; } = "";
        public string Cat { get; set; } = "";
        public int Depth { get; set; }
        public string Stop { get; set; } = "";
        public bool Expanded { get; set; }
        public bool IsContract { get; set; }
        public long TxCount { get; set; } = -1;
        public long Arrival { get; set; }
        public decimal Balance { get; set; }
        public string Frozen { get; set; } = "";
        /// <summary>True when the sender got another asset back in the same transactions: the value was swapped here, not parked.</summary>
        public bool Swap { get; set; }
        public decimal FrozenUsd { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Bx { get; set; }
        public double By { get; set; }
        public double R { get; set; } = 20;
    }

    public class GEdge
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Asset { get; set; }
        public string Contract { get; set; } = "";
        public decimal Amount { get; set; }
        public int Count { get; set; }
        public long First { get; set; }
        public long Last { get; set; }
        public List<string> Hashes { get; set; } = new();
        /// <summary>Network the transfer happened on ("" = the graph's own network).</summary>
        public string ChainKey { get; set; } = "";
        /// <summary>True for the payout side of a bridge or cross-chain swap.</summary>
        public bool Bridge { get; set; }
    }

    public class Graph
    {
        public string Chain { get; set; }
        public string Start { get; set; }
        public bool Forward { get; set; }
        public long Created { get; set; }
        public decimal UsdRate { get; set; }
        public string Options { get; set; } = "";
        public List<GNode> Nodes { get; set; } = new();
        public List<GEdge> Edges { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        public Dictionary<string, decimal> Rates { get; set; } = new();
        public GNode Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);
        /// <summary>Network key of a node id ("btc:bc1…" → btc; a bare id is on the graph's own network).</summary>
        public string ChainOf(string id) { int c = id.IndexOf(':'); return c > 0 && c < 12 ? id.Substring(0, c) : Chain; }
        public string IdFor(string chainKey, string address) => chainKey == Chain ? address : chainKey + ":" + address;
        decimal Rate(string chainKey) => Rates.TryGetValue(chainKey == "" ? Chain : chainKey, out var r) ? r : (chainKey == "" || chainKey == Chain ? UsdRate : 0);
        /// <summary>Where the value at this address stands: start, frozen, holding, exchange, mixer, bridge, dex, unexplored or passed (moved on).</summary>
        public string Status(GNode n)
        {
            if (n.Frozen != "") return "frozen";
            if (n.Depth == 0) return "start";
            if (n.Stop == "service") return n.Cat;
            if (n.Stop == "end") return "holding";
            if (n.Stop == "") return n.Expanded ? "passed" : "unexplored";
            return n.Stop == "burn" ? "burn" : n.Stop == "otherchain" ? "otherchain" : "unexplored";
        }
        /// <summary>Value that reached the address in this trace, in today's USD (0 when it cannot be priced).</summary>
        public decimal UsdIn(GNode n) => Edges.Where(e => (Forward ? e.To : e.From) == n.Id).Sum(e => Usd(e) ?? 0m);
        public decimal UsdOut(GNode n) => Edges.Where(e => (Forward ? e.From : e.To) == n.Id).Sum(e => Usd(e) ?? 0m);

        public decimal? Usd(GEdge e)
        {
            if (Chains.Stable.Contains(e.Contract) || (e.Bridge && (e.Asset == "USDT" || e.Asset == "USDC" || e.Asset == "DAI"))) return e.Amount;
            if (e.Contract == "" && Rate(e.ChainKey) > 0) return e.Amount * Rate(e.ChainKey);
            return null;
        }
    }

    /// <summary>Optional capability: tells whether an address is blacklisted (frozen) by a stablecoin issuer.</summary>
    public interface IFreezeCheck
    {
        Task<List<string>> Frozen(string address, CancellationToken ct);
        /// <summary>Balance of the frozen stablecoins still sitting at the address (USD).</summary>
        Task<decimal> FrozenAmount(string address, List<string> tokens, CancellationToken ct);
    }

    /// <summary>Fund-flow tracing, level by level: from each address follow the largest transfers made after the funds arrived.</summary>
    public static class Tracer
    {
        public static IEnumerable<string> Starts(Chain chain, string text) =>
            (text ?? "").Split(new[] { '\n', '\r', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Where(a => Addr.Detect(a) is var d && d.kind == "address" && d.family == chain.Family).Select(a => Addr.Norm(chain.Family, a)).Distinct();

        public static async Task<Graph> Run(TraceOptions o, Action<string> progress, CancellationToken ct)
        {
            var chain = Chains.Get(o.Chain);
            var starts = Starts(chain, o.Start).ToList();
            if (starts.Count == 0) throw new ApiException("No valid start address for " + chain.Name);
            var g = new Graph { Chain = o.Chain, Start = string.Join("\n", starts), Forward = o.Forward, Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            g.Options = $"hops={o.MaxHops}; branch={o.Branch}; min share={o.MinSharePct}%; max nodes={o.MaxNodes}; asset={(o.Asset == "" ? (o.AllTokens ? "all tokens" : "coin + major stablecoins") : o.Asset)}; from={Fmt.Time(o.FromTime)}; window={(o.WindowDays > 0 ? o.WindowDays + "d" : "none")}";
            foreach (var a in starts) g.Nodes.Add(new GNode { Id = a, Depth = 0, Arrival = o.FromTime });
            await Expand(g, g.Nodes.ToList(), o, o.MaxHops, progress, ct);
            return g;
        }

        /// <summary>Continue an existing graph from one of its unexpanded nodes.</summary>
        public static Task More(Graph g, GNode from, TraceOptions o, int extraHops, Action<string> progress, CancellationToken ct)
        {
            from.Stop = "";
            o.MaxNodes = g.Nodes.Count + Math.Max(10, o.MaxNodes / 2);
            return Expand(g, new[] { from }, o, from.Depth + extraHops, progress, ct, force: from);
        }

        static async Task Expand(Graph g, IEnumerable<GNode> seeds, TraceOptions o, int maxDepth, Action<string> progress, CancellationToken ct, GNode force = null)
        {
            var level = seeds.ToList();
            int done = 0;
            using var gate = new SemaphoreSlim(4);
            async Task<T> Limited<T>(Func<Task<T>> f) { await gate.WaitAsync(ct); try { return await f(); } finally { gate.Release(); } }
            void Note(string s) { lock (g.Notes) g.Notes.Add(s); }
            IChainApi Api(GNode n) { var c = Chains.All.FirstOrDefault(x => x.Key == g.ChainOf(n.Id)); return c != null && c.Ready ? c.Api : null; }
            decimal Value(string chainKey, string contract, decimal sum) => Chains.Stable.Contains(contract) ? sum : contract == "" && g.Rates.TryGetValue(chainKey, out var r) ? sum * r : 0m;

            while (level.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke($"{done} ✓  ·  {level.Count} …  ·  {Fmt.Short(level[0].Id)}");
                // 1. who are these addresses? (one explorer call each, in parallel)
                var infos = await Task.WhenAll(level.Select(n => Limited(async () =>
                {
                    var api = Api(n); if (api == null) return null;
                    try { var i = await api.Info(Addr.Bare(n.Id), ct); Labels.Learn(i); return i; }
                    catch (ApiException) { Note("nolabel"); return null; }
                })));
                var todo = new List<GNode>();
                var next = new List<GNode>();
                for (int k = 0; k < level.Count; k++)
                {
                    var n = level[k]; var info = infos[k];
                    string ck = g.ChainOf(n.Id);
                    if (Api(n) == null) { n.Stop = "otherchain"; if (n.Label == "") n.Label = Bridges.NameOf(ck); continue; }
                    if (info != null)
                    {
                        n.IsContract = info.IsContract; n.TxCount = info.TxCount; n.Balance = info.Balance;
                        if (info.Usd > 0) { g.Rates[ck] = info.Usd; if (ck == g.Chain) g.UsdRate = info.Usd; }
                    }
                    var lab = Labels.Find(n.Id);
                    if (lab != null) { n.Label = lab.Name; n.Cat = lab.Cat; }
                    else if (n.IsContract) { n.Cat = "contract"; n.Label = "Contract"; }
                    if (n.Swap && n.IsContract && (n.Cat == "contract" || n.Cat == "other" || n.Cat == "")) n.Cat = "dex";
                    bool root = n.Depth == 0 || n == force;
                    if (!root)
                    {
                        if (o.StopAtServices && Labels.IsService(n.Cat)) { n.Stop = "service"; continue; }
                        if (n.Cat == "other" && lab != null && lab.Source == "built-in") { n.Stop = "burn"; continue; }
                        if (n.IsContract) { n.Stop = "contract"; continue; }
                        if (n.TxCount > o.BusyTx) { n.Stop = "busy"; continue; }
                        // dormant stash: the coin that arrived is still there, no need to read its history
                        decimal arrived = g.Edges.Where(e => (g.Forward ? e.To : e.From) == n.Id && e.Contract == "").Sum(e => e.Amount);
                        if (g.Forward && info != null && arrived > 0 && info.Balance >= arrived * 0.95m) { n.Stop = "end"; continue; }
                    }
                    if (n.Depth >= maxDepth) { n.Stop = "hops"; continue; }
                    todo.Add(n);
                }
                // 2. did the value leave this network here? ask the bridges themselves about the transactions that entered a contract or a hub wallet
                if (g.Forward && o.FollowBridges && Chains.All.Any(c => c.Key == g.Chain && c.Explorer != ""))
                {
                    var cands = level.Where(n => n.Depth > 0 && !n.Swap && n.Cat != "mixer" && n.Cat != "exchange" && n.Stop != "end" && n.Stop != "otherchain" && n.Stop != "burn").ToList();
                    await Task.WhenAll(cands.Select(n => Limited(async () =>
                    {
                        string ck = g.ChainOf(n.Id);
                        if (ck == "demo") return 0;
                        foreach (var e in g.Edges.Where(x => x.To == n.Id && !x.Bridge).ToList())
                        {
                            bool hub = n.Stop == "contract" || n.Stop == "busy" || n.Stop == "service";
                            if (g.Usd(e) is decimal uv && uv < (hub ? 20000m : 100000m)) continue;
                            bool bulk = false;
                            foreach (string h in e.Hashes.Take(hub ? 3 : 1))
                            {
                                if (bulk) break;
                                var hops = (await Bridges.Resolve(ck, h, ct)).Where(x => x.Outbound && x.Address != "").ToList();
                                if (hops.Count > 0 && hops[0].Service == "THORChain" && e.Count > 3)
                                {
                                    try { var all = await Bridges.ThorAll(Addr.Bare(e.From), e.First - 3600, ct); if (all.Count > 0) { hops = all; bulk = true; } } catch (ApiException) { }
                                }
                                else if (hops.Count > 0 && hops[0].Service == "Relay" && e.Count > 1)
                                {
                                    try { var all = await Bridges.RelayAll(Addr.Bare(e.From), e.First - 3600, ct); if (all.Count > 0) { hops = all; bulk = true; } } catch (ApiException) { }
                                }
                                hops.RemoveAll(x => x.Chain == ck && string.Equals(x.Address, Addr.Bare(e.From), StringComparison.OrdinalIgnoreCase));
                                if (hops.Count == 0) continue;
                                lock (g)
                                {
                                    n.Cat = "bridge"; n.Stop = "service";
                                    if (n.Label == "" || n.Label == "Contract") n.Label = hops[0].Service;
                                    foreach (var hop in hops)
                                    {
                                        var dc = Chains.All.FirstOrDefault(c => c.Key == hop.Chain);
                                        string addr = dc != null ? Addr.Norm(dc.Family, hop.Address) : hop.Address;
                                        string id = g.IdFor(hop.Chain, addr);
                                        var child = g.Node(id);
                                        if (child == null)
                                        {
                                            if (g.Nodes.Count >= o.MaxNodes) { if (!g.Notes.Contains("limit")) g.Notes.Add("limit"); continue; }
                                            child = new GNode { Id = id, Depth = n.Depth + 1, Arrival = hop.Time };
                                            g.Nodes.Add(child); next.Add(child);
                                        }
                                        if (g.Edges.Any(x => x.From == n.Id && x.To == id && x.Hashes.Contains(hop.Tx))) continue;
                                        var same = g.Edges.FirstOrDefault(x => x.From == n.Id && x.To == id && x.Bridge && x.Asset == hop.Asset);
                                        if (same != null) { same.Amount += hop.Amount; same.Count++; same.Last = Math.Max(same.Last, hop.Time); same.Hashes.Add(hop.Tx); }
                                        else g.Edges.Add(new GEdge { From = n.Id, To = id, Asset = hop.Asset, Amount = hop.Amount, Count = 1, First = hop.Time, Last = hop.Time, Hashes = new List<string> { hop.Tx }, ChainKey = hop.Chain, Bridge = true });
                                    }
                                }
                            }
                            if (e.Count > 3 && n.Cat == "bridge" && !bulk && g.Edges.Any(x => x.From == n.Id && x.Bridge)) Note($"{Fmt.Short(e.From)} → {n.Label}: {e.Count} deposits, only the 3 largest were resolved.");
                        }
                        return 0;
                    })));
                }
                todo.RemoveAll(n => n.Stop == "service");
                if (level.Any(n => n.Label.StartsWith("Relay") && n.Cat == "bridge" && !g.Edges.Any(e => e.From == n.Id && e.Bridge)) && !g.Notes.Contains("relaykey")) g.Notes.Add("relaykey");
                // 3. frozen by a stablecoin issuer? (start addresses and anything that received stablecoins)
                var check = level.Where(n => Api(n) is IFreezeCheck && (n.Depth == 0 || g.Edges.Any(e => e.To == n.Id && (Chains.Stable.Contains(e.Contract) || e.Asset == "USDT" || e.Asset == "USDC")))).ToList();
                await Task.WhenAll(check.Select(n => Limited(async () =>
                {
                    var fz = (IFreezeCheck)Api(n);
                    try { var f = await fz.Frozen(Addr.Bare(n.Id), ct); if (f.Count > 0) { n.Frozen = string.Join(", ", f); n.FrozenUsd = await fz.FrozenAmount(Addr.Bare(n.Id), f, ct); } } catch (ApiException) { }
                    return 0;
                })));
                // 4. their transfers, in parallel
                var sets = await Task.WhenAll(todo.Select(n => Limited(async () =>
                {
                    try
                    {
                        long from = 0, to = 0;
                        if (o.Forward) { from = n.Arrival; if (o.WindowDays > 0 && n.Arrival > 0) to = n.Arrival + o.WindowDays * 86400L; }
                        else { to = n.Arrival; if (o.WindowDays > 0 && n.Arrival > 0) from = n.Arrival - o.WindowDays * 86400L; }
                        // with no start date the most recent activity is the meaningful window, not the oldest
                        bool asc = o.Forward && n.Arrival > 0;
                        return await Api(n).Transfers(Addr.Bare(n.Id), n.Depth == 0 ? o.RootPerNode : o.PerNode, asc, from, to, ct);
                    }
                    catch (ApiException e) { n.Stop = "error"; Note($"{Fmt.Short(n.Id)}: {e.Message}"); return null; }
                })));
                for (int k = 0; k < todo.Count; k++)
                {
                    var n = todo[k]; var set = sets[k];
                    done++;
                    if (set == null) continue;
                    string ck = g.ChainOf(n.Id); string bare = Addr.Bare(n.Id);
                    n.Expanded = true;
                    if (set.Truncated && n.Depth == 0 && n.Arrival == 0) { if (!g.Notes.Contains("recent")) g.Notes.Add("recent"); }
                    else if (set.Truncated) g.Notes.Add($"{Fmt.Short(n.Id)}: only part of its history was read (it is very active).");
                    var moves = set.Items.Where(x => (o.Forward ? x.From == bare : x.To == bare) && x.From != x.To && !Tokens.Suspicious(x) && (o.AllTokens || o.Asset != "" || Tokens.Major(x)));
                    if (o.Asset != "") moves = moves.Where(x => string.Equals(x.Asset, o.Asset, StringComparison.OrdinalIgnoreCase));
                    var groups = moves.GroupBy(x => (cp: o.Forward ? x.To : x.From, asset: x.Asset, c: x.Contract)).Where(x => !string.IsNullOrEmpty(x.Key.cp)).ToList();
                    var totals = groups.GroupBy(x => (x.Key.asset, x.Key.c)).ToDictionary(x => x.Key, x => x.Sum(y => y.Sum(z => z.Amount)));
                    var picked = groups.Select(x => { decimal sum = x.Sum(z => z.Amount); decimal tot = totals[(x.Key.asset, x.Key.c)]; return (grp: x, sum, share: tot > 0 ? (double)(sum / tot) * 100 : 0); })
                        // a split into many equal parts must not fall under the share threshold: anything above $100,000 is always kept
                        .Where(x => x.share >= o.MinSharePct || Value(ck, x.grp.Key.c, x.sum) >= 100000m)
                        .OrderByDescending(x => Value(ck, x.grp.Key.c, x.sum))
                        .ThenByDescending(x => x.share).ThenByDescending(x => x.sum).Take(o.Branch).ToList();
                    if (picked.Count == 0) { n.Stop = groups.Count == 0 ? "end" : "small"; continue; }
                    // swap signature: in the same transaction the address gives one asset and gets a different one back
                    var swapHashes = o.Forward ? set.Items.Where(x => !Tokens.Suspicious(x)).GroupBy(x => x.Hash).Where(h => h.Any(x => x.From == bare) && h.Any(x => x.To == bare && h.Any(y => y.From == bare && (y.Asset != x.Asset || y.Contract != x.Contract)))).Select(h => h.Key).ToHashSet() : new HashSet<string>();
                    foreach (var p in picked)
                    {
                        string cp = g.IdFor(ck, p.grp.Key.cp);
                        var child = g.Node(cp);
                        long t = o.Forward ? p.grp.Min(x => x.Time) : p.grp.Max(x => x.Time);
                        if (child == null)
                        {
                            if (g.Nodes.Count >= o.MaxNodes) { if (!g.Notes.Contains("limit")) g.Notes.Add("limit"); continue; }
                            child = new GNode { Id = cp, Depth = n.Depth + 1, Arrival = t };
                            var l2 = Labels.Find(cp); if (l2 != null) { child.Label = l2.Name; child.Cat = l2.Cat; }
                            child.Swap = p.grp.Count(x => swapHashes.Contains(x.Hash)) * 2 > p.grp.Count();
                            g.Nodes.Add(child); next.Add(child);
                        }
                        else if (!child.Expanded && child.Stop == "" && child.Depth > n.Depth)
                        {
                            // funds reached this address more than once: start from the earliest arrival (latest departure when going backward)
                            child.Arrival = o.Forward ? Math.Min(child.Arrival, t) : Math.Max(child.Arrival, t);
                        }
                        string ef = o.Forward ? n.Id : cp, et = o.Forward ? cp : n.Id;
                        if (g.Edges.Any(e => e.From == ef && e.To == et && e.Asset == p.grp.Key.asset && e.Contract == p.grp.Key.c)) continue;
                        g.Edges.Add(new GEdge { From = ef, To = et, Asset = p.grp.Key.asset, Contract = p.grp.Key.c, Amount = p.sum, Count = p.grp.Count(), First = p.grp.Min(x => x.Time), Last = p.grp.Max(x => x.Time), Hashes = p.grp.OrderByDescending(x => x.Amount).Take(5).Select(x => x.Hash).ToList(), ChainKey = ck == g.Chain ? "" : ck });
                    }
                }
                level = next;
            }
            Layout(g);
            Bubbles(g);
        }

        /// <summary>Bubble map: radius follows the value that reached the address; children sit around their parent, then overlaps are pushed apart.</summary>
        public static void Bubbles(Graph g)
        {
            decimal max = Math.Max(1m, g.Nodes.Max(n => Math.Max(g.UsdIn(n), g.UsdOut(n))));
            foreach (var n in g.Nodes) n.R = 16 + 54 * Math.Sqrt((double)(Math.Max(g.UsdIn(n), g.UsdOut(n)) / max));
            var kids = g.Nodes.ToDictionary(n => n.Id, n => new List<GNode>());
            var placed = new HashSet<string>();
            foreach (var n in g.Nodes.Where(n => n.Depth > 0).OrderBy(n => n.Depth))
            {
                var e = g.Edges.Where(x => (g.Forward ? x.To : x.From) == n.Id).Select(x => g.Node(g.Forward ? x.From : x.To)).FirstOrDefault(p => p != null && p.Depth < n.Depth);
                if (e != null) kids[e.Id].Add(n);
            }
            var roots = g.Nodes.Where(n => n.Depth == 0).ToList();
            void Place(GNode n, double x, double y, double a0, double a1)
            {
                n.Bx = x; n.By = y; placed.Add(n.Id);
                var ch = kids[n.Id]; if (ch.Count == 0) return;
                ch = ch.OrderByDescending(c => c.R).ToList();
                double span = a1 - a0, ring = n.R + ch[0].R + 14, used = 0, rowMax = ch[0].R;
                foreach (var c in ch)
                {
                    double need = (2 * c.R + 8) / ring;
                    if (used + need > span && used > 0) { ring += rowMax + c.R + 10; used = 0; rowMax = c.R; need = (2 * c.R + 8) / ring; }
                    double a = a0 + used + need / 2;
                    used += need;
                    double sub = Math.Min(1.3, Math.Max(need, 0.5));
                    Place(c, x + ring * Math.Cos(a), y + ring * Math.Sin(a), a - sub / 2, a + sub / 2);
                }
            }
            for (int i = 0; i < roots.Count; i++) Place(roots[i], i * 900, 0, n_a(roots.Count == 1 ? 0 : -1.2), n_a(roots.Count == 1 ? 2 * Math.PI : 1.2));
            static double n_a(double v) => v;
            foreach (var n in g.Nodes.Where(n => !placed.Contains(n.Id))) { n.Bx = 0; n.By = 400 + n.Depth * 120; }
            for (int it = 0; it < 250; it++)
            {
                bool moved = false;
                for (int i = 0; i < g.Nodes.Count; i++)
                    for (int j = i + 1; j < g.Nodes.Count; j++)
                    {
                        var a = g.Nodes[i]; var b = g.Nodes[j];
                        double dx = b.Bx - a.Bx, dy = b.By - a.By, d = Math.Sqrt(dx * dx + dy * dy), min = a.R + b.R + 8;
                        if (d >= min) continue;
                        if (d < 0.01) { dx = 1; dy = 0.3 * (j - i); d = 1; }
                        double push = (min - d) / 2;
                        double ux = dx / d, uy = dy / d;
                        if (a.Depth == 0) { b.Bx += ux * push * 2; b.By += uy * push * 2; }
                        else if (b.Depth == 0) { a.Bx -= ux * push * 2; a.By -= uy * push * 2; }
                        else { a.Bx -= ux * push; a.By -= uy * push; b.Bx += ux * push; b.By += uy * push; }
                        moved = true;
                    }
                if (!moved) break;
            }
        }

        /// <summary>Column per hop; inside a column nodes follow the order of their parents.</summary>
        public static void Layout(Graph g)
        {
            const double dx = 290, dy = 78;
            var byDepth = g.Nodes.GroupBy(n => n.Depth).OrderBy(x => x.Key).ToList();
            int tallest = byDepth.Count == 0 ? 1 : byDepth.Max(x => x.Count());
            foreach (var col in byDepth)
            {
                var ordered = col.Key == 0 ? col.ToList() : col.OrderBy(n =>
                {
                    var parents = g.Edges.Where(e => (g.Forward ? e.To : e.From) == n.Id).Select(e => g.Node(g.Forward ? e.From : e.To)).Where(p => p != null && p.Depth < n.Depth).ToList();
                    return parents.Count == 0 ? 0 : parents.Average(p => p.Y);
                }).ToList();
                double off = (tallest - ordered.Count) * dy / 2;
                for (int i = 0; i < ordered.Count; i++) { ordered[i].X = (g.Forward ? col.Key : byDepth.Count - 1 - col.Key) * dx; ordered[i].Y = off + i * dy; }
            }
        }
    }

    public class Counterparty
    {
        public string Address { get; set; }
        public string Label { get; set; }
        public string Cat { get; set; }
        public string Asset { get; set; }
        public decimal In { get; set; }
        public decimal Out { get; set; }
        public int Count { get; set; }
        public long First { get; set; }
        public long Last { get; set; }
    }

    public class Finding { public string Level, Title, Detail; public string Hash = ""; public string Address = ""; }

    public class MatchRow
    {
        public Xfer Dep, Wd;
        public int Candidates;
        public long Delay;
        public double DiffPct;
        public string Confidence;
    }

    public class LinkResult
    {
        public List<Xfer> Direct = new();
        public List<(string address, List<string> sharedBy, int transfers)> Shared = new();
        public List<(string funder, List<string> funded)> Funders = new();
    }

    /// <summary>Heuristics over an address's transfers. Each one yields leads to verify, never proof.</summary>
    public static class Analysis
    {
        public static List<Counterparty> Counterparties(string addr, IEnumerable<Xfer> xs) =>
            xs.Where(x => x.From != x.To && (x.From == addr || x.To == addr))
              .GroupBy(x => (cp: x.From == addr ? x.To : x.From, x.Asset))
              .Select(g => { var l = Labels.Find(g.Key.cp); return new Counterparty { Address = g.Key.cp, Label = l?.Name ?? "", Cat = l?.Cat ?? "", Asset = g.Key.Asset, In = g.Where(x => x.To == addr).Sum(x => x.Amount), Out = g.Where(x => x.From == addr).Sum(x => x.Amount), Count = g.Count(), First = g.Min(x => x.Time), Last = g.Max(x => x.Time) }; })
              .OrderByDescending(c => c.Count).ToList();

        public static Xfer FirstFunder(string addr, IEnumerable<Xfer> xs) =>
            xs.Where(x => x.To == addr && x.From != addr && x.Kind != "token").OrderBy(x => x.Time).FirstOrDefault();

        /// <summary>A small transfer to a counterparty followed, within the window, by a much larger one to the same counterparty.</summary>
        public static List<(Xfer small, Xfer big)> TestTxs(string addr, IEnumerable<Xfer> xs, decimal ratio = 20, long windowSec = 172800)
        {
            var res = new List<(Xfer, Xfer)>();
            foreach (var g in xs.Where(x => x.From == addr && x.To != addr).GroupBy(x => (x.To, x.Asset, x.Contract)))
            {
                var list = g.OrderBy(x => x.Time).ToList();
                if (list.Count > 6) continue;   // many payments to the same destination are routine, not a test
                for (int j = 1; j < list.Count; j++)
                {
                    if (list[j].Time - list[0].Time > windowSec) break;
                    if (list[0].Amount > 0 && list[j].Amount >= list[0].Amount * ratio) { res.Add((list[0], list[j])); break; }
                }
            }
            return res.OrderBy(x => x.Item1.Time).ToList();
        }

        /// <summary>Money that arrives and leaves almost unchanged within minutes (a relay / hop wallet).</summary>
        public static List<(Xfer inn, Xfer outt)> PassThrough(string addr, IEnumerable<Xfer> xs, long maxSec = 1800, decimal keep = 0.9m)
        {
            var res = new List<(Xfer, Xfer)>();
            var outs = xs.Where(x => x.From == addr && x.To != addr).OrderBy(x => x.Time).ToList();
            var used = new HashSet<Xfer>();
            foreach (var i in xs.Where(x => x.To == addr && x.From != addr).OrderBy(x => x.Time))
            {
                var o = outs.FirstOrDefault(x => !used.Contains(x) && x.Asset == i.Asset && x.Contract == i.Contract && x.Time >= i.Time && x.Time - i.Time <= maxSec && x.Amount <= i.Amount && x.Amount >= i.Amount * keep);
                if (o != null) { used.Add(o); res.Add((i, o)); }
            }
            return res;
        }

        public static int[] Hours(string addr, IEnumerable<Xfer> xs)
        {
            var h = new int[24];
            foreach (var t in xs.Where(x => x.From == addr).Select(x => x.Hash + "|" + x.Time).Distinct()) { long time = long.Parse(t.Substring(t.LastIndexOf('|') + 1)); h[(int)(time % 86400 / 3600)]++; }
            return h;
        }

        public static List<Finding> Findings(string addr, AddrInfo info, TransferSet set, Xfer firstFunding = null)
        {
            var f = new List<Finding>();
            var xs = Tokens.Clean(set.Items);
            int spam = set.Items.Count - xs.Count;
            var own = Labels.Find(addr);
            if (own != null && own.Cat == "threat") f.Add(new Finding { Level = "high", Title = "Address is flagged", Detail = own.Name });
            if (info != null && info.IsScam) f.Add(new Finding { Level = "high", Title = "Explorer marks this address as scam", Detail = string.Join(", ", info.Tags) });
            var ff = firstFunding ?? (set.Truncated ? null : FirstFunder(addr, xs));
            if (ff != null) f.Add(new Finding { Level = "info", Title = "First funding seen", Detail = $"{Fmt.Amt(ff.Amount)} {ff.Asset} ← {Labels.Show(ff.From)} · {Fmt.Time(ff.Time)}", Hash = ff.Hash, Address = ff.From });
            if (spam > 0) f.Add(new Finding { Level = "medium", Title = "Spam or look-alike tokens were set aside", Detail = L.F("{0} transfers of fake or advertising tokens (for example symbols imitating ETH or USDT) are ignored in the checks below. Such tokens are bait: never copy an address from them.", spam) });
            var cps = Counterparties(addr, xs);
            foreach (var cat in new[] { "mixer", "bridge", "exchange", "threat" })
            {
                var hit = cps.Where(c => c.Cat == cat).ToList();
                if (hit.Count == 0) continue;
                string names = string.Join(", ", hit.Select(c => c.Label).Distinct().Take(4));
                if (cat == "mixer")
                {
                    var got = hit.Where(c => c.In > 0).ToList(); var gave = hit.Where(c => c.Out > 0).ToList();
                    if (got.Count > 0) f.Add(new Finding { Level = "high", Title = "Received funds from a mixer", Detail = string.Join("; ", got.GroupBy(c => c.Asset).Select(x => Fmt.Amt(x.Sum(c => c.In)) + " " + x.Key)) + " ← " + string.Join(", ", got.Select(c => c.Label).Distinct().Take(4)) + " · " + Fmt.Day(got.Min(c => c.First)) + ".." + Fmt.Day(got.Max(c => c.Last)), Address = got[0].Address });
                    if (gave.Count > 0) f.Add(new Finding { Level = "high", Title = "Sent funds into a mixer", Detail = string.Join("; ", gave.GroupBy(c => c.Asset).Select(x => Fmt.Amt(x.Sum(c => c.Out)) + " " + x.Key)) + " → " + string.Join(", ", gave.Select(c => c.Label).Distinct().Take(4)) + " · " + Fmt.Day(gave.Min(c => c.First)) + ".." + Fmt.Day(gave.Max(c => c.Last)), Address = gave[0].Address });
                    continue;
                }
                f.Add(new Finding { Level = cat == "mixer" || cat == "threat" ? "high" : "medium", Title = cat switch { "mixer" => "Interacted with a mixer / privacy tool", "bridge" => "Used a cross-chain bridge", "exchange" => "Sent to / received from an exchange", _ => "Transacted with a flagged address" }, Detail = $"{names} · {hit.Sum(c => c.Count)} tx", Address = hit[0].Address });
            }
            decimal Value(Xfer x) => Chains.Stable.Contains(x.Contract) ? x.Amount : x.Kind != "token" && info != null && info.Usd > 0 ? x.Amount * info.Usd : 0m;
            var big = xs.Where(x => x.From == addr && x.To != addr && Value(x) >= 100000m).OrderByDescending(Value).ToList();
            if (big.Count > 0) f.Add(new Finding { Level = "medium", Title = "Large outgoing transfers", Detail = L.F("{0} transfers above $100,000 at today's price; the largest: {1} {2} → {3} · {4}", big.Count, Fmt.Amt(big[0].Amount), big[0].Asset, Labels.Show(big[0].To), Fmt.Time(big[0].Time)), Hash = big[0].Hash, Address = big[0].To });
            var tests = TestTxs(addr, xs);
            foreach (var t in tests.Take(5))
                f.Add(new Finding { Level = "medium", Title = "Possible test transaction", Detail = L.F("{0} {1} → {2}, then {3} {4} after {5}", Fmt.Amt(t.small.Amount), t.small.Asset, Labels.Show(t.small.To), Fmt.Amt(t.big.Amount), t.big.Asset, Fmt.Dur(t.big.Time - t.small.Time)), Hash = t.small.Hash, Address = t.small.To });
            var pt = PassThrough(addr, xs);
            if (pt.Count >= 2) f.Add(new Finding { Level = "medium", Title = "Pass-through behaviour", Detail = L.F("{0} deposits were forwarded within 30 minutes (median delay {1})", pt.Count, Fmt.Dur(pt.Select(p => p.outt.Time - p.inn.Time).OrderBy(x => x).ElementAt(pt.Count / 2))), Hash = pt[0].outt.Hash });
            int outCp = xs.Where(x => x.From == addr).Select(x => x.To).Distinct().Count(), inCp = xs.Where(x => x.To == addr).Select(x => x.From).Distinct().Count();
            if (outCp >= 20 && outCp >= inCp * 4) f.Add(new Finding { Level = "medium", Title = "Fan-out pattern", Detail = L.F("sends to {0} addresses, receives from {1}", outCp, inCp) });
            if (inCp >= 20 && inCp >= outCp * 4) f.Add(new Finding { Level = "medium", Title = "Fan-in (collection) pattern", Detail = L.F("receives from {0} addresses, sends to {1}", inCp, outCp) });
            if (set.Truncated) f.Add(new Finding { Level = "info", Title = "Partial history", Detail = L.T("Only the most recent transfers were loaded; results marked * may change with the full history.") });
            if (f.Count == 0) f.Add(new Finding { Level = "info", Title = "Nothing notable in the loaded transfers", Detail = "" });
            return f;
        }

        static bool Distinctive(decimal a)
        {
            if (a <= 0) return false;
            string s = a.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "").TrimStart('0').TrimEnd('0');
            return s.Length >= 3;
        }

        /// <summary>Pairs deposits into a service with later withdrawals of nearly the same amount.</summary>
        public static List<MatchRow> Match(IEnumerable<Xfer> deposits, IEnumerable<Xfer> withdrawals, long windowSec, double tolPct, bool sameAsset = true)
        {
            var res = new List<MatchRow>();
            var wds = withdrawals.OrderBy(x => x.Time).ToList();
            foreach (var d in deposits.OrderBy(x => x.Time))
            {
                decimal lo = d.Amount * (decimal)(1 - tolPct / 100), hi = d.Amount * 1.0005m;
                var cands = wds.Where(w => w.Time >= d.Time && w.Time - d.Time <= windowSec && w.Amount >= lo && w.Amount <= hi && (!sameAsset || string.Equals(w.Asset, d.Asset, StringComparison.OrdinalIgnoreCase)) && w.Hash != d.Hash).ToList();
                foreach (var w in cands.OrderBy(w => Math.Abs(d.Amount - w.Amount)).ThenBy(w => w.Time).Take(5))
                {
                    string conf = cands.Count == 1 && Distinctive(d.Amount) ? "high" : cands.Count <= 3 && Distinctive(d.Amount) ? "medium" : "low";
                    res.Add(new MatchRow { Dep = d, Wd = w, Candidates = cands.Count, Delay = w.Time - d.Time, DiffPct = d.Amount == 0 ? 0 : (double)((d.Amount - w.Amount) / d.Amount) * 100, Confidence = conf });
                }
            }
            return res;
        }

        /// <summary>Direct transfers, shared counterparties and shared first funders among a set of addresses.</summary>
        public static LinkResult Link(Dictionary<string, List<Xfer>> data, bool skipServices = true)
        {
            var r = new LinkResult();
            var set = new HashSet<string>(data.Keys);
            var seen = new HashSet<string>();
            foreach (var kv in data)
                foreach (var x in kv.Value)
                    if (set.Contains(x.From) && set.Contains(x.To) && x.From != x.To && seen.Add(x.Hash + x.From + x.To + x.Asset)) r.Direct.Add(x);
            var cp = new Dictionary<string, Dictionary<string, int>>();
            foreach (var kv in data)
                foreach (var x in kv.Value)
                {
                    string other = x.From == kv.Key ? x.To : x.From;
                    if (string.IsNullOrEmpty(other) || set.Contains(other)) continue;
                    if (!cp.TryGetValue(other, out var d)) cp[other] = d = new();
                    d[kv.Key] = d.GetValueOrDefault(kv.Key) + 1;
                }
            foreach (var c in cp.Where(c => c.Value.Count >= 2))
            {
                var l = Labels.Find(c.Key);
                if (skipServices && l != null && (Labels.IsService(l.Cat) || l.Cat == "contract" || l.Cat == "other")) continue;
                r.Shared.Add((c.Key, c.Value.Keys.ToList(), c.Value.Values.Sum()));
            }
            r.Shared = r.Shared.OrderByDescending(s => s.sharedBy.Count).ThenByDescending(s => s.transfers).ToList();
            var funders = data.Select(kv => (addr: kv.Key, f: FirstFunder(kv.Key, kv.Value))).Where(x => x.f != null).GroupBy(x => x.f.From).Where(g => g.Count() >= 2);
            foreach (var g in funders) r.Funders.Add((g.Key, g.Select(x => x.addr).ToList()));
            return r;
        }
    }
}
